// Verifies carrying goods between owned sites (decisions 0029, 0031): entering another owned lot moves the player's carried
// location, its lots (re-owned by the new site) and every held machine in one commit; each rejection (not granted, unknown or
// unlisted site, off the lot, no inventory, already there, reserved goods) changes and records nothing; a retried request
// replays and never moves twice; a failed save restores everything; and validation refuses a split carried site or a carried
// location on a site its holder is not granted. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class EnterSiteTests
    {
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryEnterSiteTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            GoodsSnapshotStore.Release(PathForSave);
            Directory.Delete(_saveDirectory, true);
        }

        // TEST-ONLY values, not gameplay content: "home" is an unlisted dev-style site. The diner lot is 8x8 at world (130, 200)
        // with a 6x6 shell at (1,0); the factory lot is 12x14 at world (100, 200); the rival's lot is listed but never bought.
        // Chef carries 3 dough and 1 flour on the diner and holds a 1x1 mixer bought there; sous is granted everything but
        // carries nothing.
        private static PropertyOffer Offer(string name, string category, int lotX, int width, int depth, int buildingWidth, int buildingDepth,
            params (int X, int Z)[] doors) => new()
        {
            LotId = "lot-" + name, SiteId = "site-" + name, BuildingId = name, Category = category, ForSale = true, PriceCents = 100,
            LotX = lotX, LotZ = 200, Width = width, Depth = depth, AccessX = lotX + doors[0].X, AccessZ = 200 + depth,
            BuildingX = 1, BuildingZ = 0, BuildingWidth = buildingWidth, BuildingDepth = buildingDepth,
            Doors = doors.Select(x => new GridCell { X = x.X, Z = x.Z }).ToList()
        };

        private GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "home", Kind = "storage", Capacity = 4 });
            world.Bootstrap(new GoodsCompany { Id = "company", Cash = 1000, SiteIds = new List<string> { "home" } });
            world.Grant("chef", "home");
            world.Grant("sous", "home");
            world.RegisterPropertyOffers(new[]
            {
                Offer("diner", GoodsWorld.RestaurantKind, 130, 8, 8, 6, 6, (3, 5)),
                Offer("factory", GoodsWorld.FactoryKind, 100, 12, 14, 10, 8, (5, 7), (6, 7)),
                Offer("rival", GoodsWorld.RestaurantKind, 150, 8, 8, 6, 6, (3, 5))
            });
            Assert.That(world.BuyProperty("chef", "buy-diner", "home", "lot-diner").Accepted, Is.True);
            Assert.That(world.BuyProperty("chef", "buy-factory", "home", "lot-factory").Accepted, Is.True);
            GoodsSnapshotStore.Save(world, PathForSave);
            Assert.That(world.TryGrantDurably("chef", "site-diner", PathForSave, 4, new[]
            {
                new GoodsLot { ItemId = "dough", Quantity = 3, SpoilAfterSeconds = 600 },
                new GoodsLot { ItemId = "flour", Quantity = 1, SpoilAfterSeconds = 600 }
            }), Is.True);
            world.Bootstrap(new GoodsEquipment { Id = "mixer", Kind = "mixer", SiteId = "site-diner", CellX = 0, CellZ = 7, Width = 1, Depth = 1,
                InputCapacity = 1, OutputCapacity = 1 });
            Assert.That(world.PickUp("chef", "pick-mixer", "mixer").Accepted, Is.True);
            GoodsSnapshotStore.Save(world, PathForSave);
            return world;
        }

        private static string Json(GoodsWorld world) => JsonUtility.ToJson(world.Snapshot());

        private static string Carried(GoodsSnapshot state) => state.Locations.Single(x => x.Id == GoodsWorld.InventoryLocationId("chef")).SiteId;

        [Test]
        public void EnteringMovesGoodsAndHeldMachinesInOneCommit()
        {
            var world = CreateWorld();
            var before = world.Snapshot();
            var outcome = world.EnterSiteDurably("chef", "enter", "site-factory", 105.5f, 213.9f, PathForSave);
            Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((true, "entered")));

            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.Revision, Is.EqualTo(outcome.Revision), "Goods and machines move at the request's revision.");
            Assert.That(Carried(loaded), Is.EqualTo("site-factory"));
            var inventory = GoodsWorld.InventoryLocationId("chef");
            Assert.That(loaded.Lots.Where(x => x.LocationId == inventory).Select(x => (x.Id, x.ItemId, x.Quantity, x.ExposureSeconds, x.OwnerId)),
                Is.EquivalentTo(before.Lots.Where(x => x.LocationId == inventory).Select(x => (x.Id, x.ItemId, x.Quantity, x.ExposureSeconds, "site-factory"))),
                "Every carried lot keeps its identity, quantity and exposure, owned by the new site.");
            var mixer = loaded.Equipment.Single(x => x.Id == "mixer");
            Assert.That((mixer.SiteId, mixer.State, mixer.HolderId), Is.EqualTo(("site-factory", EquipmentState.Held, "chef")));
            Assert.That((loaded.Lots.Sum(x => x.Quantity), loaded.Equipment.Count), Is.EqualTo((before.Lots.Sum(x => x.Quantity), before.Equipment.Count)),
                "Nothing is created or lost.");
            Assert.That(world.CarriedSiteOf("chef"), Is.EqualTo("site-factory"));

            // The mixer is placeable on its new site and the dough goes into it there.
            Assert.That(world.Place("chef", "place", "mixer", 0, 13, 0).Accepted, Is.True);
            var dough = world.Snapshot().Lots.First(x => x.LocationId == inventory && x.ItemId == "dough");
            Assert.That(world.Transfer("chef", new TransferIntent { RequestId = "load", LotId = dough.Id, DestinationId = "mixer:in", Quantity = 1 }).Accepted, Is.True);
        }

        [TestCase("sous", "site-factory", 105f, 205f, "no-inventory")]
        [TestCase("stranger", "site-factory", 105f, 205f, "forbidden")]
        [TestCase("chef", "home", 105f, 205f, "unknown-site")]
        [TestCase("chef", "site-rival", 152f, 204f, "forbidden")]
        [TestCase("chef", "site-factory", 97.9f, 205f, "not-on-lot")]
        [TestCase("chef", "site-factory", 105f, 216.1f, "not-on-lot")]
        [TestCase("chef", "site-factory", float.NaN, 205f, "not-on-lot")]
        [TestCase("chef", "site-diner", 133f, 204f, "already-there")]
        public void EachRejectionChangesAndRecordsNothing(string player, string siteId, float x, float z, string reason)
        {
            var world = CreateWorld();
            world.Grant("sous", "site-factory");
            GoodsSnapshotStore.Save(world, PathForSave);
            var before = Json(world);
            var outcome = world.EnterSiteDurably(player, "enter", siteId, x, z, PathForSave);
            Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((false, reason)));
            Assert.That(Json(world), Is.EqualTo(before));
            Assert.That(JsonUtility.ToJson(GoodsSnapshotStore.Load(PathForSave).Snapshot()), Is.EqualTo(before));
        }

        [Test]
        public void ReservedGoodsCannotBeCarriedAway()
        {
            var world = CreateWorld();
            var dough = world.Snapshot().Lots.First(x => x.ItemId == "dough");
            Assert.That(world.Reserve("chef", "hold", dough.Id, 1), Is.True);
            var before = Json(world);
            var outcome = world.EnterSite("chef", "enter", "site-factory", 105f, 205f);
            Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((false, "reserved")));
            Assert.That(Json(world), Is.EqualTo(before));
        }

        [Test]
        public void ARetriedRequestReplaysAndNeverMovesTwice()
        {
            var world = CreateWorld();
            var first = world.EnterSiteDurably("chef", "enter-factory", "site-factory", 105f, 205f, PathForSave);
            Assert.That(world.EnterSiteDurably("chef", "enter-diner", "site-diner", 131f, 207f, PathForSave).Accepted, Is.True);
            var afterBoth = Json(world);
            var replay = world.EnterSiteDurably("chef", "enter-factory", "site-factory", 105f, 205f, PathForSave);
            Assert.That((replay.Accepted, replay.Revision), Is.EqualTo((true, first.Revision)), "The original outcome is returned.");
            Assert.That(Json(world), Is.EqualTo(afterBoth), "The retry moves nothing: the goods stay on the diner.");
            Assert.That(Carried(world.Snapshot()), Is.EqualTo("site-diner"));
        }

        [Test]
        public void AFailedSaveRestoresEverything()
        {
            var world = CreateWorld();
            var before = Json(world);
            var failed = world.EnterSiteDurably("chef", "enter", "site-factory", 105f, 205f, Path.Combine(_saveDirectory, "missing", "goods.db"));
            Assert.That((failed.Accepted, failed.Reason), Is.EqualTo((false, "persistence-unavailable")));
            Assert.That(Json(world), Is.EqualTo(before));
            Assert.That(world.EnterSiteDurably("chef", "enter", "site-factory", 105f, 205f, PathForSave).Accepted, Is.True,
                "The failed request was not recorded, so it can succeed later.");
        }

        [Test]
        public void ValidationRefusesASplitCarriedSite()
        {
            var world = CreateWorld();
            var split = world.Snapshot();
            split.Equipment.Single(x => x.Id == "mixer").SiteId = "site-factory";
            Assert.That(() => GoodsWorld.Validate(split), Throws.InvalidOperationException.With.Message.Contains("mixer"));

            var ungranted = world.Snapshot();
            ungranted.Grants.RemoveAll(x => x.PlayerId == "chef" && x.SiteId == "site-diner");
            Assert.That(() => GoodsWorld.Validate(ungranted), Throws.InvalidOperationException.With.Message.Contains("carried:chef"));

            Assert.That(() => GoodsWorld.Validate(world.Snapshot()), Throws.Nothing);
        }
    }
}
