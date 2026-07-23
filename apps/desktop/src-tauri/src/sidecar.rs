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

/// Where in the request lifecycle a call failed.
///
/// This distinction is what makes it safe to auto-restart the sidecar for a MUTATING command
/// (create/update). Retrying is only safe when the request was never delivered.
enum CallFailure {
    /// Failed before the request was fully written and flushed - spawn or write failure. The
    /// sidecar only acts on complete lines, so it did nothing; the command can be re-sent.
    BeforeSend(SidecarError),
    /// The request was written and flushed, then reading the response failed. The sidecar may
    /// already have applied the command (e.g. committed a create), so it must NOT be re-sent -
    /// doing so could create a duplicate employee or a duplicate audit entry.
    AfterSend(SidecarError),
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
    /// Recovery policy: if the request failed BEFORE it was delivered (the common
    /// "sidecar died while idle, first write fails" case), restart the sidecar and retry
    /// exactly once. If it failed AFTER delivery, the command may already have run, so do NOT
    /// retry - a mutating command must never be silently replayed. A single retry, never a
    /// loop, so a persistently broken sidecar cannot spin.
    pub fn call(&self, method: &str, params_json: Option<String>) -> Result<String, SidecarError> {
        match self.try_call(method, params_json.clone()) {
            Ok(response) => Ok(response),

            Err(CallFailure::BeforeSend(first)) => {
                // The request never reached the sidecar, so re-sending cannot duplicate a side
                // effect. Restart on a fresh process and try once more.
                self.drop_connection();
                eprintln!("[sidecar] restarting after pre-send error: {first}");

                match self.try_call(method, params_json) {
                    Ok(response) => Ok(response),
                    Err(CallFailure::BeforeSend(e)) | Err(CallFailure::AfterSend(e)) => Err(
                        SidecarError(format!("The payroll service is not responding: {e}")),
                    ),
                }
            }

            Err(CallFailure::AfterSend(e)) => {
                // Delivered but unconfirmed. It may have committed, so it is not retried.
                // Drop the dead connection so the next (separate) request starts fresh.
                self.drop_connection();
                Err(SidecarError(format!(
                    "The payroll service did not confirm the request, so it was not retried \
                     automatically (it may or may not have been applied): {e}"
                )))
            }
        }
    }

    fn try_call(&self, method: &str, params_json: Option<String>) -> Result<String, CallFailure> {
        let mut guard = self.connection.lock().unwrap();

        if guard.is_none() {
            *guard = Some(self.spawn().map_err(CallFailure::BeforeSend)?);
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

        // A write or flush failure means the line was not fully delivered. The sidecar reads
        // and acts on whole lines only, so an incomplete line is never processed - safe to
        // retry.
        writeln!(conn.stdin, "{request}")
            .and_then(|_| conn.stdin.flush())
            .map_err(|e| {
                CallFailure::BeforeSend(SidecarError(format!("Could not send the request: {e}")))
            })?;

        // Past this point the request has been delivered. Any failure now means the command
        // MAY have been applied, so these are AfterSend failures and are never auto-retried.
        let mut response = String::new();
        let bytes = conn.stdout.read_line(&mut response).map_err(|e| {
            CallFailure::AfterSend(SidecarError(format!("Could not read the response: {e}")))
        })?;

        if bytes == 0 {
            return Err(CallFailure::AfterSend(SidecarError(
                "The payroll service closed its output stream after receiving the request.".into(),
            )));
        }

        Ok(response.trim_end().to_string())
    }

    /// Kills and clears the current connection so the next call spawns a fresh process.
    fn drop_connection(&self) {
        let mut guard = self.connection.lock().unwrap();
        if let Some(mut conn) = guard.take() {
            let _ = conn.child.kill();
            let _ = conn.child.wait();
        }
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
