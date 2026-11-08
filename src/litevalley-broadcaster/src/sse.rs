use axum::extract::{Query, State};
use axum::response::sse::{Event, KeepAlive, Sse};
use futures_util::stream::Stream;
use serde::Deserialize;
use std::convert::Infallible;
use std::sync::Arc;
use std::time::Duration;
use tokio_stream::wrappers::BroadcastStream;
use tokio_stream::StreamExt;
use tracing::{debug, info};

use crate::hub::BroadcasterHub;

#[derive(Debug, Deserialize)]
pub struct SseStreamQuery {
    pub location: Option<String>,
    pub include_all: Option<bool>,
}

struct SubGuard {
    hub: BroadcasterHub,
}

impl Drop for SubGuard {
    fn drop(&mut self) {
        let remaining = self.hub.dec_subscriber();
        info!(remaining, "Klien web menutup tab/koneksi SSE radar");
    }
}

/// Handler SSE (Server-Sent Events) untuk browser web:
/// GET /api/radar/stream?location=Farm
pub async fn sse_handler(
    State(hub): State<BroadcasterHub>,
    Query(query): Query<SseStreamQuery>,
) -> Sse<impl Stream<Item = Result<Event, Infallible>>> {
    let target_location = query.location.unwrap_or_else(|| "Farm".to_string());
    let include_all = query.include_all.unwrap_or(false);

    let sub_count = hub.inc_subscriber();
    info!(
        location = %target_location,
        subscribers = sub_count,
        "Klien web baru membuka stream SSE radar"
    );

    let receiver = hub.subscribe();
    let stream = BroadcastStream::new(receiver);

    let guard = Arc::new(SubGuard { hub: hub.clone() });
    let loc_filter = target_location.to_lowercase();

    // Stream pembuka (connected event)
    let init_json = serde_json::json!({
        "status": "connected",
        "location": target_location,
        "active_subscribers": sub_count,
    })
    .to_string();

    let init_event = Ok(Event::default().event("connected").data(init_json));

    // Stream penerusan event delta yang difilter
    let event_stream = stream.filter_map(move |result| {
        // Guard ditahan oleh closure ini; saat stream di-drop oleh client, guard di-drop.
        let _active_guard = &guard;

        match result {
            Ok(delta) => {
                let matches_location = include_all
                    || delta.location.to_lowercase() == loc_filter
                    || delta.event_type.starts_with("alert")
                    || delta.event_type.starts_with("global");

                if matches_location {
                    match serde_json::to_string(&delta) {
                        Ok(json) => Some(Ok(Event::default().event("delta").data(json))),
                        Err(_) => None,
                    }
                } else {
                    None
                }
            }
            Err(tokio_stream::wrappers::errors::BroadcastStreamRecvError::Lagged(skipped)) => {
                debug!(skipped, "Subscriber tertinggal beberapa frame delta, melompat ke posisi terbaru");
                None
            }
        }
    });

    // Gabungkan event init dengan stream event
    let combined_stream = futures_util::stream::once(async move { init_event })
        .chain(event_stream);

    Sse::new(combined_stream).keep_alive(
        KeepAlive::new()
            .interval(Duration::from_secs(15))
            .text("ping"),
    )
}
