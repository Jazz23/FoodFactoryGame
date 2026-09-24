// Verifies SQLite identity resolution, join admission ordering and client secrets using isolated temporary saves only.
using System;
using System.IO;
using System.Linq;
using System.Text;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using NUnit.Framework;
using SQLite;
using UnityEditor;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class PlayerRegistryTests
    {
        private const string SecretA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string SecretB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private string _directory;
        private PlayerRegistry _registry;

        private string WorldPath => Path.Combine(_directory, SessionOptions.WorldFileName);
        private string RegistryPath => Path.Combine(_directory, SessionOptions.RegistryFileName);

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactorySessionTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            _registry?.Dispose();
            _registry = null;
            if (File.Exists(RegistryPath)) File.SetAttributes(RegistryPath, FileAttributes.Normal);
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        // The dev seed counts its goods in slots, so it needs the real item content (max stacks) just as the server has.
        private static ItemDefinition[] ContentItems() => AssetDatabase.FindAssets("t:ItemDefinition", new[] { "Assets/Content/Items" })
            .Select(x => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(x))).ToArray();

        private SessionAdmission Open(out GoodsWorld world)
        {
            world = DevWorld.LoadOrCreate(WorldPath, items: ContentItems());
            _registry = new PlayerRegistry(RegistryPath);
            return new SessionAdmission(_registry, world, DevWorld.SiteId, WorldPath);
        }

        [Test]
        public void NewSecretCreatesIdentityAndCommitsGrantBeforeAdmission()
        {
            var admission = Open(out var world);
            var result = admission.Admit("Alice", SecretA);
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Created, Is.True);
            Assert.That(result.PlayerId, Does.StartWith("player-"));
            Assert.That(_registry.Count, Is.EqualTo(1));
            Assert.That(world.CanView(result.PlayerId, DevWorld.SiteId), Is.True);
            Assert.That(GoodsSnapshotStore.Load(WorldPath).CanView(result.PlayerId, DevWorld.SiteId), Is.True, "Grant must be committed.");
        }

        [Test]
        public void SameSecretResolvesSameIdAfterReopeningStores()
        {
            var first = Open(out _).Admit("Alice", SecretA);
            _registry.Dispose();
            var reopened = Open(out var world);
            var revision = world.Snapshot().Revision;
            var second = reopened.Admit("Alice (renamed)", SecretA);
            Assert.That(second.Accepted, Is.True);
            Assert.That(second.Created, Is.False);
            Assert.That(second.PlayerId, Is.EqualTo(first.PlayerId));
            Assert.That(_registry.Count, Is.EqualTo(1));
            Assert.That(_registry.DisplayNameOf(first.PlayerId), Is.EqualTo("Alice (renamed)"));
            Assert.That(world.Snapshot().Revision, Is.EqualTo(revision), "An existing grant is not rewritten.");
        }

        [Test]
        public void DifferentSecretWithSameNameGetsDifferentId()
        {
            var admission = Open(out _);
            var first = admission.Admit("Alice", SecretA);
            var second = admission.Admit("Alice", SecretB);
            Assert.That(second.Accepted, Is.True);
            Assert.That(second.PlayerId, Is.Not.EqualTo(first.PlayerId));
            Assert.That(_registry.Count, Is.EqualTo(2));
        }

        [Test]
        public void DuplicateOfConnectedPlayerIsRejectedBeforeAnyWrite()
        {
            var admission = Open(out var world);
            var first = admission.Admit("Alice", SecretA);
            var revision = world.Snapshot().Revision;
            var duplicate = admission.Admit("Impostor", SecretA, player => player == first.PlayerId);
            Assert.That(duplicate.Accepted, Is.False);
            Assert.That(duplicate.Reason, Is.EqualTo("already-connected"));
            Assert.That(_registry.DisplayNameOf(first.PlayerId), Is.EqualTo("Alice"), "A rejected duplicate must not rename the player.");
            Assert.That(world.Snapshot().Revision, Is.EqualTo(revision));
            Assert.That(_registry.FindBySecret(SecretA), Is.EqualTo(first.PlayerId));
            Assert.That(_registry.FindBySecret(SecretB), Is.Null);
            Assert.That(_registry.Count, Is.EqualTo(1));
        }

        [TestCase("", SecretA, "invalid-name")]
        [TestCase("   ", SecretA, "invalid-name")]
        [TestCase("123456789012345678901234567890123", SecretA, "invalid-name")]
        [TestCase("tab\tname", SecretA, "invalid-name")]
        [TestCase("Alice", "", "invalid-secret")]
        [TestCase("Alice", "short-secret", "invalid-secret")]
        public void InvalidNameOrSecretIsRejectedWithoutWrites(string name, string secret, string reason)
        {
            var admission = Open(out var world);
            var revision = world.Snapshot().Revision;
            var result = admission.Admit(name, secret);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo(reason));
            Assert.That(_registry.Count, Is.Zero);
            Assert.That(world.Snapshot().Revision, Is.EqualTo(revision));
        }

        [Test]
        public void OversizedSecretIsRejected()
        {
            var result = Open(out _).Admit("Alice", new string('x', PlayerRegistry.MaxSecretLength + 1));
            Assert.That(result.Reason, Is.EqualTo("invalid-secret"));
        }

        [Test]
        public void FailedGrantCommitRejectsAndRetryReusesIdentity()
        {
            var world = DevWorld.LoadOrCreate(WorldPath, items: ContentItems());
            _registry = new PlayerRegistry(RegistryPath);
            var missingDirectory = Path.Combine(_directory, "missing", SessionOptions.WorldFileName);
            var failing = new SessionAdmission(_registry, world, DevWorld.SiteId, missingDirectory);
            var revision = world.Snapshot().Revision;
            var rejected = failing.Admit("Alice", SecretA);
            Assert.That(rejected.Accepted, Is.False);
            Assert.That(rejected.Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(world.Snapshot().Revision, Is.EqualTo(revision), "Grant must roll back.");
            Assert.That(world.Snapshot().Grants, Is.Empty);
            // The identity row is kept without privileges; the next successful join reuses it.
            Assert.That(_registry.Count, Is.EqualTo(1));
            var retried = new SessionAdmission(_registry, world, DevWorld.SiteId, WorldPath).Admit("Alice", SecretA);
            Assert.That(retried.Accepted, Is.True);
            Assert.That(retried.Created, Is.False);
            Assert.That(world.CanView(retried.PlayerId, DevWorld.SiteId), Is.True);
        }

        [Test]
        public void UnwritableRegistryRejectsNewPlayer()
        {
            Open(out _).Admit("Alice", SecretA);
            _registry.Dispose();
            File.SetAttributes(RegistryPath, FileAttributes.ReadOnly);
            var admission = Open(out var world);
            var result = admission.Admit("Bob", SecretB);
            Assert.That(result.Accepted, Is.False);
            Assert.That(result.Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(_registry.Count, Is.EqualTo(1));
            Assert.That(world.Snapshot().Grants.Count, Is.EqualTo(1));
        }

        [Test]
        public void SecretsAreNeverStoredOrExposed()
        {
            var admission = Open(out var world);
            var player = admission.Admit("Alice", SecretA).PlayerId;
            _registry.Dispose();
            _registry = null;
            var database = File.ReadAllBytes(RegistryPath);
            Assert.That(Encoding.ASCII.GetString(database), Does.Not.Contain(SecretA));
            Assert.That(Encoding.ASCII.GetString(database), Does.Contain(PlayerRegistry.HashSecret(SecretA)));
            var snapshot = JsonUtility.ToJson(world.Snapshot()) + File.ReadAllText(WorldPath);
            Assert.That(snapshot, Does.Not.Contain(SecretA));
            Assert.That(snapshot, Does.Not.Contain(PlayerRegistry.HashSecret(SecretA)));
            var view = world.View(player, DevWorld.SiteId);
            Assert.That(view.Grants, Is.Empty);
            Assert.That(JsonUtility.ToJson(view), Does.Not.Contain(PlayerRegistry.HashSecret(SecretA)));
        }

        [Test]
        public void NewerRegistrySchemaIsRefused()
        {
            new PlayerRegistry(RegistryPath).Dispose();
            using (var db = new SQLiteConnection(RegistryPath)) db.Execute("PRAGMA user_version = 99");
            Assert.Throws<NotSupportedException>(() => new PlayerRegistry(RegistryPath));
        }

        [Test]
        public void DurableGrantRejectsUnknownSiteWithoutWriting()
        {
            var world = DevWorld.LoadOrCreate(WorldPath, items: ContentItems());
            var revision = world.Snapshot().Revision;
            Assert.Throws<ArgumentException>(() => world.TryGrantDurably("player-x", "unknown-site", WorldPath));
            Assert.That(world.Snapshot().Revision, Is.EqualTo(revision));
            Assert.That(GoodsSnapshotStore.Load(WorldPath).Snapshot().Revision, Is.EqualTo(revision));
        }

        [Test]
        public void ClientIdentityIsCreatedOnceAndReused()
        {
            var path = Path.Combine(_directory, "Identity", "client.secret");
            var first = ClientIdentity.LoadOrCreate(path);
            Assert.That(first.Length, Is.InRange(PlayerRegistry.MinSecretLength, PlayerRegistry.MaxSecretLength));
            Assert.That(ClientIdentity.LoadOrCreate(path), Is.EqualTo(first));
            Assert.That(ClientIdentity.LoadOrCreate(Path.Combine(_directory, "other.secret")), Is.Not.EqualTo(first));
            File.WriteAllText(path, "corrupt");
            Assert.Throws<InvalidDataException>(() => ClientIdentity.LoadOrCreate(path));
        }

        [Test]
        public void CommandLineSelectsModeAndIsolatedPaths()
        {
            var options = SessionOptions.FromCommandLine(new[]
            {
                "game.exe", "-connect", "10.0.0.5", "-name", "Bob", "-save", _directory, "-identity", Path.Combine(_directory, "bob.secret")
            });
            Assert.That(options.Mode, Is.EqualTo(SessionMode.Client));
            Assert.That(options.Address, Is.EqualTo("10.0.0.5"));
            Assert.That(options.DisplayName, Is.EqualTo("Bob"));
            Assert.That(options.WorldPath, Is.EqualTo(Path.Combine(Path.GetFullPath(_directory), "world.snapshot")));
            Assert.That(options.RegistryPath, Is.EqualTo(Path.Combine(Path.GetFullPath(_directory), "players.db")));
            Assert.That(SessionOptions.FromCommandLine(new[] { "-host" }).Mode, Is.EqualTo(SessionMode.Host));
            Assert.That(SessionOptions.FromCommandLine(new[] { "-server" }).Mode, Is.EqualTo(SessionMode.Server));
            Assert.That(SessionOptions.FromCommandLine(Array.Empty<string>()).Mode, Is.EqualTo(SessionMode.None));
        }
    }
}
