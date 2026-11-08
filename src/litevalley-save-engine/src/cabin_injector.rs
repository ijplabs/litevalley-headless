use crate::archiver::SaveArchiver;
use crate::error::{Result, SaveEngineError};
use crate::extractor::SaveExtractor;
use crate::inspector::SaveInspector;
use std::fs::File;
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::time::Instant;

pub struct CabinInjector;

#[derive(serde::Serialize, serde::Deserialize, Debug)]
pub struct CabinInjectionResult {
    pub success: bool,
    pub previous_cabins: usize,
    pub cabins_added: usize,
    pub final_cabins: usize,
    pub target_path: String,
    pub duration_ms: f64,
}

impl CabinInjector {
    /// Menjamin bahwa savegame memiliki minimal `target_cabins` kabin (Auto-Provisioning Kabin Coop)
    pub fn ensure_cabins(path: &Path, target_cabins: usize) -> Result<CabinInjectionResult> {
        let start = Instant::now();

        if !path.exists() {
            return Err(SaveEngineError::Io(std::io::Error::new(
                std::io::ErrorKind::NotFound,
                format!("Path savegame tidak ditemukan: {}", path.display()),
            )));
        }

        if path.is_file() {
            let ext = path.extension().and_then(|s| s.to_str()).unwrap_or("");
            if ext.eq_ignore_ascii_case("zip") {
                // Untuk file zip: ekstrak ke folder temp, modifikasi XML, lalu re-zip
                let temp_dir = tempfile::tempdir()?;
                let extract_res = SaveExtractor::extract_zip(path, temp_dir.path())?;
                let save_folder = temp_dir.path().join(&extract_res.extracted_folder_name);
                
                let res = Self::ensure_cabins_in_dir(&save_folder, target_cabins)?;

                // Re-export ke path zip asli
                let backup_zip = path.with_extension("zip.bak");
                if path.exists() {
                    let _ = std::fs::rename(path, &backup_zip);
                }
                SaveArchiver::create_zip_archive(&save_folder, path)?;
                if backup_zip.exists() {
                    let _ = std::fs::remove_file(backup_zip);
                }

                return Ok(CabinInjectionResult {
                    success: true,
                    previous_cabins: res.previous_cabins,
                    cabins_added: res.cabins_added,
                    final_cabins: res.final_cabins,
                    target_path: path.display().to_string(),
                    duration_ms: start.elapsed().as_secs_f64() * 1000.0,
                });
            }
        }

        if path.is_dir() {
            let mut res = Self::ensure_cabins_in_dir(path, target_cabins)?;
            res.duration_ms = start.elapsed().as_secs_f64() * 1000.0;
            return Ok(res);
        }

        Err(SaveEngineError::InvalidSave("Format target tidak didukung untuk mutasi kabin".to_string()))
    }

