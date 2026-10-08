// Verifies competitors linked to lots (decisions 0028 and 0030): a competitor's lot survives a save and load, a v14 save upgrades
// with every competitor unlinked, every invalid link is rejected without changing the world, and customer choice (with its
// per-district area lookup) only ever considers restaurants within each district's own range, including restaurants and
// districts added later. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class CompetitorLotTests
    {
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryCompetitorLotTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            GoodsSnapshotStore.Release(PathForSave);
            Directory.Delete(_saveDirectory, true);
        }

        // TEST-ONLY values, not gameplay content: two 8x8 restaurant lots with a 6x6 shell, "rival" listed but not for sale and
        // "diner" for sale, at world z 200; one company owning an unrelated site "home".
        private static PropertyOffer Offer(string name, bool forSale, int lotX) => new()
        {
            LotId = "lot-" + name, SiteId = "site-" + name, BuildingId = name, Category = GoodsWorld.RestaurantKind, ForSale = forSale,
            PriceCents = 2000, LotX = lotX, LotZ = 200, Width = 8, Depth = 8, AccessX = lotX + 3, AccessZ = 208,
            BuildingX = 1, BuildingZ = 0, BuildingWidth = 6, BuildingDepth = 6, Doors = { new GridCell { X = 3, Z = 5 } }
        };

        private static List<PropertyOffer> Offers() => new() { Offer("rival", false, 100), Offer("diner", true, 130) };

        private static GoodsWorld CreateWorld(bool catalog = true)
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "home", Kind = "storage", Capacity = 4 });
            world.Bootstrap(new GoodsCompany { Id = "company", Cash = 6000, SiteIds = { "home" } });
            world.Grant("chef", "home");
            if (catalog) world.RegisterPropertyOffers(Offers());
            return world;
        }

        private static GoodsCompetitor Competitor(string id, string lotId = "", int mapX = 0, int mapZ = 0) => new()
        {
            Id = id, Name = id, LotId = lotId, MapX = mapX, MapZ = mapZ, Cuisine = "bakery", Tier = 1, PriceCents = 300, Servers = 1,
            ServiceSeconds = 5, Seats = 4
        };

        [Test]
        public void CompetitorsWithLotsSaveAndReload()
        {
            var world = CreateWorld();
            world.Bootstrap(Competitor("competitor-rival", "lot-rival", 103, 208));
            world.Bootstrap(Competitor("competitor-dev"));
            GoodsSnapshotStore.Save(world, PathForSave);

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            loaded.RegisterPropertyOffers(Offers());
            Assert.That(loaded.Snapshot().Competitors.Select(x => (x.Id, x.LotId, x.MapX, x.MapZ)),
                Is.EqualTo(new[] { ("competitor-rival", "lot-rival", 103, 208), ("competitor-dev", "", 0, 0) }));
            Assert.That(loaded.Snapshot().SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(JsonUtility.ToJson(loaded.Snapshot()), Is.EqualTo(JsonUtility.ToJson(world.Snapshot())));
        }

        [Test]
        public void SchemaV14RowLoadsWithUnlinkedCompetitors()
        {
            var legacy = CreateWorld(catalog: false);
            legacy.Bootstrap(Competitor("competitor-old", mapX: 40));
            GoodsSnapshotStore.Save(legacy, PathForSave);
            var v14 = JsonUtility.ToJson(legacy.Snapshot())
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":14").Replace("\"LotId\":\"\",", "");
            Assert.That(v14, Does.Contain("\"SchemaVersion\":14").And.Contain("competitor-old").And.Not.Contain("\"LotId\":\"\""),
                "The row looks like a v14 world.");
            SnapshotDatabase.WritePayload(PathForSave, v14);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(14));

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            var competitor = loaded.Snapshot().Competitors.Single();
            Assert.That((loaded.Snapshot().SchemaVersion, competitor.Id, competitor.LotId, competitor.MapX),
                Is.EqualTo((GoodsSnapshot.CurrentSchema, "competitor-old", "", 40)));
            loaded.Bootstrap(Competitor("competitor-new", mapX: 80));
            GoodsSnapshotStore.Save(loaded, PathForSave);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(GoodsSnapshot.CurrentSchema), "The next commit is v15.");
        }

        [Test]
        public void InvalidLotLinksAreRejectedAndChangeNothing()
        {
            var world = CreateWorld();
            world.Bootstrap(Competitor("competitor-rival", "lot-rival"));
            var before = JsonUtility.ToJson(world.Snapshot());
            var rejected = new[]
            {
                Competitor("unknown", "lot-nowhere"),
                Competitor("for-sale", "lot-diner"),
                Competitor("twice", "lot-rival")
            };
            foreach (var competitor in rejected)
            {
                Assert.Throws<ArgumentException>(() => world.Bootstrap(competitor), competitor.Id);
                Assert.That(JsonUtility.ToJson(world.Snapshot()), Is.EqualTo(before), $"{competitor.Id} changed nothing.");
            }

            // An owned lot cannot hold a competitor either: the diner, once bought, is refused.
            Assert.That(world.BuyProperty("chef", "buy", "home", "lot-diner").Accepted, Is.True);
            var owned = JsonUtility.ToJson(world.Snapshot());
            Assert.Throws<ArgumentException>(() => world.Bootstrap(Competitor("on-owned", "lot-diner")));
            Assert.That(JsonUtility.ToJson(world.Snapshot()), Is.EqualTo(owned));

            // Without a catalog the static rules still hold, and the catalog refuses a world that disagrees with it.
            var bare = CreateWorld(catalog: false);
            bare.Bootstrap(Competitor("competitor-diner", "lot-diner"));
            Assert.Throws<InvalidOperationException>(() => bare.RegisterPropertyOffers(Offers()), "A competitor on a lot for sale.");
        }

        [Test]
        public void ChoiceOnlyConsidersRestaurantsInEachDistrictsRange()
        {
            var world = CreateWorld(catalog: false);
            // West district (x = 0, range 100) and east district (x = 1000, range 150); competitors every 50 m from -300 to 1300.
            world.Bootstrap(new GoodsDistrict
            {
                Id = "west", Name = "West", MapX = 0, CustomersPerHour = 60, WealthPercent = 50, AppearanceVariants = 1,
                LikedCuisines = { "bakery" }, DineInPercent = 0, RangeMetres = 100
            });
            for (var x = -300; x <= 1300; x += 50) world.Bootstrap(Competitor($"c{x}", mapX: x));
            world.Advance(60);
            world.Bootstrap(new GoodsDistrict
            {
                Id = "east", Name = "East", MapX = 1000, CustomersPerHour = 60, WealthPercent = 50, AppearanceVariants = 1,
                LikedCuisines = { "bakery" }, DineInPercent = 0, RangeMetres = 150
            });
            world.Advance(60);
            // A restaurant added later, inside only the west range, must be considered too (the lookup is rebuilt).
            world.Bootstrap(Competitor("late", mapX: 10, mapZ: 20));
            var chosen = new List<GoodsCustomer>();
            for (var second = 0; second < 240; second++)
            {
                world.Advance(1);
                chosen.AddRange(world.Snapshot().Customers.Where(x => x.State == CustomerState.Travelling));
            }

            int Distance(GoodsCustomer customer, int from) =>
                customer.RestaurantId == "late" ? Math.Abs(10 - from) + 20 : Math.Abs(int.Parse(customer.RestaurantId.Substring(1)) - from);
            var west = chosen.Where(x => x.DistrictId == "west").ToList();
            var east = chosen.Where(x => x.DistrictId == "east").ToList();
            Assert.That((west.Count, east.Count), Is.Not.EqualTo((0, 0)));
            Assert.That(west.Select(x => Distance(x, 0)), Is.All.LessThanOrEqualTo(100), "West customers choose within 100 m.");
            Assert.That(east.Select(x => Distance(x, 1000)), Is.All.LessThanOrEqualTo(150), "East customers choose within 150 m.");
            Assert.That(east.Select(x => x.RestaurantId), Has.None.EqualTo("late"));
            Assert.That(west.Select(x => x.RestaurantId), Has.Some.EqualTo("late"), "The later restaurant is chosen by the district in range.");
            Assert.That(west.Select(x => x.RestaurantId).Distinct().Count(), Is.EqualTo(6), "Every in-range restaurant is a candidate.");
            Assert.DoesNotThrow(() => GoodsWorld.Validate(world.Snapshot()));
        }
    }
}
