// Client-local prototype credential: a random secret created once per identity database and reused on every join.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SQLite;

namespace FoodFactoryGame.Session
{
    public static class ClientIdentity
    {
        public const int SchemaVersion = 1;

        // Reads the stored secret, or creates the SQLite database with a new 256-bit secret. It is not encrypted.
        // A pre-SQLite secret file at legacySecretPath is imported (and left in place) when the database is new, so an
        // existing client keeps its player identity.
        public static string LoadOrCreate(string path, string legacySecretPath = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Explicit identity path required.");
            var full = Path.GetFullPath(path);
            string imported = null;
            if (!File.Exists(full) && !string.IsNullOrWhiteSpace(legacySecretPath) && File.Exists(legacySecretPath))
                imported = Validated(File.ReadAllText(legacySecretPath, Encoding.UTF8).Trim(), legacySecretPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            using var db = new SQLiteConnection(full, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            db.BusyTimeout = TimeSpan.FromSeconds(5);
            db.ExecuteScalar<string>("PRAGMA synchronous = FULL");
            var version = db.ExecuteScalar<int>("PRAGMA user_version");
            if (version > SchemaVersion) throw new NotSupportedException("Newer client identity schema.");
            if (version == 0)
            {
                db.RunInTransaction(() =>
                {
                    db.Execute("CREATE TABLE IF NOT EXISTS identity (id INTEGER PRIMARY KEY CHECK (id = 1), secret TEXT NOT NULL)");
                    db.Execute($"PRAGMA user_version = {SchemaVersion}");
                });
            }
            // A concurrent first run may insert too; OR IGNORE keeps whichever landed first so both processes agree.
            db.Execute("INSERT OR IGNORE INTO identity (id, secret) VALUES (1, ?)", imported ?? NewSecret());
            return Validated(db.ExecuteScalar<string>("SELECT secret FROM identity WHERE id = 1"), full);
        }

        private static string NewSecret()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string Validated(string secret, string source)
        {
            if (secret == null || secret.Length < PlayerRegistry.MinSecretLength || secret.Length > PlayerRegistry.MaxSecretLength)
                throw new InvalidDataException($"Identity {source} is not a valid client secret.");
            return secret;
        }
    }
}
