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

    // externalBin normally strips the target-triple suffix at bundle time, but check the
    // suffixed name too so a differently-staged bundle still resolves.
    let suffixed_name = format!("payroll-backend-{}{}", env!("TAURI_ENV_TARGET_TRIPLE"),
        if cfg!(windows) { ".exe" } else { "" });

    // The freshly-built .NET Debug output.
    let dev_path = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../..")
        .join("services/PayrollManager.Backend/bin/Debug/net8.0/win-x64")
        .join(exe_name);
    let dev_path = dev_path.canonicalize().unwrap_or(dev_path);

    // In a DEBUG (dev) build, always prefer the fresh Debug output. Tauri copies the
    // externalBin binary next to the dev executable, and that staged copy can be stale
    // (it is only republished on a full `tauri build`), which would otherwise shadow the
    // current build and reject newly-added commands.
    if cfg!(debug_assertions) && dev_path.exists() {
        return dev_path;
    }

    // Bundled locations (installed app): the resource dir and next to the app executable,
    // which is where `bundle.externalBin` places the sidecar.
    let mut candidates: Vec<PathBuf> = Vec::new();

    if let Ok(resource_dir) = app.path().resource_dir() {
        candidates.push(resource_dir.join(exe_name));
        candidates.push(resource_dir.join(&suffixed_name));
    }

    if let Ok(current_exe) = std::env::current_exe() {
        if let Some(dir) = current_exe.parent() {
            candidates.push(dir.join(exe_name));
            candidates.push(dir.join(&suffixed_name));
        }
    }

    for candidate in &candidates {
        if candidate.exists() {
            return candidate.clone();
        }
    }

    // Last resort: the dev path (surfaces a clear "not found" error if nothing exists).
    dev_path
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
