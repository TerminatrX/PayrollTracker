use std::io::{BufRead, BufReader, Write};
use std::path::PathBuf;
use std::process::{Child, ChildStdin, ChildStdout, Command, Stdio};
use std::sync::Mutex;

#[cfg(windows)]
use std::os::windows::process::CommandExt;

/// Hides the console window that would otherwise flash up when spawning the .NET sidecar
/// from a GUI application on Windows.
#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

/// A failure to reach the sidecar at all. Distinct from a business error, which the sidecar
/// itself returns inside a normal response envelope.
#[derive(Debug)]
pub struct SidecarError(pub String);

impl std::fmt::Display for SidecarError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}", self.0)
    }
}

impl std::error::Error for SidecarError {}

struct Connection {
    child: Child,
    stdin: ChildStdin,
    stdout: BufReader<ChildStdout>,
}

/// Owns the .NET payroll sidecar process and speaks newline-delimited JSON-RPC to it.
///
/// Requests are serialized behind a mutex: exactly one request is in flight at a time, so a
/// response line always belongs to the request that was just written. That trades a little
/// concurrency - these are local, sub-millisecond calls - for the removal of an entire class
/// of response-correlation bugs. A payroll app has no workload that needs pipelining.
pub struct SidecarClient {
    exe_path: PathBuf,
    data_dir: PathBuf,
    connection: Mutex<Option<Connection>>,
    request_counter: Mutex<u64>,
}

impl SidecarClient {
    pub fn new(exe_path: PathBuf, data_dir: PathBuf) -> Self {
        Self {
            exe_path,
            data_dir,
            connection: Mutex::new(None),
            request_counter: Mutex::new(0),
        }
    }

    fn next_id(&self) -> u64 {
        let mut counter = self.request_counter.lock().unwrap();
        *counter += 1;
        *counter
    }

    fn spawn(&self) -> Result<Connection, SidecarError> {
        if !self.exe_path.exists() {
            return Err(SidecarError(format!(
                "The payroll service executable was not found at {}. \
                 Build it with: dotnet build services/PayrollManager.Backend",
                self.exe_path.display()
            )));
        }

        let mut command = Command::new(&self.exe_path);
        command
            .arg("--data-dir")
            .arg(&self.data_dir)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            // stderr is inherited so the sidecar's logs land in the same console during
            // development. It must never be piped into stdout, which carries protocol only.
            .stderr(Stdio::inherit());

        #[cfg(windows)]
        command.creation_flags(CREATE_NO_WINDOW);

        let mut child = command
            .spawn()
            .map_err(|e| SidecarError(format!("Could not start the payroll service: {e}")))?;

        let stdin = child
            .stdin
            .take()
            .ok_or_else(|| SidecarError("The payroll service stdin was unavailable.".into()))?;

        let stdout = child
            .stdout
            .take()
            .ok_or_else(|| SidecarError("The payroll service stdout was unavailable.".into()))?;

        Ok(Connection {
            child,
            stdin,
            stdout: BufReader::new(stdout),
        })
    }

    /// Sends a command and returns the raw response envelope as a JSON string.
    ///
    /// If the sidecar has died, this restarts it and retries ONCE. A single retry recovers a
    /// crashed process without risking an unbounded restart loop, and - importantly - without
    /// re-sending a request that may already have been applied more than once.
    pub fn call(&self, method: &str, params_json: Option<String>) -> Result<String, SidecarError> {
        match self.try_call(method, params_json.clone()) {
            Ok(response) => Ok(response),
            Err(first_error) => {
                // Drop the dead connection so the retry spawns a fresh process.
                {
                    let mut guard = self.connection.lock().unwrap();
                    if let Some(mut conn) = guard.take() {
                        let _ = conn.child.kill();
                        let _ = conn.child.wait();
                    }
                }

                eprintln!("[sidecar] restarting after error: {first_error}");

                self.try_call(method, params_json).map_err(|retry_error| {
                    SidecarError(format!(
                        "The payroll service is not responding: {retry_error}"
                    ))
                })
            }
        }
    }

    fn try_call(&self, method: &str, params_json: Option<String>) -> Result<String, SidecarError> {
        let mut guard = self.connection.lock().unwrap();

        if guard.is_none() {
            *guard = Some(self.spawn()?);
        }

        let conn = guard.as_mut().expect("connection was just established");

        let id = self.next_id();

        // Build the request by hand so a caller-supplied params blob is embedded verbatim
        // rather than being re-encoded (which would double-escape it).
        let request = match &params_json {
            Some(params) => format!(
                r#"{{"id":"{id}","method":"{method}","params":{params}}}"#,
                id = id,
                method = method,
                params = params
            ),
            None => format!(r#"{{"id":"{id}","method":"{method}"}}"#, id = id, method = method),
        };

        writeln!(conn.stdin, "{request}")
            .and_then(|_| conn.stdin.flush())
            .map_err(|e| SidecarError(format!("Could not send the request: {e}")))?;

        let mut response = String::new();
        let bytes = conn
            .stdout
            .read_line(&mut response)
            .map_err(|e| SidecarError(format!("Could not read the response: {e}")))?;

        if bytes == 0 {
            return Err(SidecarError(
                "The payroll service closed its output stream.".into(),
            ));
        }

        Ok(response.trim_end().to_string())
    }

    /// Stops the sidecar. Called on window close so no orphan process is left behind.
    pub fn shutdown(&self) {
        let mut guard = self.connection.lock().unwrap();

        if let Some(mut conn) = guard.take() {
            // Closing stdin is the sidecar's documented shutdown signal: its read loop sees
            // EOF and exits cleanly, flushing anything in flight.
            drop(conn.stdin);
            let _ = conn.child.wait();
        }
    }
}
