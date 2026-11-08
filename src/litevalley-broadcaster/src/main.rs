mod hub;
mod ingest;
mod models;
mod sse;
mod ws;

use axum::extract::State;
use axum::http::StatusCode;
use axum::response::IntoResponse;
use axum::routing::{get, post};
use axum::{Json, Router};
use clap::Parser;
use std::net::SocketAddr;
use tower_http::cors::{Any, CorsLayer};
use tracing::{error, info};
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

use crate::hub::BroadcasterHub;
use crate::models::WorldDeltaEvent;

#[derive(Parser, Debug)]
#[command(name = "litevalley-broadcaster")]
#[command(author = "LiteValley Cloud Team")]
#[command(version = "0.1.0")]
#[command(about = "High-performance 2D radar and SSE/WebSocket fan-out broadcast hub", long_about = None)]
struct Args {
    /// Port TCP lokal untuk ingest stream dari Game Server C#
    #[arg(short, long, env = "INGEST_PORT", default_value_t = 4040)]
    ingest_port: u16,

    /// Port HTTP untuk SSE, WebSocket, dan Healthcheck web browser
    #[arg(short, long, env = "WEB_PORT", default_value_t = 4041)]
    web_port: u16,

    /// Alamat bind untuk Ingest TCP lokal
    #[arg(long, env = "INGEST_BIND", default_value = "127.0.0.1")]
    ingest_bind: String,

    /// Alamat bind untuk Web Server publik/kontainer
    #[arg(long, env = "WEB_BIND", default_value = "0.0.0.0")]
    web_bind: String,
}

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    // Inisialisasi Tracing / Logging
    tracing_subscriber::registry()
        .with(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "litevalley_broadcaster=info,tower_http=debug".into()),
        )
        .with(tracing_subscriber::fmt::layer())
        .init();

    let args = Args::parse();
    info!("🚀 Memulai LiteValley Broadcaster v0.1.0 (Rust Tokio Engine)");

    let hub = BroadcasterHub::new(None);

    // 1. Jalankan TCP Ingest Listener di background
    let ingest_addr: SocketAddr = format!("{}:{}", args.ingest_bind, args.ingest_port).parse()?;
    let hub_for_ingest = hub.clone();
    tokio::spawn(async move {
        if let Err(e) = ingest::run_tcp_ingest(ingest_addr, hub_for_ingest).await {
            error!(error = %e, "TCP Ingest server berhenti karena error fatal");
        }
    });

    // 2. Siapkan Axum HTTP Router untuk SSE, WebSocket, dan Healthcheck
    let cors = CorsLayer::new()
        .allow_origin(Any)
        .allow_methods(Any)
        .allow_headers(Any);

    let app = Router::new()
        .route("/health", get(health_handler))
        .route("/api/radar/status", get(status_handler))
        .route("/api/radar/stream", get(sse::sse_handler))
        .route("/api/radar/ws", get(ws::ws_handler))
        .route("/api/ingest/delta", post(http_ingest_handler))
        .layer(cors)
        .with_state(hub);

    let web_addr: SocketAddr = format!("{}:{}", args.web_bind, args.web_port).parse()?;
    info!(%web_addr, "Web SSE & WebSocket Fan-out Hub aktif - Melayani koneksi browser radar");

    let listener = tokio::net::TcpListener::bind(web_addr).await?;
    axum::serve(listener, app)
        .with_graceful_shutdown(shutdown_signal())
        .await?;

    info!("Broadcaster dimatikan dengan aman (Graceful Shutdown)");
    Ok(())
}

async fn health_handler(State(hub): State<BroadcasterHub>) -> impl IntoResponse {
    let status = hub.status();
    (StatusCode::OK, Json(status))
}

async fn status_handler(State(hub): State<BroadcasterHub>) -> impl IntoResponse {
    let status = hub.status();
    (StatusCode::OK, Json(status))
}

/// Fallback HTTP POST Ingest jika C# memilih mengirim delta via HTTP
async fn http_ingest_handler(
    State(hub): State<BroadcasterHub>,
    headers: axum::http::HeaderMap,
    Json(event): Json<WorldDeltaEvent>,
) -> impl IntoResponse {
    if let Ok(secret) = std::env::var("BROADCASTER_SECRET").or_else(|_| std::env::var("ADMIN_PASSWORD")) {
        let secret_trimmed = secret.trim();
        if !secret_trimmed.is_empty() {
            let expected = format!("Bearer {}", secret_trimmed);
            let auth_header = headers
                .get(axum::http::header::AUTHORIZATION)
                .and_then(|v| v.to_str().ok())
                .unwrap_or("");
            if auth_header != expected {
                return (
                    StatusCode::UNAUTHORIZED,
                    Json(serde_json::json!({ "error": "Unauthorized ingest request" })),
                );
            }
        }
    }

    let receivers = hub.broadcast(event);
    (StatusCode::ACCEPTED, Json(serde_json::json!({ "status": "broadcasted", "receivers": receivers })))
}

