// Stores a world's generated layout once, in the world_layout table of its world.db (decision 0026). The row is written when
// the world is created, before the first snapshot, and is never updated or regenerated: a later generator version cannot
// change an existing world. The payload is WorldLayoutText's canonical text with its SHA-256; a row that fails verification
// stops loading instead of being replaced. Databases from before world generation have no row and load with no layout.
using System;
using System.IO;
using FoodFactoryGame.World;

namespace FoodFactoryGame.Goods
{
    public sealed class StoredWorldLayout
    {
        public string WorldId;
        public string Sha256;
        // Canonical text, as stored and as replicated to clients.
        public string Text;
        public WorldLayout Layout;
    }

    public static class WorldLayoutStore
    {
        // Writes the layout of a world being created. Refuses a database that already holds a layout or any world snapshot,
        // so a layout can never be added to, or replace one in, an existing world.
        public static StoredWorldLayout Create(string path, string worldId, WorldLayout layout)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(worldId) || layout == null)
                throw new ArgumentException("Explicit save path, world ID and layout required.");
            var full = Path.GetFullPath(path);
            if (!Directory.Exists(Path.GetDirectoryName(full))) throw new DirectoryNotFoundException("Create an isolated save directory first.");
            var text = WorldLayoutText.Write(layout);
            // The stored text must read back to itself, or a later load would fail.
            if (WorldLayoutText.Write(WorldLayoutText.Read(text)) != text) throw new InvalidOperationException("World layout does not round-trip.");
            var sha = WorldLayoutText.Hash(text);
            using var db = GoodsSnapshotStore.Open(full, true);
            db.Execute("BEGIN IMMEDIATE");
            try
            {
                if (db.ExecuteScalar<int>("SELECT EXISTS (SELECT 1 FROM snapshots)") == 1)
                    throw new InvalidOperationException("This world already exists; a layout is only generated when a world is created.");
                if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM world_layout") != 0)
                    throw new InvalidOperationException("This world already has a layout; layouts are never replaced.");
                db.Execute("INSERT INTO world_layout (id, world_id, format_version, generator_version, requested_seed, seed, attempt, payload, sha256, created_utc) "
                    + "VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    worldId, layout.FormatVersion, layout.GeneratorVersion, layout.RequestedSeed ?? "", unchecked((long)layout.Seed), layout.Attempt,
                    text, sha, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                db.Execute("COMMIT");
            }
            catch
            {
                db.Execute("ROLLBACK");
                throw;
            }
            return new StoredWorldLayout { WorldId = worldId, Sha256 = sha, Text = text, Layout = WorldLayoutText.Read(text) };
        }

        // The stored layout, or null when there is no database, a database from before layouts, or no layout row.
        public static StoredWorldLayout Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Explicit save path required.");
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) return null;
            // Read only: an older (layout 1) database is left exactly as it is.
            using var db = GoodsSnapshotStore.Open(full, false);
            if (db.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'world_layout'") == 0) return null;
            var rows = db.Query<Row>("SELECT world_id AS WorldId, payload AS Payload, sha256 AS Sha256, generator_version AS GeneratorVersion FROM world_layout WHERE id = 1");
            if (rows.Count == 0) return null;
            var row = rows[0];
            if (string.IsNullOrEmpty(row.Payload) || WorldLayoutText.Hash(row.Payload) != row.Sha256)
                throw new InvalidOperationException("World layout checksum mismatch; the stored layout is kept and not regenerated.");
            var layout = WorldLayoutText.Read(row.Payload);
            if (layout.GeneratorVersion != row.GeneratorVersion) throw new InvalidOperationException("World layout generator version mismatch.");
            return new StoredWorldLayout { WorldId = row.WorldId, Sha256 = row.Sha256, Text = row.Payload, Layout = layout };
        }

        private sealed class Row
        {
            public string WorldId { get; set; }
            public string Payload { get; set; }
            public string Sha256 { get; set; }
            public int GeneratorVersion { get; set; }
        }
    }
}
