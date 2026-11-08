use clap::{Parser, Subcommand};
use litevalley_save_engine::{CabinInjector, SaveArchiver, SaveExtractor, SaveInspector};
use std::path::PathBuf;
use std::process::ExitCode;

#[derive(Parser)]
#[command(
    name = "litevalley-save-engine",
    author = "LiteValley Cloud Team",
    version = "0.4.0",
    about = "Zero-allocation savegame inspector & chunked streaming backup engine for Stardew Valley 1.6",
    long_about = None
)]
struct Cli {
    #[command(subcommand)]
    command: Commands,
}

#[derive(Subcommand)]
enum Commands {
    /// Inspeksi metadata savegame (file .zip, folder kebun, atau SaveGameInfo XML)
    Inspect {
        /// Path ke file .zip atau folder savegame
        path: PathBuf,

        /// Tampilkan output dalam format JSON murni
        #[arg(long, short)]
        json: bool,
    },

    /// Ekspor & kompresi folder savegame ke arsip .zip streaming (Zero-LOH)
    Export {
        /// Folder direktori savegame yang ingin diarsipkan
        source: PathBuf,

        /// Path file .zip tujuan
        #[arg(long, short)]
        output: PathBuf,

        /// Tampilkan output dalam format JSON
        #[arg(long, short)]
        json: bool,
    },

    /// Impor arsip .zip savegame ke folder tujuan dengan proteksi ZipSlip
    Import {
        /// Path file .zip savegame sumber
        zip: PathBuf,

        /// Direktori induk target penyimpanan savegame (misal: /data/saves)
        #[arg(long, short)]
        target_dir: PathBuf,

        /// Tampilkan output dalam format JSON
        #[arg(long, short)]
        json: bool,
    },

    /// Validasi cepat integritas struktur savegame tanpa ekstraksi penuh
    Validate {
        /// Path ke file .zip atau folder savegame
        path: PathBuf,
    },

    /// Tambahkan / pastikan jumlah kabin minimal pada savegame (Auto-Provisioning Kabin Coop)
    AddCabins {
        /// Path ke file .zip atau folder direktori savegame
        path: PathBuf,

        /// Jumlah total kabin yang diinginkan (default: 3)
        #[arg(long, short, default_value_t = 3)]
        count: usize,

        /// Tampilkan output dalam format JSON
        #[arg(long, short)]
        json: bool,
    },
}

