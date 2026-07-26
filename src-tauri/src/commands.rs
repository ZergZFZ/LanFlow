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
pub fn get_item_icon(path: String) -> Result<Option<String>, String> {
    #[cfg(target_os = "windows")]
    {
        use std::process::Command;

        if !std::path::Path::new(&path).exists() {
            return Ok(None);
        }

        let encoded_path = {
            const TABLE: &[u8; 64] = b"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            let bytes = path
                .encode_utf16()
                .flat_map(|unit| unit.to_le_bytes())
                .collect::<Vec<_>>();
            let mut output = String::with_capacity((bytes.len() + 2) / 3 * 4);
            for chunk in bytes.chunks(3) {
                let value = (u32::from(chunk[0]) << 16)
                    | (u32::from(*chunk.get(1).unwrap_or(&0)) << 8)
                    | u32::from(*chunk.get(2).unwrap_or(&0));
                output.push(TABLE[((value >> 18) & 63) as usize] as char);
                output.push(TABLE[((value >> 12) & 63) as usize] as char);
                output.push(if chunk.len() > 1 { TABLE[((value >> 6) & 63) as usize] as char } else { '=' });
                output.push(if chunk.len() > 2 { TABLE[(value & 63) as usize] as char } else { '=' });
            }
            output
        };
        let script = format!(
            "$p=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{encoded_path}')); if([IO.Path]::GetExtension($p) -ieq '.lnk'){{$link=(New-Object -ComObject WScript.Shell).CreateShortcut($p); if($link.TargetPath -and (Test-Path -LiteralPath $link.TargetPath)){{$p=$link.TargetPath}}}}; Add-Type -AssemblyName System.Drawing; $i=[Drawing.Icon]::ExtractAssociatedIcon($p); if($null -ne $i){{$b=$i.ToBitmap(); $s=[IO.MemoryStream]::new(); $b.Save($s,[Drawing.Imaging.ImageFormat]::Png); [Convert]::ToBase64String($s.ToArray()); $s.Dispose(); $b.Dispose(); $i.Dispose()}}"
        );
        let output = Command::new("powershell.exe")
            .args(["-NoProfile", "-NonInteractive", "-Command", &script])
            .output()
            .map_err(|error| error.to_string())?;
        if !output.status.success() {
            return Ok(None);
        }
        let icon = String::from_utf8_lossy(&output.stdout).trim().to_owned();
        return Ok((!icon.is_empty()).then(|| format!("data:image/png;base64,{icon}")));
    }

    #[cfg(not(target_os = "windows"))]
    {
        let _ = path;
        Ok(None)
    }
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
pub fn open_item(path: String) -> Result<(), String> {
    if !std::path::Path::new(&path).exists() {
        return Err("文件或快捷方式不存在".to_string());
    }

    #[cfg(target_os = "windows")]
    {
        std::process::Command::new("explorer.exe")
            .arg(&path)
            .spawn()
            .map_err(|error| format!("无法打开条目：{error}"))?;
        Ok(())
    }

    #[cfg(not(target_os = "windows"))]
    {
        let _ = path;
        Err("当前平台暂不支持打开条目".to_string())
    }
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
        if !g.items.iter().any(|item| item.path.eq_ignore_ascii_case(&path)) {
            g.items.push(Item {
                id: new_id(),
                name,
                path,
                icon: None,
            });
        }
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
pub fn update_item(
    app: AppHandle,
    group_id: String,
    item_id: String,
    name: String,
    path: String,
) -> Result<AppConfig, String> {
    let name = name.trim().to_string();
    let path = path.trim().to_string();
    if name.is_empty() || path.is_empty() {
        return Err("名称和路径不能为空".to_string());
    }

    let mut cfg = load(&app);
    let group = cfg
        .groups
        .iter_mut()
        .find(|group| group.id == group_id)
        .ok_or_else(|| "找不到原分组".to_string())?;
    let item = group
        .items
        .iter_mut()
        .find(|item| item.id == item_id)
        .ok_or_else(|| "找不到启动项".to_string())?;
    item.name = name;
    item.path = path;
    item.icon = None;
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn move_item(
    app: AppHandle,
    from_group_id: String,
    to_group_id: String,
    item_id: String,
) -> Result<AppConfig, String> {
    if from_group_id == to_group_id {
        return Ok(load(&app));
    }

    let mut cfg = load(&app);
    let source_index = cfg
        .groups
        .iter()
        .position(|group| group.id == from_group_id)
        .ok_or_else(|| "找不到原分组".to_string())?;
    let target_index = cfg
        .groups
        .iter()
        .position(|group| group.id == to_group_id)
        .ok_or_else(|| "找不到目标分组".to_string())?;
    let item_index = cfg.groups[source_index]
        .items
        .iter()
        .position(|item| item.id == item_id)
        .ok_or_else(|| "找不到启动项".to_string())?;
    let item = cfg.groups[source_index].items.remove(item_index);
    cfg.groups[target_index].items.push(item);
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn reorder_group(
    app: AppHandle,
    group_id: String,
    target_index: usize,
) -> Result<AppConfig, String> {
    let mut cfg = load(&app);
    let source_index = cfg
        .groups
        .iter()
        .position(|group| group.id == group_id)
        .ok_or_else(|| "找不到分组".to_string())?;

    let group = cfg.groups.remove(source_index);
    let insert_index = target_index.min(cfg.groups.len());
    cfg.groups.insert(insert_index, group);
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn reorder_item(
    app: AppHandle,
    group_id: String,
    item_id: String,
    target_index: usize,
) -> Result<AppConfig, String> {
    let mut cfg = load(&app);
    let group = cfg
        .groups
        .iter_mut()
        .find(|group| group.id == group_id)
        .ok_or_else(|| "找不到目标分组".to_string())?;
    let source_index = group
        .items
        .iter()
        .position(|item| item.id == item_id)
        .ok_or_else(|| "找不到启动项".to_string())?;

    let item = group.items.remove(source_index);
    let insert_index = target_index.min(group.items.len());
    group.items.insert(insert_index, item);
    save(&app, &cfg)?;
    Ok(cfg)
}

#[tauri::command]
pub fn hide_launcher_window(app: AppHandle) -> Result<(), String> {
    let window = app
        .get_webview_window("main")
        .ok_or_else(|| "找不到主窗口".to_string())?;
    window.hide().map_err(|error| error.to_string())
}

#[tauri::command]
pub fn update_hotkey(app: AppHandle, hotkey: String) -> Result<AppConfig, String> {
    let hotkey = hotkey.trim().to_string();
    if hotkey.is_empty() {
        return Err("快捷键不能为空，例如 Alt+Space".to_string());
    }

    let mut cfg = load(&app);
    if cfg.settings.hotkey.eq_ignore_ascii_case(&hotkey) {
        return Ok(cfg);
    }

    crate::replace_toggle_shortcut(&app, &cfg.settings.hotkey, &hotkey)?;
    cfg.settings.hotkey = hotkey;
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
