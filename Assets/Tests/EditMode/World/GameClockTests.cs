// Decision 0039: road traffic and customer demand share one game hour, and district rates stored before it (layout format 4
// and older) read as the re-tuned per-game-hour rates a format 5 layout stores, so older and newer worlds agree.
using System.Linq;
using FoodFactoryGame.World;
using NUnit.Framework;

namespace FoodFactoryGame.World.Tests
{
    public sealed class GameClockTests
    {
        [Test]
        public void TrafficCountsTheGameHour()
        {
            Assert.That((RoadTraffic.HourSeconds, RoadTraffic.DaySeconds), Is.EqualTo((GameClock.HourSeconds, GameClock.DaySeconds)));
            Assert.That(RoadTraffic.Hour(GameClock.HourSeconds * 8 + 1), Is.EqualTo(8));
        }

        [Test]
        public void OldClockHourRatesAreReTunedHalfUp()
        {
            Assert.That(GameClock.FromClockHourRate(0), Is.Zero);
            Assert.That(GameClock.FromClockHourRate(-5), Is.Zero);
            Assert.That(GameClock.FromClockHourRate(4), Is.EqualTo(0));
            Assert.That(GameClock.FromClockHourRate(5), Is.EqualTo(1));
            Assert.That(GameClock.FromClockHourRate(900), Is.EqualTo(900 / GameClock.RetuneDivisor));
        }

        [Test]
        public void TheDefaultDistrictRatesAreGeneratorV5RatesReTuned()
        {
            // Generator v5's per-3600 s rates (WorldSettings before decision 0039).
            var v5 = new[] { (DistrictKind.Downtown, 900), (DistrictKind.Residential, 500), (DistrictKind.Wealthy, 350), (DistrictKind.Industrial, 120) };
            var settings = WorldSettings.Default;
            Assert.That(v5.Select(x => settings.Profile(x.Item1).CustomersPerHour), Is.EqualTo(v5.Select(x => GameClock.FromClockHourRate(x.Item2))));
        }

        [Test]
        public void AFormat4LayoutReadsItsRatesAsReTunedGameHourRates()
        {
            var layout = WorldGenerator.Generate("clock", 20261007).Layout;
            Assert.That(layout.FormatVersion, Is.EqualTo(WorldLayout.CurrentFormat));
            var current = layout.Districts.Select(layout.RatePerGameHour).ToList();
            Assert.That(current, Is.EqualTo(layout.Districts.Select(x => x.CustomersPerHour)), "Format 5 stores game-hour rates as they are.");

            // The same city as a format 4 layout stored it: per 3600 clock s, and RetuneDivisor times larger.
            var older = WorldLayoutText.Read(WorldLayoutText.Write(layout));
            older.FormatVersion = 4;
            foreach (var district in older.Districts) district.CustomersPerHour *= GameClock.RetuneDivisor;
            Assert.That(older.Districts.Select(older.RatePerGameHour), Is.EqualTo(current));
        }
    }
}
