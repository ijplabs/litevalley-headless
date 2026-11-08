use thiserror::Error;

#[derive(Error, Debug)]
pub enum SaveEngineError {
    #[error("I/O error: {0}")]
    Io(#[from] std::io::Error),

    #[error("XML parsing error: {0}")]
    Xml(#[from] quick_xml::Error),

    #[error("Zip archive error: {0}")]
    Zip(#[from] zip::result::ZipError),

    #[error("JSON serialization error: {0}")]
    Json(#[from] serde_json::Error),

    #[error("ZipSlip security violation: path '{0}' attempts to escape target directory")]
    ZipSlip(String),

    #[error("Invalid savegame format: {0}")]
    InvalidSave(String),

    #[error("Missing required metadata field: {0}")]
    MissingData(String),
}

pub type Result<T> = std::result::Result<T, SaveEngineError>;
