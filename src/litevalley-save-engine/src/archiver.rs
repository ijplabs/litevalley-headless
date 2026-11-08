use crate::error::{Result, SaveEngineError};
use std::fs::File;
use std::io::{BufReader, BufWriter, Read, Write};
use std::path::{Path, PathBuf};
use walkdir::WalkDir;
use zip::write::SimpleFileOptions;
use zip::{CompressionMethod, ZipWriter};

pub struct SaveArchiver;

pub struct ArchiveResult {
    pub output_path: PathBuf,
    pub uncompressed_bytes: u64,
    pub compressed_bytes: u64,
    pub files_count: usize,
    pub duration_ms: f64,
}

impl SaveArchiver {
    /// Kompresi folder savegame ke dalam arsip .zip secara streaming dengan buffer 64KB (Zero-LOH)
    pub fn create_zip_archive(source_dir: &Path, output_zip_path: &Path) -> Result<ArchiveResult> {
        let start = std::time::Instant::now();

        if !source_dir.is_dir() {
            return Err(SaveEngineError::InvalidSave(format!(
                "Direktori sumber '{}' tidak ditemukan atau bukan folder.",
                source_dir.display()
            )));
        }

        // Pastikan parent directory tujuan ada
        if let Some(parent) = output_zip_path.parent() {
            std::fs::create_dir_all(parent)?;
        }

        let file = File::create(output_zip_path)?;
        let writer = BufWriter::with_capacity(64 * 1024, file);
        let mut zip = ZipWriter::new(writer);

        let options = SimpleFileOptions::default()
            .compression_method(CompressionMethod::Deflated)
            .unix_permissions(0o644);

        let mut uncompressed_total = 0u64;
        let mut files_count = 0usize;
        let mut chunk_buf = vec![0u8; 64 * 1024]; // 64 KB chunk konstan

        let dir_name = source_dir
            .file_name()
            .and_then(|s| s.to_str())
            .unwrap_or("Save");

        for entry in WalkDir::new(source_dir) {
            let entry = entry.map_err(|e| SaveEngineError::Io(e.into()))?;
            let path = entry.path();

            let rel_path = path
                .strip_prefix(source_dir)
                .unwrap_or(path)
                .to_string_lossy()
                .replace('\\', "/");

            let zip_entry_name = if rel_path.is_empty() {
                format!("{}/", dir_name)
            } else {
                format!("{}/{}", dir_name, rel_path)
            };

            if entry.file_type().is_dir() {
                if !rel_path.is_empty() {
                    zip.add_directory(format!("{}/", zip_entry_name.trim_end_matches('/')), options)?;
                }
            } else if entry.file_type().is_file() {
                zip.start_file(zip_entry_name, options)?;
                let mut reader = BufReader::new(File::open(path)?);

                loop {
                    let n = reader.read(&mut chunk_buf)?;
                    if n == 0 {
                        break;
                    }
                    zip.write_all(&chunk_buf[..n])?;
                    uncompressed_total += n as u64;
                }
                files_count += 1;
            }
        }

        let mut finished_writer = zip.finish()?;
        finished_writer.flush()?;

        let compressed_bytes = std::fs::metadata(output_zip_path)?.len();
        let duration_ms = start.elapsed().as_secs_f64() * 1000.0;

        Ok(ArchiveResult {
            output_path: output_zip_path.to_path_buf(),
            uncompressed_bytes: uncompressed_total,
            compressed_bytes,
            files_count,
            duration_ms,
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use tempfile::tempdir;

    #[test]
    fn test_create_zip_archive() {
        let dir = tempdir().unwrap();
        let save_folder = dir.path().join("TestFarm_12345");
        std::fs::create_dir(&save_folder).unwrap();

        let sgi = save_folder.join("SaveGameInfo");
        std::fs::write(&sgi, b"<Farmer><name>Budi</name></Farmer>").unwrap();

        let world = save_folder.join("TestFarm_12345");
        std::fs::write(&world, b"<SaveGame><player><name>Budi</name></player></SaveGame>").unwrap();

        let out_zip = dir.path().join("backup.zip");
        let result = SaveArchiver::create_zip_archive(&save_folder, &out_zip).unwrap();

        assert_eq!(result.files_count, 2);
        assert!(result.compressed_bytes > 0);
        assert!(out_zip.exists());
    }
}
