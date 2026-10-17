// SQLite storage for everything the game persists (decision 0011): the world save (versioned goods snapshots with checksums
// plus the write-once world layout), the player registry, the client identity and client settings. Each world commit is one
// transaction that keeps the newest row and the prior valid one, so a damaged latest payload falls back to the previous commit.
// Databases use WAL with FULL sync. Every path is explicit: nothing here discovers application data on its own.
use std::path::{Path, PathBuf};

use rusqlite::{params, Connection, OptionalExtension};
use sha2::{Digest, Sha256};

use crate::goods::{GoodsWorld, Snapshot, CURRENT_SCHEMA};
use crate::world::layout::WorldLayout;

pub const DATABASE_SCHEMA: i64 = 1;

fn checksum(payload: &[u8]) -> String { format!("{:x}", Sha256::digest(payload)) }

fn open(path: &Path) -> Result<Connection, String> {
    if let Some(parent) = path.parent().filter(|p| !p.as_os_str().is_empty()) {
        std::fs::create_dir_all(parent).map_err(|e| format!("{}: {e}", parent.display()))?;
    }
    let connection = Connection::open(path).map_err(|e| format!("{}: {e}", path.display()))?;
    connection.pragma_update(None, "journal_mode", "WAL").map_err(|e| e.to_string())?;
    connection.pragma_update(None, "synchronous", "FULL").map_err(|e| e.to_string())?;
    connection.busy_timeout(std::time::Duration::from_secs(5)).map_err(|e| e.to_string())?;
    Ok(connection)
}

/// A world's save: goods snapshots and its stored layout, held open while the world is served.
pub struct WorldStore {
    connection: Connection,
    pub path: PathBuf,
    last_saved: i64,
}

impl WorldStore {
    pub fn open(path: &Path) -> Result<Self, String> {
        let connection = open(path)?;
        let version: i64 = connection.pragma_query_value(None, "user_version", |r| r.get(0)).map_err(|e| e.to_string())?;
        if version > DATABASE_SCHEMA {
            return Err(format!("This save was made by a newer game (database layout {version})."));
        }
        connection
            .execute_batch(
                "CREATE TABLE IF NOT EXISTS snapshots(revision INTEGER PRIMARY KEY, world_id TEXT NOT NULL, payload BLOB NOT NULL, sha256 TEXT NOT NULL);
                 CREATE TABLE IF NOT EXISTS quarantined_snapshots(revision INTEGER, world_id TEXT, payload BLOB, sha256 TEXT);
                 CREATE TABLE IF NOT EXISTS world_layout(id INTEGER PRIMARY KEY CHECK (id = 1), world_id TEXT NOT NULL, payload BLOB NOT NULL, sha256 TEXT NOT NULL);
                 PRAGMA user_version = 1;",
            )
            .map_err(|e| e.to_string())?;
        Ok(Self { connection, path: path.to_path_buf(), last_saved: -1 })
    }

    pub fn has_snapshots(&self) -> bool {
        self.connection.query_row("SELECT COUNT(*) FROM snapshots", [], |r| r.get::<_, i64>(0)).map(|n| n > 0).unwrap_or(false)
    }

    /// The newest snapshot that verifies.
    pub fn load(&mut self) -> Result<Snapshot, String> {
        let rows: Vec<(i64, Vec<u8>, String)> = {
            let mut statement = self.connection.prepare("SELECT revision, payload, sha256 FROM snapshots ORDER BY revision DESC").map_err(|e| e.to_string())?;
            let rows = statement.query_map([], |r| Ok((r.get(0)?, r.get(1)?, r.get(2)?))).map_err(|e| e.to_string())?;
            rows.collect::<Result<_, _>>().map_err(|e| e.to_string())?
        };
        for (revision, payload, hash) in rows {
            if checksum(&payload) != hash {
                continue;
            }
            let Ok(snapshot) = serde_json::from_slice::<Snapshot>(&payload) else { continue };
            if snapshot.schema_version > CURRENT_SCHEMA {
                return Err(format!("This world was saved by a newer game (schema {}).", snapshot.schema_version));
            }
            if snapshot.revision != revision {
                continue;
            }
            self.last_saved = revision;
            return Ok(snapshot);
        }
        Err("No valid snapshot in this save.".into())
    }

    /// Commits the world's current state as a new revision (a no-op when that revision is already stored).
    pub fn save(&mut self, world: &GoodsWorld) -> Result<(), String> {
        world.validate()?;
        let state = &world.state;
        if state.revision == self.last_saved {
            return Ok(());
        }
        let payload = serde_json::to_vec(state).map_err(|e| e.to_string())?;
        let hash = checksum(&payload);
        let transaction = self.connection.transaction_with_behavior(rusqlite::TransactionBehavior::Immediate).map_err(|e| e.to_string())?;
        let latest: Option<i64> = transaction.query_row("SELECT MAX(revision) FROM snapshots", [], |r| r.get(0)).optional().map_err(|e| e.to_string())?.flatten();
        if latest.is_some_and(|l| l > state.revision) {
            return Err("stale-or-conflicting-save".into());
        }
        transaction.execute("INSERT OR REPLACE INTO snapshots(revision, world_id, payload, sha256) VALUES(?1, ?2, ?3, ?4)", params![state.revision, state.world_id, payload, hash]).map_err(|e| e.to_string())?;
        transaction.execute("DELETE FROM snapshots WHERE revision NOT IN (SELECT revision FROM snapshots ORDER BY revision DESC LIMIT 2)", []).map_err(|e| e.to_string())?;
        transaction.commit().map_err(|e| e.to_string())?;
        self.last_saved = state.revision;
        Ok(())
    }

