// Verifies the server's world-generation step with the real dev seed (decision 0026), on isolated saves: a new world stores
// its layout before its first snapshot and later starts load it instead of regenerating (even with another seed), an existing
// dev save without a layout keeps none and is not changed, a blank seed is random but recorded, and -seed is read.
using System;
using System.IO;
using FoodFactoryGame.Goods;
using FoodFactoryGame.World;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class WorldGenerationTests
    {
        private string _directory;

        private string WorldPath => Path.Combine(_directory, SessionOptions.WorldFileName);
        private string LegacyPath => Path.Combine(_directory, SessionOptions.LegacyWorldFileName);

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactorySessionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private GoodsWorld OpenDevWorld() => DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems());

        [Test]
        public void ANewWorldStoresItsLayoutAndLaterStartsNeverRegenerateIt()
        {
            var created = WorldGeneration.PrepareLayout(WorldPath, LegacyPath, DevWorld.WorldId, "alpha");
            Assert.That(created.Layout.RequestedSeed, Is.EqualTo("alpha"));
            Assert.That(created.Layout.Seed, Is.EqualTo(WorldRandom.DeriveSeed(WorldSeed.FromText("alpha"), created.Layout.Attempt)));
            var world = OpenDevWorld();
            Assert.That(world.Snapshot().Buildings, Has.Some.Matches<GoodsBuilding>(x => x.Id == DevWorld.RestaurantId), "the dev seed still runs");

            var again = WorldGeneration.PrepareLayout(WorldPath, LegacyPath, DevWorld.WorldId, "beta");
            Assert.That((again.Sha256, again.Layout.RequestedSeed), Is.EqualTo((created.Sha256, "alpha")));
            Assert.That(JsonUtility.ToJson(OpenDevWorld().Snapshot()), Is.EqualTo(JsonUtility.ToJson(world.Snapshot())));
        }

        [Test]
        public void AnExistingSaveWithoutALayoutKeepsNone()
        {
            var saved = JsonUtility.ToJson(OpenDevWorld().Snapshot());
            Assert.That(WorldGeneration.PrepareLayout(WorldPath, LegacyPath, DevWorld.WorldId, "alpha"), Is.Null);
            Assert.That(WorldLayoutStore.Load(WorldPath), Is.Null);
            Assert.That(JsonUtility.ToJson(OpenDevWorld().Snapshot()), Is.EqualTo(saved));
        }

        [Test]
        public void ABlankSeedIsRandomAndRecorded()
        {
            var layout = WorldGeneration.PrepareLayout(WorldPath, LegacyPath, DevWorld.WorldId, "  ").Layout;
            var requested = ulong.Parse(layout.RequestedSeed);
            Assert.That(layout.Seed, Is.EqualTo(WorldRandom.DeriveSeed(requested, layout.Attempt)));
        }

        [Test]
        public void TheSeedSwitchIsRead()
        {
            Assert.That(SessionOptions.FromCommandLine(new[] { "-host", "-seed", "Sunny Valley" }).WorldSeed, Is.EqualTo("Sunny Valley"));
            Assert.That(SessionOptions.FromCommandLine(Array.Empty<string>()).WorldSeed, Is.Empty);
        }
    }
}