    fn ensure_cabins_in_dir(save_dir: &Path, target_cabins: usize) -> Result<CabinInjectionResult> {
        let meta = SaveInspector::inspect_directory(save_dir, Instant::now())?;
        let current_cabins = meta.cabins_count;

        if current_cabins >= target_cabins {
            return Ok(CabinInjectionResult {
                success: true,
                previous_cabins: current_cabins,
                cabins_added: 0,
                final_cabins: current_cabins,
                target_path: save_dir.display().to_string(),
                duration_ms: 0.0,
            });
        }

        let needed = target_cabins - current_cabins;

        // Cari file XML dunia utama (atau telusuri subfolder save terbaru jika save_dir adalah root /data/saves)
        let mut world_file_path: Option<PathBuf> = None;
        let mut latest_subdir: Option<(std::time::SystemTime, PathBuf)> = None;

        for entry in std::fs::read_dir(save_dir)? {
            let entry = entry?;
            let p = entry.path();
            if p.is_file() {
                let file_name = p.file_name().and_then(|s| s.to_str()).unwrap_or("");
                if !file_name.eq_ignore_ascii_case("SaveGameInfo")
                    && !file_name.ends_with("_old")
                    && !file_name.ends_with(".tmp")
                    && !file_name.ends_with(".zip")
                    && !file_name.starts_with('.')
                {
                    world_file_path = Some(p);
                    break;
                }
            } else if p.is_dir() {
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

        if world_file_path.is_none() {
            if let Some((_, sub_path)) = latest_subdir {
                return Self::ensure_cabins_in_dir(&sub_path, target_cabins);
            }
        }

        let world_path = world_file_path.ok_or_else(|| {
            SaveEngineError::InvalidSave("File XML dunia utama tidak ditemukan di dalam direktori save.".to_string())
        })?;

        // Baca konten XML dunia
        let mut xml_content = String::new();
        {
            let mut file = File::open(&world_path)?;
            file.read_to_string(&mut xml_content)?;
        }

        // Generate node XML kabin dengan uniqueName unik per indeks kabin
        let mut cabins_xml = String::new();
        for i in 0..needed {
            let cabin_index = current_cabins + i + 1;
            let tile_x = 48 + (cabin_index as i32 * 6);
            let tile_y = 14;
            let uuid = format!("{:08x}_{}", rand_u32(cabin_index as u32), cabin_index);

            cabins_xml.push_str(&format!(
                r#"<Building><buildingType>Cabin</buildingType><tileX>{}</tileX><tileY>{}</tileY><tilesHigh>3</tilesHigh><tilesWide>5</tilesWide><maxOccupants>1</maxOccupants><currentOccupants>0</currentOccupants><daysOfConstructionLeft>0</daysOfConstructionLeft><indoors xsi:type="Cabin"><uniqueName>Cabin_{}</uniqueName><name>Cabin</name><isFarm>false</isFarm><isOutdoors>false</isOutdoors><isStructure>true</isStructure><ignoreDebrisWeather>false</ignoreDebrisWeather><ignoreOutdoorLighting>false</ignoreOutdoorLighting><ignoreLights>false</ignoreLights><upgradeLevel>0</upgradeLevel></indoors></Building>"#,
                tile_x, tile_y, uuid
            ));
        }

        // Cari posisi penyuntikan di dalam <GameLocation xsi:type="Farm"> -> <buildings>
        let mut modified = false;
        if let Some(farm_loc_pos) = xml_content.find(r#"xsi:type="Farm""#) {
            let after_farm = &xml_content[farm_loc_pos..];
            if let Some(buildings_pos) = after_farm.find("<buildings>") {
                let insert_idx = farm_loc_pos + buildings_pos + "<buildings>".len();
                xml_content.insert_str(insert_idx, &cabins_xml);
                modified = true;
            } else if let Some(self_closing_pos) = after_farm.find("<buildings />") {
                let start_idx = farm_loc_pos + self_closing_pos;
                let end_idx = start_idx + "<buildings />".len();
                let replacement = format!("<buildings>{}</buildings>", cabins_xml);
                xml_content.replace_range(start_idx..end_idx, &replacement);
                modified = true;
            } else if let Some(self_closing_pos2) = after_farm.find("<buildings/>") {
                let start_idx = farm_loc_pos + self_closing_pos2;
                let end_idx = start_idx + "<buildings/>".len();
                let replacement = format!("<buildings>{}</buildings>", cabins_xml);
                xml_content.replace_range(start_idx..end_idx, &replacement);
                modified = true;
            }
        }

        if !modified {
            // Fallback: sisipkan sebelum </SaveGame> jika tag farm buildings tidak ditemukan
            if let Some(pos) = xml_content.rfind("</SaveGame>") {
                let wrapper = format!("<locations><GameLocation xsi:type=\"Farm\"><buildings>{}</buildings></GameLocation></locations>", cabins_xml);
                xml_content.insert_str(pos, &wrapper);
                modified = true;
            }
        }

        if modified {
            let mut file = File::create(&world_path)?;
            file.write_all(xml_content.as_bytes())?;
            file.flush()?;
        }

        Ok(CabinInjectionResult {
            success: modified,
            previous_cabins: current_cabins,
            cabins_added: if modified { needed } else { 0 },
            final_cabins: if modified { target_cabins } else { current_cabins },
            target_path: save_dir.display().to_string(),
            duration_ms: 0.0,
        })
    }
}

fn rand_u32(salt: u32) -> u32 {
    use std::time::SystemTime;
    let nanos = SystemTime::now()
        .duration_since(SystemTime::UNIX_EPOCH)
        .map(|d| d.subsec_nanos())
        .unwrap_or(12345678);
    nanos.wrapping_add(salt.wrapping_mul(0x9e3779b9)) ^ 0x5bd1e995
}


#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn test_ensure_cabins_in_dir() {
        let dir = tempdir().unwrap();
        let save_dir = dir.path().join("Sunny_1234");
        std::fs::create_dir_all(&save_dir).unwrap();

        let sgi_content = r#"<Farmer><name>Luffy</name><farmName>Sunny</farmName><uniqueIDForThisGame>1234</uniqueIDForThisGame></Farmer>"#;
        std::fs::write(save_dir.join("SaveGameInfo"), sgi_content).unwrap();

        let world_content = r#"<SaveGame><player><name>Luffy</name></player><locations><GameLocation xsi:type="Farm"><buildings /></GameLocation></locations></SaveGame>"#;
        std::fs::write(save_dir.join("Sunny_1234"), world_content).unwrap();

        let res = CabinInjector::ensure_cabins(&save_dir, 3).unwrap();
        assert_eq!(res.previous_cabins, 0);
        assert_eq!(res.cabins_added, 3);
        assert_eq!(res.final_cabins, 3);

        // Verifikasi file XML hasil modifikasi sekarang terdeteksi 3 kabin
        let inspected = SaveInspector::inspect_directory(&save_dir, Instant::now()).unwrap();
        assert_eq!(insenspected_cabins(&inspected), 3);
    }

    fn insenspected_cabins(meta: &crate::models::SaveMetadataDto) -> usize {
        meta.cabins_count
    }
}
