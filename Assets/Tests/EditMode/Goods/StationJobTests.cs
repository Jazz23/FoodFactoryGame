// Verifies server-authoritative station jobs: atomic input consumption, clock-driven output, pickup refunds, durability and schema v2.
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class StationJobTests
    {
        [Serializable] private sealed class TestEnvelope
        {
            public string Payload;
            public string Sha256;
        }

        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "goods.snapshot");
        private string BadPath => Path.Combine(_saveDirectory, "missing", "goods.snapshot");

        [SetUp]
        public void SetUp()
        {
            _world = CreateWorld();
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryStationJobTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        // TEST-ONLY values: one restaurant with an oven (input 10, output 4), a refrigerated-output oven, a pantry,
        // a player carried location of 5, and a 5-second recipe turning 2 dough + 1 sauce into 1 pizza.
        // Not gameplay content or configuration.
        private static GoodsWorld CreateWorld()
        {
            var world = new GoodsWorld("test-world");
            world.Bootstrap(new GoodsLocation { Id = "pantry", SiteId = "restaurant", Kind = "storage", Capacity = 50 });
            world.Bootstrap(new GoodsLocation { Id = "oven-in", SiteId = "restaurant", Kind = "machine-buffer", Capacity = 10 });
            world.Bootstrap(new GoodsLocation { Id = "oven-out", SiteId = "restaurant", Kind = "machine-buffer", Capacity = 4 });
            world.Bootstrap(new GoodsLocation { Id = "cold-in", SiteId = "restaurant", Kind = "machine-buffer", Capacity = 10 });
            world.Bootstrap(new GoodsLocation { Id = "cold-out", SiteId = "restaurant", Kind = "machine-buffer", Capacity = 4, Refrigerated = true });
            world.Bootstrap(new GoodsLocation { Id = "hands", SiteId = "restaurant", Kind = "carried", Capacity = 5 });
            world.Bootstrap(new GoodsLocation { Id = "elsewhere", SiteId = "warehouse", Kind = "storage", Capacity = 5 });
            world.Bootstrap(new GoodsStation { Id = "oven-1", SiteId = "restaurant", Kind = "oven", InputLocationId = "oven-in", OutputLocationId = "oven-out" });
            world.Bootstrap(new GoodsStation { Id = "oven-cold", SiteId = "restaurant", Kind = "oven", InputLocationId = "cold-in", OutputLocationId = "cold-out" });
            world.Bootstrap(new GoodsLot { Id = "dough-a", ItemId = "dough", OwnerId = "restaurant", LocationId = "oven-in", Quantity = 3, SpoilAfterSeconds = 100 });
            world.Bootstrap(new GoodsLot { Id = "dough-b", ItemId = "dough", OwnerId = "restaurant", LocationId = "oven-in", Quantity = 2, ExposureSeconds = 4, SpoilAfterSeconds = 100 });
            world.Bootstrap(new GoodsLot { Id = "sauce", ItemId = "sauce", OwnerId = "restaurant", LocationId = "oven-in", Quantity = 2, SpoilAfterSeconds = 100 });
            world.Bootstrap(new GoodsLot { Id = "cold-dough", ItemId = "dough", OwnerId = "restaurant", LocationId = "cold-in", Quantity = 2, SpoilAfterSeconds = 100 });
            world.Bootstrap(new GoodsLot { Id = "cold-sauce", ItemId = "sauce", OwnerId = "restaurant", LocationId = "cold-in", Quantity = 1, SpoilAfterSeconds = 100 });
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
            return JsonUtility.ToJson(new GoodsSnapshot { WorldId = "x", Lots = state.Lots, Reservations = state.Reservations, Stations = state.Stations, Jobs = state.Jobs });
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
            Assert.That(pizza.LocationId, Is.EqualTo("oven-out"));
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
            _world.Bootstrap(new GoodsLot { Id = "old-cheese", ItemId = "cheese", OwnerId = "restaurant", LocationId = "oven-in", Quantity = 1, ExposureSeconds = 5, SpoilAfterSeconds = 5, Spoiled = true });
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
            Assert.That(_world.Snapshot().Lots.Single(x => x.LocationId == "oven-out").ExposureSeconds, Is.EqualTo(7));
            Assert.That(_world.Snapshot().Lots.Single(x => x.LocationId == "cold-out").ExposureSeconds, Is.Zero);
            Assert.That(_world.Snapshot().ClockSeconds, Is.EqualTo(stepped.Snapshot().ClockSeconds));
        }

        [Test]
        public void FullOutputBlocksJobUntilRoomWithoutLoss()
        {
            _world.Bootstrap(new GoodsLot { Id = "filler", ItemId = "plate", OwnerId = "restaurant", LocationId = "oven-out", Quantity = 4, SpoilAfterSeconds = 1000 });
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
            File.WriteAllText(PathForSave, "corrupt");

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
        public void PickupRefundsUnprocessedInputsToCarriedLocation()
        {
            var started = Start("bake");
            _world.Advance(3);
            var removed = _world.RemoveStation("chef", "pickup", "oven-1", "hands");
            Assert.That(removed.Accepted, Is.True);
            Assert.That(removed.JobId, Is.EqualTo(started.JobId));
            var state = _world.Snapshot();
            Assert.That(state.Stations.Any(x => x.Id == "oven-1"), Is.False);
            Assert.That(state.Jobs, Is.Empty);
            var carried = state.Lots.Where(x => x.LocationId == "hands").ToList();
            Assert.That(carried.Where(x => x.ItemId == "dough").Sum(x => x.Quantity), Is.EqualTo(2));
            Assert.That(carried.Where(x => x.ItemId == "sauce").Sum(x => x.Quantity), Is.EqualTo(1));
            Assert.That(carried.Single(x => x.ItemId == "dough").ExposureSeconds, Is.EqualTo(4), "Inputs do not age while processing.");
            Assert.That(carried.All(x => x.OwnerId == "restaurant"), Is.True);
            Assert.That(Count(_world, "dough"), Is.EqualTo(7));
            Assert.That(Count(_world, "sauce"), Is.EqualTo(3));
            Assert.That(state.Lots.Any(x => x.ItemId == "pizza"), Is.False);
            Assert.That(_world.RemoveStation("chef", "pickup", "oven-1", "hands").Accepted, Is.True, "Replay returns the stored outcome.");
            Assert.That(_world.Snapshot().Lots.Count(x => x.LocationId == "hands"), Is.EqualTo(2));
        }

        [Test]
        public void PickupOfBlockedJobHandsOverFinishedOutput()
        {
            _world.Bootstrap(new GoodsLot { Id = "filler", ItemId = "plate", OwnerId = "restaurant", LocationId = "oven-out", Quantity = 4, SpoilAfterSeconds = 1000 });
            var started = Start("bake");
            _world.Advance(10);
            Assert.That(_world.RemoveStation("chef", "pickup", "oven-1", "hands").Accepted, Is.True);
            var pizza = _world.Snapshot().Lots.Single(x => x.ItemId == "pizza");
            Assert.That(pizza.Id, Is.EqualTo(started.JobId + ":out"));
            Assert.That(pizza.LocationId, Is.EqualTo("hands"));
            Assert.That(Count(_world, "dough"), Is.EqualTo(5));
        }

        [Test]
        public void PickupRejectionsLeaveJobRunning()
        {
            Start("bake");
            _world.Bootstrap(new GoodsLot { Id = "held", ItemId = "plate", OwnerId = "restaurant", LocationId = "hands", Quantity = 3, SpoilAfterSeconds = 1000 });
            var before = Goods(_world);
            Assert.That(_world.RemoveStation("chef", "full", "oven-1", "hands").Reason, Is.EqualTo("capacity"));
            Assert.That(_world.RemoveStation("intruder", "steal", "oven-1", "hands").Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.RemoveStation("chef", "remote", "oven-1", "elsewhere").Reason, Is.EqualTo("invalid-route"));
            Assert.That(_world.RemoveStation("chef", "buffer", "oven-1", "oven-out").Reason, Is.EqualTo("invalid-route"));
            Assert.That(Goods(_world), Is.EqualTo(before));
        }

        [Test]
        public void DurablePickupSurvivesReloadAndFailedCommitRollsBack()
        {
            GoodsSnapshotStore.Save(_world, PathForSave);
            var started = _world.StartJobDurably("chef", "bake", "oven-1", "bake", PathForSave);
            var before = Goods(_world);
            Assert.That(_world.RemoveStationDurably("chef", "pickup", "oven-1", "hands", BadPath).Reason, Is.EqualTo("persistence-unavailable"));
            Assert.That(Goods(_world), Is.EqualTo(before));
            Assert.That(_world.RemoveStationDurably("chef", "pickup", "oven-1", "hands", PathForSave).JobId, Is.EqualTo(started.JobId));
            _world = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(_world.RemoveStationDurably("chef", "pickup", "oven-1", "hands", PathForSave).Accepted, Is.True);
            Assert.That(_world.Snapshot().Lots.Where(x => x.LocationId == "hands").Sum(x => x.Quantity), Is.EqualTo(3));
            Assert.That(Count(_world, "dough"), Is.EqualTo(7));
        }

        [Test]
        public void SchemaV1SaveLoadsAsV2AndIsRewrittenAsV2()
        {
            var legacy = new GoodsWorld("legacy-world");
            legacy.Bootstrap(new GoodsLocation { Id = "storage", SiteId = "restaurant", Kind = "storage", Capacity = 20 });
            legacy.Bootstrap(new GoodsLot { Id = "lot-1", ItemId = "ingredient", OwnerId = "restaurant", LocationId = "storage", Quantity = 10, SpoilAfterSeconds = 10 });
            var v2 = JsonUtility.ToJson(legacy.Snapshot());
            var v1 = v2.Replace("\"SchemaVersion\":2", "\"SchemaVersion\":1").Replace(",\"Stations\":[],\"Jobs\":[]", "");
            Assert.That(v1, Does.Not.Contain("Stations"));
            Assert.That(v1, Does.Contain("\"SchemaVersion\":1"));
            File.WriteAllText(PathForSave, JsonUtility.ToJson(new TestEnvelope { Payload = v1, Sha256 = Digest(v1) }), new UTF8Encoding(false));

            var loaded = GoodsSnapshotStore.Load(PathForSave);
            var state = loaded.Snapshot();
            Assert.That(state.SchemaVersion, Is.EqualTo(2));
            Assert.That(state.Stations, Is.Empty);
            Assert.That(state.Jobs, Is.Empty);
            Assert.That(state.Lots.Single().Quantity, Is.EqualTo(10));

            Assert.That(loaded.TryAdvanceDurably(1, PathForSave), Is.True);
            var written = JsonUtility.FromJson<TestEnvelope>(File.ReadAllText(PathForSave)).Payload;
            Assert.That(written, Does.Contain("\"SchemaVersion\":2"));
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

        private static string Digest(string payload)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
        }
    }
}
