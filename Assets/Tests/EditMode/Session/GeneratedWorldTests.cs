// Verifies generated worlds (decision 0028, piece 2) with the real generator and item content, on isolated saves: a new world
// creates the starting restaurant's site once, owned by its one company with the PROTOTYPE cash and no dev seed (customers and
// the starting counter and table: GeneratedWorldCustomersTests), and
// reloads without changing; layouts without lots, and a format 3 save made before piece 2, keep the dev world; joining grants
// the starting site with an inventory (the dough-only start kit, decision 0039) and every other site of the company without one;
// the starting cash covers a basic kit; an older world loads with re-tuned district rates and its cash; players spawn on the
// starting apron.
using System;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.World;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Session.Tests
{
    public sealed class GeneratedWorldTests
    {
        private string _directory;

        private string WorldPath => Path.Combine(_directory, SessionOptions.WorldFileName);
        private string LegacyPath => Path.Combine(_directory, SessionOptions.LegacyWorldFileName);

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FoodFactoryGeneratedWorldTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private StoredWorldLayout Layout() => WorldGeneration.PrepareLayout(WorldPath, LegacyPath, DevWorld.WorldId, "piece-two");

        private GoodsWorld Open(StoredWorldLayout stored) => GeneratedWorld.LoadOrCreate(WorldPath, stored, SessionTestFiles.ContentItems());

        private string LatestPayload()
        {
            using var db = new SQLite.SQLiteConnection(WorldPath, SQLite.SQLiteOpenFlags.ReadOnly);
            return db.ExecuteScalar<string>("SELECT payload FROM snapshots ORDER BY revision DESC LIMIT 1");
        }

        // TEST-ONLY: replaces the latest row with an older-schema payload and its checksum (base64 SHA-256, as GoodsSnapshotStore).
        private void ReplaceLatestPayload(string payload)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var digest = Convert.ToBase64String(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload)));
            using var db = new SQLite.SQLiteConnection(WorldPath, SQLite.SQLiteOpenFlags.ReadWrite);
            db.Execute("UPDATE snapshots SET payload = ?, sha256 = ?, schema_version = ? WHERE revision = (SELECT MAX(revision) FROM snapshots)",
                payload, digest, JsonUtility.FromJson<GoodsSnapshot>(payload).SchemaVersion);
        }

        [Test]
        public void ANewWorldCreatesTheStartingSiteOnceAndReloadsUnchanged()
        {
            var stored = Layout();
            Assert.That(GeneratedWorld.Supports(stored), Is.True);
            var start = GeneratedWorld.StartOffer(stored.Layout);
            Assert.That((start.BuildingId, start.ForSale), Is.EqualTo((stored.Layout.StartRestaurantId, false)));

            var created = Open(stored).Snapshot();
            Assert.That(created.Companies.Select(x => (x.Id, x.Cash, string.Join(",", x.SiteIds))),
                Is.EqualTo(new[] { (GeneratedWorld.CompanyId, GeneratedWorld.StartingCash, start.SiteId) }));
            Assert.That(created.Properties.Select(x => (x.LotId, x.SiteId, x.CompanyId)),
                Is.EqualTo(new[] { (start.LotId, start.SiteId, GeneratedWorld.CompanyId) }));
            Assert.That(created.Sites.Single().Id, Is.EqualTo(start.SiteId));
            Assert.That(created.SiteLayouts.Select(x => (x.SiteId, x.Width, x.Depth)), Is.EqualTo(new[] { (start.SiteId, start.Width, start.Depth) }));
            Assert.That(created.Buildings.Select(x => (x.Id, x.Kind, x.SiteId)), Is.EqualTo(new[] { (start.BuildingId, GoodsWorld.RestaurantKind, start.SiteId) }));
            Assert.That((created.Locations.Count, created.Lots.Count, created.Equipment.Count, created.Belts.Count, created.Employees.Count,
                created.Trucks.Count, created.Grants.Count), Is.EqualTo((0, 0, 0, 0, 0, 0, 0)),
                "Nothing of the dev seed (no counter or table definition is passed here).");
            Assert.That(created.Districts.Select(x => x.Id), Has.None.EqualTo(DevWorld.DistrictId), "Map districts, not the dev one.");
            var committed = JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot());
            Assert.That(committed, Is.EqualTo(JsonUtility.ToJson(created)), "Committed before serving.");

            var again = Layout();
            Assert.That(again.Sha256, Is.EqualTo(stored.Sha256));
            Assert.That(JsonUtility.ToJson(Open(again).Snapshot()), Is.EqualTo(committed));
            Assert.That(JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot()), Is.EqualTo(committed), "Reloading writes nothing.");
        }

        [Test]
        public void LayoutsWithoutLotsAndOlderSavesKeepTheDevWorld()
        {
            var stored = Layout();
            var older = new StoredWorldLayout { WorldId = stored.WorldId, Sha256 = stored.Sha256, Text = stored.Text, Layout = WorldLayoutText.Read(stored.Text) };
            older.Layout.FormatVersion = 2;
            older.Layout.Lots.Clear();
            Assert.That((GeneratedWorld.Supports(older), GeneratedWorld.Supports(null)), Is.EqualTo((false, false)));
            Assert.Throws<ArgumentException>(() => GeneratedWorld.LoadOrCreate(WorldPath, older));

            // A format 3 world first opened by piece 1 holds the dev world: it is left alone.
            var dev = JsonUtility.ToJson(DevWorld.LoadOrCreate(WorldPath, items: SessionTestFiles.ContentItems()).Snapshot());
            Assert.That(Open(stored), Is.Null);
            Assert.That(JsonUtility.ToJson(GoodsSnapshotStore.Load(WorldPath).Snapshot()), Is.EqualTo(dev));
        }

        [Test]
        public void JoiningGrantsTheStartingSiteAndEveryCompanySite()
        {
            var stored = Layout();
            var world = Open(stored);
            var start = GeneratedWorld.StartOffer(stored.Layout);
            // TEST-ONLY: a second lot given to the company directly, standing in for an earlier purchase.
            var other = WorldLayoutShells.PropertyOffers(stored.Layout).First(x => x.ForSale && x.Category == GoodsWorld.FactoryKind);
            world.Bootstrap(other, GeneratedWorld.CompanyId);
            GoodsSnapshotStore.Save(world, WorldPath);

            using var registry = new PlayerRegistry(Path.Combine(_directory, SessionOptions.RegistryFileName));
            var admission = new SessionAdmission(registry, world, start.SiteId, WorldPath, DevWorld.InventoryCapacity, GeneratedWorld.StarterGoods);
            Assert.That(admission.PrimarySiteId, Is.EqualTo(start.SiteId));
            var player = admission.Admit("Tester", new string('b', 43)).PlayerId;
            Assert.That(player, Is.Not.Null);
            var saved = GoodsSnapshotStore.Load(WorldPath);
            Assert.That((saved.CanView(player, start.SiteId), saved.CanView(player, other.SiteId)), Is.EqualTo((true, true)));
            var inventory = saved.Snapshot().Locations.Single(x => x.Id == GoodsWorld.InventoryLocationId(player));
            Assert.That(inventory.SiteId, Is.EqualTo(start.SiteId), "The inventory is on the starting site only.");
            Assert.That(saved.Snapshot().Lots.Where(x => x.LocationId == inventory.Id).Select(x => (x.ItemId, x.Quantity, x.SpoilAfterSeconds)),
                Is.EqualTo(new[] { (DevWorld.DoughItemId, GeneratedWorld.StarterDough, DevWorld.DoughSpoilAfterSeconds) }),
                "Decision 0039: a generated world's start kit is dough only, no belts or lifts.");
        }

        // Decision 0039 (owner decision 9): the starting cash covers about an oven, a fridge, a few tables and an hour of dough,
        // priced from the real offers, with less than 2x slack, so a price change shows up here.
        [Test]
        public void TheStartingCashCoversABasicKitAndLittleMore()
        {
            long Price(string name) => UnityEditor.AssetDatabase.LoadAssetAtPath<Equipment.OfferAsset>($"Assets/Content/Offers/{name}.asset").PriceCents;
            var dough = UnityEditor.AssetDatabase.LoadAssetAtPath<Equipment.OfferAsset>("Assets/Content/Offers/Dough5.asset");
            // PROTOTYPE: an hour of dough at about one sale a real minute (the pacing target), in whole packs.
            var hourOfDough = (60 + dough.Quantity - 1) / dough.Quantity * dough.PriceCents;
            var kit = Price("Oven1") + Price("Fridge1") + 3 * Price("Table1") + hourOfDough;
            TestContext.WriteLine($"basic kit {kit} cents, starting cash {GeneratedWorld.StartingCash} cents");
            Assert.That(GeneratedWorld.StartingCash, Is.GreaterThanOrEqualTo(kit).And.LessThan(2 * kit));
        }

        // Decision 0039: a world created before the game hour (goods v20, layout format 4) loads with its districts re-tuned in
        // memory, gains no second copy of any district, and keeps its cash.
        [Test]
        public void AnOlderWorldLoadsWithReTunedDistrictsAndItsCash()
        {
            var stored = Layout();
            var world = Open(stored);
            var created = world.Snapshot();
            // TEST-ONLY: the same world as decision 0038 left it, with $1,000,000 and per-3600 s district rates in a v20 save.
            Assert.That(world.AdjustCashDurably(GeneratedWorld.CompanyId, 100_000_000 - GeneratedWorld.StartingCash, WorldPath), Is.Null);
            var payload = LatestPayload();
            var v20 = System.Text.RegularExpressions.Regex.Replace(payload, "\"CustomersPerHour\":(\\d+)",
                    m => $"\"CustomersPerHour\":{int.Parse(m.Groups[1].Value) * GameClock.RetuneDivisor}")
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":20");
            ReplaceLatestPayload(v20);

            var loaded = Open(stored).Snapshot();
            Assert.That(loaded.Districts.Select(x => (x.Id, x.CustomersPerHour)), Is.EqualTo(created.Districts.Select(x => (x.Id, x.CustomersPerHour))),
                "Rates come back as the game-hour rates a new world gets; no district is added twice.");
            Assert.That(loaded.Companies.Single().Cash, Is.EqualTo(100_000_000L), "An older world keeps its cash.");
        }

        [Test]
        public void PlayersSpawnOnTheStartingApronInFrontOfTheDoor()
        {
            var start = GeneratedWorld.StartOffer(Layout().Layout);
            var grid = new SiteLayout { SiteId = start.SiteId, Width = start.Width, Depth = start.Depth };
            for (var index = 0; index < 5; index++)
            {
                var (position, rotation) = SessionRoot.ApronSpawn(start, index);
                var (x, z) = Equipment.SiteGridSpace.AnchorAt(grid, position, 1, 1);
                Assert.That(x >= 0 && x < start.Width && z >= 0 && z < start.Depth, Is.True, $"Spawn {index} stands on the lot.");
                Assert.That(SiteGrid.Overlaps(x, z, 1, 1, start.BuildingX, start.BuildingZ, start.BuildingWidth, start.BuildingDepth), Is.False,
                    $"Spawn {index} stands outside the building.");
                var door = Equipment.SiteGridSpace.FootprintCenter(grid, start.Doors[0].X, start.Doors[0].Z, 1, 1);
                Assert.That(Vector3.Dot(rotation * Vector3.forward, (door - position).normalized), Is.GreaterThan(0f), "Facing the door.");
            }
        }
    }
}
