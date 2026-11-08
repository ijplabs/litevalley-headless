use crate::error::{Result, SaveEngineError};
use crate::models::{map_farm_type_name, FarmerSummaryDto, InGameDateDto, SaveMetadataDto};
use quick_xml::events::Event;
use quick_xml::reader::Reader;
use std::fs::File;
use std::io::{BufReader, Read};
use std::path::Path;
use std::time::Instant;
use zip::ZipArchive;

pub struct SaveInspector;

impl SaveInspector {
    /// Inspeksi savegame dari path (dapat berupa direktori save, file .zip, atau file .xml tunggal)
    pub fn inspect_path(path: &Path) -> Result<SaveMetadataDto> {
        let start = Instant::now();

        if !path.exists() {
            return Err(SaveEngineError::Io(std::io::Error::new(
                std::io::ErrorKind::NotFound,
                format!("Path tidak ditemukan: {}", path.display()),
            )));
        }

        if path.is_file() {
            let ext = path.extension().and_then(|s| s.to_str()).unwrap_or("");
            if ext.eq_ignore_ascii_case("zip") {
                Self::inspect_zip(path, start)
            } else {
                // Anggap sebagai file XML mentah (SaveGameInfo atau world file)
                Self::inspect_xml_file(path, start)
            }
        } else if path.is_dir() {
            Self::inspect_directory(path, start)
        } else {
            Err(SaveEngineError::InvalidSave(format!(
                "Tipe file tidak didukung: {}",
                path.display()
            )))
        }
    }

    /// Inspeksi dari direktori savegame lokal (misal: Saves/Lembah_123456789)
    pub fn inspect_directory(dir_path: &Path, start_time: Instant) -> Result<SaveMetadataDto> {
        let mut total_size = 0u64;
        let mut save_game_info_path = None;
        let mut world_file_path = None;

        for entry in std::fs::read_dir(dir_path)? {
            let entry = entry?;
            let p = entry.path();
            if p.is_file() {
                if let Ok(meta) = entry.metadata() {
                    total_size += meta.len();
                }

                let file_name = p.file_name().and_then(|s| s.to_str()).unwrap_or("");
                if file_name.eq_ignore_ascii_case("SaveGameInfo") {
                    save_game_info_path = Some(p.clone());
                } else if !file_name.ends_with("_old") && !file_name.ends_with(".tmp") {
                    // Kemungkinan file XML dunia utama
                    world_file_path = Some(p.clone());
                }
            }
        }

        // Prioritas 1: Jika SaveGameInfo ada, parse terlebih dahulu untuk profil cepat host
        if let Some(sgi_path) = save_game_info_path {
            let file = File::open(&sgi_path)?;
            let reader = BufReader::new(file);
            let mut meta = Self::parse_xml_stream(reader, false)?;
            meta.file_size_bytes = total_size;
            meta.source_type = "directory".to_string();

            // Pindai world file jika ada untuk mengambil cabins_count dan farmhands riil
            if let Some(world_path) = world_file_path {
                if let Ok(world_file) = File::open(&world_path) {
                    let world_reader = BufReader::new(world_file);
                    if let Ok(world_meta) = Self::parse_xml_stream(world_reader, true) {
                        meta.cabins_count = world_meta.cabins_count;
                        if !world_meta.farmhands.is_empty() {
                            meta.farmhands = world_meta.farmhands;
                        }
                    }
                }
            }

            meta.parse_duration_ms = start_time.elapsed().as_secs_f64() * 1000.0;
            return Ok(meta);
        }

        // Prioritas 2: Parse file dunia jika SaveGameInfo tidak ada
        if let Some(world_path) = world_file_path {
            let file = File::open(&world_path)?;
            let reader = BufReader::new(file);
            let mut meta = Self::parse_xml_stream(reader, true)?;
            meta.file_size_bytes = total_size;
            meta.source_type = "directory".to_string();
            meta.parse_duration_ms = start_time.elapsed().as_secs_f64() * 1000.0;
            return Ok(meta);
        }

        // Prioritas 3: Jika dir_path adalah direktori induk (misal /data/saves), cari subfolder save terbaru
        let mut latest_subdir: Option<(std::time::SystemTime, std::path::PathBuf)> = None;
        if let Ok(entries) = std::fs::read_dir(dir_path) {
            for entry in entries.flatten() {
                let p = entry.path();
                if p.is_dir() {
                    let dir_name = p.file_name().and_then(|s| s.to_str()).unwrap_or("");
                    if !dir_name.eq_ignore_ascii_case("archive") && !dir_name.starts_with('.') {
                        let mtime = entry
                            .metadata()
                            .and_then(|m| m.modified())
                            .unwrap_or(std::time::SystemTime::UNIX_EPOCH);
                        if latest_subdir.as_ref().map_or(true, |(prev_t, _)| mtime >= *prev_t) {
                            latest_subdir = Some((mtime, p));
                        }
                    }
                }
            }
        }

        if let Some((_, sub_path)) = latest_subdir {
            return Self::inspect_directory(&sub_path, start_time);
        }

        Err(SaveEngineError::InvalidSave(format!(
            "Direktori '{}' tidak memuat file savegame Stardew Valley yang valid (SaveGameInfo atau world file).",
            dir_path.display()
        )))
    }

