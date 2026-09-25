// Measures the PROTOTYPE customer choice model (CustomerChoiceModel) at the GDD section 28 target of 1,000 customers and 20
// sites, plus a lunch-rush burst and a tenfold population. Budgets are PROVISIONAL: no server tick rate or server budget is
// decided, so these assume a 10 Hz tick and give customer choice a small slice of it. Editor (Mono JIT) timings are not
// player or dedicated-server timings. Each run logs one "[Benchmark]" line with the machine, timings and outcome counts.
using System;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Debug = UnityEngine.Debug;
using Is = NUnit.Framework.Is;
using UnityIs = UnityEngine.TestTools.Constraints.Is;

namespace FoodFactoryGame.Benchmarks.Tests
{
    [Category("Benchmark")]
    public sealed class CustomerChoiceBenchmarkTests
    {
        // PROVISIONAL budgets for 1,000 customers at a 10 Hz tick (100 ms): a steady tick and a burst where every customer
        // decides at once.
        private const double TargetMeanMs = 0.5;
        private const double TargetP99Ms = 1.0;
        private const double BurstMedianMs = 2.0;
        private const double TenfoldMeanMs = 5.0;

        private const int TwentyMinutesOfTicks = 12000;

        [OneTimeSetUp]
        public void WarmUp()
        {
            // JIT-compile every path before any timing.
            var model = new CustomerChoiceModel(new CustomerChoiceSettings { Customers = 200, LunchRush = true });
            for (var i = 0; i < 3000; i++)
                model.Step();
            var live = new CustomerChoiceModel(new CustomerChoiceSettings { Customers = 200, Wait = WaitSignal.Live, Rule = ChoiceRule.BestScore });
            for (var i = 0; i < 3000; i++)
                live.Step();
        }

        [Test]
        public void TargetPopulation_SteadyTickFitsProvisionalBudget()
        {
            var run = Measure("target 1000 customers / 20 sites", new CustomerChoiceSettings(), TwentyMinutesOfTicks);

            Assert.That(run.Summary.Served, Is.GreaterThan(1000), "the model must actually serve customers");
            Assert.That(run.MeanMs, Is.LessThanOrEqualTo(TargetMeanMs), "mean tick");
            Assert.That(run.P99Ms, Is.LessThanOrEqualTo(TargetP99Ms), "p99 tick");
            AssertNoTickAllocations(run.Model);
        }

        [Test]
        public void LunchRush_EveryCustomerDecidesInOneTick()
        {
            const int repeats = 15;
            var times = new double[repeats];
            var settings = new CustomerChoiceSettings { LunchRush = true };
            CustomerChoiceModel model = null;
            for (var i = 0; i < repeats; i++)
            {
                settings.Seed = 1000UL + (ulong)i;
                model = new CustomerChoiceModel(settings);
                var start = Stopwatch.GetTimestamp();
                model.Step();
                times[i] = ToMs(Stopwatch.GetTimestamp() - start);
                Assert.That(model.Decisions, Is.EqualTo(settings.Customers), "every customer decides in the first tick");
                Assert.That(model.CheckInvariants(), Is.Null);
            }

            Array.Sort(times);
            var median = times[repeats / 2];
            Report($"lunch rush 1000 customers / 20 sites: burst tick median={median:F3}ms max={times[repeats - 1]:F3}ms over {repeats} worlds; " +
                   $"evaluations in last burst={model.Evaluations}");
            Assert.That(median, Is.LessThanOrEqualTo(BurstMedianMs), "median burst tick");
        }

        [Test]
        public void TenfoldPopulation_ReportsScaling()
        {
            var settings = new CustomerChoiceSettings { Customers = 10000, Restaurants = 200, WorldMetres = 6000f };
            var run = Measure("tenfold 10000 customers / 200 sites", settings, 3000);

            Assert.That(run.MeanMs, Is.LessThanOrEqualTo(TenfoldMeanMs), "mean tick");
            AssertNoTickAllocations(run.Model);
        }

