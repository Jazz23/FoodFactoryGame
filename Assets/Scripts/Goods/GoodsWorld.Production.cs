// Station jobs share GoodsWorld's lock and snapshot so input consumption, output creation and refunds commit atomically with goods.
// A job keeps copies of its consumed inputs and its recipe output, so recovery and pickup never depend on registered recipe content.
// Stations are created and removed only with placed equipment (GoodsWorld.Equipment.cs).
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
        // Work in progress: consumed input slices with their exposure frozen at start. LocationId is empty until refunded.
        public List<GoodsLot> Inputs = new();

        public string OutputLotId => Id + ":out";
    }

    public sealed partial class GoodsWorld
    {
        private readonly Dictionary<string, RecipeDefinition> _recipes = new();

        public void RegisterRecipe(RecipeDefinition recipe)
        {
            lock (_gate)
            {
                if (recipe == null || string.IsNullOrWhiteSpace(recipe.Id) || string.IsNullOrWhiteSpace(recipe.StationKind)
                    || recipe.DurationSeconds < 1 || recipe.Inputs == null || recipe.Inputs.Count == 0
                    || recipe.Inputs.Any(x => x == null || string.IsNullOrWhiteSpace(x.ItemId) || x.Quantity < 1)
                    || recipe.Inputs.GroupBy(x => x.ItemId).Any(x => x.Count() != 1)
                    || string.IsNullOrWhiteSpace(recipe.OutputItemId) || recipe.OutputQuantity < 1
                    || recipe.OutputSpoilAfterSeconds < 1 || _recipes.ContainsKey(recipe.Id))
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

                // Most-exposed edible lots are consumed first; lot ID breaks ties so the choice is deterministic.
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
                    if (needed > 0) return Record(requestId, playerId, false, "missing-inputs", null);
                }

                // All checks precede this single locked mutation.
                var job = new StationJob
                {
                    Id = Guid.NewGuid().ToString("N"), StationId = station.Id, RecipeId = recipe.Id, StartedBy = playerId,
                    StartedAtSeconds = _state.ClockSeconds, DurationSeconds = recipe.DurationSeconds,
                    RemainingSeconds = recipe.DurationSeconds, State = StationJobState.Running,
                    OutputItemId = recipe.OutputItemId, OutputQuantity = recipe.OutputQuantity,
                    OutputSpoilAfterSeconds = recipe.OutputSpoilAfterSeconds
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

        private void EmitBlockedOutputs()
        {
            foreach (var job in _state.Jobs.Where(x => x.State == StationJobState.Blocked).ToList())
            {
                var station = _state.Stations.First(x => x.Id == job.StationId);
                if (!Fits(station.OutputLocationId, job.OutputQuantity)) continue;
                _state.Lots.Add(OutputLot(job, station, station.OutputLocationId, 0));
                _state.Jobs.Remove(job);
            }
        }

        // Completion is exactly StartedAt + Duration. Ambient overshoot past that instant counts as output exposure,
        // so one large step matches many small ones.
        private void ProgressJobs(long seconds)
        {
            foreach (var job in _state.Jobs.Where(x => x.State == StationJobState.Running).ToList())
            {
                var worked = Math.Min(seconds, job.RemainingSeconds);
                job.RemainingSeconds -= worked;
                if (job.RemainingSeconds > 0) continue;
                var station = _state.Stations.First(x => x.Id == job.StationId);
                var output = _state.Locations.First(x => x.Id == station.OutputLocationId);
                if (!Fits(output.Id, job.OutputQuantity))
                {
                    job.State = StationJobState.Blocked;
                    continue;
                }
                _state.Lots.Add(OutputLot(job, station, output.Id, output.Refrigerated ? 0 : seconds - worked));
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
                    || string.IsNullOrWhiteSpace(x.OutputItemId) || x.OutputQuantity < 1 || x.OutputSpoilAfterSeconds < 1
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