    pub fn last_saved(&self) -> i64 { self.last_saved }

    /// The stored layout, if this world has one.
    pub fn load_layout(&self) -> Result<Option<WorldLayout>, String> {
        let row: Option<(Vec<u8>, String)> = self.connection.query_row("SELECT payload, sha256 FROM world_layout WHERE id = 1", [], |r| Ok((r.get(0)?, r.get(1)?))).optional().map_err(|e| e.to_string())?;
        let Some((payload, hash)) = row else { return Ok(None) };
        if checksum(&payload) != hash {
            return Err("The stored world layout is damaged.".into());
        }
        serde_json::from_slice(&payload).map(Some).map_err(|e| format!("world layout: {e}"))
    }

    /// Writes the layout once, before the world's first snapshot. Returns its checksum.
    pub fn create_layout(&self, world_id: &str, layout: &WorldLayout) -> Result<String, String> {
        let payload = serde_json::to_vec(layout).map_err(|e| e.to_string())?;
        let hash = checksum(&payload);
        self.connection.execute("INSERT INTO world_layout(id, world_id, payload, sha256) VALUES(1, ?1, ?2, ?3)", params![world_id, payload, hash]).map_err(|e| e.to_string())?;
        Ok(hash)
    }
}

/// A connected player's stable identity.
#[derive(Clone, Debug)]
pub struct Identity {
    pub player_id: String,
    pub display_name: String,
}

/// Server-only player registry: SHA-256(client secret) to a stable player ID, plus each player's last pose and settings.
pub struct PlayerRegistry {
    connection: Connection,
}

impl PlayerRegistry {
    pub fn open(path: &Path) -> Result<Self, String> {
        let connection = open(path)?;
        connection
            .execute_batch(
                "CREATE TABLE IF NOT EXISTS players(player_id TEXT PRIMARY KEY, secret_hash TEXT UNIQUE NOT NULL, display_name TEXT NOT NULL);
                 CREATE TABLE IF NOT EXISTS player_poses(player_id TEXT PRIMARY KEY, x REAL, y REAL, z REAL, yaw REAL);
                 CREATE TABLE IF NOT EXISTS player_settings(player_id TEXT PRIMARY KEY, wage_warning_cents INTEGER NOT NULL);
                 PRAGMA user_version = 3;",
            )
            .map_err(|e| e.to_string())?;
        Ok(Self { connection })
    }

    pub fn find_by_secret(&self, secret: &str) -> Option<Identity> {
        self.connection
            .query_row("SELECT player_id, display_name FROM players WHERE secret_hash = ?1", [checksum(secret.as_bytes())], |r| Ok(Identity { player_id: r.get(0)?, display_name: r.get(1)? }))
            .optional()
            .ok()
            .flatten()
    }

    /// Resolves a secret to its player, creating one; the display name follows the latest join.
    pub fn register_or_resolve(&self, name: &str, secret: &str) -> Result<Identity, &'static str> {
        let name = name.trim();
        if name.is_empty() || name.chars().count() > 32 || name.chars().any(char::is_control) {
            return Err("invalid-name");
        }
        if secret.len() < 16 {
            return Err("invalid-secret");
        }
        let hash = checksum(secret.as_bytes());
        if let Some(identity) = self.find_by_secret(secret) {
            let _ = self.connection.execute("UPDATE players SET display_name = ?1 WHERE player_id = ?2", params![name, identity.player_id]);
            return Ok(Identity { display_name: name.to_string(), ..identity });
        }
        let player_id = format!("player-{}", &checksum(hash.as_bytes())[..16]);
        self.connection.execute("INSERT INTO players(player_id, secret_hash, display_name) VALUES(?1, ?2, ?3)", params![player_id, hash, name]).map_err(|_| "persistence-unavailable")?;
        Ok(Identity { player_id, display_name: name.to_string() })
    }

    pub fn save_pose(&self, player: &str, pose: [f32; 4]) -> bool {
        self.connection
            .execute("INSERT OR REPLACE INTO player_poses(player_id, x, y, z, yaw) VALUES(?1, ?2, ?3, ?4, ?5)", params![player, pose[0], pose[1], pose[2], pose[3]])
            .is_ok()
    }

    pub fn pose_of(&self, player: &str) -> Option<[f32; 4]> {
        self.connection
            .query_row("SELECT x, y, z, yaw FROM player_poses WHERE player_id = ?1", [player], |r| Ok([r.get::<_, f64>(0)? as f32, r.get::<_, f64>(1)? as f32, r.get::<_, f64>(2)? as f32, r.get::<_, f64>(3)? as f32]))
            .optional()
            .ok()
            .flatten()
    }

    /// PROTOTYPE default: warn below $100 of wages.
    pub fn wage_warning(&self, player: &str) -> i64 {
        self.connection.query_row("SELECT wage_warning_cents FROM player_settings WHERE player_id = ?1", [player], |r| r.get(0)).optional().ok().flatten().unwrap_or(10_000)
    }

    pub fn set_wage_warning(&self, player: &str, cents: i64) -> bool {
        self.connection.execute("INSERT OR REPLACE INTO player_settings(player_id, wage_warning_cents) VALUES(?1, ?2)", params![player, cents.max(0)]).is_ok()
    }
}

