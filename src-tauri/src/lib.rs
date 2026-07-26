mod commands;
mod models;

use std::{
    str::FromStr,
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc,
    },
};

use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::TrayIconBuilder,
    AppHandle, Emitter, Manager, WindowEvent,
};
use tauri_plugin_autostart::MacosLauncher;
use tauri_plugin_global_shortcut::{GlobalShortcutExt, Shortcut, ShortcutState};

struct EditModeState(Arc<AtomicBool>);

#[tauri::command]
fn set_edit_mode(state: tauri::State<'_, EditModeState>, enabled: bool) {
    state.0.store(enabled, Ordering::Relaxed);
}

fn toggle_main_window(app: &AppHandle) {
    if let Some(w) = app.get_webview_window("main") {
        let is_minimized = w.is_minimized().unwrap_or(false);
        if is_minimized {
            let _ = w.unminimize();
            let _ = w.show();
            let _ = w.set_focus();
            let _ = w.emit("launcher-show", ());
        } else if w.is_visible().unwrap_or(false) {
            let _ = w.emit("launcher-hide-request", ());
        } else {
            let _ = w.show();
            let _ = w.set_focus();
            let _ = w.emit("launcher-show", ());
        }
    }
}

fn register_toggle_shortcut(app: &AppHandle, shortcut: Shortcut) -> Result<(), String> {
    app.global_shortcut()
        .on_shortcut(shortcut, |app, _sc, event| {
            if event.state == ShortcutState::Pressed {
                toggle_main_window(app);
            }
        })
        .map_err(|error| error.to_string())
}

pub(crate) fn replace_toggle_shortcut(
    app: &AppHandle,
    previous: &str,
    next: &str,
) -> Result<(), String> {
    let next_shortcut = Shortcut::from_str(next)
        .map_err(|error| format!("快捷键格式无效：{error}"))?;
    let previous_shortcut = Shortcut::from_str(previous)
        .map_err(|error| format!("现有快捷键格式无效：{error}"))?;

    app.global_shortcut()
        .unregister(previous_shortcut)
        .map_err(|error| format!("无法注销当前快捷键：{error}"))?;

    if let Err(error) = register_toggle_shortcut(app, next_shortcut) {
        let _ = register_toggle_shortcut(app, previous_shortcut);
        return Err(format!("无法注册快捷键，可能已被其他应用占用：{error}"));
    }

    Ok(())
}

pub fn run() {
    let edit_mode = Arc::new(AtomicBool::new(false));
    tauri::Builder::default()
        .manage(EditModeState(edit_mode.clone()))
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_autostart::init(
            MacosLauncher::LaunchAgent,
            Some(vec![]),
        ))
        .plugin(tauri_plugin_global_shortcut::Builder::new().build())
        .setup(|app| {
            let configured_hotkey = commands::load_config(app.handle().clone()).settings.hotkey;
            let shortcut = Shortcut::from_str(&configured_hotkey)
                .or_else(|_| Shortcut::from_str("Alt+Space"))
                .expect("默认快捷键必须有效");

            if let Err(error) = register_toggle_shortcut(app.handle(), shortcut) {
                eprintln!("警告：全局快捷键注册失败（可能被系统占用）：{error}");
            }

            if let Some(icon) = app.default_window_icon() {
                let show =
                    MenuItem::with_id(app, "show", "显示 / 隐藏", true, None::<&str>).unwrap();
                let sep = PredefinedMenuItem::separator(app).unwrap();
                let quit = MenuItem::with_id(app, "quit", "退出", true, None::<&str>).unwrap();
                let menu = Menu::with_items(app, &[&show, &sep, &quit]).unwrap();
                let _tray = TrayIconBuilder::with_id("lanflow-tray")
                    .icon(icon.clone())
                    .menu(&menu)
                    .on_menu_event(|app, event| match event.id.as_ref() {
                        "show" => toggle_main_window(app),
                        "quit" => app.exit(0),
                        _ => {}
                    })
                    .build(app)
                    .unwrap();
            }
            Ok(())
        })
        .on_window_event(move |window, event| match event {
            WindowEvent::CloseRequested { api, .. } => {
                api.prevent_close();
                if !edit_mode.load(Ordering::Relaxed) {
                    let _ = window.emit("launcher-hide-request", ());
                }
            }
            WindowEvent::Focused(false)
                if window.label() == "main" && !edit_mode.load(Ordering::Relaxed) =>
            {
                let _ = window.emit("launcher-hide-request", ());
            }
            _ => {}
        })
        .invoke_handler(tauri::generate_handler![
            commands::load_config,
            commands::save_config,
            commands::add_group,
            commands::remove_group,
            commands::rename_group,
            commands::add_item,
            commands::get_item_icon,
            commands::open_item,
            commands::hide_launcher_window,
            commands::remove_item,
            commands::update_item,
            commands::move_item,
            commands::reorder_group,
            commands::reorder_item,
            set_edit_mode,
            commands::update_hotkey,
            commands::update_settings,
            commands::search,
        ])
        .run(tauri::generate_context!())
        .expect("error while running LanFlow");
}