        [Test]
        public void WaitSignal_ComparesLiveBestScoreWithPublishedLogit()
        {
            // Evidence for the design discussion, not a requirement: does a live queue plus always-best choice pile customers
            // into one restaurant compared with a published estimate plus weighted choice? Only consistency is asserted.
            var live = Measure("live wait + best score", new CustomerChoiceSettings { Wait = WaitSignal.Live, Rule = ChoiceRule.BestScore }, TwentyMinutesOfTicks);
            var published = Measure("published wait + logit", new CustomerChoiceSettings(), TwentyMinutesOfTicks);

            Assert.That(live.Summary.Served, Is.GreaterThan(0));
            Assert.That(published.Summary.Served, Is.GreaterThan(0));
        }

        [Test]
        public void AllocationCheck_DetectsAnAllocation()
        {
            // Guards the zero-allocation assertions above against passing because nothing is measured.
            TestDelegate allocates = () => GC.KeepAlive(new byte[64]);
            Assert.That(allocates, UnityIs.AllocatingGCMemory());
        }

        [Test]
        public void SameSeed_GivesSameOutcome()
        {
            var first = Run(new CustomerChoiceSettings { Seed = 77 }, 3000);
            var second = Run(new CustomerChoiceSettings { Seed = 77 }, 3000);

            Assert.That(second.ToString(), Is.EqualTo(first.ToString()));
        }

        private static CustomerChoiceSummary Run(CustomerChoiceSettings settings, int ticks)
        {
            var model = new CustomerChoiceModel(settings);
            for (var i = 0; i < ticks; i++)
                model.Step();
            Assert.That(model.CheckInvariants(), Is.Null);
            return model.Summarize();
        }

        private static BenchmarkRun Measure(string label, CustomerChoiceSettings settings, int ticks)
        {
            var model = new CustomerChoiceModel(settings);
            var times = new double[ticks];
            for (var i = 0; i < ticks; i++)
            {
                var start = Stopwatch.GetTimestamp();
                model.Step();
                times[i] = Stopwatch.GetTimestamp() - start;
            }

            Assert.That(model.CheckInvariants(), Is.Null);

            var total = 0.0;
            for (var i = 0; i < ticks; i++)
            {
                times[i] = ToMs((long)times[i]);
                total += times[i];
            }
            Array.Sort(times);

            var run = new BenchmarkRun
            {
                MeanMs = total / ticks,
                P50Ms = times[ticks / 2],
                P99Ms = times[(int)(ticks * 0.99)],
                MaxMs = times[ticks - 1],
                Model = model,
                Summary = model.Summarize(),
            };

            Report($"{label}: ticks={ticks} tick={settings.TickSeconds}s simulated={model.Now:F0}s mean={run.MeanMs:F4}ms " +
                   $"p50={run.P50Ms:F4}ms p99={run.P99Ms:F4}ms max={run.MaxMs:F4}ms; {run.Summary}");
            return run;
        }

        // Unity's Mono does not implement GC.GetAllocatedBytesForCurrentThread, so allocations are checked with the Test
        // Framework's profiler-based constraint over further ticks of the same, already running world.
        private static void AssertNoTickAllocations(CustomerChoiceModel model)
        {
            TestDelegate ticks = () =>
            {
                for (var i = 0; i < 600; i++)
                    model.Step();
            };
            Assert.That(ticks, UnityIs.Not.AllocatingGCMemory(), "a tick allocated managed memory");
        }

        private static double ToMs(long stopwatchTicks) => stopwatchTicks * 1000.0 / Stopwatch.Frequency;

        private static void Report(string line)
        {
            var message = $"[Benchmark] {line} | cpu=\"{SystemInfo.processorType}\" cores={SystemInfo.processorCount} unity={Application.unityVersion} editor-mono";
            TestContext.WriteLine(message);
            Debug.Log(message);
        }

        private struct BenchmarkRun
        {
            public double MeanMs, P50Ms, P99Ms, MaxMs;
            public CustomerChoiceModel Model;
            public CustomerChoiceSummary Summary;
        }
    }
}
