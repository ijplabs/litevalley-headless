use crate::error::{Result, SaveEngineError};
use std::fs::File;
use std::io::{BufReader, BufWriter, Read, Write};
use std::path::{Component, Path, PathBuf};
use zip::ZipArchive;

pub const MAX_TOTAL_BYTES: u64 = 300 * 1024 * 1024; // 300 MB Limit (Zip Bomb Protection)
pub const MAX_FILES_COUNT: usize = 100; // Limit jumlah file dalam savegame Stardew

pub struct SaveExtractor;

pub struct ExtractionResult {
    pub target_dir: PathBuf,
    pub extracted_folder_name: String,
    pub files_count: usize,
    pub total_bytes: u64,
    pub duration_ms: f64,
}

impl SaveExtractor {
    /// Ekstrak arsip .zip savegame secara aman ke direktori target dengan proteksi ZipSlip & Zip Bomb
    pub fn extract_zip(zip_path: &Path, target_base_dir: &Path) -> Result<ExtractionResult> {
        let start = std::time::Instant::now();

        if !zip_path.is_file() {
            return Err(SaveEngineError::InvalidSave(format!(
                "File zip '{}' tidak ditemukan.",
                zip_path.display()
            )));
        }

        std::fs::create_dir_all(target_base_dir)?;

        let file = File::open(zip_path)?;
        let mut archive = ZipArchive::new(BufReader::new(file))?;

        if archive.len() > MAX_FILES_COUNT {
            return Err(SaveEngineError::InvalidSave(format!(
                "Jumlah file dalam arsip zip ({}) melebihi batas wajar {} file (Zip bomb protection).",
                archive.len(),
                MAX_FILES_COUNT
            )));
        }

        let canonical_base = target_base_dir.canonicalize()?;

        // Pindai awal: Deteksi apakah zip memiliki folder pembungkus atau file langsung di root
        let mut has_root_wrapper = false;
        let mut detected_folder_name = String::new();
        let mut fallback_world_name = String::new();

        for i in 0..archive.len() {
            let entry = archive.by_index(i)?;
            let raw_name = entry.name().replace('\\', "/");
            let path = Path::new(&raw_name);

            // Verifikasi tidak ada traversal di nama entry
            for comp in path.components() {
                match comp {
                    Component::ParentDir | Component::RootDir | Component::Prefix(_) => {
                        return Err(SaveEngineError::ZipSlip(raw_name.clone()));
                    }
                    _ => {}
                }
            }

            let components: Vec<_> = path.components().collect();
            if components.len() > 1 {
                has_root_wrapper = true;
                if detected_folder_name.is_empty() {
                    detected_folder_name = components[0].as_os_str().to_string_lossy().to_string();
                }
            } else if components.len() == 1 {
                let fname = components[0].as_os_str().to_string_lossy();
                if !fname.eq_ignore_ascii_case("SaveGameInfo")
                    && !fname.ends_with("_old")
                    && !fname.ends_with(".zip")
                    && !fname.starts_with('.')
                {
                    fallback_world_name = fname.to_string();
                }
            }
        }

        // Jika file diekstrak langsung di root tanpa folder pembungkus (Bug 1.5):
        // Bungkus dalam folder save khusus agar tidak mencemari root direktori /data/saves
        let wrap_subfolder = if !has_root_wrapper {
            let folder_name = if !fallback_world_name.is_empty() {
                fallback_world_name
            } else {
                zip_path
                    .file_stem()
                    .and_then(|s| s.to_str())
                    .unwrap_or("ImportedFarm")
                    .to_string()
            };
            detected_folder_name = folder_name.clone();
            Some(folder_name)
        } else {
            None
        };

        let mut files_count = 0usize;
        let mut total_bytes = 0u64;
        let mut chunk_buf = vec![0u8; 64 * 1024];

        for i in 0..archive.len() {
            let mut entry = archive.by_index(i)?;
            let raw_name = entry.name().replace('\\', "/");
            let entry_path = Path::new(&raw_name);

            let final_relative_path = match &wrap_subfolder {
                Some(sub) => Path::new(sub).join(entry_path),
                None => entry_path.to_path_buf(),
            };

            // Validasi ZipSlip: path harus berada di bawah canonical_base
            let out_path = canonical_base.join(&final_relative_path);
            if !out_path.starts_with(&canonical_base) {
                return Err(SaveEngineError::ZipSlip(raw_name));
            }

            if entry.is_dir() {
                std::fs::create_dir_all(&out_path)?;
                let canonical_dir = out_path.canonicalize()?;
                if !canonical_dir.starts_with(&canonical_base) {
                    return Err(SaveEngineError::ZipSlip(raw_name));
                }
            } else {
                if let Some(parent) = out_path.parent() {
                    std::fs::create_dir_all(parent)?;
                    let canonical_parent = parent.canonicalize()?;
                    if !canonical_parent.starts_with(&canonical_base) {
                        return Err(SaveEngineError::ZipSlip(raw_name));
                    }
                }

                let out_file = File::create(&out_path)?;
                let mut writer = BufWriter::with_capacity(64 * 1024, out_file);

                loop {
                    let n = entry.read(&mut chunk_buf)?;
                    if n == 0 {
                        break;
                    }
                    total_bytes += n as u64;
                    if total_bytes > MAX_TOTAL_BYTES {
                        return Err(SaveEngineError::InvalidSave(format!(
                            "Ukuran total dekompresi melebihi batas maksimum {} MB (Zip bomb protection).",
                            MAX_TOTAL_BYTES / (1024 * 1024)
                        )));
                    }
                    writer.write_all(&chunk_buf[..n])?;
                }
                writer.flush()?;
                files_count += 1;
            }
        }

        let duration_ms = start.elapsed().as_secs_f64() * 1000.0;

        Ok(ExtractionResult {
            target_dir: target_base_dir.to_path_buf(),
            extracted_folder_name: detected_folder_name,
            files_count,
            total_bytes,
            duration_ms,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn test_extract_zip_safety() {
        let dir = tempdir().unwrap();
        let target_dir = dir.path().join("saves");

        // Buat zip sederhana dengan subfolder
        let zip_file = dir.path().join("test.zip");
        {
            let file = File::create(&zip_file).unwrap();
            let mut zip = zip::ZipWriter::new(file);
            let opts = zip::write::SimpleFileOptions::default();
            zip.start_file("Lembah_12345/SaveGameInfo", opts).unwrap();
            zip.write_all(b"<Farmer><name>Budi</name></Farmer>").unwrap();
            zip.finish().unwrap();
        }

        let res = SaveExtractor::extract_zip(&zip_file, &target_dir).unwrap();
        assert_eq!(res.files_count, 1);
        assert_eq!(res.extracted_folder_name, "Lembah_12345");
        assert!(target_dir.join("Lembah_12345/SaveGameInfo").exists());
    }

    #[test]
    fn test_extract_bare_zip_auto_wrap() {
        let dir = tempdir().unwrap();
        let target_dir = dir.path().join("saves");

        // Buat zip tanpa subfolder (file langsung di root)
        let zip_file = dir.path().join("Sunny_43210.zip");
        {
            let file = File::create(&zip_file).unwrap();
            let mut zip = zip::ZipWriter::new(file);
            let opts = zip::write::SimpleFileOptions::default();
            zip.start_file("SaveGameInfo", opts).unwrap();
            zip.write_all(b"<Farmer><name>Luffy</name></Farmer>").unwrap();
            zip.start_file("Sunny_43210", opts).unwrap();
            zip.write_all(b"<SaveGame><player><name>Luffy</name></player></SaveGame>").unwrap();
            zip.finish().unwrap();
        }

        let res = SaveExtractor::extract_zip(&zip_file, &target_dir).unwrap();
        assert_eq!(res.files_count, 2);
        assert_eq!(res.extracted_folder_name, "Sunny_43210");
        // Harus terbungkus rapi di dalam subfolder target_dir/Sunny_43210/
        assert!(target_dir.join("Sunny_43210/SaveGameInfo").exists());
        assert!(target_dir.join("Sunny_43210/Sunny_43210").exists());
    }
}
