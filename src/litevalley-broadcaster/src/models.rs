use serde::{Deserialize, Serialize};

/// Event delta entitas dunia (pergerakan pemain, update tanaman, alert anti-cheat, dll).
/// Mendukung format ringkas ("t", "loc", "id", "sp", "d", "ts") maupun format C#
/// PascalCase ("EventType", "LocationName", "EntityId", "TileX", "TileY") via Serde alias.
#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct WorldDeltaEvent {
    #[serde(
        alias = "EventType",
        alias = "eventType",
        alias = "event_type",
        rename = "t"
    )]
    pub event_type: String,

    #[serde(
        alias = "LocationName",
        alias = "locationName",
        alias = "location",
        alias = "Location",
        rename = "loc"
    )]
    pub location: String,

    #[serde(
        alias = "EntityId",
        alias = "entityId",
        alias = "entity_id",
        alias = "EntityID",
        rename = "id"
    )]
    pub entity_id: String,

    #[serde(alias = "TileX", alias = "tileX", alias = "tile_x", alias = "X")]
    pub x: i32,

    #[serde(alias = "TileY", alias = "tileY", alias = "tile_y", alias = "Y")]
    pub y: i32,

    #[serde(
        alias = "SpriteKey",
        alias = "spriteKey",
        alias = "sprite_key",
        rename = "sp",
        default,
        skip_serializing_if = "Option::is_none"
    )]
    pub sprite_key: Option<String>,

    #[serde(
        alias = "ExtraData",
        alias = "extraData",
        alias = "extra_data",
        rename = "d",
        default,
        skip_serializing_if = "Option::is_none"
    )]
    pub extra_data: Option<serde_json::Value>,

    #[serde(
        alias = "TimestampTicks",
        alias = "timestampTicks",
        alias = "timestamp_ms",
        rename = "ts",
        default = "default_timestamp"
    )]
    pub timestamp_ms: u64,
}

fn default_timestamp() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}

/// Status diagnostik & metrik performa broadcaster
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct BroadcasterStatus {
    pub status: String,
    pub uptime_seconds: u64,
    pub active_subscribers: usize,
    pub total_events_received: u64,
    pub total_events_broadcast: u64,
    pub memory_allocated_mb: f64,
}