    /// Inspeksi langsung dari arsip .zip di memory tanpa ekstrak ke disk
    pub fn inspect_zip(zip_path: &Path, start_time: Instant) -> Result<SaveMetadataDto> {
        let file = File::open(zip_path)?;
        let total_size = file.metadata()?.len();
        let mut archive = ZipArchive::new(BufReader::new(file))?;

        // Cari entry SaveGameInfo dan world file di dalam arsip
        let mut sgi_index = None;
        let mut world_index = None;

        for i in 0..archive.len() {
            let entry = archive.by_index(i)?;
            let name = entry.name().replace('\\', "/");
            let base_name = Path::new(&name)
                .file_name()
                .and_then(|s| s.to_str())
                .unwrap_or("");

            if base_name.eq_ignore_ascii_case("SaveGameInfo") {
                sgi_index = Some(i);
            } else if !entry.is_dir()
                && !base_name.ends_with("_old")
                && !base_name.ends_with(".zip")
                && !base_name.starts_with('.')
                && !base_name.is_empty()
            {
                world_index = Some(i);
            }
        }

        if let Some(idx) = sgi_index {
            let entry = archive.by_index(idx)?;
            let reader = BufReader::new(entry.take(crate::extractor::MAX_TOTAL_BYTES));
            let mut meta = Self::parse_xml_stream(reader, false)?;
            meta.file_size_bytes = total_size;
            meta.source_type = "zip".to_string();

            // Pindai world file jika ada untuk mengambil cabins_count dan farmhands riil
            if let Some(w_idx) = world_index {
                if let Ok(w_entry) = archive.by_index(w_idx) {
                    let w_reader = BufReader::new(w_entry.take(crate::extractor::MAX_TOTAL_BYTES));
                    if let Ok(w_meta) = Self::parse_xml_stream(w_reader, true) {
                        meta.cabins_count = w_meta.cabins_count;
                        if !w_meta.farmhands.is_empty() {
                            meta.farmhands = w_meta.farmhands;
                        }
                    }
                }
            }

            meta.parse_duration_ms = start_time.elapsed().as_secs_f64() * 1000.0;
            return Ok(meta);
        }

        if let Some(idx) = world_index {
            let entry = archive.by_index(idx)?;
            let reader = BufReader::new(entry.take(crate::extractor::MAX_TOTAL_BYTES));
            let mut meta = Self::parse_xml_stream(reader, true)?;
            meta.file_size_bytes = total_size;
            meta.source_type = "zip".to_string();
            meta.parse_duration_ms = start_time.elapsed().as_secs_f64() * 1000.0;
            return Ok(meta);
        }

        Err(SaveEngineError::InvalidSave(format!(
            "Arsip zip '{}' tidak memuat file savegame Stardew Valley yang valid.",
            zip_path.display()
        )))
    }

