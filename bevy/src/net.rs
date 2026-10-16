// TCP transport for remote players: the same message channels as the host's in-process link, carried as newline-delimited
// JSON frames. Each socket gets a reader thread (frames to the server's inbound channel) and a writer thread (the
// connection's outbox to the socket); the game never blocks on socket I/O.
use std::io::{BufRead, BufReader, Read};
use std::net::{SocketAddr, TcpListener, TcpStream, ToSocketAddrs};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::mpsc::{self, Receiver, Sender};
use std::sync::Arc;
use std::time::Duration;

use crate::protocol::{write_frame, ClientMessage, ServerMessage};
use crate::server::Inbound;

/// Largest frame accepted (a big site baseline fits easily).
const MAX_FRAME: usize = 64 * 1024 * 1024;

pub struct Listener {
    pub address: SocketAddr,
    stop: Arc<AtomicBool>,
    thread: Option<std::thread::JoinHandle<()>>,
}

impl Listener {
    /// Accepts players on `address` and feeds their messages to the server.
    pub fn start(address: &str, inbound: Sender<Inbound>, ids: Arc<AtomicU64>) -> Result<Self, String> {
        let listener = TcpListener::bind(address).map_err(|e| format!("{address}: {e}"))?;
        let local = listener.local_addr().map_err(|e| e.to_string())?;
        listener.set_nonblocking(true).map_err(|e| e.to_string())?;
        let stop = Arc::new(AtomicBool::new(false));
        let flag = stop.clone();
        let thread = std::thread::Builder::new()
            .name("food-factory-listener".into())
            .spawn(move || {
                while !flag.load(Ordering::Acquire) {
                    match listener.accept() {
                        Ok((stream, _)) => {
                            let id = ids.fetch_add(1, Ordering::Relaxed);
                            serve(stream, id, inbound.clone());
                        }
                        Err(error) if error.kind() == std::io::ErrorKind::WouldBlock => std::thread::sleep(Duration::from_millis(20)),
                        Err(_) => std::thread::sleep(Duration::from_millis(50)),
                    }
                }
            })
            .map_err(|e| e.to_string())?;
        Ok(Self { address: local, stop, thread: Some(thread) })
    }
}

impl Drop for Listener {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Release);
        if let Some(thread) = self.thread.take() {
            let _ = thread.join();
        }
    }
}

fn serve(stream: TcpStream, id: u64, inbound: Sender<Inbound>) {
    let _ = stream.set_nodelay(true);
    let _ = stream.set_nonblocking(false);
    let Ok(mut writer) = stream.try_clone() else { return };
    let (outbox, messages) = mpsc::channel::<ServerMessage>();
    if inbound.send(Inbound::Open { connection: id, outbox }).is_err() {
        return;
    }
    std::thread::spawn(move || {
        while let Ok(message) = messages.recv() {
            if write_frame(&mut writer, &message).is_err() {
                break;
            }
        }
        let _ = writer.shutdown(std::net::Shutdown::Both);
    });
    std::thread::spawn(move || {
        let mut reader = BufReader::new(stream);
        let mut line = Vec::new();
        loop {
            line.clear();
            match reader.by_ref().take(MAX_FRAME as u64).read_until(b'\n', &mut line) {
                Ok(0) | Err(_) => break,
                Ok(_) => {}
            }
            let Ok(message) = serde_json::from_slice::<ClientMessage>(&line) else { break };
            if inbound.send(Inbound::Message { connection: id, message }).is_err() {
                break;
            }
        }
        let _ = inbound.send(Inbound::Closed { connection: id });
    });
}

/// Connects to a server; the channels behave like the host's local link. Connection failures arrive as a Rejected message.
pub fn connect(address: &str) -> (Sender<ClientMessage>, Receiver<ServerMessage>) {
    let (client_tx, client_rx) = mpsc::channel::<ClientMessage>();
    let (server_tx, server_rx) = mpsc::channel::<ServerMessage>();
    let address = address.to_string();
    std::thread::spawn(move || {
        let resolved: Vec<SocketAddr> = match address.to_socket_addrs() {
            Ok(list) => list.collect(),
            Err(error) => {
                let _ = server_tx.send(ServerMessage::Rejected { reason: format!("{address}: {error}") });
                return;
            }
        };
        let stream = resolved.iter().find_map(|a| TcpStream::connect_timeout(a, Duration::from_secs(4)).ok());
        let Some(stream) = stream else {
            let _ = server_tx.send(ServerMessage::Rejected { reason: format!("Could not reach {address}.") });
            return;
        };
        let _ = stream.set_nodelay(true);
        let Ok(mut writer) = stream.try_clone() else { return };
        std::thread::spawn(move || {
            while let Ok(message) = client_rx.recv() {
                if write_frame(&mut writer, &message).is_err() {
                    break;
                }
            }
            let _ = writer.shutdown(std::net::Shutdown::Both);
        });
        let mut reader = BufReader::new(stream);
        let mut line = Vec::new();
        loop {
            line.clear();
            match reader.by_ref().take(MAX_FRAME as u64).read_until(b'\n', &mut line) {
                Ok(0) | Err(_) => break,
                Ok(_) => {}
            }
            match serde_json::from_slice::<ServerMessage>(&line) {
                Ok(message) => {
                    if server_tx.send(message).is_err() {
                        break;
                    }
                }
                Err(_) => break,
            }
        }
        let _ = server_tx.send(ServerMessage::Rejected { reason: "Disconnected from the server.".into() });
    });
    (client_tx, server_rx)
}
