// Verifies which save folders the host menu offers as saved worlds and their order, using an isolated temporary saves directory.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class SavedWorldsTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "FoodFactorySavedWorldsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void World(string folder, string file, DateTime written)
        {
            var directory = Path.Combine(_root, folder);
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, file);
            File.WriteAllText(path, "");
            File.SetLastWriteTimeUtc(path, written);
        }

        [Test]
        public void ListsWorldSavesMostRecentlyPlayedFirst()
        {
            var day = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            World("old", SessionOptions.WorldFileName, day);
            World("recent", SessionOptions.WorldFileName, day.AddDays(2));
            World("legacy", SessionOptions.LegacyWorldFileName, day.AddDays(1));
            // A later write to any file (here the player registry) counts as playing it.
            World("old", SessionOptions.RegistryFileName, day.AddDays(3));

            var worlds = SessionOptions.SavedWorlds(_root);

            Assert.That(worlds.Select(x => x.Name), Is.EqualTo(new[] { "old", "recent", "legacy" }));
            Assert.That(worlds[0].LastPlayedUtc, Is.EqualTo(day.AddDays(3)));
        }

        [Test]
        public void SkipsFoldersWithoutAWorldSaveOrWithAnInvalidName()
        {
            Directory.CreateDirectory(Path.Combine(_root, "empty"));
            World("players-only", SessionOptions.RegistryFileName, DateTime.UtcNow);
            World("has space", SessionOptions.WorldFileName, DateTime.UtcNow);
            World("good_one", SessionOptions.WorldFileName, DateTime.UtcNow);

            Assert.That(SessionOptions.SavedWorlds(_root).Select(x => x.Name), Is.EqualTo(new[] { "good_one" }));
        }

        [Test]
        public void MissingSavesDirectoryHasNoWorlds()
        {
            Assert.That(SessionOptions.SavedWorlds(Path.Combine(_root, "absent")), Is.Empty);
        }
    }
}
