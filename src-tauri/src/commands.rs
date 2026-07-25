use std::fs;
use std::path::PathBuf;

use serde::Serialize;
use tauri::{AppHandle, Manager};

use crate::models::{AppConfig, Group, Item, Settings};

#[derive(Serialize)]
pub struct SearchResult {
    pub group_id: String,
    pub group_name: String,
    pub item: Item,
}

fn config_path(app: &AppHandle) -> Result<PathBuf, String> {
    let dir = app
        .path()
        .app_config_dir()
        .map_err(|e| e.to_string())?;
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    Ok(dir.join("config.json"))
}

fn load(app: &AppHandle) -> AppConfig {
    match config_path(app) {
        Ok(p) => fs::read_to_string(&p)
            .ok()
            .and_then(|s| serde_json::from_str(&s).ok())
            .unwrap_or_default(),
        Err(_) => AppConfig::default(),
    }
}

fn save(app: &AppHandle, cfg: &AppConfig) -> Result<(), String> {
    let p = config_path(app)?;
    let s = serde_json::to_string_pretty(cfg).map_err(|e| e.to_string())?;
    fs::write(&p, s).map_err(|e| e.to_string())
}

fn new_id() -> String {
    uuid::Uuid::new_v4().to_string()
}

#[tauri::command]
pub fn load_config(app: AppHandle) -> AppConfig {
    load(&app)
}

#[tauri::command]
pub fn save_config(app: AppHandle, config: AppConfig) -> Result<(), String> {
    save(&app, &config)
}

#[tauri::command]
pub fn add_group(app: AppHandle, name: String) -> Result<AppConfig, String> {
    let mut cfg = load(&app);
    cfg.groups.push(Group {
        id: new_id(),
        name,
        collapsed: false,
        items: vec![],
    });
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn remove_group(app: AppHandle, group_id: String) -> Result<AppConfig, String> {
    let mut cfg = load(&app);
    cfg.groups.retain(|g| g.id != group_id);
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn rename_group(
    app: AppHandle,
    group_id: String,
    name: String,
) -> Result<AppConfig, String> {
    let mut cfg = load(&app);
    if let Some(g) = cfg.groups.iter_mut().find(|g| g.id == group_id) {
        g.name = name;
    }
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn add_item(
    app: AppHandle,
    group_id: String,
    name: String,
    path: String,
) -> Result<AppConfig, String> {
    let mut cfg = load(&app);
    if let Some(g) = cfg.groups.iter_mut().find(|g| g.id == group_id) {
        g.items.push(Item {
            id: new_id(),
            name,
            path,
            icon: None,
        });
    }
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn remove_item(
    app: AppHandle,
    group_id: String,
    item_id: String,
) -> Result<AppConfig, String> {
    let mut cfg = load(&app);
    if let Some(g) = cfg.groups.iter_mut().find(|g| g.id == group_id) {
        g.items.retain(|i| i.id != item_id);
    }
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn update_settings(app: AppHandle, settings: Settings) -> Result<AppConfig, String> {
    let mut cfg = load(&app);
    cfg.settings = settings;
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn search(app: AppHandle, query: String) -> Vec<SearchResult> {
    let cfg = load(&app);
    let q = query.trim().to_lowercase();
    if q.is_empty() {
        return vec![];
    }
    let mut out = vec![];
    for g in &cfg.groups {
        for it in &g.items {
            if it.name.to_lowercase().contains(&q) || it.path.to_lowercase().contains(&q) {
                out.push(SearchResult {
                    group_id: g.id.clone(),
                    group_name: g.name.clone(),
                    item: it.clone(),
                });
            }
        }
    }
    out
}
