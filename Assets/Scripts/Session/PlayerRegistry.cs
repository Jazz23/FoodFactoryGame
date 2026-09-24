// Server-only SQLite registry mapping a hashed client secret to a stable player ID; raw secrets are never stored.
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
        public const int SchemaVersion = 1;
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
