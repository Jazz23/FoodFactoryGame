// Station jobs share GoodsWorld's lock and snapshot so input consumption, output creation and refunds commit atomically with goods.
// A job keeps copies of its consumed inputs and its recipe output, so recovery and pickup never depend on registered recipe content.
// Stations are created and removed only with placed equipment (GoodsWorld.Equipment.cs). Jobs start on request, or by
// themselves when the server turns AutomaticJobs on. A sale recipe (decision 0013) is a job whose result is cash for the
// site's company instead of goods: its inputs leave the world and the company is paid in the same commit.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class RecipeInput
    {
        public string ItemId;
        public int Quantity;
    }

    // Content, not state: registered by the server at bootstrap and never saved.
    [Serializable] public sealed class RecipeDefinition
    {
        public string Id;
        public string StationKind;
        public long DurationSeconds;
        public List<RecipeInput> Inputs = new();
        public string OutputItemId;
        public int OutputQuantity;
        public long OutputSpoilAfterSeconds;
        // A sale (decision 0013): when above zero the recipe produces no goods; completing it pays this many cents to the
        // company that owns the station's site. Output fields are then empty/zero.
        public long SaleCents;

        public bool IsSale => SaleCents > 0;
    }

    [Serializable] public sealed class GoodsStation
    {
        public string Id;
        public string SiteId;
        public string Kind;
        public string InputLocationId;
        public string OutputLocationId;
    }

    public enum StationJobState
    {
        Running,
        Blocked
    }

    [Serializable] public sealed class StationJob
    {
        public string Id;
        public string StationId;
        public string RecipeId;
        public string StartedBy;
        public long StartedAtSeconds;
        public long DurationSeconds;
        public long RemainingSeconds;
        public StationJobState State;
        public string OutputItemId;
        public int OutputQuantity;
        public long OutputSpoilAfterSeconds;
        // Copied from a sale recipe: cents paid to the site's company on completion. A sale job has no goods output and is
        // never blocked; picking the station up mid-sale refunds its inputs and pays nothing.
        public long SaleCents;
        // Work in progress: consumed input slices with their exposure frozen at start. LocationId is empty until refunded.
        public List<GoodsLot> Inputs = new();

        public string OutputLotId => Id + ":out";
        public bool IsSale => SaleCents > 0;
    }

    public sealed partial class GoodsWorld
    {
        // StartedBy of a job an automatic station started for itself.
        public const string AutomaticStarter = "automatic";

        private readonly Dictionary<string, RecipeDefinition> _recipes = new();
        private bool _automaticJobs;

        // Server configuration, like recipes: never saved, set by the server owner on every start. When on, stations start
        // batches by themselves (StartReadyJobs); explicit StartJob requests still work when a station is idle.
        public bool AutomaticJobs
        {
            get { lock (_gate) return _automaticJobs; }
            set { lock (_gate) _automaticJobs = value; }
        }

        public void RegisterRecipe(RecipeDefinition recipe)
        {
            lock (_gate)
            {
                if (recipe == null || string.IsNullOrWhiteSpace(recipe.Id) || string.IsNullOrWhiteSpace(recipe.StationKind)
                    || recipe.DurationSeconds < 1 || recipe.Inputs == null || recipe.Inputs.Count == 0
                    || recipe.Inputs.Any(x => x == null || string.IsNullOrWhiteSpace(x.ItemId) || x.Quantity < 1)
                    || recipe.Inputs.GroupBy(x => x.ItemId).Any(x => x.Count() != 1)
                    || recipe.SaleCents < 0
                    // Exactly one result: goods, or a sale with no goods output.
                    || (recipe.IsSale
                        ? !string.IsNullOrEmpty(recipe.OutputItemId) || recipe.OutputQuantity != 0 || recipe.OutputSpoilAfterSeconds != 0
                        : string.IsNullOrWhiteSpace(recipe.OutputItemId) || recipe.OutputQuantity < 1 || recipe.OutputSpoilAfterSeconds < 1)
                    || _recipes.ContainsKey(recipe.Id))
                    throw new ArgumentException("Invalid or duplicate recipe.");
                _recipes.Add(recipe.Id, JsonUtility.FromJson<RecipeDefinition>(JsonUtility.ToJson(recipe)));
            }
        }

        // Volatile primitive for tests. Live request handlers must call StartJobDurably.
        public GoodsOutcome StartJob(string playerId, string requestId, string stationId, string recipeId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                var station = _state.Stations.FirstOrDefault(x => x.Id == stationId);
                if (station == null || !_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == station.SiteId))
                    return Record(requestId, playerId, false, "forbidden", null);
                if (recipeId == null || !_recipes.TryGetValue(recipeId, out var recipe) || recipe.StationKind != station.Kind)
                    return Record(requestId, playerId, false, "invalid-recipe", null);
                if (_state.Jobs.Any(x => x.StationId == station.Id))
                    return Record(requestId, playerId, false, "station-busy", null);
                if (recipe.IsSale && CompanyOfSiteLocked(station.SiteId) is null)
                    return Record(requestId, playerId, false, "no-company", null);
                var plan = InputPlan(station, recipe);
                if (plan == null) return Record(requestId, playerId, false, "missing-inputs", null);

                // All checks precede this single locked mutation.
                var job = AddJob(station, recipe, plan, playerId);
                var result = Record(requestId, playerId, true, "started", null);
                result.JobId = job.Id;
                _state.Outcomes[_state.Outcomes.Count - 1].JobId = job.Id;
                return result;
            }
        }

        public GoodsOutcome StartJobDurably(string playerId, string requestId, string stationId, string recipeId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => StartJob(playerId, requestId, stationId, recipeId));
        }

        // Most-exposed edible lots are consumed first; lot ID breaks ties so the choice is deterministic. Null if any input is short.
        private List<(GoodsLot Lot, int Take)> InputPlan(GoodsStation station, RecipeDefinition recipe)
        {
            var plan = new List<(GoodsLot Lot, int Take)>();
            foreach (var input in recipe.Inputs)
            {
                var needed = input.Quantity;
                var candidates = _state.Lots
                    .Where(x => x.LocationId == station.InputLocationId && x.ItemId == input.ItemId
                        && x.OwnerId == station.SiteId && !x.Spoiled && Available(x) > 0)
                    .OrderByDescending(x => x.ExposureSeconds).ThenBy(x => x.Id, StringComparer.Ordinal);
                foreach (var lot in candidates)
                {
                    if (needed == 0) break;
                    var take = Math.Min(needed, Available(lot));
                    plan.Add((lot, take));
                    needed -= take;
                }
                if (needed > 0) return null;
            }
            return plan;
        }

        private StationJob AddJob(GoodsStation station, RecipeDefinition recipe, List<(GoodsLot Lot, int Take)> plan, string startedBy)
        {
            var job = new StationJob
            {
                Id = Guid.NewGuid().ToString("N"), StationId = station.Id, RecipeId = recipe.Id, StartedBy = startedBy,
                StartedAtSeconds = _state.ClockSeconds, DurationSeconds = recipe.DurationSeconds,
                RemainingSeconds = recipe.DurationSeconds, State = StationJobState.Running,
                OutputItemId = recipe.OutputItemId ?? "", OutputQuantity = recipe.OutputQuantity,
                OutputSpoilAfterSeconds = recipe.OutputSpoilAfterSeconds, SaleCents = recipe.SaleCents
            };
            foreach (var (lot, take) in plan)
            {
                job.Inputs.Add(new GoodsLot
                {
                    Id = job.Id + ":in:" + job.Inputs.Count, ItemId = lot.ItemId, OwnerId = lot.OwnerId, LocationId = "",
                    Quantity = take, ExposureSeconds = lot.ExposureSeconds, SpoilAfterSeconds = lot.SpoilAfterSeconds
                });
                lot.Quantity -= take;
                if (lot.Quantity == 0) _state.Lots.Remove(lot);
            }
            _state.Jobs.Add(job);
            return job;
        }

        // Automatic stations (decision 0008): an idle station starts the first recipe for its kind, by recipe ID, whose inputs
        // are present and whose output fits the output buffer now. Runs inside the caller's locked mutation (an accepted
        // transfer or a clock step), so the start commits atomically with it. A full output stops new starts, not a batch.
        private void StartReadyJobs()
        {
            if (!_automaticJobs) return;
            foreach (var station in _state.Stations) StartReadyJob(station);
        }

        // Starts the first ready recipe on an idle station, or returns null.
        private StationJob StartReadyJob(GoodsStation station)
        {
            if (_state.Jobs.Any(x => x.StationId == station.Id)) return null;
            foreach (var recipe in _recipes.Values.Where(x => x.StationKind == station.Kind).OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                // A sale needs a company to pay; goods need room for their output.
                if (recipe.IsSale ? CompanyOfSiteLocked(station.SiteId) is null
                        : !Fits(station.OutputLocationId, recipe.OutputItemId, false, recipe.OutputQuantity)) continue;
                var plan = InputPlan(station, recipe);
                if (plan == null) continue;
                return AddJob(station, recipe, plan, AutomaticStarter);
            }
            return null;
        }

        // A sale completes by paying the site's company and removing the job; its inputs already left the world. If the
        // balance has no headroom for the price, the job waits at zero remaining (goods kept, nothing thrown) and is retried
        // on the next step. Validate guarantees the site has a company while a sale job exists.
        private bool TryCompleteSale(StationJob job, GoodsStation station)
        {
            var company = _state.Companies.First(x => x.SiteIds.Contains(station.SiteId));
            if (company.Cash > long.MaxValue - job.SaleCents) return false;
            TryCredit(company.Id, job.SaleCents);
            _state.Jobs.Remove(job);
            return true;
        }

        private void EmitBlockedOutputs()
        {
            foreach (var job in _state.Jobs.Where(x => x.State == StationJobState.Blocked).ToList())
            {
                var station = _state.Stations.First(x => x.Id == job.StationId);
                var lot = OutputLot(job, station, station.OutputLocationId, 0);
                if (!Fits(lot.LocationId, lot.ItemId, lot.Spoiled, lot.Quantity)) continue;
                _state.Lots.Add(lot);
                _state.Jobs.Remove(job);
            }
        }

        // Completion is exactly StartedAt + Duration. Ambient overshoot past that instant counts as output exposure,
        // so one large step matches many small ones.
        private void ProgressJobs(long seconds)
        {
            foreach (var job in _state.Jobs.Where(x => x.State == StationJobState.Running).ToList())
            {
                // Sale jobs started while catching up earlier in this loop are not in the list; a waiting sale (no headroom)
                // is retried here with zero remaining.
                if (!_state.Jobs.Contains(job)) continue;
                var worked = Math.Min(seconds, job.RemainingSeconds);
                job.RemainingSeconds -= worked;
                if (job.RemainingSeconds > 0) continue;
                var station = _state.Stations.First(x => x.Id == job.StationId);
                if (job.IsSale)
                {
                    // Paid in this same commit. The rest of the step serves the next customers, so revenue does not depend
                    // on how the server divides time: one long step sells what many short ones would.
                    var left = seconds - worked;
                    var sale = job;
                    while (TryCompleteSale(sale, station) && left > 0 && _automaticJobs)
                    {
                        sale = StartReadyJob(station);
                        if (sale is null || !sale.IsSale) break;
                        var served = Math.Min(left, sale.RemainingSeconds);
                        sale.RemainingSeconds -= served;
                        left -= served;
                        if (sale.RemainingSeconds > 0) break;
                    }
                    continue;
                }
                var output = _state.Locations.First(x => x.Id == station.OutputLocationId);
                var lot = OutputLot(job, station, output.Id, output.Refrigerated ? 0 : seconds - worked);
                if (!Fits(output.Id, lot.ItemId, lot.Spoiled, lot.Quantity))
                {
                    job.State = StationJobState.Blocked;
                    continue;
                }
                _state.Lots.Add(lot);
                _state.Jobs.Remove(job);
            }
        }

        private static GoodsLot OutputLot(StationJob job, GoodsStation station, string locationId, long exposure)
        {
            exposure = Math.Min(exposure, job.OutputSpoilAfterSeconds);
            return new GoodsLot
            {
                Id = job.OutputLotId, ItemId = job.OutputItemId, OwnerId = station.SiteId, LocationId = locationId,
                Quantity = job.OutputQuantity, ExposureSeconds = exposure, SpoilAfterSeconds = job.OutputSpoilAfterSeconds,
                Spoiled = exposure >= job.OutputSpoilAfterSeconds
            };
        }

        private static void ValidateProduction(GoodsSnapshot state)
        {
            var lotIds = new HashSet<string>(state.Lots.Select(x => x.Id));
            if (state.Stations.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.SiteId)
                    || string.IsNullOrWhiteSpace(x.Kind)
                    || !state.Locations.Any(y => y.Id == x.InputLocationId && y.SiteId == x.SiteId)
                    || !state.Locations.Any(y => y.Id == x.OutputLocationId && y.SiteId == x.SiteId))
                || state.Stations.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Jobs.Any(x => x == null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.StartedBy)
                    || !state.Stations.Any(y => y.Id == x.StationId)
                    || x.DurationSeconds < 1 || x.RemainingSeconds < 0 || x.RemainingSeconds > x.DurationSeconds
                    || (x.State == StationJobState.Blocked && x.RemainingSeconds != 0)
                    || (x.State != StationJobState.Running && x.State != StationJobState.Blocked)
                    || x.SaleCents < 0
                    || (x.IsSale
                        ? !string.IsNullOrEmpty(x.OutputItemId) || x.OutputQuantity != 0 || x.OutputSpoilAfterSeconds != 0
                            || x.State != StationJobState.Running
                        : string.IsNullOrWhiteSpace(x.OutputItemId) || x.OutputQuantity < 1 || x.OutputSpoilAfterSeconds < 1)
                    || lotIds.Contains(x.OutputLotId)
                    || x.Inputs == null || x.Inputs.Count == 0
                    || x.Inputs.Any(y => y == null || string.IsNullOrWhiteSpace(y.Id) || string.IsNullOrWhiteSpace(y.ItemId)
                        || string.IsNullOrWhiteSpace(y.OwnerId) || y.Quantity < 1 || y.SpoilAfterSeconds < 1
                        || y.ExposureSeconds < 0 || y.Spoiled || y.ExposureSeconds >= y.SpoilAfterSeconds
                        || lotIds.Contains(y.Id)))
                || state.Jobs.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Jobs.GroupBy(x => x.StationId).Any(x => x.Count() != 1)
                || state.Jobs.SelectMany(x => x.Inputs).GroupBy(x => x.Id).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot violates station or job invariants.");
        }
    }
}