    /// Inspeksi dari file XML tunggal
    pub fn inspect_xml_file(file_path: &Path, start_time: Instant) -> Result<SaveMetadataDto> {
        let file = File::open(file_path)?;
        let file_size = file.metadata()?.len();
        let reader = BufReader::new(file);

        let is_world_file = !file_path
            .file_name()
            .and_then(|s| s.to_str())
            .map(|s| s.eq_ignore_ascii_case("SaveGameInfo"))
            .unwrap_or(false);

        let mut meta = Self::parse_xml_stream(reader, is_world_file)?;
        meta.file_size_bytes = file_size;
        meta.source_type = "xml_file".to_string();
        meta.parse_duration_ms = start_time.elapsed().as_secs_f64() * 1000.0;
        Ok(meta)
    }

    /// Inti parser XML streaming berbasis SAX via quick-xml (Zero-Copy) dengan pelacakan hierarki tag
    fn parse_xml_stream<R: Read>(reader: R, is_world_file: bool) -> Result<SaveMetadataDto> {
        let mut xml_reader = Reader::from_reader(BufReader::new(reader));
        xml_reader.config_mut().trim_text(true);

        let mut buf = Vec::with_capacity(4096);

        // State pengumpul metadata
        let mut farm_name = String::new();
        let mut farm_type = 0u8;
        let mut unique_id = String::new();
        let mut game_version = None;
        let mut season = String::new();
        let mut day = 1u32;
        let mut year = 1u32;

        let mut host = FarmerSummaryDto::default();
        let mut farmhands = Vec::new();
        let mut cabins_count = 0usize;

        let mut in_farmhands = false;
        let mut current_farmhand: Option<FarmerSummaryDto> = None;
        let mut in_player = false;
        let mut host_name_found = false;

        // Tag hierarchy stack untuk depth checking akurat
        let mut tag_stack: Vec<String> = Vec::with_capacity(16);
        let mut valid_stardew_xml = false;

        loop {
            match xml_reader.read_event_into(&mut buf)? {
                Event::Start(e) => {
                    let tag = String::from_utf8_lossy(e.name().as_ref()).to_string();
                    let tag_lower = tag.to_ascii_lowercase();

                    if tag_lower == "farmer" || tag_lower == "savegame" {
                        valid_stardew_xml = true;
                    }

                    if tag_lower == "player" && is_world_file {
                        in_player = true;
                    } else if tag_lower == "farmhands" {
                        in_farmhands = true;
                    } else if (in_farmhands && tag_lower == "farmer") || tag_lower == "farmhand" {
                        current_farmhand = Some(FarmerSummaryDto {
                            name: String::new(),
                            money: 500,
                            total_money_earned: 500,
                            daily_luck: 0.0,
                        });
                    }

                    tag_stack.push(tag);
                }
                Event::End(e) => {
                    let name_bytes = e.name();
                    let tag = String::from_utf8_lossy(name_bytes.as_ref());
                    let tag_lower = tag.to_ascii_lowercase();

                    if tag_lower == "player" {
                        in_player = false;
                    } else if tag_lower == "farmhands" {
                        in_farmhands = false;
                    } else if (in_farmhands && tag_lower == "farmer") || tag_lower == "farmhand" {
                        if let Some(fh) = current_farmhand.take() {
                            if !fh.name.is_empty() {
                                farmhands.push(fh);
                            }
                        }
                    }

                    tag_stack.pop();
                }
                Event::Text(e) => {
                    let text = e.unescape().unwrap_or_default().to_string();
                    if text.is_empty() {
                        buf.clear();
                        continue;
                    }

                    let current_tag = tag_stack.last().map(|s| s.as_str()).unwrap_or("");
                    let parent_tag = if tag_stack.len() >= 2 {
                        tag_stack[tag_stack.len() - 2].as_str()
                    } else {
                        ""
                    };

                    match current_tag {
                        "name" => {
                            if current_farmhand.is_some()
                                && (parent_tag.eq_ignore_ascii_case("Farmer")
                                    || parent_tag.eq_ignore_ascii_case("farmhand"))
                            {
                                if let Some(fh) = &mut current_farmhand {
                                    if fh.name.is_empty() {
                                        fh.name = text;
                                    }
                                }
                            } else if (in_player && parent_tag.eq_ignore_ascii_case("player"))
                                || (!is_world_file && parent_tag.eq_ignore_ascii_case("Farmer"))
                            {
                                // Hanya ambil nama jika tag <name> adalah anak langsung dari <player> atau <Farmer>
                                if !host_name_found {
                                    host.name = text;
                                    host_name_found = true;
                                }
                            }
                        }
                        "money" => {
                            if let Ok(m) = text.parse::<i32>() {
                                if in_farmhands {
                                    if let Some(fh) = &mut current_farmhand {
                                        fh.money = m;
                                    }
                                } else if (in_player && parent_tag.eq_ignore_ascii_case("player"))
                                    || (!is_world_file && parent_tag.eq_ignore_ascii_case("Farmer"))
                                {
                                    host.money = m;
                                }
                            }
                        }
                        "totalMoneyEarned" => {
                            if let Ok(m) = text.parse::<u32>() {
                                if in_farmhands {
                                    if let Some(fh) = &mut current_farmhand {
                                        fh.total_money_earned = m;
                                    }
                                } else if (in_player && parent_tag.eq_ignore_ascii_case("player"))
                                    || (!is_world_file && parent_tag.eq_ignore_ascii_case("Farmer"))
                                {
                                    host.total_money_earned = m;
                                }
                            }
                        }
                        "dailyLuck" => {
                            if let Ok(l) = text.parse::<f64>() {
                                if (in_player && parent_tag.eq_ignore_ascii_case("player"))
                                    || (!is_world_file && parent_tag.eq_ignore_ascii_case("Farmer"))
                                {
                                    host.daily_luck = l;
                                }
                            }
                        }
                        "farmName" => {
                            if farm_name.is_empty() {
                                farm_name = text;
                            }
                        }
                        "whichFarm" => {
                            if let Ok(wf) = text.parse::<u8>() {
                                farm_type = wf;
                            }
                        }
                        "uniqueIDForThisGame" => {
                            if unique_id.is_empty() {
                                unique_id = text;
                            }
                        }
                        "gameVersion" => {
                            if game_version.is_none() {
                                game_version = Some(text);
                            }
                        }
                        "currentSeason" => {
                            if season.is_empty() {
                                season = text.to_lowercase();
                            }
                        }
                        "dayOfMonth" => {
                            if let Ok(d) = text.parse::<u32>() {
                                day = d;
                            }
                        }
                        "year" => {
                            if let Ok(y) = text.parse::<u32>() {
                                year = y;
                            }
                        }
                        "buildingType" => {
                            if text.eq_ignore_ascii_case("Cabin") {
                                cabins_count += 1;
                            }
                        }
                        _ => {}
                    }
                }
                Event::Eof => break,
                _ => {}
            }
            buf.clear();
        }

        if !valid_stardew_xml && unique_id.is_empty() && farm_name.is_empty() {
            return Err(SaveEngineError::InvalidSave(
                "File XML tidak memuat struktur savegame Stardew Valley yang valid (<Farmer> atau <SaveGame>).".to_string(),
            ));
        }

        if season.is_empty() {
            season = "spring".to_string();
        }

        let season_display = {
            let mut chars = season.chars();
            match chars.next() {
                Some(first) => first.to_uppercase().collect::<String>() + chars.as_str(),
                None => "Spring".to_string(),
            }
        };

        let date_display = format!("{}, Day {} of Year {}", season_display, day, year);
        let farm_type_name = map_farm_type_name(farm_type).to_string();

        Ok(SaveMetadataDto {
            valid: true,
            game_version,
            farm_name: if farm_name.is_empty() { "Unnamed Farm".to_string() } else { farm_name },
            farm_type,
            farm_type_name,
            unique_id,
            in_game_date: InGameDateDto {
                season,
                day,
                year,
                display: date_display,
            },
            host,
            farmhands,
            cabins_count, // Hasil riil hitungan kabin, tanpa pemaksaan angka 4
            file_size_bytes: 0,
            parse_duration_ms: 0.0,
            source_type: "unknown".to_string(),
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Cursor;

    #[test]
    fn test_parse_save_game_info_mock() {
        let xml_mock = r#"<?xml version="1.0" encoding="utf-8"?>
<Farmer xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <name>Budi</name>
  <isCustomized>true</isCustomized>
  <farmName>LembahIndah</farmName>
  <currentSeason>summer</currentSeason>
  <dayOfMonth>18</dayOfMonth>
  <year>2</year>
  <money>48250</money>
  <totalMoneyEarned>125000</totalMoneyEarned>
  <whichFarm>7</whichFarm>
  <uniqueIDForThisGame>39182049182</uniqueIDForThisGame>
  <gameVersion>1.6.14</gameVersion>
  <dailyLuck>0.08</dailyLuck>
  <items>
    <Item xsi:type="Tool">
      <name>Axe</name>
    </Item>
    <Item xsi:type="Object">
      <name>Parsnip</name>
    </Item>
  </items>
</Farmer>"#;

        let meta = SaveInspector::parse_xml_stream(Cursor::new(xml_mock), false).unwrap();
        assert_eq!(meta.farm_name, "LembahIndah");
        assert_eq!(meta.farm_type, 7);
        assert_eq!(meta.farm_type_name, "Meadowlands");
        assert_eq!(meta.in_game_date.season, "summer");
        assert_eq!(meta.in_game_date.day, 18);
        assert_eq!(meta.in_game_date.year, 2);
        // Nama petani tidak boleh tertimpa oleh nama item <name>Axe</name> atau <name>Parsnip</name>!
        assert_eq!(meta.host.name, "Budi");
        assert_eq!(meta.host.money, 48250);
        assert_eq!(meta.unique_id, "39182049182");
        assert_eq!(meta.game_version.as_deref(), Some("1.6.14"));
        // Solo savegame SaveGameInfo saja memiliki 0 kabin secara akurat
        assert_eq!(meta.cabins_count, 0);
    }

    #[test]
    fn test_parse_world_save_cabins_mock() {
        let xml_world = r#"<?xml version="1.0" encoding="utf-8"?>
<SaveGame>
  <player>
    <name>Luffy</name>
    <farmName>Sunny</farmName>
    <money>99999</money>
  </player>
  <currentSeason>fall</currentSeason>
  <dayOfMonth>10</dayOfMonth>
  <year>1</year>
  <uniqueIDForThisGame>12345678</uniqueIDForThisGame>
  <whichFarm>0</whichFarm>
  <locations>
    <GameLocation xsi:type="Farm">
      <buildings>
        <Building>
          <buildingType>Coop</buildingType>
        </Building>
        <Building>
          <buildingType>Cabin</buildingType>
        </Building>
        <Building>
          <buildingType>Cabin</buildingType>
        </Building>
      </buildings>
    </GameLocation>
  </locations>
</SaveGame>"#;

        let meta = SaveInspector::parse_xml_stream(Cursor::new(xml_world), true).unwrap();
        assert_eq!(meta.host.name, "Luffy");
        assert_eq!(meta.farm_name, "Sunny");
        assert_eq!(meta.cabins_count, 2);
    }

    #[test]
    fn test_invalid_xml_rejected() {
        let xml_invalid = r#"<RandomRoot><someData>foo</someData></RandomRoot>"#;
        let res = SaveInspector::parse_xml_stream(Cursor::new(xml_invalid), false);
        assert!(res.is_err());
    }
}