async fn shutdown_signal() {
    #[cfg(unix)]
    {
        use tokio::signal::unix::{signal, SignalKind};
        if let Ok(mut sigterm) = signal(SignalKind::terminate()) {
            tokio::select! {
                _ = tokio::signal::ctrl_c() => {
                    info!("Menerima sinyal SIGINT (CTRL+C), menghentikan proses broadcaster...");
                }
                _ = sigterm.recv() => {
                    info!("Menerima sinyal SIGTERM, menghentikan proses broadcaster...");
                }
            }
            return;
        }
    }
    tokio::signal::ctrl_c()
        .await
        .expect("Gagal menginstal handler sinyal CTRL+C");
    info!("Menerima sinyal shutdown (CTRL+C), menghentikan proses broadcaster...");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_hub_broadcast_and_subscribe() {
        let hub = BroadcasterHub::new(Some(100));
        let mut rx = hub.subscribe();

        let event = WorldDeltaEvent {
            event_type: "player_move".to_string(),
            location: "Farm".to_string(),
            entity_id: "farmer_1".to_string(),
            x: 42,
            y: 20,
            sprite_key: Some("farmer_down".to_string()),
            extra_data: None,
            timestamp_ms: 1000,
        };

        hub.broadcast(event.clone());

        let received = rx.recv().await.expect("Harus menerima event");
        assert_eq!(received.entity_id, "farmer_1");
        assert_eq!(received.x, 42);
        assert_eq!(received.y, 20);
        assert_eq!(received.location, "Farm");
    }

    #[tokio::test]
    async fn test_models_serde_alias_compatibility() {
        // Uji kompatibilitas format C# PascalCase/camelCase dengan format ringkas Rust
        let csharp_json = r#"{
            "EventType": "crop_watered",
            "LocationName": "Farm",
            "EntityId": "crop_10_10",
            "TileX": 10,
            "TileY": 10,
            "SpriteKey": "crop_parsnip",
            "ExtraData": {"watered": true},
            "TimestampTicks": 123456789
        }"#;

        let parsed: WorldDeltaEvent = serde_json::from_str(csharp_json).expect("Harus dapat mem-parse DTO C#");
        assert_eq!(parsed.event_type, "crop_watered");
        assert_eq!(parsed.location, "Farm");
        assert_eq!(parsed.entity_id, "crop_10_10");
        assert_eq!(parsed.x, 10);
        assert_eq!(parsed.y, 10);
    }

    #[tokio::test]
    async fn test_tcp_ingest_stream() {
        use tokio::io::AsyncWriteExt;
        use tokio::net::TcpStream;

        let hub = BroadcasterHub::new(Some(50));
        let mut rx = hub.subscribe();

        // Bind ke port 0 (ephemeral port)
        let listener = tokio::net::TcpListener::bind("127.0.0.1:0").await.unwrap();
        let port = listener.local_addr().unwrap().port();
        let hub_for_ingest = hub.clone();

        tokio::spawn(async move {
            if let Ok((stream, peer)) = listener.accept().await {
                ingest::handle_ingest_connection(stream, peer, hub_for_ingest).await;
            }
        });

        // Kirim NDJSON event dari client simulasi
        let mut client = TcpStream::connect(format!("127.0.0.1:{}", port)).await.unwrap();
        let payload = "{\"t\":\"alert\",\"loc\":\"Farm\",\"id\":\"cheat_1\",\"x\":12,\"y\":34}\n";
        client.write_all(payload.as_bytes()).await.unwrap();
        client.flush().await.unwrap();

        let received = rx.recv().await.expect("Harus menerima event dari TCP Ingest");
        assert_eq!(received.event_type, "alert");
        assert_eq!(received.entity_id, "cheat_1");
        assert_eq!(received.x, 12);
        assert_eq!(received.y, 34);
    }

    #[tokio::test]
    async fn test_status_metrics() {
        let hub = BroadcasterHub::new(Some(10));
        assert_eq!(hub.active_subscribers_count(), 0);

        hub.inc_subscriber();
        assert_eq!(hub.active_subscribers_count(), 1);

        hub.dec_subscriber();
        assert_eq!(hub.active_subscribers_count(), 0);

        let status = hub.status();
        assert_eq!(status.status, "healthy");
    }
}
