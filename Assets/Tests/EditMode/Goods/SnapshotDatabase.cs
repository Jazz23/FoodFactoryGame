// Test-only access to goods save databases: writes pre-SQLite snapshot files and reads or damages stored rows directly.
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

        public static void WriteLegacy(string path, string payload)
        {
            using var sha = SHA256.Create();
            var digest = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            File.WriteAllText(path, JsonUtility.ToJson(new LegacyEnvelope { Payload = payload, Sha256 = digest }), new UTF8Encoding(false));
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
