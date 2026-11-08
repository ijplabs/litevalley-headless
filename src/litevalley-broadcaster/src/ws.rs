use axum::extract::ws::{Message, WebSocket, WebSocketUpgrade};
use axum::extract::{Query, State};
use axum::response::IntoResponse;
use futures_util::{SinkExt, StreamExt};
use serde::{Deserialize, Serialize};
use tokio_stream::wrappers::BroadcastStream;
use tracing::{debug, info};

use crate::hub::BroadcasterHub;
use crate::sse::SseStreamQuery;

#[derive(Debug, Deserialize)]
#[serde(tag = "action")]
pub enum ClientWsMessage {
    #[serde(rename = "set_location")]
    SetLocation { location: String },
    #[serde(rename = "ping")]
    Ping,
}

#[derive(Debug, Serialize)]
#[serde(tag = "type")]
pub enum ServerWsMessage<'a> {
    #[serde(rename = "connected")]
    Connected {
        status: &'a str,
        location: &'a str,
        active_subscribers: usize,
    },
    #[serde(rename = "delta")]
    Delta { payload: &'a crate::models::WorldDeltaEvent },
    #[serde(rename = "pong")]
    Pong { ts: u64 },
}

pub async fn ws_handler(
    ws: WebSocketUpgrade,
    State(hub): State<BroadcasterHub>,
    Query(query): Query<SseStreamQuery>,
) -> impl IntoResponse {
    let initial_location = query.location.unwrap_or_else(|| "Farm".to_string());
    ws.on_upgrade(move |socket| handle_websocket(socket, hub, initial_location))
}

async fn handle_websocket(socket: WebSocket, hub: BroadcasterHub, initial_location: String) {
    let sub_count = hub.inc_subscriber();
    info!(location = %initial_location, subscribers = sub_count, "Klien web baru membuka WebSocket radar");

    let (mut sender, mut receiver) = socket.split();
    let mut current_location = initial_location.to_lowercase();

    // Kirim pesan connected
    let welcome = ServerWsMessage::Connected {
        status: "connected",
        location: &initial_location,
        active_subscribers: sub_count,
    };
    if let Ok(json) = serde_json::to_string(&welcome) {
        let _ = sender.send(Message::Text(json.into())).await;
    }

    let rx = hub.subscribe();
    let mut broadcast_stream = BroadcastStream::new(rx);

    // Loop seleksi pesan masuk dan keluar
    loop {
        tokio::select! {
            // Pesan dari browser client (perintah ganti lokasi atau ping)
            client_msg = receiver.next() => {
                match client_msg {
                    Some(Ok(Message::Text(text))) => {
                        if let Ok(action) = serde_json::from_str::<ClientWsMessage>(&text) {
                            match action {
                                ClientWsMessage::SetLocation { location } => {
                                    info!(new_location = %location, "Klien radar mengubah filter lokasi via WebSocket");
                                    current_location = location.to_lowercase();
                                }
                                ClientWsMessage::Ping => {
                                    let now = std::time::SystemTime::now()
                                        .duration_since(std::time::UNIX_EPOCH)
                                        .unwrap_or_default()
                                        .as_millis() as u64;
                                    let pong = ServerWsMessage::Pong { ts: now };
                                    if let Ok(json) = serde_json::to_string(&pong) {
                                        let _ = sender.send(Message::Text(json.into())).await;
                                    }
                                }
                            }
                        }
                    }
                    Some(Ok(Message::Close(_))) | None => {
                        break;
                    }
                    _ => {}
                }
            }

            // Event broadcast dari Game Server
            broadcast_msg = broadcast_stream.next() => {
                match broadcast_msg {
                    Some(Ok(delta)) => {
                        let matches_location = delta.location.to_lowercase() == current_location
                            || delta.event_type.starts_with("alert")
                            || delta.event_type.starts_with("global");

                        if matches_location {
                            let msg = ServerWsMessage::Delta { payload: &delta };
                            if let Ok(json) = serde_json::to_string(&msg) {
                                if sender.send(Message::Text(json.into())).await.is_err() {
                                    break;
                                }
                            }
                        }
                    }
                    Some(Err(tokio_stream::wrappers::errors::BroadcastStreamRecvError::Lagged(skipped))) => {
                        debug!(skipped, "Subscriber WebSocket tertinggal beberapa frame delta");
                    }
                    None => {
                        break;
                    }
                }
            }
        }
    }

    let remaining = hub.dec_subscriber();
    info!(remaining, "Klien web menutup WebSocket radar");
}
