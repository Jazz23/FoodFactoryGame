// Server-only SQLite registry mapping a hashed client secret to a stable player ID (raw secrets are never stored), and each
// player's last pose, so rejoining starts where they left (schema v2, decision 0031), and each player's settings (schema v3:
// the wage warning of decision 0039).
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SQLite;

namespace FoodFactoryGame.Session
{
    public readonly struct PlayerResolution
    {
        public readonly bool Accepted;
        public readonly bool Created;
        public readonly string PlayerId;
        public readonly string Reason;

        private PlayerResolution(bool accepted, bool created, string playerId, string reason)
        {
            Accepted = accepted;
            Created = created;
            PlayerId = playerId;
            Reason = reason;
        }

        public static PlayerResolution Resolved(string playerId, bool created) => new(true, created, playerId, created ? "created" : "resolved");
        public static PlayerResolution Rejected(string reason) => new(false, false, null, reason);
    }

    public sealed class PlayerRegistry : IDisposable
    {
        public const int SchemaVersion = 3;
        // Decision 0039 PROTOTYPE default: warn when company cash covers less than this many game hours of wages.
        public const int DefaultWageWarningHours = 1;
        public const int MaxNameLength = 32;
        public const int MinSecretLength = 32;
        public const int MaxSecretLength = 128;

        private readonly SQLiteConnection _db;
        private readonly object _gate = new();

        public string DatabasePath { get; }