fn main() -> ExitCode {
    let cli = Cli::parse();

    match cli.command {
        Commands::Inspect { path, json } => {
            match SaveInspector::inspect_path(&path) {
                Ok(meta) => {
                    if json {
                        println!("{}", serde_json::to_string_pretty(&meta).unwrap());
                    } else {
                        println!("========================================================");
                        println!("  🌾 LITEVALLEY FAST SAVEGAME INSPECTOR (RUST CORE)");
                        println!("========================================================");
                        println!(" Kebun           : {}", meta.farm_name);
                        println!(" Tipe Lahan      : {} (ID: {})", meta.farm_type_name, meta.farm_type);
                        println!(" Host / Pemilik  : {}", meta.host.name);
                        println!(" Kas Petani      : {}g (Total: {}g)", meta.host.money, meta.host.total_money_earned);
                        println!(" Tanggal In-Game : {}", meta.in_game_date.display);
                        println!(" Unique Game ID  : {}", meta.unique_id);
                        println!(" Versi Game      : {}", meta.game_version.as_deref().unwrap_or("Unknown"));
                        println!(" Jumlah Kabin    : {}", meta.cabins_count);
                        println!(" Farmhands       : {} pemain terdaftar", meta.farmhands.len());
                        println!(" Sumber          : {} ({} bytes)", meta.source_type, meta.file_size_bytes);
                        println!(" Waktu Parse     : {:.2} ms (Zero-Allocation)", meta.parse_duration_ms);
                        println!("========================================================");
                    }
                    ExitCode::SUCCESS
                }
                Err(err) => {
                    eprintln!("❌ Gagal menginspeksi savegame: {}", err);
                    ExitCode::FAILURE
                }
            }
        }

        Commands::Export { source, output, json } => {
            match SaveArchiver::create_zip_archive(&source, &output) {
                Ok(res) => {
                    if json {
                        println!(
                            "{}",
                            serde_json::json!({
                                "success": true,
                                "outputPath": res.output_path,
                                "uncompressedBytes": res.uncompressed_bytes,
                                "compressedBytes": res.compressed_bytes,
                                "filesCount": res.files_count,
                                "durationMs": res.duration_ms
                            })
                        );
                    } else {
                        println!("✅ Arsip savegame berhasil dibuat!");
                        println!("   Output        : {}", res.output_path.display());
                        println!("   Ukuran Asli   : {:.2} MB", res.uncompressed_bytes as f64 / 1_048_576.0);
                        println!("   Ukuran Zip    : {:.2} MB", res.compressed_bytes as f64 / 1_048_576.0);
                        println!("   Jumlah File   : {}", res.files_count);
                        println!("   Waktu Kompres : {:.2} ms", res.duration_ms);
                    }
                    ExitCode::SUCCESS
                }
                Err(err) => {
                    eprintln!("❌ Gagal mengekspor arsip savegame: {}", err);
                    ExitCode::FAILURE
                }
            }
        }

        Commands::Import { zip, target_dir, json } => {
            match SaveExtractor::extract_zip(&zip, &target_dir) {
                Ok(res) => {
                    if json {
                        println!(
                            "{}",
                            serde_json::json!({
                                "success": true,
                                "targetDir": res.target_dir,
                                "extractedFolder": res.extracted_folder_name,
                                "filesCount": res.files_count,
                                "totalBytes": res.total_bytes,
                                "durationMs": res.duration_ms
                            })
                        );
                    } else {
                        println!("✅ Arsip savegame berhasil diekstrak dengan aman!");
                        println!("   Folder Target : {}", res.target_dir.display());
                        println!("   Folder Kebun  : {}", res.extracted_folder_name);
                        println!("   Jumlah File   : {}", res.files_count);
                        println!("   Total Ukuran  : {:.2} MB", res.total_bytes as f64 / 1_048_576.0);
                        println!("   Waktu Ekstrak : {:.2} ms", res.duration_ms);
                    }
                    ExitCode::SUCCESS
                }
                Err(err) => {
                    eprintln!("❌ Gagal mengekstrak arsip savegame: {}", err);
                    ExitCode::FAILURE
                }
            }
        }

        Commands::Validate { path } => {
            match SaveInspector::inspect_path(&path) {
                Ok(meta) => {
                    if meta.valid {
                        println!("✅ Savegame VALID: '{}' (ID: {}, Host: {})", meta.farm_name, meta.unique_id, meta.host.name);
                        ExitCode::SUCCESS
                    } else {
                        println!("⚠️ Savegame TIDAK LENGKAP atau corrupt.");
                        ExitCode::FAILURE
                    }
                }
                Err(err) => {
                    eprintln!("❌ Validasi GAGAL: {}", err);
                    ExitCode::FAILURE
                }
            }
        }

        Commands::AddCabins { path, count, json } => {
            match CabinInjector::ensure_cabins(&path, count) {
                Ok(res) => {
                    if json {
                        println!("{}", serde_json::to_string_pretty(&res).unwrap());
                    } else {
                        println!("✅ Injeksi kabin berhasil!");
                        println!("   Target Path     : {}", res.target_path);
                        println!("   Kabin Sebelumnya: {}", res.previous_cabins);
                        println!("   Kabin Ditambah  : {}", res.cabins_added);
                        println!("   Total Kabin Kini: {}", res.final_cabins);
                        println!("   Waktu Eksekusi  : {:.2} ms", res.duration_ms);
                    }
                    ExitCode::SUCCESS
                }
                Err(err) => {
                    eprintln!("❌ Gagal menambahkan kabin ke savegame: {}", err);
                    ExitCode::FAILURE
                }
            }
        }
    }
}
