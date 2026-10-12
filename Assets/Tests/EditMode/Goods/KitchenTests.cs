// Verifies decision 0043's kitchen rules: a machine set to one recipe makes only that recipe while automatic keeps the old
// first-ready-by-ID rule, and a refused choice changes nothing; a worker-bound station (the kitchen counter) starts and advances
// batches only while someone works it; customers choose only dishes a restaurant has in stock at a staffed register, and a
// wealthy district only tier 2 and up; the setting survives a save, and v19 saves load with every machine on automatic.
// Isolated saves only.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class KitchenTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");

        [SetUp]
        public void SetUp()
        {
            _world = CreateWorld();
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryKitchenTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: one 10x10 site with a mixer (two recipes from the same flour and egg) and a worker-bound counter
        // (beef -> patty in 4 s), each stocked, and a chef granted the site; a stranger has no grant. Not gameplay content.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            foreach (var item in new[] { "flour", "egg", "beef", "breading", "pasta", "patty" }) world.RegisterItem(item, 20);
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "site", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "site", Width = 10, Depth = 10 });
            world.Bootstrap(new GoodsEquipment { Id = "mixer-1", Kind = "mixer", SiteId = "site", Width = 1, Depth = 1, InputCapacity = 4, OutputCapacity = 4 });
            world.Bootstrap(new GoodsEquipment { Id = "prep-1", Kind = "prep", SiteId = "site", CellX = 3, Width = 2, Depth = 1, InputCapacity = 4, OutputCapacity = 4 });
            world.Bootstrap(new GoodsLot { Id = "flour", ItemId = "flour", OwnerId = "site", LocationId = "mixer-1:in", Quantity = 4, SpoilAfterSeconds = 1000 });
            world.Bootstrap(new GoodsLot { Id = "egg", ItemId = "egg", OwnerId = "site", LocationId = "mixer-1:in", Quantity = 4, SpoilAfterSeconds = 1000 });
            world.Bootstrap(new GoodsLot { Id = "beef", ItemId = "beef", OwnerId = "site", LocationId = "prep-1:in", Quantity = 4, SpoilAfterSeconds = 1000 });
            world.Grant("chef", "site");
            world.RegisterRecipe(Recipe("mixer-breading", "mixer", "breading", ("flour", 1), ("egg", 1)));
            world.RegisterRecipe(Recipe("mixer-pasta", "mixer", "pasta", ("flour", 1), ("egg", 1)));
            world.RegisterRecipe(Recipe("prep-patty", "prep", "patty", ("beef", 1)));
            world.RegisterWorkerBound("prep");
            world.AutomaticJobs = true;
            return world;
        }

        private static RecipeDefinition Recipe(string id, string kind, string output, params (string Item, int Quantity)[] inputs)
        {
            var recipe = new RecipeDefinition
            {
                Id = id, StationKind = kind, DurationSeconds = 4, OutputItemId = output, OutputQuantity = 1, OutputSpoilAfterSeconds = 500
            };
            foreach (var (item, quantity) in inputs) recipe.Inputs.Add(new RecipeInput { ItemId = item, Quantity = quantity });
            return recipe;
        }

        private StationJob Job(string station) => _world.Snapshot().Jobs.SingleOrDefault(x => x.StationId == station);
        private int Units(string location, string item) =>
            _world.Snapshot().Lots.Where(x => x.LocationId == location && x.ItemId == item).Sum(x => x.Quantity);
        private string Chosen(string equipment) => _world.Snapshot().Equipment.Single(x => x.Id == equipment).RecipeId;

        [Test]
        public void AMachineSetToARecipeMakesOnlyThatRecipe()
        {
            Assert.That(_world.SelectableRecipes("mixer"), Is.EqualTo(new[] { "mixer-breading", "mixer-pasta" }));
            _world.Advance(1);
            Assert.That(Job("mixer-1")?.RecipeId, Is.EqualTo("mixer-breading"), "Automatic starts the first ready recipe by ID, as before.");

            var set = _world.SelectRecipe("chef", "set-pasta", "mixer-1", "mixer-pasta");
            Assert.That((set.Accepted, set.Reason), Is.EqualTo((true, "recipe-set")));
            Assert.That(Chosen("mixer-1"), Is.EqualTo("mixer-pasta"));
            Assert.That(Job("mixer-1")?.RecipeId, Is.EqualTo("mixer-breading"), "The running batch keeps its recipe.");
            // Four flour and four egg make four batches: the one already running, then three of the chosen recipe. A clock step
            // starts at most one batch per station, so the clock runs a second at a time.
            for (var second = 0; second < 40; second++) _world.Advance(1);
            Assert.That((Units("mixer-1:out", "breading"), Units("mixer-1:out", "pasta")), Is.EqualTo((1, 3)));

            Assert.That(_world.SelectRecipe("chef", "auto", "mixer-1", "").Accepted, Is.True, "Empty sets it back to automatic.");
            Assert.That(Chosen("mixer-1"), Is.EqualTo(""));
            Assert.That(_world.SelectRecipe("chef", "auto", "mixer-1", "mixer-pasta").Reason, Is.EqualTo("recipe-set"), "A retry replays.");
            Assert.That(Chosen("mixer-1"), Is.EqualTo(""), "A replay changes nothing.");
        }

        [Test]
        public void ARefusedRecipeChoiceChangesNothing()
        {
            _world.RegisterRecipe(new RecipeDefinition
            {
                Id = "sell-pasta", StationKind = GoodsWorld.CounterKind, DurationSeconds = 4, SaleCents = 500,
                Inputs = { new RecipeInput { ItemId = "pasta", Quantity = 1 } }, OutputItemId = ""
            });
            Assert.That(_world.SelectRecipe("stranger", "a", "mixer-1", "mixer-pasta").Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.SelectRecipe("chef", "b", "missing", "mixer-pasta").Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.SelectRecipe("chef", "c", "mixer-1", "prep-patty").Reason, Is.EqualTo("invalid-recipe"), "Another kind's recipe.");
            Assert.That(_world.SelectRecipe("chef", "d", "mixer-1", "sell-pasta").Reason, Is.EqualTo("invalid-recipe"), "A menu item.");
            Assert.That(_world.SelectRecipe("chef", "e", "mixer-1", "nothing").Reason, Is.EqualTo("invalid-recipe"));
            Assert.That(_world.PickUp("chef", "pick", "mixer-1").Accepted, Is.True);
            Assert.That(_world.SelectRecipe("chef", "f", "mixer-1", "mixer-pasta").Reason, Is.EqualTo("not-placed"));
            Assert.That(_world.Snapshot().Equipment.All(x => x.RecipeId == ""), Is.True, "No refused choice set anything.");
        }

        [Test]
        public void AWorkerBoundStationRunsOnlyWhileSomeoneWorksIt()
        {
            Assert.That(_world.RequiresWorker("prep"), Is.True);
            Assert.That(_world.RequiresWorker("mixer"), Is.False);
            _world.Advance(5);
            Assert.That(Job("prep-1"), Is.Null, "Nobody works the counter, so nothing starts.");
            Assert.That(_world.StartJob("chef", "manual", "prep-1", "prep-patty").Reason, Is.EqualTo("unstaffed"));

            var work = _world.Staff("chef", "work", "prep-1", "chef");
            Assert.That((work.Accepted, work.Reason), Is.EqualTo((true, "staffed")));
            Assert.That(Job("prep-1")?.RemainingSeconds, Is.EqualTo(4), "Working it starts a ready batch at once.");
            _world.Advance(2);
            Assert.That(_world.Staff("chef", "leave", "prep-1", "").Reason, Is.EqualTo("unstaffed"));
            _world.Advance(30);
            Assert.That(Job("prep-1")?.RemainingSeconds, Is.EqualTo(2), "The batch waits where it is while nobody works.");
            Assert.That(Units("prep-1:out", "patty"), Is.Zero);

            Assert.That(_world.Staff("chef", "back", "prep-1", "chef").Accepted, Is.True);
            _world.Advance(2);
            Assert.That(Units("prep-1:out", "patty"), Is.EqualTo(1), "It resumes where it paused.");
            Assert.That(Job("prep-1")?.RemainingSeconds, Is.EqualTo(4), "The next batch starts while it is worked.");

            Assert.That(_world.Staff("chef", "mixer", "mixer-1", "chef").Reason, Is.EqualTo("not-a-register"), "Only registers and worker-bound kinds.");
        }

        [Test]
        public void TheSettingSurvivesASaveAndAV19RowLoadsOnAutomatic()
        {
            Assert.That(_world.SelectRecipe("chef", "set", "mixer-1", "mixer-pasta").Accepted, Is.True);
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Equipment.Single(x => x.Id == "mixer-1").RecipeId, Is.EqualTo("mixer-pasta"));

            var v19 = JsonUtility.ToJson(_world.Snapshot())
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":19")
                .Replace(",\"RecipeId\":\"mixer-pasta\"", "").Replace(",\"RecipeId\":\"\"", "");
            Assert.That(v19, Does.Not.Contain("\"RecipeId\""));
            SnapshotDatabase.WritePayload(PathForSave, v19);
            Assert.That(SnapshotDatabase.LatestSchemaColumn(PathForSave), Is.EqualTo(19), "The row looks like a real v19 commit.");
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(loaded.SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(loaded.Equipment.All(x => x.RecipeId == ""), Is.True, "Every machine loads on automatic.");
        }

        // TEST-ONLY restaurant for customer choice: a staffed register on the map origin with a company, a cheap tier 1 dish and a
        // dearer tier 2 dish, and one district 20 m away that spawns a customer every second.
        private static GoodsWorld Restaurant(int wealthPercent)
        {
            var world = new GoodsWorld("test-world");
            world.RegisterItem("fries", 20);
            world.RegisterItem("pizza", 20);
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "restaurant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new SiteLayout { SiteId = "restaurant", Width = 10, Depth = 10 });
            world.Bootstrap(new GoodsSite { Id = "restaurant", Name = "Restaurant" });
            world.Bootstrap(new GoodsEquipment { Id = "counter-1", Kind = GoodsWorld.CounterKind, SiteId = "restaurant", Width = 2, Depth = 1, InputCapacity = 5, OutputCapacity = 1 });
            world.Bootstrap(new GoodsCompany { Id = "co", Cash = 1000, SiteIds = { "restaurant" } });
            world.Grant("chef", "restaurant");
            Assert.That(world.Staff("chef", "staff", "counter-1", "chef").Accepted, Is.True);
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "sell-fries", StationKind = GoodsWorld.CounterKind, DurationSeconds = 4, Tier = 1, Cuisine = "fast-food", SaleCents = 200,
                Inputs = { new RecipeInput { ItemId = "fries", Quantity = 1 } }, OutputItemId = ""
            });
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "sell-pizza", StationKind = GoodsWorld.CounterKind, DurationSeconds = 4, Tier = 2, Cuisine = "fast-food", SaleCents = 700,
                Inputs = { new RecipeInput { ItemId = "pizza", Quantity = 1 } }, OutputItemId = ""
            });
            world.Bootstrap(new GoodsDistrict
            {
                Id = "district", Name = "Test", MapZ = 20, CustomersPerHour = 3600, WealthPercent = wealthPercent, AppearanceVariants = 1,
                LikedCuisines = { "fast-food" }, DineInPercent = 0, RangeMetres = 100
            });
            world.AutomaticJobs = true;
            return world;
        }

        private static void Stock(GoodsWorld world, string item) => world.Bootstrap(new GoodsLot
        {
            Id = "stock-" + item, ItemId = item, OwnerId = "restaurant", LocationId = "counter-1:in", Quantity = 20, SpoilAfterSeconds = 100_000
        });

        [Test]
        public void CustomersOnlyChooseDishesInStock()
        {
            var world = Restaurant(40);
            world.Advance(30);
            Assert.That(world.Snapshot().Customers, Is.Empty, "Nothing in stock: the restaurant is not considered.");
            Stock(world, "fries");
            world.Advance(60);
            var customers = world.Snapshot().Customers;
            Assert.That(customers, Is.Not.Empty);
            Assert.That(customers.Select(x => x.RecipeId).Distinct(), Is.EqualTo(new[] { "sell-fries" }), "Only the stocked dish is chosen.");
        }

        [Test]
        public void AWealthyDistrictBuysOnlyTierTwoAndUp()
        {
            Assert.That(GoodsWorld.MinTier(new GoodsDistrict { WealthPercent = GoodsWorld.PickyWealthPercent }), Is.EqualTo(2));
            Assert.That(GoodsWorld.MinTier(new GoodsDistrict { WealthPercent = GoodsWorld.PickyWealthPercent - 1 }), Is.EqualTo(1));
            var world = Restaurant(85);
            Stock(world, "fries");
            world.Advance(60);
            Assert.That(world.Snapshot().Customers, Is.Empty, "Tier 1 food alone does not tempt a wealthy district.");
            Stock(world, "pizza");
            world.Advance(60);
            var customers = world.Snapshot().Customers;
            Assert.That(customers, Is.Not.Empty);
            Assert.That(customers.Select(x => x.RecipeId).Distinct(), Is.EqualTo(new[] { "sell-pizza" }));
        }
    }
}
