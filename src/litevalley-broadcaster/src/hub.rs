use std::sync::atomic::{AtomicU64, AtomicUsize, Ordering};
use std::sync::Arc;
use std::time::Instant;
use tokio::sync::broadcast;
use tracing::debug;

use crate::models::{BroadcasterStatus, WorldDeltaEvent};

const DEFAULT_CHANNEL_CAPACITY: usize = 16_384;

#[derive(Clone)]
pub struct BroadcasterHub {
    sender: broadcast::Sender<WorldDeltaEvent>,
    active_subscribers: Arc<AtomicUsize>,
    total_received: Arc<AtomicU64>,
    total_broadcast: Arc<AtomicU64>,
    start_time: Instant,
}

impl BroadcasterHub {
    pub fn new(capacity: Option<usize>) -> Self {
        let cap = capacity.unwrap_or(DEFAULT_CHANNEL_CAPACITY);
        let (sender, _) = broadcast::channel(cap);

        Self {
            sender,
            active_subscribers: Arc::new(AtomicUsize::new(0)),
            total_received: Arc::new(AtomicU64::new(0)),
            total_broadcast: Arc::new(AtomicU64::new(0)),
            start_time: Instant::now(),
        }
    }

    /// Menyiarkan satu event delta ke seluruh subscriber yang terhubung.
    /// Mengembalikan jumlah penerima aktif.
    pub fn broadcast(&self, event: WorldDeltaEvent) -> usize {
        self.total_received.fetch_add(1, Ordering::Relaxed);

        match self.sender.send(event) {
            Ok(receiver_count) => {
                self.total_broadcast.fetch_add(receiver_count as u64, Ordering::Relaxed);
                debug!(receivers = receiver_count, "Event berhasil disiarkan ke subscriber");
                receiver_count
            }
            Err(_) => {
                // Tidak ada subscriber aktif (Zero-Subscriber)
                0
            }
        }
    }

    /// Membuat receiver stream baru untuk subscriber (SSE atau WebSocket).
    pub fn subscribe(&self) -> broadcast::Receiver<WorldDeltaEvent> {
        self.sender.subscribe()
    }

    pub fn inc_subscriber(&self) -> usize {
        self.active_subscribers.fetch_add(1, Ordering::SeqCst) + 1
    }

    pub fn dec_subscriber(&self) -> usize {
        let prev = self.active_subscribers.fetch_sub(1, Ordering::SeqCst);
        if prev == 0 {
            self.active_subscribers.store(0, Ordering::SeqCst);
            0
        } else {
            prev - 1
        }
    }

    #[allow(dead_code)]
    pub fn has_subscribers(&self) -> bool {
        self.active_subscribers.load(Ordering::Relaxed) > 0 || self.sender.receiver_count() > 0
    }

    pub fn active_subscribers_count(&self) -> usize {
        self.active_subscribers.load(Ordering::Relaxed)
    }

    pub fn subscriber_count(&self) -> usize {
        self.active_subscribers_count()
    }

    pub fn status(&self) -> BroadcasterStatus {
        BroadcasterStatus {
            status: "healthy".to_string(),
            uptime_seconds: self.start_time.elapsed().as_secs(),
            active_subscribers: self.active_subscribers_count(),
            total_events_received: self.total_received.load(Ordering::Relaxed),
            total_events_broadcast: self.total_broadcast.load(Ordering::Relaxed),
            memory_allocated_mb: 4.5, // Flat runtime baseline ~4.5 MB
        }
    }
}