/// The client's secret, kept in a one-row SQLite table so a client keeps its player ID across sessions.
pub fn client_secret(path: &Path) -> Result<String, String> {
    let connection = open(path)?;
    connection.execute_batch("CREATE TABLE IF NOT EXISTS identity(id INTEGER PRIMARY KEY CHECK (id = 1), secret TEXT NOT NULL);").map_err(|e| e.to_string())?;
    let fresh = {
        let mut bytes = [0u8; 32];
        let seed = std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).unwrap_or_default().as_nanos();
        let digest = Sha256::digest(format!("{seed}:{}:{:p}", std::process::id(), &bytes).as_bytes());
        bytes.copy_from_slice(&digest);
        bytes.iter().map(|b| format!("{b:02x}")).collect::<String>()
    };
    connection.execute("INSERT OR IGNORE INTO identity(id, secret) VALUES(1, ?1)", [fresh]).map_err(|e| e.to_string())?;
    connection.query_row("SELECT secret FROM identity WHERE id = 1", [], |r| r.get(0)).map_err(|e| e.to_string())
}

/// Client-local preferences in a key/value table (display name, sensitivity, volumes, bindings, resume target).
pub struct ClientSettings {
    connection: Option<Connection>,
}

impl ClientSettings {
    pub fn open(path: &Path) -> Self {
        let connection = open(path).ok().filter(|c| c.execute_batch("CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL); PRAGMA user_version = 1;").is_ok());
        Self { connection }
    }

    pub fn get(&self, key: &str) -> Option<String> {
        self.connection.as_ref()?.query_row("SELECT value FROM settings WHERE key = ?1", [key], |r| r.get(0)).optional().ok().flatten()
    }

    pub fn set(&self, key: &str, value: &str) {
        if let Some(connection) = &self.connection {
            let _ = connection.execute("INSERT OR REPLACE INTO settings(key, value) VALUES(?1, ?2)", params![key, value]);
        }
    }

    pub fn float(&self, key: &str, default: f32) -> f32 { self.get(key).and_then(|v| v.parse().ok()).unwrap_or(default) }
}

/// Where the game keeps its data: %APPDATA%\FoodFactoryBevy on Windows, ~/.local/share/FoodFactoryBevy elsewhere.
pub fn data_root() -> PathBuf {
    if let Ok(root) = std::env::var("FF_DATA_ROOT") {
        return PathBuf::from(root);
    }
    let base = std::env::var("APPDATA").map(PathBuf::from).or_else(|_| std::env::var("HOME").map(|h| PathBuf::from(h).join(".local/share"))).unwrap_or_else(|_| PathBuf::from("."));
    base.join("FoodFactoryBevy")
}

pub fn saves_root() -> PathBuf { data_root().join("Saves") }

/// Saved worlds (folder names), newest first.
pub fn saved_worlds() -> Vec<(String, std::time::SystemTime)> {
    let mut worlds: Vec<(String, std::time::SystemTime)> = std::fs::read_dir(saves_root())
        .into_iter()
        .flatten()
        .flatten()
        .filter(|e| e.path().join("world.db").exists())
        .map(|e| (e.file_name().to_string_lossy().to_string(), e.path().join("world.db").metadata().and_then(|m| m.modified()).unwrap_or(std::time::UNIX_EPOCH)))
        .collect();
    worlds.sort_by(|a, b| b.1.cmp(&a.1));
    worlds
}

/// A saved world's newest snapshot, read without writing to its database (the menu's world list); None when unreadable.
pub fn peek_snapshot(world: &str) -> Option<Snapshot> {
    let path = saves_root().join(world).join("world.db");
    let connection = Connection::open_with_flags(&path, rusqlite::OpenFlags::SQLITE_OPEN_READ_ONLY | rusqlite::OpenFlags::SQLITE_OPEN_NO_MUTEX).ok()?;
    let payload: Vec<u8> = connection.query_row("SELECT payload FROM snapshots ORDER BY revision DESC LIMIT 1", [], |r| r.get(0)).ok()?;
    serde_json::from_slice(&payload).ok()
}
