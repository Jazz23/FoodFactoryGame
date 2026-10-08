// Decision 0039: districts spawn per game hour (GameClock.HourSeconds), one long clock step matches many one-second steps, and a
// v20 save's per-3600 s rates upgrade in memory to re-tuned game-hour rates with their spawn progress kept as a fraction of one
// customer, writing nothing. TEST-ONLY districts with no restaurant in range, so every spawned customer stays home and the
// customer counter counts spawns exactly. Isolated saves only.
using System;
using System.IO;
using System.Linq;
using FoodFactoryGame.World;
using NUnit.Framework;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class DemandClockTests
    {
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryDemandClockTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private static GoodsWorld World(int customersPerHour, long progress = 0)
        {
            var world = new GoodsWorld("demand-clock-test");
            world.Bootstrap(new GoodsDistrict
            {
                Id = "district", Name = "Test", CustomersPerHour = customersPerHour, WealthPercent = 50, AppearanceVariants = 1,
                DineInPercent = 50, RangeMetres = 10, SpawnProgress = progress
            });
            return world;
        }

        [Test]
        public void ADistrictSpawnsItsRateEveryGameHour()
        {
            var world = World(6);
            world.Advance(GameClock.HourSeconds - 1);
            Assert.That(world.Snapshot().NextCustomerNumber, Is.EqualTo(5), "Not the sixth before the hour is out.");
            world.Advance(1);
            Assert.That(world.Snapshot().NextCustomerNumber, Is.EqualTo(6));
            world.Advance(9 * GameClock.HourSeconds);
            Assert.That(world.Snapshot().NextCustomerNumber, Is.EqualTo(60), "Ten game hours, sixty customers.");
            Assert.That(world.Snapshot().Districts.Single().SpawnProgress, Is.Zero);
        }

        [Test]
        public void OneLongStepMatchesManyOneSecondSteps()
        {
            var stepped = World(7, 13);
            var jumped = World(7, 13);
            for (var second = 0; second < 250; second++) stepped.Advance(1);
            jumped.Advance(250);
            var a = stepped.Snapshot();
            var b = jumped.Snapshot();
            Assert.That((a.NextCustomerNumber, a.Districts.Single().SpawnProgress), Is.EqualTo((b.NextCustomerNumber, b.Districts.Single().SpawnProgress)));
            Assert.That(a.NextCustomerNumber, Is.EqualTo((13 + 7 * 250) / GameClock.HourSeconds));
        }

        [Test]
        public void ValidateRefusesProgressOfAWholeCustomer()
        {
            Assert.Throws<ArgumentException>(() => World(6, GameClock.HourSeconds));
            Assert.DoesNotThrow(() => World(6, GameClock.HourSeconds - 1));
        }

        [Test]
        public void AV20SaveUpgradesItsRatesToReTunedGameHourRates()
        {
            var world = World(900, 30);
            GoodsSnapshotStore.Save(world, PathForSave);
            // The same save shaped as v20: 900 customers per 3600 clock s, half way (1800 of 3600) to the next one.
            var v20 = SnapshotDatabase.LatestPayload(PathForSave)
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":20")
                .Replace("\"SpawnProgress\":30", "\"SpawnProgress\":1800");
            Assert.That(v20, Does.Contain("\"SchemaVersion\":20").And.Contain("\"SpawnProgress\":1800"), "The payload is shaped like a v20 save.");
            SnapshotDatabase.WritePayload(PathForSave, v20);

            var upgraded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            var district = upgraded.Districts.Single();
            Assert.That((upgraded.SchemaVersion, district.CustomersPerHour, district.SpawnProgress),
                Is.EqualTo((GoodsSnapshot.CurrentSchema, GameClock.FromClockHourRate(900), (long)GameClock.HourSeconds / 2)));
            Assert.That(SnapshotDatabase.LatestPayload(PathForSave), Is.EqualTo(v20), "Loading upgrades in memory and writes nothing.");

            // Per clock second the upgraded district makes 60 / RetuneDivisor times its old demand.
            var restored = GoodsWorld.Restore(upgraded);
            restored.Advance(10 * GameClock.HourSeconds);
            Assert.That(restored.Snapshot().NextCustomerNumber, Is.EqualTo(10 * GameClock.FromClockHourRate(900)));
        }
    }
}
