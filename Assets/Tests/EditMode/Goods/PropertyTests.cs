// Verifies buying generated buildings (decision 0028): a purchase creates the lot's site, layout, shell, property record and
// grant and debits the exact price in one commit; a failed save or any rejection leaves nothing behind; a retried request never
// pays twice; properties survive a save and load, and a v13 save upgrades; the new site takes equipment on its apron and around
// its walls; the starting restaurant can be given without charge; the catalog refuses bad offers and properties that disagree
// with it. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class PropertyTests
    {
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryPropertyTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values, not gameplay content or configuration: chef works at "home", whose company has 6000 cents, and is also
        // granted "lonely", which no company owns. The factory lot is 12x14 with a 10x8 shell at (1,0) whose doors (5,7) and (6,7)
        // face a 6-cell apron to the north; it costs 5000. The diner lot is 8x8 with a 6x6 shell at (1,0) for 2000, the rival's
        // restaurant is listed but not for sale, and the farm lot (no shell) costs 1000. Lots stand at world z 200.
        private const long StartCash = 6000;
        private const long FactoryPrice = 5000;
        private const long DinerPrice = 2000;
        private const long FarmPrice = 1000;

        private static PropertyOffer Offer(string name, string category, bool forSale, long price, int lotX, int width, int depth,
            int buildingX, int buildingWidth, int buildingDepth, params (int X, int Z)[] doors) => new()
        {
            LotId = "lot-" + name, SiteId = "site-" + name, BuildingId = name, Category = category, ForSale = forSale, PriceCents = price,
            LotX = lotX, LotZ = 200, Width = width, Depth = depth, AccessX = lotX + doors[0].X, AccessZ = 200 + depth,
            BuildingX = buildingX, BuildingZ = 0, BuildingWidth = buildingWidth, BuildingDepth = buildingDepth,
            Doors = doors.Select(x => new GridCell { X = x.X, Z = x.Z }).ToList()
        };

        private static List<PropertyOffer> Offers() => new()
        {
            Offer("factory", GoodsWorld.FactoryKind, true, FactoryPrice, 100, 12, 14, 1, 10, 8, (5, 7), (6, 7)),
            Offer("diner", GoodsWorld.RestaurantKind, true, DinerPrice, 130, 8, 8, 1, 6, 6, (3, 5)),
            Offer("rival", GoodsWorld.RestaurantKind, false, 3000, 150, 8, 8, 1, 6, 6, (3, 5)),
            Offer("farm", GoodsWorld.FarmCategory, true, FarmPrice, 170, 10, 14, 0, 10, 6, (5, 5))
        };

        private static GoodsWorld CreateWorld(long cash = StartCash, bool catalog = true)
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "home", Kind = "storage", Capacity = 4 });
            world.Bootstrap(new GoodsLocation { Id = "shed", SiteId = "lonely", Kind = "storage", Capacity = 4 });
            world.Bootstrap(new GoodsCompany { Id = "company", Cash = cash, SiteIds = new List<string> { "home" } });
            world.Grant("chef", "home");
            world.Grant("chef", "lonely");
            if (catalog) world.RegisterPropertyOffers(Offers());
            return world;
        }

        private static string Json(GoodsWorld world) => JsonUtility.ToJson(world.Snapshot());
        private static long Cash(GoodsWorld world) => world.Snapshot().Companies.Single().Cash;

        [Test]
        public void BuyingCreatesTheSiteAndDebitsTheExactPriceInOneCommit()
        {
            var world = CreateWorld();
            GoodsSnapshotStore.Save(world, PathForSave);
            var outcome = world.BuyPropertyDurably("chef", "buy-factory", "home", "lot-factory", PathForSave);
            Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((true, "property-bought")));

            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.Revision, Is.EqualTo(outcome.Revision), "The site, ownership and payment commit at the order's revision.");
            Assert.That(loaded.Companies.Single().Cash, Is.EqualTo(StartCash - FactoryPrice));
            var entry = loaded.Ledger.Single();
            Assert.That((entry.Kind, entry.Cents, entry.SiteId, entry.RequestId, entry.OfferId),
                Is.EqualTo((GoodsWorld.LedgerProperty, -(long)FactoryPrice, "home", "buy-factory", "lot-factory")), "The ledger entry names the paying site and the lot (decision 0038).");
            Assert.That(loaded.Companies.Single().SiteIds, Is.EqualTo(new[] { "home", "site-factory" }));
            var property = loaded.Properties.Single();
            Assert.That((property.LotId, property.SiteId, property.CompanyId), Is.EqualTo(("lot-factory", "site-factory", "company")));
            var site = loaded.Sites.Single();
            Assert.That((site.Id, site.Name, site.MapX, site.MapZ), Is.EqualTo(("site-factory", "factory", 105, 214)), "Trucks drive to the access point.");
            var layout = loaded.SiteLayouts.Single();
            Assert.That((layout.SiteId, layout.Width, layout.Depth), Is.EqualTo(("site-factory", 12, 14)), "The site covers the whole lot.");
            var building = loaded.Buildings.Single();
            Assert.That((building.Id, building.SiteId, building.Kind, building.CellX, building.CellZ, building.Width, building.Depth, building.Floors),
                Is.EqualTo(("factory", "site-factory", GoodsWorld.FactoryKind, 1, 0, 10, 8, 1)));
            Assert.That(building.Doors.Select(x => (x.X, x.Z)), Is.EqualTo(new[] { (5, 7), (6, 7) }));
            Assert.That(loaded.Grants.Any(x => x.PlayerId == "chef" && x.SiteId == "site-factory"), "The buyer can use the new site.");
            Assert.That(loaded.Locations.Any(x => x.SiteId == "site-factory"), Is.False, "A bought site starts empty.");

            // The rest of the cash buys the farm exactly; a farm has no shell.
            Assert.That(world.BuyPropertyDurably("chef", "buy-farm", "home", "lot-farm", PathForSave).Accepted, Is.True);
            var both = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That((both.Companies.Single().Cash, both.Properties.Count, both.Buildings.Count, both.SiteLayouts.Single(x => x.SiteId == "site-farm").Depth),
                Is.EqualTo((0L, 2, 1, 14)));
        }

        [Test]
        public void AFailedSaveLeavesNoSitePropertyOrDebit()
        {
            var world = CreateWorld();
            var before = Json(world);
            var unwritable = Path.Combine(_saveDirectory, "missing", "goods.db");
            var failed = world.BuyPropertyDurably("chef", "buy", "home", "lot-factory", unwritable);
            Assert.That((failed.Accepted, failed.Reason), Is.EqualTo((false, "persistence-unavailable")));
            Assert.That(Json(world), Is.EqualTo(before), "No site, layout, building, property, grant or debit is left behind.");
            Assert.That(world.HasSite("site-factory"), Is.False);

            var retried = world.BuyPropertyDurably("chef", "buy", "home", "lot-factory", PathForSave);
            Assert.That(retried.Accepted, Is.True, "The failed order was not recorded, so the same request can succeed later.");
            Assert.That(Cash(world), Is.EqualTo(StartCash - FactoryPrice));
        }

        [Test]
        public void EveryRejectionLeavesTheStateUnchanged()
        {
            var world = CreateWorld(cash: FarmPrice + 500);
            Assert.That(world.BuyProperty("chef", "farm", "home", "lot-farm").Accepted, Is.True);
            var before = Json(world);
            void Rejected(string reason, string player, string payingSiteId, string lotId)
            {
                var outcome = world.BuyProperty(player, "order-" + reason, payingSiteId, lotId);
                Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((false, reason)));
                Assert.That(Json(world), Is.EqualTo(before), $"{reason} is answered, not recorded.");
            }

            Rejected("unknown-lot", "chef", "home", "lot-nowhere");
            Rejected("not-for-sale", "chef", "home", "lot-rival");
            Rejected("owned", "chef", "home", "lot-farm");
            Rejected("no-grant", "stranger", "home", "lot-diner");
            Rejected("no-company", "chef", "lonely", "lot-diner");
            Rejected("insufficient-funds", "chef", "home", "lot-diner");
            Assert.That(CreateWorld(catalog: false).BuyProperty("chef", "x", "home", "lot-diner").Reason, Is.EqualTo("unknown-lot"),
                "Nothing is listed before the catalog is registered.");

            var rich = CreateWorld();
            var richBefore = Json(rich);
            var unavailable = rich.BuyPropertyDurably("chef", "io", "home", "lot-diner", Path.Combine(_saveDirectory, "missing", "goods.db"));
            Assert.That((unavailable.Accepted, unavailable.Reason), Is.EqualTo((false, "persistence-unavailable")));
            Assert.That(Json(rich), Is.EqualTo(richBefore));
        }

        [Test]
        public void ARetriedOrderReplaysAndNeverPaysTwice()
        {
            var world = CreateWorld();
            var first = world.BuyProperty("chef", "buy", "home", "lot-diner");
            var replay = world.BuyProperty("chef", "buy", "home", "lot-diner");
            Assert.That((replay.Accepted, replay.Reason, replay.Revision), Is.EqualTo((true, "property-bought", first.Revision)));
            Assert.That((Cash(world), world.Snapshot().Properties.Count), Is.EqualTo((StartCash - DinerPrice, 1)));
            Assert.That(world.BuyProperty("chef", "again", "home", "lot-diner").Reason, Is.EqualTo("owned"), "A new order for the lot is refused.");

            var durable = CreateWorld();
            GoodsSnapshotStore.Save(durable, PathForSave);
            var bought = durable.BuyPropertyDurably("chef", "buy-d", "home", "lot-diner", PathForSave);
            var reloaded = GoodsSnapshotStore.Load(PathForSave);
            reloaded.RegisterPropertyOffers(Offers());
            var replayed = reloaded.BuyPropertyDurably("chef", "buy-d", "home", "lot-diner", PathForSave);
            Assert.That((replayed.Accepted, replayed.Revision), Is.EqualTo((true, bought.Revision)), "The outcome survives a restart.");
            Assert.That(Cash(reloaded), Is.EqualTo(StartCash - DinerPrice));
        }

        [Test]
        public void PropertiesSurviveASaveAndLoadAndMustAgreeWithTheCatalog()
        {
            var world = CreateWorld();
            GoodsSnapshotStore.Save(world, PathForSave);
            Assert.That(world.BuyPropertyDurably("chef", "buy", "home", "lot-diner", PathForSave).Accepted, Is.True);
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            var property = loaded.Snapshot().Properties.Single();
            Assert.That((property.LotId, property.SiteId, property.CompanyId), Is.EqualTo(("lot-diner", "site-diner", "company")));
            Assert.DoesNotThrow(() => loaded.RegisterPropertyOffers(Offers()));
            Assert.That(loaded.BuyProperty("chef", "again", "home", "lot-diner").Reason, Is.EqualTo("owned"));

            var resized = Offers();
            resized.Single(x => x.LotId == "lot-diner").Depth = 9;
            Assert.Throws<InvalidOperationException>(() => GoodsSnapshotStore.Load(PathForSave).RegisterPropertyOffers(resized),
                "A property's site has its lot's size.");
            var unlisted = Offers().Where(x => x.LotId != "lot-diner").ToList();
            Assert.Throws<InvalidOperationException>(() => GoodsSnapshotStore.Load(PathForSave).RegisterPropertyOffers(unlisted));

            var orphan = loaded.Snapshot();
            orphan.Properties.Clear();
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(orphan).RegisterPropertyOffers(Offers()),
                "A bought site never loses its property record.");
            var stray = loaded.Snapshot();
            stray.Properties.Single().CompanyId = "nobody";
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(stray), "The owning company lists the site.");
            var twice = loaded.Snapshot();
            twice.Properties.Add(new GoodsProperty { LotId = "lot-diner", SiteId = "site-diner", CompanyId = "company" });
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(twice));
            var siteless = loaded.Snapshot();
            siteless.Sites.Clear();
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(siteless), "A property's site exists.");
        }

        [Test]
        public void SchemaV13RowLoadsWithNoProperties()
        {
            var legacy = CreateWorld(catalog: false);
            GoodsSnapshotStore.Save(legacy, PathForSave);
            var v13 = JsonUtility.ToJson(legacy.Snapshot())
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":13").Replace(",\"Properties\":[]", "");
            Assert.That(v13, Does.Not.Contain("Properties").And.Contain("\"SchemaVersion\":13"), "The row looks like a v13 world.");
            SnapshotDatabase.WritePayload(PathForSave, v13);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(13));

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That((loaded.Snapshot().SchemaVersion, loaded.Snapshot().Properties.Count), Is.EqualTo((GoodsSnapshot.CurrentSchema, 0)));
            loaded.RegisterPropertyOffers(Offers());
            Assert.That(loaded.BuyPropertyDurably("chef", "buy", "home", "lot-diner", PathForSave).Accepted, Is.True);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(GoodsSnapshot.CurrentSchema));
        }

        [Test]
        public void EquipmentPlacesOnTheBoughtSiteAroundItsWalls()
        {
            var world = CreateWorld();
            Assert.That(world.BuyProperty("chef", "buy", "home", "lot-factory").Accepted, Is.True);
            world.Bootstrap(new GoodsLocation { Id = GoodsWorld.InventoryLocationId("builder"), SiteId = "site-factory", Kind = "carried", Capacity = 4 });
            world.Grant("builder", "site-factory");
            world.Bootstrap(new GoodsEquipment
            {
                Id = "fridge", Kind = "fridge", SiteId = "site-factory", CellX = 0, CellZ = 13, Width = 1, Depth = 1, InputCapacity = 1, OutputCapacity = 1
            });
            Assert.That(world.PickUp("builder", "pick-1", "fridge").Accepted, Is.True);
            Assert.That(world.Place("builder", "wall", "fridge", 1, 3, 0).Reason, Is.EqualTo("blocked"), "The shell's walls block equipment.");
            Assert.That(world.Place("builder", "outside", "fridge", 12, 0, 0).Reason, Is.EqualTo("out-of-bounds"), "The site ends at the lot.");
            Assert.That(world.Place("builder", "apron", "fridge", 6, 11, 0).Accepted, Is.True, "The apron in front of the doors is outdoor ground.");
            Assert.That(world.PickUp("builder", "pick-2", "fridge").Accepted, Is.True);
            Assert.That(world.Place("builder", "inside", "fridge", 4, 3, 0).Accepted, Is.True, "The interior holds equipment.");
            Assert.DoesNotThrow(() => GoodsWorld.Restore(world.Snapshot()));
        }

        [Test]
        public void TheStartingRestaurantIsGivenWithoutChargeOrGrant()
        {
            var world = CreateWorld();
            var start = Offers().Single(x => x.LotId == "lot-rival");
            world.Bootstrap(start, "company");
            var state = world.Snapshot();
            Assert.That(state.Companies.Single().Cash, Is.EqualTo(StartCash), "World creation charges nothing.");
            Assert.That((state.Properties.Single().LotId, state.Buildings.Single().SiteId, state.Sites.Single().Id),
                Is.EqualTo(("lot-rival", "site-rival", "site-rival")));
            Assert.That(state.Grants.Any(x => x.SiteId == "site-rival"), Is.False, "Access is granted separately.");
            Assert.DoesNotThrow(() => GoodsWorld.Restore(state));

            Assert.Throws<ArgumentException>(() => world.Bootstrap(start, "company"), "A lot is owned once.");
            Assert.Throws<ArgumentException>(() => world.Bootstrap(Offers()[1], "nobody"));
            var altered = Offers()[1];
            altered.PriceCents = 1;
            Assert.Throws<ArgumentException>(() => world.Bootstrap(altered, "company"), "Only the listed offer is accepted.");
            Assert.That(world.Snapshot().Properties.Count, Is.EqualTo(1));
        }

        [Test]
        public void TheCatalogRefusesBadOffers()
        {
            var corner = Offers();
            corner[0].Doors[0].X = 1;
            Assert.Throws<ArgumentException>(() => CreateWorld(catalog: false).RegisterPropertyOffers(corner), "A shell's door is never a corner.");
            var outside = Offers();
            outside[1].BuildingX = 3;
            Assert.Throws<ArgumentException>(() => CreateWorld(catalog: false).RegisterPropertyOffers(outside), "The building lies inside its lot.");
            var shared = Offers();
            shared[1].SiteId = shared[0].SiteId;
            Assert.Throws<ArgumentException>(() => CreateWorld(catalog: false).RegisterPropertyOffers(shared), "Reserved site IDs are unique.");
            var scenery = Offers();
            scenery[3].Category = "house";
            Assert.Throws<ArgumentException>(() => CreateWorld(catalog: false).RegisterPropertyOffers(scenery));
            Assert.Throws<ArgumentException>(() => CreateWorld().RegisterPropertyOffers(Offers()), "The catalog is registered once.");
        }

        // Piece 2: everyone who acts for the buying company is given the new site in the purchase's own commit; the company's
        // employee (a site-bound worker) and a player of an unrelated site are not.
        [Test]
        public void APurchaseGrantsTeammatesButNotEmployeesOrOutsiders()
        {
            var world = CreateWorld();
            world.Grant("sous", "home");
            world.Bootstrap(new GoodsEmployee { Id = GoodsWorld.EmployeePrefix + "1", SiteId = "home", Name = "Worker" }, 1);
            world.Grant("stranger", "lonely");
            GoodsSnapshotStore.Save(world, PathForSave);
            Assert.That(world.BuyPropertyDurably("chef", "buy-diner", "home", "lot-diner", PathForSave).Accepted, Is.True);

            var saved = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(saved.Snapshot().Grants.Where(x => x.SiteId == "site-diner").Select(x => x.PlayerId), Is.EquivalentTo(new[] { "chef", "sous" }));
            Assert.That((saved.CanView(GoodsWorld.EmployeePrefix + "1", "site-diner"), saved.CanView("stranger", "site-diner")), Is.EqualTo((false, false)));
        }

        // Ownership is public: every site's view carries every property record, and a bought site with no locations yet is
        // still named by its baseline.
        [Test]
        public void ViewsCarryEveryPropertyAndNameABoughtSite()
        {
            var world = CreateWorld();
            world.Grant("stranger", "lonely");
            Assert.That(world.BuyProperty("chef", "buy-farm", "home", "lot-farm").Accepted, Is.True);

            var outsider = world.View("stranger", "lonely");
            Assert.That(outsider.Properties.Select(x => (x.LotId, x.CompanyId)), Is.EqualTo(new[] { ("lot-farm", "company") }));
            Assert.That(outsider.Companies, Is.Empty, "Cash stays private to the owner's sites.");
            var bought = world.View("chef", "site-farm");
            Assert.That(bought.Locations.Where(x => x.SiteId == "site-farm"), Is.Empty);
            Assert.That(GoodsWorld.ViewSiteId(bought), Is.EqualTo("site-farm"));
            Assert.That(GoodsWorld.ViewSiteId(world.View("chef", "home")), Is.EqualTo("home"));
        }

        // Every rejection leaves the world byte-for-byte unchanged, teammates' grants included.
        [Test]
        public void RejectionsGrantAndChargeNothing()
        {
            var world = CreateWorld(cash: DinerPrice - 1);
            world.Grant("sous", "home");
            var before = Json(world);
            var reasons = new[]
            {
                world.BuyProperty("chef", "a", "home", "lot-missing").Reason,
                world.BuyProperty("chef", "b", "home", "lot-rival").Reason,
                world.BuyProperty("chef", "c", "home", "lot-diner").Reason,
                world.BuyProperty("stranger", "d", "home", "lot-farm").Reason,
                world.BuyProperty("chef", "e", "lonely", "lot-farm").Reason
            };
            Assert.That(reasons, Is.EqualTo(new[] { "unknown-lot", "not-for-sale", "insufficient-funds", "no-grant", "no-company" }));
            Assert.That(Json(world), Is.EqualTo(before));
        }
    }
}
