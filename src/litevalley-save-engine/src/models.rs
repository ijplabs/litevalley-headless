use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct InGameDateDto {
    pub season: String,
    pub day: u32,
    pub year: u32,
    pub display: String,
}

impl Default for InGameDateDto {
    fn default() -> Self {
        Self {
            season: "spring".to_string(),
            day: 1,
            year: 1,
            display: "Spring 1, Year 1".to_string(),
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct FarmerSummaryDto {
    pub name: String,
    pub money: i32,
    pub total_money_earned: u32,
    pub daily_luck: f64,
}

impl Default for FarmerSummaryDto {
    fn default() -> Self {
        Self {
            name: "Farmer".to_string(),
            money: 500,
            total_money_earned: 500,
            daily_luck: 0.0,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize, PartialEq)]
pub struct SaveMetadataDto {
    pub valid: bool,
    pub game_version: Option<String>,
    pub farm_name: String,
    pub farm_type: u8,
    pub farm_type_name: String,
    pub unique_id: String,
    pub in_game_date: InGameDateDto,
    pub host: FarmerSummaryDto,
    pub farmhands: Vec<FarmerSummaryDto>,
    pub cabins_count: usize,
    pub file_size_bytes: u64,
    pub parse_duration_ms: f64,
    pub source_type: String, // "directory", "zip", "xml_file"
}

pub fn map_farm_type_name(farm_type: u8) -> &'static str {
    match farm_type {
        0 => "Standard",
        1 => "Riverland",
        2 => "Forest",
        3 => "Hilltop",
        4 => "Wilderness",
        5 => "Four Corners",
        6 => "Beach",
        7 => "Meadowlands",
        _ => "Custom/Unknown",
    }
}