        // The caller supplies an explicit path so tests and tools never open the application's registry by default.
        public PlayerRegistry(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("Explicit registry path required.");
            DatabasePath = Path.GetFullPath(databasePath);
            if (!Directory.Exists(Path.GetDirectoryName(DatabasePath)))
                throw new DirectoryNotFoundException("Create the save directory before opening the player registry.");
            _db = new SQLiteConnection(DatabasePath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            try
            {
                // FULL sync: an admitted identity must survive a crash immediately after the reply.
                _db.ExecuteScalar<string>("PRAGMA synchronous = FULL");
                var version = _db.ExecuteScalar<int>("PRAGMA user_version");
                if (version > SchemaVersion) throw new NotSupportedException("Newer player registry schema.");
                if (version == 0)
                {
                    _db.RunInTransaction(() =>
                    {
                        _db.Execute("CREATE TABLE IF NOT EXISTS players ("
                            + "player_id TEXT PRIMARY KEY NOT NULL, "
                            + "display_name TEXT NOT NULL, "
                            + "secret_hash TEXT NOT NULL UNIQUE, "
                            + "created_utc INTEGER NOT NULL)");
                        CreatePoses();
                        CreateSettings();
                        _db.Execute($"PRAGMA user_version = {SchemaVersion}");
                    });
                }
                else if (version < SchemaVersion)
                {
                    // v1 -> v2: where each player last stood (decision 0031); players without a row spawn at the default point.
                    // v2 -> v3: player settings (decision 0039); players without a row use the defaults.
                    _db.RunInTransaction(() =>
                    {
                        if (version < 2) CreatePoses();
                        CreateSettings();
                        _db.Execute($"PRAGMA user_version = {SchemaVersion}");
                    });
                }
            }
            catch
            {
                _db.Dispose();
                throw;
            }
        }

        // A known secret resolves its existing ID (and refreshes the display name); a new secret creates one.
        // Any storage failure rejects rather than admitting a player without a durable identity.
        public PlayerResolution RegisterOrResolve(string displayName, string secret)
        {
            var name = displayName?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength || name.Any(char.IsControl))
                return PlayerResolution.Rejected("invalid-name");
            if (secret == null || secret.Length < MinSecretLength || secret.Length > MaxSecretLength)
                return PlayerResolution.Rejected("invalid-secret");
            var hash = HashSecret(secret);
            lock (_gate)
            {
                try
                {
                    string playerId = null;
                    var created = false;
                    _db.RunInTransaction(() =>
                    {
                        playerId = _db.ExecuteScalar<string>("SELECT player_id FROM players WHERE secret_hash = ?", hash);
                        if (playerId != null)
                        {
                            _db.Execute("UPDATE players SET display_name = ? WHERE player_id = ? AND display_name <> ?", name, playerId, name);
                            return;
                        }
                        playerId = "player-" + Guid.NewGuid().ToString("N");
                        created = true;
                        _db.Execute("INSERT INTO players (player_id, display_name, secret_hash, created_utc) VALUES (?, ?, ?, ?)",
                            playerId, name, hash, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    });
                    return PlayerResolution.Resolved(playerId, created);
                }
                catch (SQLiteException)
                {
                    return PlayerResolution.Rejected("persistence-unavailable");
                }
            }
        }

        // Read-only lookup so callers can refuse a join (e.g. already connected) before anything is written.
        public string FindBySecret(string secret)
        {
            if (secret == null || secret.Length < MinSecretLength || secret.Length > MaxSecretLength) return null;
            var hash = HashSecret(secret);
            lock (_gate)
            {
                try { return _db.ExecuteScalar<string>("SELECT player_id FROM players WHERE secret_hash = ?", hash); }
                catch (SQLiteException) { return null; }
            }
        }

        public string DisplayNameOf(string playerId)
        {
            lock (_gate) return _db.ExecuteScalar<string>("SELECT display_name FROM players WHERE player_id = ?", playerId);
        }

        private void CreatePoses() => _db.Execute("CREATE TABLE IF NOT EXISTS player_poses ("
            + "player_id TEXT PRIMARY KEY NOT NULL, x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, yaw REAL NOT NULL)");

        private void CreateSettings() => _db.Execute("CREATE TABLE IF NOT EXISTS player_settings ("
            + "player_id TEXT PRIMARY KEY NOT NULL, wage_warning_hours INTEGER NOT NULL)");

        // The player's wage warning (game hours of wages; decision 0039), or the default when none is saved or the store is
        // unavailable.
        public int WageWarningHoursOf(string playerId)
        {
            lock (_gate)
            {
                try
                {
                    var row = _db.Query<SettingsRow>("SELECT wage_warning_hours AS WageWarningHours FROM player_settings WHERE player_id = ?",
                        playerId).FirstOrDefault();
                    return row?.WageWarningHours ?? DefaultWageWarningHours;
                }
                catch (SQLiteException)
                {
                    return DefaultWageWarningHours;
                }
            }
        }

        // Saves the player's wage warning; a negative value is refused. Returns false when it is refused or the store is
        // unavailable.
        public bool SaveWageWarningHours(string playerId, int hours)
        {
            if (string.IsNullOrWhiteSpace(playerId) || hours < 0) return false;
            lock (_gate)
            {
                try
                {
                    _db.Execute("INSERT OR REPLACE INTO player_settings (player_id, wage_warning_hours) VALUES (?, ?)", playerId, hours);
                    return true;
                }
                catch (SQLiteException)
                {
                    return false;
                }
            }
        }

        private sealed class SettingsRow
        {
            public int WageWarningHours { get; set; }
        }

        // Where a player last stood (scene metres and degrees about up), saved when they leave or the server stops, so they
        // rejoin there (owner decision, 0031). Non-finite values are refused. Returns false when the store is unavailable.
        public bool SavePose(string playerId, float x, float y, float z, float yaw)
        {
            if (string.IsNullOrWhiteSpace(playerId) || !new[] { x, y, z, yaw }.All(v => !float.IsNaN(v) && !float.IsInfinity(v))) return false;
            lock (_gate)
            {
                try
                {
                    _db.Execute("INSERT OR REPLACE INTO player_poses (player_id, x, y, z, yaw) VALUES (?, ?, ?, ?, ?)",
                        playerId, x, y, z, yaw);
                    return true;
                }
                catch (SQLiteException)
                {
                    return false;
                }
            }
        }

        // The player's last saved pose, or null when none was saved (a new player) or the store is unavailable.
        public (float X, float Y, float Z, float Yaw)? PoseOf(string playerId)
        {
            lock (_gate)
            {
                try
                {
                    var row = _db.Query<PoseRow>("SELECT x AS X, y AS Y, z AS Z, yaw AS Yaw FROM player_poses WHERE player_id = ?", playerId).FirstOrDefault();
                    return row == null ? null : (row.X, row.Y, row.Z, row.Yaw);
                }
                catch (SQLiteException)
                {
                    return null;
                }
            }
        }

        private sealed class PoseRow
        {
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public float Yaw { get; set; }
        }

        public int Count
        {
            get { lock (_gate) return _db.ExecuteScalar<int>("SELECT COUNT(*) FROM players"); }
        }

        public void Dispose()
        {
            lock (_gate) _db.Dispose();
        }

        public static string HashSecret(string secret)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(secret));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes) builder.Append(value.ToString("x2"));
            return builder.ToString();
        }
    }
}
