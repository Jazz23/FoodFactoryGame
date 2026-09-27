// Server step before the world save is loaded or created (decision 0026): returns the world's stored layout, or generates and
// stores one when this start creates the world. A save that already exists and has no layout (made before world generation,
// or in a scene that does not generate worlds) keeps having none; nothing is ever regenerated.
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    public static class WorldGeneration
    {
        public static StoredWorldLayout PrepareLayout(string worldPath, string legacyWorldPath, string worldId, string seedText)
        {
            var stored = WorldLayoutStore.Load(worldPath);
            if (stored != null)
            {
                if (!WorldSeed.IsBlank(seedText) && seedText.Trim() != stored.Layout.RequestedSeed)
                    Debug.Log($"[World] Seed '{seedText.Trim()}' ignored: this world already exists with seed '{stored.Layout.RequestedSeed}'.");
                return stored;
            }
            var legacy = !string.IsNullOrWhiteSpace(legacyWorldPath) && (File.Exists(legacyWorldPath) || File.Exists(legacyWorldPath + ".previous"));
            if (GoodsSnapshotStore.HasSnapshots(worldPath) || legacy)
            {
                Debug.Log("[World] This save was created before world generation; it has no generated layout.");
                return null;
            }
            var (requested, seed) = WorldSeed.Resolve(seedText);
            var result = WorldGenerator.Generate(requested, seed);
            foreach (var failed in result.Attempts.Where(x => x.Problems.Count > 0))
                Debug.LogWarning($"[World] Seed {failed.Seed} (attempt {failed.Attempt}) failed validation with {failed.Problems.Count} problem(s), "
                    + $"first: {failed.Problems[0]}; retrying with a derived seed.");
            stored = WorldLayoutStore.Create(worldPath, worldId, result.Layout);
            var layout = stored.Layout;
            Debug.Log($"[World] Generated world from seed '{requested}' (seed {layout.Seed}, attempt {layout.Attempt}, generator v{layout.GeneratorVersion}): "
                + $"{layout.Buildings.Count} buildings, {layout.Roads.Count} road segments, {layout.Rails.Count} rail lines; start {layout.StartRestaurantId}; "
                + $"sha256 {stored.Sha256}.");
            return stored;
        }
    }
}
