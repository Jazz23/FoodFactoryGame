// Verifies server-authoritative station jobs: atomic input consumption, clock-driven output, pickup refunds and buffer sweeps, durability and schema upgrades.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class StationJobTests
    {
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.db");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "goods.db");

        [SetUp]
        public void SetUp()
        {
            _world = CreateWorld();
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryStationJobTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: one 10x10 restaurant with a 1x1 oven (input 10, output 4), a refrigerated-output oven, a pantry,
        // a chef inventory location of 10, and a 5-second recipe turning 2 dough + 1 sauce into 1 pizza.
        // Not gameplay content or configuration.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "pantry", SiteId = "restaurant", Kind = "storage", Capacity = 50 });
            world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "restaurant", Kind = "carried", Capacity = 10 });
            world.Bootstrap(new GoodsLocation { Id = "elsewhere", SiteId = "warehouse", Kind = "storage", Capacity = 5 });
            world.Bootstrap(new SiteLayout { SiteId = "restaurant", Width = 10, Depth = 10 });
            world.Bootstrap(new GoodsEquipment { Id = "oven-1", Kind = "oven", SiteId = "restaurant", Width = 1, Depth = 1, InputCapacity = 10, OutputCapacity = 4 });
            world.Bootstrap(new GoodsEquipment { Id = "oven-cold", Kind = "oven", SiteId = "restaurant", CellX = 2, Width = 1, Depth = 1, InputCapacity = 10, OutputCapacity = 4, OutputRefrigerated = true });
            world.Bootstrap(new GoodsLot { Id = "dough-a", ItemId = "dough", OwnerId = "restaurant", LocationId = "oven-1:in", Quantity = 3, SpoilAfterSeconds = 100 });
            world.Bootstrap(new GoodsLot { Id = "dough-b", ItemId = "dough", OwnerId = "restaurant", LocationId = "oven-1:in", Quantity = 2, ExposureSeconds = 4, SpoilAfterSeconds = 100 });
            world.Bootstrap(new GoodsLot { Id = "sauce", ItemId = "sauce", OwnerId = "restaurant", LocationId = "oven-1:in", Quantity = 2, SpoilAfterSeconds = 100 });
            world.Bootstrap(new GoodsLot { Id = "cold-dough", ItemId = "dough", OwnerId = "restaurant", LocationId = "oven-cold:in", Quantity = 2, SpoilAfterSeconds = 100 });
            world.Bootstrap(new GoodsLot { Id = "cold-sauce", ItemId = "sauce", OwnerId = "restaurant", LocationId = "oven-cold:in", Quantity = 1, SpoilAfterSeconds = 100 });
            world.Grant("chef", "restaurant");
            world.Grant("sous", "restaurant");
            RegisterRecipes(world);
            return world;
        }

        private static void RegisterRecipes(GoodsWorld world)
        {
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "bake", StationKind = "oven", DurationSeconds = 5,
                Inputs = { new RecipeInput { ItemId = "dough", Quantity = 2 }, new RecipeInput { ItemId = "sauce", Quantity = 1 } },
                OutputItemId = "pizza", OutputQuantity = 1, OutputSpoilAfterSeconds = 20
            });
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "fry", StationKind = "fryer", DurationSeconds = 5,
                Inputs = { new RecipeInput { ItemId = "dough", Quantity = 1 } },
                OutputItemId = "fries", OutputQuantity = 1, OutputSpoilAfterSeconds = 20
            });
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "big-bake", StationKind = "oven", DurationSeconds = 5,
                Inputs = { new RecipeInput { ItemId = "dough", Quantity = 6 } },
                OutputItemId = "loaf", OutputQuantity = 1, OutputSpoilAfterSeconds = 20
            });
            world.RegisterRecipe(new RecipeDefinition
            {
                Id = "cheese-bake", StationKind = "oven", DurationSeconds = 5,
                Inputs = { new RecipeInput { ItemId = "cheese", Quantity = 1 } },
                OutputItemId = "melt", OutputQuantity = 1, OutputSpoilAfterSeconds = 20
            });
        }

        private GoodsOutcome Start(string request, string recipe = "bake", string player = "chef", string station = "oven-1") =>
            _world.StartJob(player, request, station, recipe);

        // Goods and production state only; outcomes and revision are expected to change on a recorded rejection.
        private static string Goods(GoodsWorld world)
        {
            var state = world.Snapshot();
            return JsonUtility.ToJson(new GoodsSnapshot { WorldId = "x", Lots = state.Lots, Reservations = state.Reservations, Stations = state.Stations, Jobs = state.Jobs, Locations = state.Locations, Equipment = state.Equipment });
        }

        private static int Count(GoodsWorld world, string item) =>
            world.Snapshot().Lots.Where(x => x.ItemId == item).Sum(x => x.Quantity)
            + world.Snapshot().Jobs.SelectMany(x => x.Inputs).Where(x => x.ItemId == item).Sum(x => x.Quantity);

        [Test]
        public void StartConsumesMostExposedInputsAndCompletionCreatesOutput()
        {
            var started = Start("bake-1");
            Assert.That(started.Accepted, Is.True);
            Assert.That(started.JobId, Is.Not.Empty);
            var state = _world.Snapshot();
            Assert.That(state.Lots.Any(x => x.Id == "dough-b"), Is.False, "Most-exposed lot is consumed first and removed when emptied.");
            Assert.That(state.Lots.Single(x => x.Id == "dough-a").Quantity, Is.EqualTo(3));
            Assert.That(state.Lots.Single(x => x.Id == "sauce").Quantity, Is.EqualTo(1));
            Assert.That(state.Jobs.Single().Inputs.Sum(x => x.Quantity), Is.EqualTo(3));
            Assert.That(Count(_world, "dough"), Is.EqualTo(7), "Consumed inputs remain accounted for inside the job.");

            var view = _world.View("chef", "restaurant");
            Assert.That(view.Stations.Select(x => x.Id), Is.EquivalentTo(new[] { "oven-1", "oven-cold" }));
            Assert.That(view.Jobs.Single().RemainingSeconds, Is.EqualTo(5));
            Assert.That(_world.View("chef", "warehouse"), Is.Null);

            _world.Advance(4);
            Assert.That(_world.Snapshot().Lots.Any(x => x.ItemId == "pizza"), Is.False);
            _world.Advance(1);
            state = _world.Snapshot();
            var pizza = state.Lots.Single(x => x.ItemId == "pizza");
            Assert.That(pizza.Id, Is.EqualTo(started.JobId + ":out"));
            Assert.That(pizza.OwnerId, Is.EqualTo("restaurant"));
            Assert.That(pizza.LocationId, Is.EqualTo("oven-1:out"));
            Assert.That(pizza.Quantity, Is.EqualTo(1));
            Assert.That(pizza.ExposureSeconds, Is.Zero);
            Assert.That(pizza.SpoilAfterSeconds, Is.EqualTo(20));
            Assert.That(state.Jobs, Is.Empty);
            Assert.That(Count(_world, "dough"), Is.EqualTo(5));
            Assert.That(Count(_world, "sauce"), Is.EqualTo(2));
        }

        [Test]
        public void RejectionsLeaveGoodsUntouched()
        {
            _world.Bootstrap(new GoodsLot { Id = "old-cheese", ItemId = "cheese", OwnerId = "restaurant", LocationId = "oven-1:in", Quantity = 1, ExposureSeconds = 5, SpoilAfterSeconds = 5, Spoiled = true });
            var before = Goods(_world);
            Assert.That(Start("intruder", player: "intruder").Reason, Is.EqualTo("forbidden"));
            Assert.That(Start("no-station", station: "nowhere").Reason, Is.EqualTo("forbidden"));
            Assert.That(Start("wrong-kind", "fry").Reason, Is.EqualTo("invalid-recipe"));
            Assert.That(Start("unknown", "unknown").Reason, Is.EqualTo("invalid-recipe"));
            Assert.That(Start("too-much", "big-bake").Reason, Is.EqualTo("missing-inputs"));
            Assert.That(Start("spoiled", "cheese-bake").Reason, Is.EqualTo("missing-inputs"));
            Assert.That(Goods(_world), Is.EqualTo(before));
            Assert.That(_world.Reserve("sous", "hold-a", "dough-a", 3), Is.True);
            Assert.That(_world.Reserve("sous", "hold-b", "dough-b", 1), Is.True);
            var reserved = Goods(_world);
            Assert.That(Start("reserved").Reason, Is.EqualTo("missing-inputs"));
            Assert.That(Goods(_world), Is.EqualTo(reserved));
            Assert.That(_world.Cancel("sous", "free-a", "hold-a").Accepted, Is.True);
            Assert.That(_world.Cancel("sous", "free-b", "hold-b").Accepted, Is.True);
            Assert.That(Start("first").Accepted, Is.True);
            var running = Goods(_world);
            Assert.That(Start("second").Reason, Is.EqualTo("station-busy"));
            Assert.That(Goods(_world), Is.EqualTo(running));
            Assert.That(before, Is.Not.EqualTo(running));
        }

        [Test]
        public void AutomaticStationStartsOnTransferAndRunsUntilInputsRunOut()
        {
            _world.Bootstrap(new GoodsLot { Id = "pantry-sauce", ItemId = "sauce", OwnerId = "restaurant", LocationId = "pantry", Quantity = 2, SpoilAfterSeconds = 100 });
            _world.Advance(1);
            Assert.That(_world.Snapshot().Jobs, Is.Empty, "Stations are manual unless the server turns automatic jobs on.");

            _world.AutomaticJobs = true;
            GoodsSnapshotStore.Save(_world, PathForSave);
            var before = Goods(_world);
            var sauce = new TransferIntent { RequestId = "add-sauce", LotId = "pantry-sauce", DestinationId = "oven-1:in", Quantity = 1 };
            Assert.That(_world.TransferDurably("chef", sauce, BadPath).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(Goods(_world), Is.EqualTo(before), "A failed commit rolls back the transfer and the start together.");

            Assert.That(_world.TransferDurably("chef", sauce, PathForSave).Accepted, Is.True);
            var job = _world.Snapshot().Jobs.Single(x => x.StationId == "oven-1");
            Assert.That(job.StartedBy, Is.EqualTo(GoodsWorld.AutomaticStarter));
            Assert.That(job.RecipeId, Is.EqualTo("bake"), "The first matching recipe by ID starts.");
            Assert.That(job.RemainingSeconds, Is.EqualTo(5));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Jobs.Any(x => x.Id == job.Id), Is.True, "The start commits with the transfer.");
            Assert.That(_world.Snapshot().Jobs.Any(x => x.StationId == "oven-cold"), Is.True, "Every ready station starts.");
            Assert.That(Start("manual").Reason, Is.EqualTo("station-busy"));

            // 5 dough + 3 sauce make exactly two pizzas; the second batch starts in the step that finishes the first.
            _world.Advance(5);
            Assert.That(_world.Snapshot().Jobs.Single(x => x.StationId == "oven-1").Id, Is.Not.EqualTo(job.Id));
            _world.Advance(5);
            _world.Advance(5);
            var state = _world.Snapshot();
            Assert.That(state.Lots.Where(x => x.LocationId == "oven-1:out").Sum(x => x.Quantity), Is.EqualTo(2));
            Assert.That(state.Jobs.Any(x => x.StationId == "oven-1"), Is.False);
            Assert.That(state.Lots.Where(x => x.LocationId == "oven-1:in").Sum(x => x.Quantity), Is.EqualTo(2), "1 dough and 1 sauce are left.");
            Assert.That(Count(_world, "dough"), Is.EqualTo(7 - 2 * 2 - 2));
        }

        [Test]
        public void AutomaticStationWaitsForRoomInItsOutput()
        {
            _world.Bootstrap(new GoodsLot { Id = "filler", ItemId = "plate", OwnerId = "restaurant", LocationId = "oven-1:out", Quantity = 4, SpoilAfterSeconds = 1000 });
            _world.AutomaticJobs = true;
            _world.Advance(1);
            Assert.That(_world.Snapshot().Jobs.Any(x => x.StationId == "oven-1"), Is.False, "No batch starts while the output is full.");
            Assert.That(_world.Snapshot().Lots.Where(x => x.LocationId == "oven-1:in" && x.ItemId == "dough").Sum(x => x.Quantity), Is.EqualTo(5));

            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = "clear", LotId = "filler", DestinationId = "pantry", Quantity = 4 }).Accepted, Is.True);
            Assert.That(_world.Snapshot().Jobs.Single(x => x.StationId == "oven-1").StartedBy, Is.EqualTo(GoodsWorld.AutomaticStarter),
                "Freeing the output starts the station in the same command.");
        }

        [Test]
        public void ReservedQuantityIsNotConsumed()
        {
            Assert.That(_world.Reserve("sous", "hold", "dough-a", 2), Is.True);
            Assert.That(Start("bake").Accepted, Is.True);
            var dough = _world.Snapshot().Lots.Single(x => x.Id == "dough-a");
            Assert.That(dough.Quantity, Is.EqualTo(3), "dough-b supplied both units; the reserved dough-a units remain.");
            Assert.That(_world.Snapshot().Reservations.Single().Active, Is.True);
        }

        [Test]
        public void ReplayReturnsOriginalJobAndOtherPlayerGetsOwnOutcome()
        {
            var first = Start("shared");
            Assert.That(Start("shared").JobId, Is.EqualTo(first.JobId));
            Assert.That(_world.Snapshot().Jobs.Count, Is.EqualTo(1));
            var other = Start("shared", player: "sous");
            Assert.That(other.Accepted, Is.False);
            Assert.That(other.Reason, Is.EqualTo("station-busy"));
            Assert.That(Start("shared").Accepted, Is.True);
            Assert.That(Count(_world, "dough"), Is.EqualTo(7));
        }

        [Test]
        public void OneLargeStepMatchesManySmallStepsIncludingOvershoot()
        {
            var stepped = CreateWorld();
            Assert.That(Start("bake").Accepted, Is.True);
            Assert.That(_world.StartJob("chef", "cold", "oven-cold", "bake").Accepted, Is.True);
            Assert.That(stepped.StartJob("chef", "bake", "oven-1", "bake").Accepted, Is.True);
            Assert.That(stepped.StartJob("chef", "cold", "oven-cold", "bake").Accepted, Is.True);
            _world.Advance(12);
            for (var i = 0; i < 12; i++) stepped.Advance(1);

            string Shape(GoodsWorld world) => string.Join("|", world.Snapshot().Lots.OrderBy(x => x.ItemId).ThenBy(x => x.LocationId)
                .Select(x => $"{x.ItemId}@{x.LocationId}:{x.Quantity}:{x.ExposureSeconds}:{x.Spoiled}"));
            Assert.That(Shape(_world), Is.EqualTo(Shape(stepped)));
            Assert.That(_world.Snapshot().Lots.Single(x => x.LocationId == "oven-1:out").ExposureSeconds, Is.EqualTo(7));
            Assert.That(_world.Snapshot().Lots.Single(x => x.LocationId == "oven-cold:out").ExposureSeconds, Is.Zero);
            Assert.That(_world.Snapshot().ClockSeconds, Is.EqualTo(stepped.Snapshot().ClockSeconds));
        }

        [Test]
        public void FullOutputBlocksJobUntilRoomWithoutLoss()
        {
            _world.Bootstrap(new GoodsLot { Id = "filler", ItemId = "plate", OwnerId = "restaurant", LocationId = "oven-1:out", Quantity = 4, SpoilAfterSeconds = 1000 });
            var started = Start("bake");
            _world.Advance(10);
            var job = _world.Snapshot().Jobs.Single();
            Assert.That(job.State, Is.EqualTo(StationJobState.Blocked));
            Assert.That(job.RemainingSeconds, Is.Zero);
            Assert.That(_world.Snapshot().Lots.Any(x => x.ItemId == "pizza"), Is.False);
            Assert.That(Start("again").Reason, Is.EqualTo("station-busy"));
            _world.Advance(5);
            Assert.That(_world.Snapshot().Jobs.Single().State, Is.EqualTo(StationJobState.Blocked));

            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = "clear", LotId = "filler", DestinationId = "pantry", Quantity = 4 }).Accepted, Is.True);
            _world.Advance(1);
            var pizza = _world.Snapshot().Lots.Single(x => x.ItemId == "pizza");
            Assert.That(pizza.Id, Is.EqualTo(started.JobId + ":out"));
            Assert.That(pizza.ExposureSeconds, Is.EqualTo(1), "Blocked output starts fresh when emitted, then ages with that step.");
            Assert.That(_world.Snapshot().Jobs, Is.Empty);
        }

        [Test]
        public void FailedCommitsRollBackStartAndTick()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var before = Goods(_world);
            var failed = _world.StartJobDurably("chef", "bake", "oven-1", "bake", BadPath);
            Assert.That(failed.Accepted, Is.False);
            Assert.That(failed.Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(Goods(_world), Is.EqualTo(before));

            var started = _world.StartJobDurably("chef", "bake", "oven-1", "bake", PathForSave);
            Assert.That(started.Accepted, Is.True);
            Assert.That(_world.TryAdvanceDurably(5, BadPath), Is.False);
            Assert.That(_world.Snapshot().Jobs.Single().RemainingSeconds, Is.EqualTo(5));
            Assert.That(_world.Snapshot().Lots.Any(x => x.ItemId == "pizza"), Is.False);
            Assert.That(_world.TryAdvanceDurably(5, PathForSave), Is.True);
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().Lots.Single(x => x.ItemId == "pizza").Id, Is.EqualTo(started.JobId + ":out"));
        }

        [Test]
        public void JobSavedMidRunCompletesExactlyOnceAfterReload()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var started = _world.StartJobDurably("chef", "bake", "oven-1", "bake", PathForSave);
            Assert.That(_world.TryAdvanceDurably(2, PathForSave), Is.True);

            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.Snapshot().Jobs.Single().RemainingSeconds, Is.EqualTo(3));
            Assert.That(_world.StartJobDurably("chef", "bake", "oven-1", "bake", PathForSave).JobId, Is.EqualTo(started.JobId),
                "Replay after recovery needs no registered recipe and creates no second job.");
            Assert.That(_world.TryAdvanceDurably(3, PathForSave), Is.True);
            Assert.That(_world.TryAdvanceDurably(10, PathForSave), Is.True);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.Snapshot().Lots.Count(x => x.ItemId == "pizza"), Is.EqualTo(1));
            Assert.That(_world.Snapshot().Jobs, Is.Empty);
            Assert.That(Count(_world, "dough"), Is.EqualTo(5));
        }

        [Test]
        public void CorruptLatestRecoversRunningJobFromPrevious()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var started = _world.StartJobDurably("chef", "bake", "oven-1", "bake", PathForSave);
            Assert.That(_world.TryAdvanceDurably(2, PathForSave), Is.True);
            SnapshotDatabase.CorruptLatest(PathForSave);

            _world = GoodsSnapshotStore.Load(PathForSave);
            var job = _world.Snapshot().Jobs.Single();
            Assert.That(job.Id, Is.EqualTo(started.JobId));
            Assert.That(job.RemainingSeconds, Is.EqualTo(5));
            Assert.That(_world.TryAdvanceDurably(5, PathForSave), Is.True);
            Assert.That(_world.TryAdvanceDurably(5, PathForSave), Is.True);
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.Snapshot().Lots.Count(x => x.ItemId == "pizza"), Is.EqualTo(1));
            Assert.That(Count(_world, "dough"), Is.EqualTo(5));
        }

        [Test]
        public void PickupRefundsInputsAndSweepsBuffersIntoInventory()
        {
            var started = Start("bake");
            _world.Advance(3);
            var picked = _world.PickUp("chef", "pickup", "oven-1");
            Assert.That(picked.Accepted, Is.True);
            Assert.That(picked.Reason, Is.EqualTo("picked-up"));
            Assert.That(picked.JobId, Is.EqualTo(started.JobId));
            var state = _world.Snapshot();
            Assert.That(state.Stations.Any(x => x.Id == "oven-1"), Is.False);
            Assert.That(state.Locations.Any(x => x.Id == "oven-1:in" || x.Id == "oven-1:out"), Is.False);
            Assert.That(state.Jobs, Is.Empty);
            var carried = state.Lots.Where(x => x.LocationId == "carried:chef").ToList();
            // 2 refunded dough + 3 buffered dough-a; 1 refunded sauce + 1 buffered sauce.
            Assert.That(carried.Where(x => x.ItemId == "dough").Sum(x => x.Quantity), Is.EqualTo(5));
            Assert.That(carried.Where(x => x.ItemId == "sauce").Sum(x => x.Quantity), Is.EqualTo(2));
            Assert.That(carried.Single(x => x.Id == started.JobId + ":in:0").ExposureSeconds, Is.EqualTo(4), "Inputs do not age while processing.");
            Assert.That(carried.Any(x => x.Id == "dough-a") && carried.Any(x => x.Id == "sauce"), Is.True, "Swept lots keep their IDs.");
            Assert.That(carried.All(x => x.OwnerId == "restaurant"), Is.True);
            Assert.That(Count(_world, "dough"), Is.EqualTo(7));
            Assert.That(Count(_world, "sauce"), Is.EqualTo(3));
            Assert.That(state.Lots.Any(x => x.ItemId == "pizza"), Is.False);
            Assert.That(_world.PickUp("chef", "pickup", "oven-1").Accepted, Is.True, "Replay returns the stored outcome.");
            Assert.That(_world.Snapshot().Lots.Count(x => x.LocationId == "carried:chef"), Is.EqualTo(4));
        }

        [Test]
        public void PickupOfBlockedJobHandsOverFinishedOutput()
        {
            _world.Bootstrap(new GoodsLot { Id = "filler", ItemId = "plate", OwnerId = "restaurant", LocationId = "oven-1:out", Quantity = 4, SpoilAfterSeconds = 1000 });
            var started = Start("bake");
            _world.Advance(10);
            Assert.That(_world.PickUp("chef", "pickup", "oven-1").Accepted, Is.True);
            var pizza = _world.Snapshot().Lots.Single(x => x.ItemId == "pizza");
            Assert.That(pizza.Id, Is.EqualTo(started.JobId + ":out"));
            Assert.That(pizza.LocationId, Is.EqualTo("carried:chef"));
            Assert.That(_world.Snapshot().Lots.Single(x => x.Id == "filler").LocationId, Is.EqualTo("carried:chef"));
            Assert.That(Count(_world, "dough"), Is.EqualTo(5));
        }

        [Test]
        public void PickupRejectionsLeaveJobRunning()
        {
            Start("bake");
            _world.Bootstrap(new GoodsLot { Id = "held", ItemId = "plate", OwnerId = "restaurant", LocationId = "carried:chef", Quantity = 4, SpoilAfterSeconds = 1000 });
            var before = Goods(_world);
            Assert.That(_world.PickUp("chef", "full", "oven-1").Reason, Is.EqualTo("capacity"));
            Assert.That(_world.PickUp("intruder", "steal", "oven-1").Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.PickUp("sous", "no-bag", "oven-1").Reason, Is.EqualTo("no-inventory"));
            Assert.That(_world.PickUp("chef", "missing", "nowhere").Reason, Is.EqualTo("forbidden"));
            Assert.That(Goods(_world), Is.EqualTo(before));

            // With room in the inventory, a reservation on a buffered lot still blocks the sweep.
            Assert.That(_world.Transfer("chef", new TransferIntent { RequestId = "drop", LotId = "held", DestinationId = "pantry", Quantity = 4 }).Accepted, Is.True);
            Assert.That(_world.Reserve("sous", "hold", "dough-a", 1), Is.True);
            var reserved = Goods(_world);
            Assert.That(_world.PickUp("chef", "reserved", "oven-1").Reason, Is.EqualTo("reserved"));
            Assert.That(Goods(_world), Is.EqualTo(reserved));
        }

        [Test]
        public void DurablePickupSurvivesReloadAndFailedCommitRollsBack()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var started = _world.StartJobDurably("chef", "bake", "oven-1", "bake", PathForSave);
            var before = Goods(_world);
            Assert.That(_world.PickUpDurably("chef", "pickup", "oven-1", BadPath).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(Goods(_world), Is.EqualTo(before));
            Assert.That(_world.PickUpDurably("chef", "pickup", "oven-1", PathForSave).JobId, Is.EqualTo(started.JobId));
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.PickUpDurably("chef", "pickup", "oven-1", PathForSave).Accepted, Is.True);
            Assert.That(_world.Snapshot().Lots.Where(x => x.LocationId == "carried:chef").Sum(x => x.Quantity), Is.EqualTo(7));
            Assert.That(_world.Snapshot().Equipment.Single(x => x.Id == "oven-1").HolderId, Is.EqualTo("chef"));
            Assert.That(Count(_world, "dough"), Is.EqualTo(7));
        }

        [Test]
        public void SchemaV1SaveLoadsAsCurrentAndIsRewrittenAsCurrent()
        {
            var legacy = new GoodsWorld("legacy-world");
            legacy.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 20 });
            legacy.Bootstrap(new GoodsLot { Id = "lot-1", ItemId = "ingredient", OwnerId = "restaurant", LocationId = "storage", Quantity = 10, SpoilAfterSeconds = 10 });
            var current = JsonUtility.ToJson(legacy.Snapshot());
            var v1 = current.Replace("\"SchemaVersion\":5", "\"SchemaVersion\":1").Replace(",\"BeltPosition\":0", "")
                .Replace(",\"Stations\":[],\"Jobs\":[],\"Equipment\":[],\"SiteLayouts\":[],\"Belts\":[],\"Companies\":[]", "");
            Assert.That(v1, Does.Not.Contain("Stations"));
            Assert.That(v1, Does.Not.Contain("Equipment"));
            Assert.That(v1, Does.Contain("\"SchemaVersion\":1"));
            var legacyPath = Path.Combine(_saveDirectory, "legacy.snapshot");
            SnapshotDatabase.WriteLegacy(legacyPath, v1);
            GoodsSnapshotStore.ImportLegacy(legacyPath, PathForSave, true);
            Assert.That(File.Exists(PathForSave), Is.False, "A dry run writes nothing.");
            GoodsSnapshotStore.ImportLegacy(legacyPath, PathForSave, false);
            Assert.Throws<IOException>(() => GoodsSnapshotStore.ImportLegacy(legacyPath, PathForSave, false));

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            var state = loaded.Snapshot();
            Assert.That(state.SchemaVersion, Is.EqualTo(GoodsSnapshot.CurrentSchema));
            Assert.That(state.Stations, Is.Empty);
            Assert.That(state.Jobs, Is.Empty);
            Assert.That(state.Equipment, Is.Empty);
            Assert.That(state.Lots.Single().Quantity, Is.EqualTo(10));

            Assert.That(loaded.TryAdvanceDurably(1, PathForSave), Is.True);
            var written = SnapshotDatabase.LatestPayload(PathForSave);
            Assert.That(written, Does.Contain("\"SchemaVersion\":5"));
            Assert.That(written, Does.Contain("\"Stations\":[]"));
            Assert.That(GoodsSnapshotStore.Load(PathForSave).Snapshot().ClockSeconds, Is.EqualTo(1));
        }

        [Test]
        public void ValidateRejectsInvalidStationsAndJobs()
        {
            Start("bake");
            var valid = _world.Snapshot();
            Assert.DoesNotThrow(() => GoodsWorld.Restore(valid));

            GoodsSnapshot Mutate(Action<GoodsSnapshot> change)
            {
                var copy = _world.Snapshot();
                change(copy);
                return copy;
            }
            StationJob Clone(StationJob job) => JsonUtility.FromJson<StationJob>(JsonUtility.ToJson(job));

            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Stations.Add(s.Stations[0]))), "duplicate station");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Stations[0].OutputLocationId = "elsewhere")), "cross-site location");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Jobs[0].StationId = "nowhere")), "orphan job");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s =>
            {
                var second = Clone(s.Jobs[0]);
                second.Id = "second";
                second.Inputs.ForEach(x => x.Id = "second:" + x.Id);
                s.Jobs.Add(second);
            })), "two jobs on one station");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Jobs[0].RemainingSeconds = 6)), "remaining above duration");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Jobs[0].RemainingSeconds = -1)), "negative remaining");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s => s.Jobs[0].State = StationJobState.Blocked)), "blocked before completion");
            Assert.Throws<InvalidOperationException>(() => GoodsWorld.Restore(Mutate(s =>
            {
                var lot = JsonUtility.FromJson<GoodsLot>(JsonUtility.ToJson(s.Lots[0]));
                lot.Id = s.Jobs[0].Id + ":out";
                lot.Quantity = 1;
                lot.LocationId = "pantry";
                s.Lots.Add(lot);
            })), "output ID collision");
        }
    }
}
