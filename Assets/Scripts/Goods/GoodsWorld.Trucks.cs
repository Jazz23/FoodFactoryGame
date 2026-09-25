// Trucks (decision 0022): company vehicles that repeat a player-set route between two loading docks on different sites of
// the same company, over the public road network. Roads are abstract: each site has a map position and a trip takes the
// Manhattan distance between the two sites divided by the truck's speed. A truck loads at the pickup dock's outgoing buffer
// (its input, <id>:in) and unloads into the dropoff dock's incoming buffer (its output, <id>:out), a few units a second, so
// dock throughput is a real bottleneck. It leaves as soon as the dock has nothing more it may or can load and it has cargo,
// and turns back once empty.
// Cargo is an ordinary location of kind "vehicle" on the reserved "road" site, which nobody is ever granted: players,
// employees and belts cannot reach it, and only the truck's own loading and unloading move goods in or out. Cargo keeps its
// lot IDs, exposure and owner while aboard (trucks are not refrigerated, so goods age in transit) and takes the dropoff
// site as owner when it is unloaded. A dock that is picked up, or a full or empty buffer, makes the truck wait there; goods
// are never dropped or duplicated.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    // A site's place on the region map (whole metres), for road distances. Server data like the site layout.
    [Serializable] public sealed class GoodsSite
    {
        public string Id;
        public string Name;
        public int MapX;
        public int MapZ;
    }

    public enum TruckState
    {
        // No route: stands at SiteId.
        Parked,
        ToPickup,
        Loading,
        ToDropoff,
        Unloading
    }

    [Serializable] public sealed class GoodsTruck
    {
        public string Id;
        public string CompanyId;
        public string Name;
        // Copied from content when the truck is created, like a machine's buffer capacities.
        public int CargoSlots;
        public int SpeedMetresPerSecond;
        public int LoadUnitsPerSecond;
        // Route: equipment IDs of two docks on different sites of the truck's company; both empty while Parked.
        public string PickupDockId = "";
        public string DropoffDockId = "";
        // Items the truck may load at the pickup dock; empty means any.
        public List<string> AllowedItemIds = new();
        public TruckState State;
        // The site the truck stands at or, while driving, the one it left.
        public string SiteId;
        // Where a driving truck arrives; empty otherwise.
        public string DestinationSiteId = "";
        // Seconds of driving left; 0 unless driving.
        public long RemainingSeconds;

        public string CargoLocationId => Id + ":cargo";
        public bool Driving => State is TruckState.ToPickup or TruckState.ToDropoff;
    }

    public sealed partial class GoodsWorld
    {
        public const string DockKind = "dock";
        public const string VehicleLocationKind = "vehicle";
        // Reserved site of every truck's cargo location; never granted, never owned by a company.
        public const string RoadSiteId = "road";
        public const int MaxAllowedItems = 16;

        // Server-only: a site's map record. Its ID must be a site with locations, so grants and companies can refer to it.
        public void Bootstrap(GoodsSite site)
        {
            lock (_gate)
            {
                if (site is null || string.IsNullOrWhiteSpace(site.Id) || site.Id == RoadSiteId || site.Name is null
                    || _state.Sites.Any(x => x.Id == site.Id) || _state.Locations.All(x => x.SiteId != site.Id))
                    throw new ArgumentException("Invalid or duplicate site, or a site without locations.");
                _state.Sites.Add(JsonUtility.FromJson<GoodsSite>(JsonUtility.ToJson(site)));
                _state.Revision++;
            }
        }

        // Server-only: adds a truck and its empty cargo location. A truck may start parked (no route) or on a valid route; the
        // result must satisfy Validate or nothing changes.
        public void Bootstrap(GoodsTruck truck)
        {
            lock (_gate)
            {
                var copy = truck is null ? null : JsonUtility.FromJson<GoodsTruck>(JsonUtility.ToJson(truck));
                if (copy is null || string.IsNullOrWhiteSpace(copy.Id) || _state.Trucks.Any(x => x.Id == copy.Id)
                    || _state.Locations.Any(x => x.Id == copy.CargoLocationId) || copy.CargoSlots < 1)
                    throw new ArgumentException("Invalid or duplicate truck.");
                var before = Snapshot();
                _state.Trucks.Add(copy);
                _state.Locations.Add(new GoodsLocation
                {
                    Id = copy.CargoLocationId, SiteId = RoadSiteId, Kind = VehicleLocationKind, Capacity = copy.CargoSlots
                });
                _state.Revision++;
                try { Validate(_state); }
                catch (InvalidOperationException error)
                {
                    _state = before;
                    throw new ArgumentException("Invalid truck: " + error.Message, error);
                }
            }
        }

        // Server-only: puts a site that has locations into a company's holdings (a new dev site, in the seed).
        public void AddCompanySite(string companyId, string siteId)
        {
            lock (_gate)
            {
                var company = _state.Companies.FirstOrDefault(x => x.Id == companyId);
                if (company is null || string.IsNullOrWhiteSpace(siteId) || siteId == RoadSiteId
                    || _state.Locations.All(x => x.SiteId != siteId) || _state.Companies.Any(x => x.SiteIds.Contains(siteId)))
                    throw new ArgumentException("Unknown company, or a site that does not exist or is already owned.");
                company.SiteIds.Add(siteId);
                _state.Revision++;
            }
        }

        // Whether any location stands on the site (sites exist through their locations).
        public bool HasSite(string siteId)
        {
            lock (_gate) return _state.Locations.Any(x => x.SiteId == siteId);
        }

        // Road distance in metres between two mapped sites: the public roads form a grid, so it is the Manhattan distance.
        // Null if either site has no map record.
        public static long? RoadMetres(GoodsSnapshot state, string fromSiteId, string toSiteId)
        {
            var from = state.Sites?.FirstOrDefault(x => x.Id == fromSiteId);
            var to = state.Sites?.FirstOrDefault(x => x.Id == toSiteId);
            if (from is null || to is null) return null;
            return Math.Abs((long)from.MapX - to.MapX) + Math.Abs((long)from.MapZ - to.MapZ);
        }

        // Whole seconds a truck needs between two sites: none on the same site, otherwise at least one.
        public static long RoadSeconds(GoodsSnapshot state, string fromSiteId, string toSiteId, int speedMetresPerSecond)
        {
            if (fromSiteId == toSiteId) return 0;
            var metres = RoadMetres(state, fromSiteId, toSiteId) ?? 0;
            return Math.Max(1, (metres + speedMetresPerSecond - 1) / Math.Max(1, speedMetresPerSecond));
        }

        // Volatile primitive for tests. Live request handlers must call SetTruckRouteDurably.
        // Checks, in order: identity and replay; the truck (forbidden); both docks (invalid-dock); the player's grants on both
        // dock sites and the truck's company owning both (forbidden); different sites (same-site); the cargo filter
        // (invalid-cargo); map records for both sites (no-road). Then the truck heads for the dropoff if it carries anything,
        // otherwise for the pickup, from the site it stands at or last left.
        public GoodsOutcome SetTruckRoute(string playerId, string requestId, string truckId, string pickupDockId, string dropoffDockId,
            IReadOnlyList<string> allowedItemIds)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                // Like purchases, rejections change nothing and are not recorded; only the accepted change replays.
                GoodsOutcome Reject(string reason) => new()
                {
                    RequestId = requestId, PlayerId = playerId, Accepted = false, Reason = reason, Revision = _state.Revision
                };
                var truck = _state.Trucks.FirstOrDefault(x => x.Id == truckId);
                if (truck is null) return Reject("forbidden");
                var pickup = _state.Equipment.FirstOrDefault(x => x.Id == pickupDockId && x.Kind == DockKind);
                var dropoff = _state.Equipment.FirstOrDefault(x => x.Id == dropoffDockId && x.Kind == DockKind);
                if (pickup is null || dropoff is null) return Reject("invalid-dock");
                if (!CanView(playerId, pickup.SiteId) || !CanView(playerId, dropoff.SiteId)) return Reject("forbidden");
                var items = allowedItemIds?.ToList() ?? new List<string>();
                var problem = RouteProblem(_state, truck.CompanyId, pickup.Id, dropoff.Id) ?? CargoFilterProblem(items);
                if (problem is not null) return Reject(problem);

                truck.PickupDockId = pickup.Id;
                truck.DropoffDockId = dropoff.Id;
                truck.AllowedItemIds = items;
                Dispatch(truck);
                return Record(requestId, playerId, true, "route-set", null);
            }
        }

        public GoodsOutcome SetTruckRouteDurably(string playerId, string requestId, string truckId, string pickupDockId, string dropoffDockId,
            IReadOnlyList<string> allowedItemIds, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => SetTruckRoute(playerId, requestId, truckId, pickupDockId, dropoffDockId, allowedItemIds));
        }

        // Null when two docks form a route for a company's truck; otherwise invalid-dock, forbidden, same-site or no-road.
        private static string RouteProblem(GoodsSnapshot state, string companyId, string pickupDockId, string dropoffDockId)
        {
            var pickup = state.Equipment.FirstOrDefault(x => x.Id == pickupDockId && x.Kind == DockKind);
            var dropoff = state.Equipment.FirstOrDefault(x => x.Id == dropoffDockId && x.Kind == DockKind);
            if (pickup is null || dropoff is null) return "invalid-dock";
            var company = state.Companies.FirstOrDefault(x => x.Id == companyId);
            if (company is null || !company.SiteIds.Contains(pickup.SiteId) || !company.SiteIds.Contains(dropoff.SiteId)) return "forbidden";
            if (pickup.SiteId == dropoff.SiteId) return "same-site";
            return RoadMetres(state, pickup.SiteId, dropoff.SiteId) is null ? "no-road" : null;
        }

        private static string CargoFilterProblem(IReadOnlyCollection<string> items) =>
            items.Count > MaxAllowedItems || items.Any(string.IsNullOrWhiteSpace) || items.Distinct().Count() != items.Count
                ? "invalid-cargo" : null;

        private string DockSite(string dockId) => _state.Equipment.First(x => x.Id == dockId).SiteId;

        private bool HasCargo(GoodsTruck truck) => _state.Lots.Any(x => x.LocationId == truck.CargoLocationId);

        // Sends a routed truck on: loaded to the dropoff, empty to the pickup.
        private void Dispatch(GoodsTruck truck)
        {
            if (HasCargo(truck)) Drive(truck, DockSite(truck.DropoffDockId), TruckState.ToDropoff, TruckState.Unloading);
            else Drive(truck, DockSite(truck.PickupDockId), TruckState.ToPickup, TruckState.Loading);
        }

        // A redirected truck restarts from the site it left (PROTOTYPE: no position between sites is kept).
        private void Drive(GoodsTruck truck, string destinationSiteId, TruckState driving, TruckState arrived)
        {
            var seconds = RoadSeconds(_state, truck.SiteId, destinationSiteId, truck.SpeedMetresPerSecond);
            truck.State = seconds == 0 ? arrived : driving;
            truck.SiteId = seconds == 0 ? destinationSiteId : truck.SiteId;
            truck.DestinationSiteId = seconds == 0 ? "" : destinationSiteId;
            truck.RemainingSeconds = seconds;
        }

        // Runs inside Advance. Trucks act one simulated second at a time in ID order, so two trucks at one dock share it the
        // same way whatever the step size; while every active truck is driving, time skips ahead to the next arrival. A truck
        // that cannot work (nothing to load and no cargo, a full buffer, a dock not placed) waits: nothing else in the step
        // can change that, so it is left alone until the next step.
        private void MoveTrucks(long seconds)
        {
            if (_state.Trucks.Count == 0) return;
            var trucks = _state.Trucks.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var waiting = new HashSet<GoodsTruck>();
            var left = seconds;
            while (left > 0)
            {
                var working = trucks.Where(x => x.State is TruckState.Loading or TruckState.Unloading && !waiting.Contains(x)).ToList();
                var driving = trucks.Where(x => x.Driving).ToList();
                if (working.Count == 0 && driving.Count == 0) return;
                var step = working.Count > 0 ? 1 : Math.Min(left, driving.Min(x => x.RemainingSeconds));
                foreach (var truck in trucks)
                {
                    if (truck.Driving)
                    {
                        truck.RemainingSeconds -= step;
                        if (truck.RemainingSeconds > 0) continue;
                        truck.SiteId = truck.DestinationSiteId;
                        truck.DestinationSiteId = "";
                        truck.State = truck.State == TruckState.ToPickup ? TruckState.Loading : TruckState.Unloading;
                    }
                    else if (working.Contains(truck) && !(truck.State == TruckState.Loading ? LoadSecond(truck) : UnloadSecond(truck)))
                        waiting.Add(truck);
                }
                left -= step;
            }
        }

        // One second at the pickup dock: loads up to the truck's rate of allowed, unreserved goods from the dock's outgoing
        // buffer, most exposed first. With nothing loaded and cargo aboard it leaves for the dropoff. False when it waits.
        private bool LoadSecond(GoodsTruck truck)
        {
            var dock = PlacedDock(truck.PickupDockId);
            if (dock is null) return false;
            var budget = truck.LoadUnitsPerSecond;
            var candidates = _state.Lots
                .Where(x => x.LocationId == dock.InputLocationId && x.OwnerId == dock.SiteId
                    && (truck.AllowedItemIds.Count == 0 || truck.AllowedItemIds.Contains(x.ItemId)) && Available(x) > 0)
                .OrderByDescending(x => x.ExposureSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            foreach (var lot in candidates)
            {
                var take = (int)Math.Min(Math.Min(budget, Available(lot)), FreeUnits(truck.CargoLocationId, lot));
                if (take == 0) continue;
                MoveUnits(lot, truck.CargoLocationId, take, lot.OwnerId);
                budget -= take;
                if (budget == 0) break;
            }
            if (budget < truck.LoadUnitsPerSecond) return true;
            if (!HasCargo(truck)) return false;
            Drive(truck, DockSite(truck.DropoffDockId), TruckState.ToDropoff, TruckState.Unloading);
            return true;
        }

        // One second at the dropoff dock: unloads up to the truck's rate into the dock's incoming buffer, re-owned by that
        // site, most exposed first. Once empty it heads back to the pickup. False when it waits (full buffer, no dock).
        private bool UnloadSecond(GoodsTruck truck)
        {
            if (!HasCargo(truck))
            {
                Drive(truck, DockSite(truck.PickupDockId), TruckState.ToPickup, TruckState.Loading);
                return true;
            }
            var dock = PlacedDock(truck.DropoffDockId);
            if (dock is null) return false;
            var budget = truck.LoadUnitsPerSecond;
            var cargo = _state.Lots.Where(x => x.LocationId == truck.CargoLocationId)
                .OrderByDescending(x => x.ExposureSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            foreach (var lot in cargo)
            {
                var take = (int)Math.Min(Math.Min(budget, lot.Quantity), FreeUnits(dock.OutputLocationId, lot));
                if (take == 0) continue;
                MoveUnits(lot, dock.OutputLocationId, take, dock.SiteId);
                budget -= take;
                if (budget == 0) break;
            }
            if (budget == truck.LoadUnitsPerSecond) return false;
            if (!HasCargo(truck)) Drive(truck, DockSite(truck.PickupDockId), TruckState.ToPickup, TruckState.Loading);
            return true;
        }

        private GoodsEquipment PlacedDock(string dockId) =>
            _state.Equipment.FirstOrDefault(x => x.Id == dockId && x.Kind == DockKind && x.State == EquipmentState.Placed);

        private long FreeUnits(string locationId, GoodsLot lot) => GoodsSlots.FreeUnits(
            _state.Locations.FirstOrDefault(x => x.Id == locationId), _state.Lots, lot.ItemId, lot.Spoiled, MaxStackLocked);

        // Moves units of a lot, keeping its ID when all of it moves and splitting under a new ID otherwise; exposure is kept.
        private void MoveUnits(GoodsLot lot, string locationId, int quantity, string ownerId)
        {
            if (quantity == lot.Quantity)
            {
                lot.LocationId = locationId;
                lot.OwnerId = ownerId;
                return;
            }
            lot.Quantity -= quantity;
            _state.Lots.Add(new GoodsLot
            {
                Id = Guid.NewGuid().ToString("N"), ItemId = lot.ItemId, OwnerId = ownerId, LocationId = locationId, Quantity = quantity,
                ExposureSeconds = lot.ExposureSeconds, SpoilAfterSeconds = lot.SpoilAfterSeconds, Spoiled = lot.Spoiled
            });
        }

        // A site's baseline carries its company's fleet (with each truck's cargo), and the map records of the company's sites,
        // so the route screen can show every truck wherever it is.
        private void ViewLogistics(GoodsSnapshot view, string siteId)
        {
            var company = _state.Companies.FirstOrDefault(x => x.SiteIds.Contains(siteId));
            view.Sites = _state.Sites.Where(x => x.Id == siteId || company?.SiteIds.Contains(x.Id) == true)
                .Select(x => JsonUtility.FromJson<GoodsSite>(JsonUtility.ToJson(x))).ToList();
            view.Trucks = company is null ? new List<GoodsTruck>()
                : _state.Trucks.Where(x => x.CompanyId == company.Id).Select(x => JsonUtility.FromJson<GoodsTruck>(JsonUtility.ToJson(x))).ToList();
            // Appended after the site's own locations: a baseline names its site by its first location.
            var cargoIds = new HashSet<string>(view.Trucks.Select(x => x.CargoLocationId));
            view.Locations.AddRange(_state.Locations.Where(x => cargoIds.Contains(x.Id)).Select(x => JsonUtility.FromJson<GoodsLocation>(JsonUtility.ToJson(x))));
            view.Lots.AddRange(_state.Lots.Where(x => cargoIds.Contains(x.LocationId)).Select(x => JsonUtility.FromJson<GoodsLot>(JsonUtility.ToJson(x))));
        }

        private static void ValidateTrucks(GoodsSnapshot state)
        {
            var siteIds = new HashSet<string>(state.Sites.Where(x => x is not null).Select(x => x.Id));
            if (state.Sites.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || x.Id == RoadSiteId || x.Name is null
                    || state.Locations.All(y => y.SiteId != x.Id))
                || state.Sites.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Grants.Any(x => x.SiteId == RoadSiteId)
                || state.Companies.Any(x => x.SiteIds.Contains(RoadSiteId))
                || state.Trucks.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id))
                || state.Trucks.GroupBy(x => x.Id).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot violates site or truck identity invariants.");
            foreach (var truck in state.Trucks)
            {
                var cargo = state.Locations.FirstOrDefault(x => x.Id == truck.CargoLocationId);
                var items = truck.AllowedItemIds;
                var valid = state.Companies.Any(x => x.Id == truck.CompanyId) && truck.Name is not null
                    && truck.CargoSlots >= 1 && truck.SpeedMetresPerSecond >= 1 && truck.LoadUnitsPerSecond >= 1
                    && items is not null && CargoFilterProblem(items) is null
                    && cargo is not null && cargo.Kind == VehicleLocationKind && cargo.SiteId == RoadSiteId && cargo.Capacity == truck.CargoSlots
                    && !cargo.Refrigerated
                    && siteIds.Contains(truck.SiteId)
                    && truck.State is TruckState.Parked or TruckState.ToPickup or TruckState.Loading or TruckState.ToDropoff or TruckState.Unloading
                    && (truck.State == TruckState.Parked
                        ? string.IsNullOrEmpty(truck.PickupDockId) && string.IsNullOrEmpty(truck.DropoffDockId)
                        : RouteProblem(state, truck.CompanyId, truck.PickupDockId, truck.DropoffDockId) is null)
                    && (truck.Driving
                        ? siteIds.Contains(truck.DestinationSiteId) && truck.RemainingSeconds >= 1
                        : string.IsNullOrEmpty(truck.DestinationSiteId) && truck.RemainingSeconds == 0);
                if (!valid) throw new InvalidOperationException($"Truck {truck.Id} is inconsistent.");
            }
            // Every vehicle location is one truck's cargo; nothing else stands on the road.
            var cargoIds = new HashSet<string>(state.Trucks.Select(x => x.CargoLocationId));
            if (state.Locations.Any(x => (x.Kind == VehicleLocationKind || x.SiteId == RoadSiteId) && !cargoIds.Contains(x.Id)))
                throw new InvalidOperationException("Goods snapshot has a vehicle location without its truck.");
        }
    }
}
