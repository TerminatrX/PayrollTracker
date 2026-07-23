// Hides the console window on Windows release builds.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

mod sidecar;

use std::path::PathBuf;
use std::sync::Arc;

use tauri::{Manager, RunEvent};

use sidecar::SidecarClient;

struct AppState {
    sidecar: Arc<SidecarClient>,
}

/// The single bridge command exposed to the frontend.
///
/// `method` is forwarded to the sidecar, which only recognises the fixed set of business
/// commands it registered. An unknown name comes back as a structured `unknown_method` error
/// rather than doing anything - the frontend cannot name an executable, pass process
/// arguments, or express SQL.
#[tauri::command]
async fn backend_command(
    state: tauri::State<'_, AppState>,
    method: String,
    params_json: Option<String>,
) -> Result<String, String> {
    let sidecar = state.sidecar.clone();

    // The sidecar call is blocking I/O; keep it off the async runtime's worker threads.
    tauri::async_runtime::spawn_blocking(move || {
        sidecar
            .call(&method, params_json)
            .map_err(|e| e.to_string())
    })
    .await
    .map_err(|e| format!("The payroll service task failed: {e}"))?
}

/// Resolves the sidecar executable.
///
/// In development it is the .NET build output in the repository; in a bundled app it sits
/// beside the application executable as an external binary.
fn resolve_sidecar_path(app: &tauri::App) -> PathBuf {
    let exe_name = if cfg!(windows) {
        "payroll-backend.exe"
    } else {
        "payroll-backend"
    };

    // Bundled location first: alongside the app executable.
    if let Ok(resource_dir) = app.path().resource_dir() {
        let bundled = resource_dir.join(exe_name);
        if bundled.exists() {
            return bundled;
        }
    }

    if let Ok(current_exe) = std::env::current_exe() {
        if let Some(dir) = current_exe.parent() {
            let sibling = dir.join(exe_name);
            if sibling.exists() {
                return sibling;
            }
        }
    }

    // Development fallback: walk up from src-tauri to the repository root.
    let dev_path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../..")
        .join("services/PayrollManager.Backend/bin/Debug/net8.0/win-x64")
        .join(exe_name);

    dev_path
        .canonicalize()
        .unwrap_or(dev_path)
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .setup(|app| {
            let sidecar_path = resolve_sidecar_path(app);

            // The database lives under the OS per-user app-data directory, never inside the
            // install directory (read-only for standard users, replaced by upgrades).
            let data_dir = app
                .path()
                .app_local_data_dir()
                .expect("the platform must provide a local app data directory");

            std::fs::create_dir_all(&data_dir).ok();

            eprintln!("[tauri] sidecar: {}", sidecar_path.display());
            eprintln!("[tauri] data dir: {}", data_dir.display());

            app.manage(AppState {
                sidecar: Arc::new(SidecarClient::new(sidecar_path, data_dir)),
            });

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![backend_command])
        .build(tauri::generate_context!())
        .expect("failed to build the PayrollManager window")
        .run(|app_handle, event| {
            // Stop the sidecar on exit so no orphaned process keeps the database file locked.
            if let RunEvent::ExitRequested { .. } | RunEvent::Exit = event {
                if let Some(state) = app_handle.try_state::<AppState>() {
                    state.sidecar.shutdown();
                }
            }
        });
}
