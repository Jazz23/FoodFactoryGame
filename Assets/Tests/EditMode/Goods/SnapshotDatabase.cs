// Test-only access to goods save databases: writes pre-SQLite snapshot files and reads, replaces or damages stored rows directly.
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SQLite;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    internal static class SnapshotDatabase
    {
        [Serializable] private sealed class LegacyEnvelope
        {
            public string Payload;
            public string Sha256;
        }

        // Same checksum as GoodsSnapshotStore: base64 SHA-256 of the UTF-8 payload.
        private static string Digest(string payload)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }

        public static void WriteLegacy(string path, string payload) => File.WriteAllText(path,
            JsonUtility.ToJson(new LegacyEnvelope { Payload = payload, Sha256 = Digest(payload) }), new UTF8Encoding(false));

        // Replaces the latest row with a hand-written payload, its checksum and its own schema version, e.g. an older-schema
        // world, so the row matches what a real commit of that payload would store. Save something to the database first.
        public static void WritePayload(string path, string payload)
        {
            var schema = JsonUtility.FromJson<GoodsSnapshot>(payload).SchemaVersion;
            using var db = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite);
            db.Execute("UPDATE snapshots SET payload = ?, sha256 = ?, schema_version = ? WHERE revision = (SELECT MAX(revision) FROM snapshots)",
                payload, Digest(payload), schema);
        }

        public static int LatestSchemaColumn(string path)
        {
            using var db = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly);
            return db.ExecuteScalar<int>("SELECT schema_version FROM snapshots ORDER BY revision DESC LIMIT 1");
        }

        public static string LatestPayload(string path)
        {
            using var db = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly);
            return db.ExecuteScalar<string>("SELECT payload FROM snapshots ORDER BY revision DESC LIMIT 1");
        }

        public static int Quarantined(string path)
        {
            using var db = new SQLiteConnection(path, SQLiteOpenFlags.ReadOnly);
            return db.ExecuteScalar<int>("SELECT COUNT(*) FROM quarantined_snapshots");
        }

        // Simulates a damaged latest commit; the checksum no longer matches, so recovery must use the previous row.
        public static void CorruptLatest(string path, string payload = "corrupt")
        {
            using var db = new SQLiteConnection(path, SQLiteOpenFlags.ReadWrite);
            db.Execute("UPDATE snapshots SET payload = ? WHERE revision = (SELECT MAX(revision) FROM snapshots)", payload);
        }
    }
}
