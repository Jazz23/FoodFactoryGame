// Commits verified versioned goods snapshots to a SQLite database in one transaction and recovers the newest valid one.
// The newest row and the prior valid row are kept, so a damaged latest payload falls back to the previous commit.
// Databases use WAL with FULL sync: one fsync per commit. A served world holds its connection open between commits,
// because closing the last connection checkpoints and deletes the WAL, which costs as much as the old journal.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SQLite;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    // Envelope of the pre-SQLite snapshot file; read only by ImportLegacy.
    [Serializable] internal sealed class GoodsEnvelope
    {
        public string Payload;
        public string Sha256;
    }

    public static class GoodsSnapshotStore
    {
        // Database layout version (PRAGMA user_version), separate from the snapshot payload's SchemaVersion.
        public const int DatabaseSchema = 1;

        private static readonly object SaveGate = new();

        // Connections kept open by Hold, keyed by full path; null after a failed commit until the next Save reopens it.
        // Guarded by SaveGate.
        private static readonly Dictionary<string, SQLiteConnection> Held = new(StringComparer.OrdinalIgnoreCase);

        // Cost of every commit in this process, from snapshot serialization to the end of the transaction.
        public static GoodsCommitStats Stats { get; } = new();

        // Keeps one connection to an existing database open for Save until Release. The database's files stay open
        // (and cannot be deleted) meanwhile. Holding an already held path does nothing.
        public static void Hold(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Explicit save path required.");
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException("No goods database.", full);
            lock (SaveGate)
            {
                if (!Held.ContainsKey(full)) Held[full] = Open(full, true);
            }
        }

        // Closes a held connection; the last close checkpoints the WAL into the database file. Unheld paths are ignored.
        public static void Release(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path);
            lock (SaveGate)
            {
                if (Held.Remove(full, out var db)) db?.Dispose();
            }
        }

        public static void Save(GoodsWorld world, string path)
        {
            if (world == null || string.IsNullOrWhiteSpace(path)) throw new ArgumentException("World and explicit save path required.");
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(Path.GetDirectoryName(full))) throw new DirectoryNotFoundException("Create an isolated save directory first.");
            var timer = Stopwatch.StartNew();
            var state = world.Snapshot();
            GoodsWorld.Validate(state);
            var payload = JsonUtility.ToJson(state);
            timer.Stop();
            var wrote = false;
            lock (SaveGate)
            {
                timer.Start();
                var held = Held.TryGetValue(full, out var db);
                // A held path whose connection was dropped after a failure reopens here.
                if (held && db == null) Held[full] = db = Open(full, true);
                using var opened = held ? null : Open(full, true);
                db ??= opened;
                try
                {
                    // IMMEDIATE takes the write lock before the revision check, so another process cannot commit in between.
                    // Waiting for another writer is contention, not commit cost, so it is left out of the measurement.
                    timer.Stop();
                    db.Execute("BEGIN IMMEDIATE");
                    timer.Start();
                    try
                    {
                        var prior = LatestValid(db, true);
                        if (prior != null)
                        {
                            if (prior.WorldId != state.WorldId || prior.Revision > state.Revision
                                || (prior.Revision == state.Revision && JsonUtility.ToJson(prior) != payload))
                                throw new IOException("Refusing a stale or conflicting world snapshot.");
                        }
                        if (prior == null || prior.Revision != state.Revision)
                        {
                            db.Execute("INSERT INTO snapshots (revision, world_id, schema_version, payload, sha256, saved_utc) VALUES (?, ?, ?, ?, ?, ?)",
                                state.Revision, state.WorldId, state.SchemaVersion, payload, Digest(payload), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                            if (prior != null) db.Execute("DELETE FROM snapshots WHERE revision < ?", prior.Revision);
                            wrote = true;
                        }
                        db.Execute("COMMIT");
                    }
                    catch
                    {
                        db.Execute("ROLLBACK");
                        throw;
                    }
                }
                catch when (held)
                {
                    // A held connection may be left mid-transaction if ROLLBACK failed; never reuse it after a failure.
                    Held[full] = null;
                    db.Dispose();
                    throw;
                }
            }
            // The save now holds this revision (written, or already identical).
            world.MarkCommitted(state.Revision);
            if (wrote) Stats.Record(timer.Elapsed.TotalMilliseconds, Encoding.UTF8.GetByteCount(payload));
        }

        public static GoodsWorld Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Explicit save path required.");
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException("No goods database.", full);
            // Opened read-write (never created) so SQLite can recover a WAL or roll back a hot journal left by a crash.
            using var db = Open(full, false);
            var world = GoodsWorld.Restore(LatestValid(db, false) ?? throw new InvalidOperationException("No valid goods snapshot."));
            world.MarkCommitted(world.Snapshot().Revision);
            return world;
        }

        // Copies a pre-SQLite snapshot file (or its .previous fallback) into a new database; the legacy files are left as
        // they are. A dry run validates and upgrades in memory and writes nothing. Refuses to touch an existing database.
        public static GoodsWorld ImportLegacy(string legacyPath, string databasePath, bool dryRun)
        {
            if (string.IsNullOrWhiteSpace(legacyPath) || string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Explicit legacy and database paths required.");
            var legacy = Path.GetFullPath(legacyPath);
            if (File.Exists(Path.GetFullPath(databasePath))) throw new IOException("The goods database already exists; nothing was imported.");
            GoodsSnapshot state;
            try { state = ReadLegacy(legacy); }
            catch (Exception error) when (error is IOException || error is InvalidOperationException || error is ArgumentException)
            {
                state = ReadLegacy(legacy + ".previous");
            }
            var world = GoodsWorld.Restore(state);
            if (!dryRun) Save(world, databasePath);
            return world;
        }

        private static SQLiteConnection Open(string full, bool create)
        {
            var flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.FullMutex | (create ? SQLiteOpenFlags.Create : 0);
            var db = new SQLiteConnection(full, flags);
            try
            {
                db.BusyTimeout = TimeSpan.FromSeconds(5);
                var version = db.ExecuteScalar<int>("PRAGMA user_version");
                if (version > DatabaseSchema) throw new NotSupportedException("Newer goods database schema.");
                if (!create)
                {
                    if (version == 0) throw new InvalidOperationException("Not an initialized goods database.");
                    return db;
                }
                // WAL is stored in the database file, so later opens (including Load) use it too. FULL sync: an
                // acknowledged mutation must survive a crash immediately after the reply.
                if (!string.Equals(db.ExecuteScalar<string>("PRAGMA journal_mode = WAL"), "wal", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The goods database could not switch to WAL.");
                db.ExecuteScalar<string>("PRAGMA synchronous = FULL");
                if (version == 0)
                {
                    db.RunInTransaction(() =>
                    {
                        db.Execute("CREATE TABLE IF NOT EXISTS snapshots ("
                            + "revision INTEGER PRIMARY KEY NOT NULL, "
                            + "world_id TEXT NOT NULL, "
                            + "schema_version INTEGER NOT NULL, "
                            + "payload TEXT NOT NULL, "
                            + "sha256 TEXT NOT NULL, "
                            + "saved_utc INTEGER NOT NULL)");
                        // Rows that fail verification are moved here, not deleted, so damage stays inspectable.
                        db.Execute("CREATE TABLE IF NOT EXISTS quarantined_snapshots ("
                            + "id INTEGER PRIMARY KEY AUTOINCREMENT, "
                            + "revision INTEGER NOT NULL, "
                            + "payload TEXT, "
                            + "sha256 TEXT, "
                            + "quarantined_utc INTEGER NOT NULL)");
                        db.Execute($"PRAGMA user_version = {DatabaseSchema}");
                    });
                }
                return db;
            }
            catch
            {
                db.Dispose();
                throw;
            }
        }

        // Newest row that verifies, or null for an empty database. When writable, unverifiable newer rows are quarantined
        // so the next commit cannot collide with them; a newer schema is never read as an older backup and always throws.
        private static GoodsSnapshot LatestValid(SQLiteConnection db, bool quarantine)
        {
            foreach (var revision in db.QueryScalars<long>("SELECT revision FROM snapshots ORDER BY revision DESC"))
            {
                var payload = db.ExecuteScalar<string>("SELECT payload FROM snapshots WHERE revision = ?", revision);
                var sha = db.ExecuteScalar<string>("SELECT sha256 FROM snapshots WHERE revision = ?", revision);
                try { return ReadValid(payload, sha); }
                catch (Exception error) when (error is InvalidOperationException || error is ArgumentException)
                {
                    if (!quarantine) continue;
                    db.Execute("INSERT INTO quarantined_snapshots (revision, payload, sha256, quarantined_utc) VALUES (?, ?, ?, ?)",
                        revision, payload, sha, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                    db.Execute("DELETE FROM snapshots WHERE revision = ?", revision);
                }
            }
            return null;
        }

        private static GoodsSnapshot ReadLegacy(string path)
        {
            var envelope = JsonUtility.FromJson<GoodsEnvelope>(File.ReadAllText(path, Encoding.UTF8));
            if (envelope == null) throw new InvalidOperationException("Goods snapshot checksum mismatch.");
            return ReadValid(envelope.Payload, envelope.Sha256);
        }

        private static GoodsSnapshot ReadValid(string payload, string sha)
        {
            if (string.IsNullOrEmpty(payload) || sha != Digest(payload))
                throw new InvalidOperationException("Goods snapshot checksum mismatch.");
            var state = JsonUtility.FromJson<GoodsSnapshot>(payload);
            // An unknown new schema is never interpreted as an older backup.
            if (state != null && state.SchemaVersion > GoodsSnapshot.CurrentSchema) throw new NotSupportedException("Newer goods snapshot schema.");
            // Older schemas are upgraded in memory and written as the current schema by the next commit.
            // v1 had no stations or jobs. v2 had no equipment or layouts; a v2 station without equipment then fails validation.
            if (state != null && state.SchemaVersion == 1)
            {
                state.Stations = new();
                state.Jobs = new();
                state.SchemaVersion = 2;
            }
            if (state != null && state.SchemaVersion == 2)
            {
                state.Equipment ??= new();
                state.SiteLayouts ??= new();
                state.SchemaVersion = 3;
            }
            // v3 had no belts; no lot rode one, so every BeltPosition reads 0.
            if (state != null && state.SchemaVersion == 3)
            {
                state.Belts ??= new();
                state.SchemaVersion = 4;
            }
            // v4 had no companies; the server's seed owner adds one before serving (DevWorld.EnsureCompany).
            if (state != null && state.SchemaVersion == 4)
            {
                state.Companies ??= new();
                state.SchemaVersion = 5;
            }
            // v5 had no sale jobs; every job's SaleCents reads 0 (a goods job). The version still changes so an older build
            // refuses a v6 save instead of quarantining its sale jobs as invalid.
            if (state != null && state.SchemaVersion == 5) state.SchemaVersion = 6;
            GoodsWorld.Validate(state);
            return state;
        }

        private static string Digest(string payload)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
    }
}
