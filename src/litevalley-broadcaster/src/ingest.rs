use std::net::SocketAddr;
use tokio::io::{AsyncBufReadExt, AsyncReadExt, AsyncWriteExt, BufReader};
use tokio::net::{TcpListener, TcpStream};
use tracing::{error, info, warn};

use crate::hub::BroadcasterHub;
use crate::models::WorldDeltaEvent;

const MAX_LINE_BYTES: usize = 64 * 1024; // 64 KB per line max (DoS protection)

/// Menjalankan server TCP Ingest lokal untuk menerima streaming event dari C# StardewHeadless.Server.
pub async fn run_tcp_ingest(addr: SocketAddr, hub: BroadcasterHub) -> Result<(), std::io::Error> {
    let listener = TcpListener::bind(addr).await?;
    info!(%addr, "TCP Ingest Listener aktif - Siap menerima stream delta dari C# Game Server");

    loop {
        match listener.accept().await {
            Ok((stream, peer_addr)) => {
                let hub_clone = hub.clone();
                tokio::spawn(async move {
                    handle_ingest_connection(stream, peer_addr, hub_clone).await;
                });
            }
            Err(err) => {
                error!(error = %err, "Gagal menerima koneksi TCP Ingest");
            }
        }
    }
}

pub async fn handle_ingest_connection(stream: TcpStream, peer_addr: SocketAddr, hub: BroadcasterHub) {
    if let Err(e) = stream.set_nodelay(true) {
        warn!(error = %e, "Gagal set TCP_NODELAY pada koneksi ingest");
    }

    info!(%peer_addr, "Koneksi ingest baru tersambung dari Game Server C#");
    let (read_half, mut write_half) = stream.into_split();
    let mut reader = BufReader::new(read_half);
    let mut line_buf = String::with_capacity(1024);

    // Kirim jumlah subscriber awal ke C#
    let init_msg = format!("SUBS:{}\n", hub.subscriber_count());
    let _ = write_half.write_all(init_msg.as_bytes()).await;

    loop {
        line_buf.clear();
        match (&mut reader).take((MAX_LINE_BYTES + 1) as u64).read_line(&mut line_buf).await {
            Ok(0) => break, // EOF
            Ok(n) => {
                if n > MAX_LINE_BYTES {
                    warn!(%peer_addr, "Baris data melebihi 64KB, koneksi ditutup demi keamanan memori");
                    break;
                }
                let trimmed = line_buf.trim();
                if trimmed.is_empty() {
                    continue;
                }

                // Handle keepalive ping dari C# & balas dengan jumlah subscriber aktif
                if trimmed.eq_ignore_ascii_case("ping") {
                    let pong = format!("SUBS:{}\n", hub.subscriber_count());
                    if let Err(e) = write_half.write_all(pong.as_bytes()).await {
                        warn!(error = %e, "Gagal membalas SUBS ping ke C#");
                        break;
                    }
                    continue;
                }

                match serde_json::from_str::<WorldDeltaEvent>(trimmed) {
                    Ok(event) => {
                        hub.broadcast(event);
                    }
                    Err(err) => {
                        warn!(error = %err, raw_line = trimmed, "Format JSON delta tidak valid dari ingest stream");
                    }
                }
            }
            Err(err) => {
                warn!(error = %err, %peer_addr, "Gagal membaca line dari TCP ingest");
                break;
            }
        }
    }

    info!(%peer_addr, "Koneksi ingest dari Game Server terputus");
}
