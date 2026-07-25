mod commands;
mod models;

use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::TrayIconBuilder,
    Manager, WindowEvent,
};
use tauri_plugin_autostart::MacosLauncher;
use tauri_plugin_global_shortcut::{Code, Modifiers, Shortcut};

fn toggle_main_window(app: &tauri::AppHandle) {
    if let Some(w) = app.get_webview_window("main") {
        if w.is_visible().unwrap_or(false) {
            let _ = w.hide();
        } else {
            let _ = w.show();
            let _ = w.set_focus();
        }
    }
}

pub fn run() {
    let shortcut = Shortcut::new(Some(Modifiers::ALT), Code::Space);
    let sc_for_handler = shortcut.clone();

    tauri::Builder::default()
        .plugin(tauri_plugin_shell::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_autostart::init(
            MacosLauncher::LaunchAgent,
            Some(vec![]),
        ))
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_shortcuts([shortcut])
                .expect("global-shortcut with_shortcuts")
                .with_handler(move |app, sc, _event| {
                    if sc == &sc_for_handler {
                        toggle_main_window(app);
                    }
                })
                .build(),
        )
        .setup(|app| {
            // 系统托盘
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
        .on_window_event(|window, event| {
            // 关闭窗口改为隐藏，保持启动器常驻
            if let WindowEvent::CloseRequested { api, .. } = event {
                api.prevent_close();
                let _ = window.hide();
            }
        })
        .invoke_handler(tauri::generate_handler![
            commands::load_config,
            commands::save_config,
            commands::add_group,
            commands::remove_group,
            commands::rename_group,
            commands::add_item,
            commands::remove_item,
            commands::update_settings,
            commands::search,
        ])
        .run(tauri::generate_context!())
        .expect("error while running LanFlow");
}
