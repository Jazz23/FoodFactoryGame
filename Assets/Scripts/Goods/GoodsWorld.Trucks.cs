// Trucks (decisions 0022, 0023): company vehicles that repeat a player-made route between two loading docks on different
// sites of the same company, over the public road network. A route is its own record; any number of the company's trucks
// may be assigned to it, and a truck with no route is parked. Roads are abstract: each site has a map position and a trip takes the
// Manhattan distance between the two sites divided by the truck's speed. A truck loads at the pickup dock's outgoing buffer
// (its input, <id>:in) and unloads into the dropoff dock's incoming buffer (its output, <id>:out), a few units a second, so
// dock throughput is a real bottleneck. It leaves as soon as the dock has nothing more it may or can load and it has cargo,
// and turns back once empty.
// Cargo is an ordinary location of kind "vehicle" on the reserved "road" site, which nobody is ever granted: players,
// employees and belts cannot reach it, and only the truck's own loading and unloading move goods in or out. Cargo keeps its
// lot IDs, exposure and owner while aboard (trucks are not refrigerated, so goods age in transit) and takes the dropoff
// site as owner when it is unloaded. A dock that is picked up, or a full or empty buffer, makes the truck wait there; goods
// are never dropped or duplicated.
// Generated worlds (decision 0032): with the layout's road network registered, a truck between two sites on the network drives
// the real roads one leg (a straight piece of one segment) at a time, choosing the quickest way on at every junction from the
// current traffic (RoadTraffic): a leg's time comes from the road kind, the load of city cars and trucks on it, and the wait at
// its end node. A truck can find the next segment full and wait at the end of its leg. A redirected truck finishes its leg and
// reroutes; parking a driving truck sends it on to the nearer of its route's two sites. Sites off the network (dev worlds)
// keep the abstract Manhattan trip above.
// Restaurant docks (decision 0034, slice 5): a dock on a restaurant's site serves one truck at a time (the truck holding it is
// Docked; others wait their turn in ID order) and is usable only while a walkable path reaches it from the lot's street edge
// (PROTOTYPE dock rule). The truck arrives from the street after the abstract trip time and goods move at its LoadUnitsPerSecond
// (PROTOTYPE transfer rate). Docks on other sites (warehouses, factories) keep the rules above.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.World;
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
        Unloading,
        // No route: driving to DestinationSiteId, where it parks (decision 0032: a truck parked while driving the roads).
        ToPark
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
        // A route of the truck's company; empty exactly while Parked.
        public string RouteId = "";
        public TruckState State;
        // The site the truck stands at or, while driving, the one it left.
        public string SiteId;
        // Where a driving truck arrives; empty otherwise.
        public string DestinationSiteId = "";
        // Seconds of driving left; 0 unless driving.
        public long RemainingSeconds;
        // True while this truck holds the restaurant dock it is loading or unloading at (decision 0034, v16); other trucks wait.
        public bool Docked;
        // Road trips (decision 0032, v17). While driving the network: the segment of the leg being driven (empty on an
        // abstract trip and whenever the truck is not driving), the leg's ends in metres from the segment's From node, its
        // driving time and its whole time including the wait at its end node (both fixed when the leg starts).
        // RemainingSeconds is then the time left in the leg; 0 means the truck stands at the leg's end waiting to enter the
        // next segment, and QueuedSeconds counts that wait.
        public string LegSegmentId = "";
        public int LegFrom;
        public int LegTo;
        public long LegDriveSeconds;
        public long LegSeconds;
        public long QueuedSeconds;
        // While queued (RemainingSeconds 0 on the road): the leg it waits to enter, kept so each retry only asks the segment
        // again instead of planning anew; empty otherwise. NextFinal marks the last leg of the trip (no junction wait).
        public string NextSegmentId = "";
        public int NextFrom;
        public int NextTo;
        public bool NextFinal;

        public string CargoLocationId => Id + ":cargo";
        public bool Driving => State is TruckState.ToPickup or TruckState.ToDropoff or TruckState.ToPark;
        public bool OnRoad => !string.IsNullOrEmpty(LegSegmentId);
    }

    // What the trucks assigned to it repeat: load at one dock, deliver to a dock on another site of the same company.
    [Serializable] public sealed class GoodsRoute
    {
        public string Id;
        public string CompanyId;
        public string PickupDockId;
        public string DropoffDockId;
        // Items the trucks may load at the pickup dock; empty means any.
        public List<string> AllowedItemIds = new();
    }

    // Content, not state: one truck model at the supplier (decision 0023). A bought truck keeps copies of the stats.
    [Serializable] public sealed class TruckOffer
    {
        public string Id;
        // Whole cents for one truck.
        public long PriceCents;
        // Bought trucks are called "<Name> <n>".
        public string Name;
        public int CargoSlots;
        public int SpeedMetresPerSecond;
        public int LoadUnitsPerSecond;
    }

    public sealed partial class GoodsWorld
    {
        public const string DockKind = "dock";
        public const string VehicleLocationKind = "vehicle";
        // Reserved site of every truck's cargo location; never granted, never owned by a company.
        public const string RoadSiteId = "road";
        public const int MaxAllowedItems = 16;

        // Server-only: a site's map record. The record itself makes the site exist (SiteExists), so a bought property's empty
        // site can be granted and owned before anything stands on it.
        public void Bootstrap(GoodsSite site)
        {
            lock (_gate)
            {
                if (site is null || string.IsNullOrWhiteSpace(site.Id) || site.Id == RoadSiteId || site.Name is null
                    || _state.Sites.Any(x => x.Id == site.Id))
                    throw new ArgumentException("Invalid or duplicate site.");
                _state.Sites.Add(JsonUtility.FromJson<GoodsSite>(JsonUtility.ToJson(site)));
                InvalidateDiners();
                _state.Revision++;
            }
        }

        // Server-only: adds a truck and its empty cargo location. A truck may start parked (no route) or on an existing route;
        // the result must satisfy Validate or nothing changes.
        public void Bootstrap(GoodsTruck truck)
        {
            lock (_gate)
            {
                var copy = truck is null ? null : JsonUtility.FromJson<GoodsTruck>(JsonUtility.ToJson(truck));
                if (copy is null || string.IsNullOrWhiteSpace(copy.Id) || _state.Trucks.Any(x => x.Id == copy.Id)
                    || _state.Locations.Any(x => x.Id == copy.CargoLocationId) || copy.CargoSlots < 1)
                    throw new ArgumentException("Invalid or duplicate truck.");
                var before = Snapshot();
                AddTruck(copy);
                _state.Revision++;
                ValidateOrRestore(before, "truck");
            }
        }

        // Server-only: adds a route of the company that owns both docks, with no trucks yet (the dev seed's route).
        public void Bootstrap(GoodsRoute route)
        {
            lock (_gate)
            {
                var copy = route is null ? null : JsonUtility.FromJson<GoodsRoute>(JsonUtility.ToJson(route));
                if (copy is null || string.IsNullOrWhiteSpace(copy.Id) || _state.Routes.Any(x => x.Id == copy.Id))
                    throw new ArgumentException("Invalid or duplicate route.");
                var before = Snapshot();
                _state.Routes.Add(copy);
                _state.Revision++;
                ValidateOrRestore(before, "route");
            }
        }

        private void AddTruck(GoodsTruck truck)
        {
            _state.Trucks.Add(truck);
            _state.Locations.Add(new GoodsLocation
            {
                Id = truck.CargoLocationId, SiteId = RoadSiteId, Kind = VehicleLocationKind, Capacity = truck.CargoSlots
            });
        }

        private void ValidateOrRestore(GoodsSnapshot before, string what)
        {
            try { Validate(_state); }
            catch (InvalidOperationException error)
            {
                _state = before;
                InvalidateDiners();
                throw new ArgumentException($"Invalid {what}: " + error.Message, error);
            }
        }

        // Server-only: puts a site that has locations into a company's holdings (a new dev site, in the seed).
        public void AddCompanySite(string companyId, string siteId)
        {
            lock (_gate)
            {
                var company = _state.Companies.FirstOrDefault(x => x.Id == companyId);
                if (company is null || string.IsNullOrWhiteSpace(siteId) || siteId == RoadSiteId
                    || !SiteExists(_state, siteId) || _state.Companies.Any(x => x.SiteIds.Contains(siteId)))
                    throw new ArgumentException("Unknown company, or a site that does not exist or is already owned.");
                company.SiteIds.Add(siteId);
                InvalidateDiners();
                _state.Revision++;
            }
        }

        // Server-only: moves a mapped site to a new map position; true if it changed. Trucks already driving keep their
        // remaining time; later trips use the new distance.
        public bool MoveSite(string siteId, int mapX, int mapZ)
        {
            lock (_gate)
            {
                var site = _state.Sites.FirstOrDefault(x => x.Id == siteId) ?? throw new ArgumentException("Unknown site.");
                if (site.MapX == mapX && site.MapZ == mapZ) return false;
                site.MapX = mapX;
                site.MapZ = mapZ;
                _sitePoints.Remove(siteId);
                InvalidateDiners();
                _state.Revision++;
                return true;
            }
        }

        // Whether the site exists (SiteExists).
        public bool HasSite(string siteId)
        {
            lock (_gate) return SiteExists(_state, siteId);
        }

        // A site exists once it has a map record (a bought property's site, decision 0028, starts with nothing on it) or any
        // location (the dev sites). Grants, company ownership and map records all refer only to existing sites.
        private static bool SiteExists(GoodsSnapshot state, string siteId) => !string.IsNullOrWhiteSpace(siteId)
            && (state.Sites.Any(x => x is not null && x.Id == siteId) || state.Locations.Any(x => x is not null && x.SiteId == siteId));

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

        // Volatile primitive for tests. Live request handlers must call CreateRouteDurably.
        // A new route with no trucks, ID route:<player>:<request>, for the company that owns both docks. Checks, in order:
        // identity and replay; both docks (invalid-dock); the player's grants on both dock sites and one company owning both
        // (forbidden); different sites (same-site); map records for both sites (no-road); the cargo filter (invalid-cargo).
        public GoodsOutcome CreateRoute(string playerId, string requestId, string pickupDockId, string dropoffDockId,
            IReadOnlyList<string> allowedItemIds)
        {
            return RouteCommand(playerId, requestId, reject =>
            {
                var items = allowedItemIds?.ToList() ?? new List<string>();
                var pickup = _state.Equipment.FirstOrDefault(x => x.Id == pickupDockId && x.Kind == DockKind);
                var companyId = pickup is null ? null : CompanyOfSiteLocked(pickup.SiteId);
                var problem = RouteRequestProblem(playerId, companyId, pickupDockId, dropoffDockId, items);
                if (problem is not null) return reject(problem);
                _state.Routes.Add(new GoodsRoute
                {
                    Id = RouteIdFor(playerId, requestId), CompanyId = companyId, PickupDockId = pickupDockId, DropoffDockId = dropoffDockId,
                    AllowedItemIds = items
                });
                return Record(requestId, playerId, true, "route-created", null);
            });
        }

        public GoodsOutcome CreateRouteDurably(string playerId, string requestId, string pickupDockId, string dropoffDockId,
            IReadOnlyList<string> allowedItemIds, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => CreateRoute(playerId, requestId, pickupDockId, dropoffDockId, allowedItemIds));
        }

        // The ID an accepted CreateRoute gives its route: unique, because an accepted request replays instead of running again.
        public static string RouteIdFor(string playerId, string requestId) => $"route:{playerId}:{requestId}";

        // Volatile primitive for tests. Live request handlers must call SetRouteDurably.
        // Edits a route (forbidden if unknown, then CreateRoute's checks against the route's company). Every truck on it is
        // sent on: to the new dropoff if it carries anything, otherwise to the new pickup, from the site it stands at or last left.
        public GoodsOutcome SetRoute(string playerId, string requestId, string routeId, string pickupDockId, string dropoffDockId,
            IReadOnlyList<string> allowedItemIds)
        {
            return RouteCommand(playerId, requestId, reject =>
            {
                var route = _state.Routes.FirstOrDefault(x => x.Id == routeId);
                if (route is null) return reject("forbidden");
                var items = allowedItemIds?.ToList() ?? new List<string>();
                var problem = RouteRequestProblem(playerId, route.CompanyId, pickupDockId, dropoffDockId, items);
                if (problem is not null) return reject(problem);
                route.PickupDockId = pickupDockId;
                route.DropoffDockId = dropoffDockId;
                route.AllowedItemIds = items;
                foreach (var truck in _state.Trucks.Where(x => x.RouteId == route.Id)) Dispatch(truck);
                return Record(requestId, playerId, true, "route-set", null);
            });
        }

        public GoodsOutcome SetRouteDurably(string playerId, string requestId, string routeId, string pickupDockId, string dropoffDockId,
            IReadOnlyList<string> allowedItemIds, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => SetRoute(playerId, requestId, routeId, pickupDockId, dropoffDockId, allowedItemIds));
        }

        // Volatile primitive for tests. Live request handlers must call DeleteRouteDurably.
        // Parks every truck on the route, cargo aboard, and removes it. Needs grants on both of its dock sites (forbidden).
        public GoodsOutcome DeleteRoute(string playerId, string requestId, string routeId)
        {
            return RouteCommand(playerId, requestId, reject =>
            {
                var route = _state.Routes.FirstOrDefault(x => x.Id == routeId);
                if (route is null || !CanView(playerId, DockSite(route.PickupDockId)) || !CanView(playerId, DockSite(route.DropoffDockId)))
                    return reject("forbidden");
                foreach (var truck in _state.Trucks.Where(x => x.RouteId == route.Id)) Park(truck);
                _state.Routes.Remove(route);
                return Record(requestId, playerId, true, "route-deleted", null);
            });
        }

        public GoodsOutcome DeleteRouteDurably(string playerId, string requestId, string routeId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => DeleteRoute(playerId, requestId, routeId));
        }

        // Volatile primitive for tests. Live request handlers must call AssignTruckDurably.
        // Puts a truck on a route of its company (grants on both dock sites, or forbidden) and sends it on; a truck already on
        // that route is left as it is. An empty route parks the truck, which needs a grant on any site of its company.
        public GoodsOutcome AssignTruck(string playerId, string requestId, string truckId, string routeId)
        {
            return RouteCommand(playerId, requestId, reject =>
            {
                var truck = _state.Trucks.FirstOrDefault(x => x.Id == truckId);
                if (truck is null) return reject("forbidden");
                if (string.IsNullOrEmpty(routeId))
                {
                    var company = _state.Companies.FirstOrDefault(x => x.Id == truck.CompanyId);
                    if (company is null || !company.SiteIds.Any(x => CanView(playerId, x))) return reject("forbidden");
                    Park(truck);
                    return Record(requestId, playerId, true, "parked", null);
                }
                var route = _state.Routes.FirstOrDefault(x => x.Id == routeId);
                if (route is null || route.CompanyId != truck.CompanyId
                    || !CanView(playerId, DockSite(route.PickupDockId)) || !CanView(playerId, DockSite(route.DropoffDockId)))
                    return reject("forbidden");
                if (truck.RouteId != route.Id)
                {
                    truck.RouteId = route.Id;
                    Dispatch(truck);
                }
                return Record(requestId, playerId, true, "assigned", null);
            });
        }

        public GoodsOutcome AssignTruckDurably(string playerId, string requestId, string truckId, string routeId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => AssignTruck(playerId, requestId, truckId, routeId));
        }

        // Shared frame of the route commands: identity and replay first. Like purchases, rejections change nothing and are not
        // recorded; only the accepted change replays.
        private GoodsOutcome RouteCommand(string playerId, string requestId, Func<Func<string, GoodsOutcome>, GoodsOutcome> body)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                return body(reason => new GoodsOutcome
                {
                    RequestId = requestId, PlayerId = playerId, Accepted = false, Reason = reason, Revision = _state.Revision
                });
            }
        }

        // Null when the player may make the two docks a route of the company; otherwise the first problem in command order.
        private string RouteRequestProblem(string playerId, string companyId, string pickupDockId, string dropoffDockId,
            IReadOnlyCollection<string> items)
        {
            var pickup = _state.Equipment.FirstOrDefault(x => x.Id == pickupDockId && x.Kind == DockKind);
            var dropoff = _state.Equipment.FirstOrDefault(x => x.Id == dropoffDockId && x.Kind == DockKind);
            if (pickup is null || dropoff is null) return "invalid-dock";
            if (!CanView(playerId, pickup.SiteId) || !CanView(playerId, dropoff.SiteId)) return "forbidden";
            return RouteProblem(_state, companyId, pickup.Id, dropoff.Id) ?? CargoFilterProblem(items);
        }

        // The route a truck repeats, or null while parked. For presentation as well as the simulation.
        public static GoodsRoute RouteOf(GoodsSnapshot state, GoodsTruck truck) =>
            string.IsNullOrEmpty(truck?.RouteId) ? null : state.Routes?.FirstOrDefault(x => x.Id == truck.RouteId);

        // A parked truck stands where it is. One driving the roads goes on to whichever of its route's two sites is quicker to
        // reach from the end of its leg and parks there (decision 0032); one on an abstract trip parks at the site it left
        // (PROTOTYPE, like a redirect). Cargo stays aboard.
        private void Park(GoodsTruck truck)
        {
            var route = RouteOf(_state, truck);
            truck.RouteId = "";
            truck.Docked = false;
            if (truck.Driving && truck.OnRoad)
            {
                // Already on its way to park (no route), it keeps going.
                if (route is not null)
                {
                    var pickup = DockSite(route.PickupDockId);
                    var dropoff = DockSite(route.DropoffDockId);
                    var nearer = EstimateMillis(truck, dropoff) < EstimateMillis(truck, pickup) ? dropoff : pickup;
                    if (nearer != truck.DestinationSiteId) ClearNext(truck);
                    truck.DestinationSiteId = nearer;
                }
                truck.State = TruckState.ToPark;
                return;
            }
            truck.State = TruckState.Parked;
            truck.DestinationSiteId = "";
            truck.RemainingSeconds = 0;
            ClearLeg(truck);
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

        private GoodsRoute Route(GoodsTruck truck) => _state.Routes.First(x => x.Id == truck.RouteId);

        // Sends a routed truck on: loaded to the dropoff, empty to the pickup.
        private void Dispatch(GoodsTruck truck)
        {
            if (HasCargo(truck)) Drive(truck, DockSite(Route(truck).DropoffDockId), TruckState.ToDropoff, TruckState.Unloading);
            else Drive(truck, DockSite(Route(truck).PickupDockId), TruckState.ToPickup, TruckState.Loading);
        }

        // Sends a truck toward a site. One standing at a site leaves it: by road when both sites are on the registered network,
        // otherwise on an abstract trip that restarts from the site it left (PROTOTYPE, dev worlds). One already driving the
        // roads keeps its current leg and reroutes from the leg's end (decision 0032).
        private void Drive(GoodsTruck truck, string destinationSiteId, TruckState driving, TruckState arrived)
        {
            truck.Docked = false;
            if (truck.Driving && truck.OnRoad)
            {
                truck.State = driving;
                if (truck.DestinationSiteId != destinationSiteId) ClearNext(truck);
                truck.DestinationSiteId = destinationSiteId;
                return;
            }
            var from = SitePoint(truck.SiteId);
            var to = SitePoint(destinationSiteId);
            if (truck.SiteId != destinationSiteId && from is { } start && to is not null)
            {
                truck.State = driving;
                truck.DestinationSiteId = destinationSiteId;
                truck.RemainingSeconds = 0;
                // A zero-length leg at the site's road point: the truck enters the network like any leg's end, and waits there
                // if the first segment is full.
                SetLeg(truck, start.Segment, start.Offset, start.Offset, 0, 0);
                ContinueOnRoad(truck, departing: true);
                return;
            }
            ClearLeg(truck);
            var seconds = RoadSeconds(_state, truck.SiteId, destinationSiteId, truck.SpeedMetresPerSecond);
            truck.State = seconds == 0 ? arrived : driving;
            truck.SiteId = seconds == 0 ? destinationSiteId : truck.SiteId;
            truck.DestinationSiteId = seconds == 0 ? "" : destinationSiteId;
            truck.RemainingSeconds = seconds;
        }

        // A driving truck reaches DestinationSiteId: it starts loading or unloading there, or parks.
        private static void Arrive(GoodsTruck truck)
        {
            truck.SiteId = truck.DestinationSiteId;
            truck.DestinationSiteId = "";
            truck.RemainingSeconds = 0;
            truck.State = truck.State switch
            {
                TruckState.ToPickup => TruckState.Loading,
                TruckState.ToDropoff => TruckState.Unloading,
                _ => TruckState.Parked
            };
            ClearLeg(truck);
        }

        // Runs inside Advance. Trucks act one simulated second at a time in ID order, so two trucks at one dock (or one
        // segment) share it the same way whatever the step size; while every active truck is driving a leg, time skips ahead to
        // the next leg end, the only moment a truck chooses or is refused anything. A truck that cannot work (nothing to load
        // and no cargo, a full buffer, a dock not placed) waits: nothing else in the step can change that, so it is left alone
        // until the next step. A truck queued at a full segment retries every second.
        private void MoveTrucks(long seconds)
        {
            if (_state.Trucks.Count == 0) return;
            var trucks = _state.Trucks.OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var waiting = new HashSet<GoodsTruck>();
            // Nothing in a clock step changes walls or placed pieces, so street reachability is worked out once per step.
            _usableDocks.Clear();
            var left = seconds;
            try
            {
                while (left > 0)
                {
                    _truckSecond = _state.ClockSeconds - left;
                    var working = trucks.Where(x => x.State is TruckState.Loading or TruckState.Unloading && !waiting.Contains(x)).ToList();
                    var driving = trucks.Where(x => x.Driving).ToList();
                    if (working.Count == 0 && driving.Count == 0) return;
                    var queued = driving.Any(x => x.RemainingSeconds == 0);
                    var step = working.Count > 0 || queued ? 1 : Math.Min(left, driving.Min(x => x.RemainingSeconds));
                    // Choices made in this step (entering a leg, a light's phase) happen at its end.
                    _truckNow = _truckSecond + step;
                    foreach (var truck in trucks)
                    {
                        if (truck.Driving)
                        {
                            if (truck.RemainingSeconds > 0)
                            {
                                truck.RemainingSeconds -= step;
                                if (truck.RemainingSeconds > 0) continue;
                            }
                            else truck.QueuedSeconds += step;
                            if (truck.OnRoad) ContinueOnRoad(truck, departing: false);
                            else Arrive(truck);
                        }
                        else if (working.Contains(truck))
                        {
                            // A restaurant dock serves one truck at a time: wait (rechecked every second) while another holds it.
                            if (!TakeDock(truck, trucks)) continue;
                            if (!(truck.State == TruckState.Loading ? LoadSecond(truck) : UnloadSecond(truck))) waiting.Add(truck);
                        }
                    }
                    left -= step;
                }
            }
            finally { _truckNow = null; }
        }

        // The time road choices are made at: the end of the truck step being worked through, or the clock for a command.
        private long Now => _truckNow ?? _state.ClockSeconds;
        private long? _truckNow;

        // Server content, registered once at start for a generated world: its road network (decision 0032). Without it every
        // trip is abstract. A saved truck must be on one of its segments.
        public void RegisterRoads(RoadNetwork network)
        {
            lock (_gate)
            {
                if (network is null || _roads is not null) throw new ArgumentException("Missing or duplicate road network.");
                var problem = RoadsProblem(_state, network);
                if (problem is not null) throw new InvalidOperationException(problem);
                _roads = network;
                _sitePoints.Clear();
            }
        }

        public bool HasRoads
        {
            get { lock (_gate) return _roads is not null; }
        }

        private RoadNetwork _roads;
        private readonly Dictionary<string, RoadPoint?> _sitePoints = new(StringComparer.Ordinal);

        // Null when every truck leg names a segment of the network and lies on it.
        private static string RoadsProblem(GoodsSnapshot state, RoadNetwork network)
        {
            foreach (var truck in state.Trucks.Where(x => x.OnRoad))
                if (!network.TryGetSegment(truck.LegSegmentId, out var index)
                    || Math.Max(truck.LegFrom, truck.LegTo) > network.Segments[index].Length
                    || (truck.NextSegmentId != "" && (!network.TryGetSegment(truck.NextSegmentId, out var next)
                        || Math.Max(truck.NextFrom, truck.NextTo) > network.Segments[next].Length)))
                    return $"Truck {truck.Id} drives a leg that is not on the road network.";
            return null;
        }

        private void ValidateRoads()
        {
            if (_roads is null) return;
            var problem = RoadsProblem(_state, _roads);
            if (problem is not null) throw new InvalidOperationException(problem);
        }

        // Where a site meets the road network: the point nearest its map position (a bought lot's access cell), or null
        // without a network or when the site is off it.
        private RoadPoint? SitePoint(string siteId)
        {
            if (_roads is null || string.IsNullOrEmpty(siteId)) return null;
            if (_sitePoints.TryGetValue(siteId, out var point)) return point;
            var site = _state.Sites.FirstOrDefault(x => x.Id == siteId);
            point = site is null ? null : _roads.Locate(site.MapX, site.MapZ);
            _sitePoints[siteId] = point;
            return point;
        }

        // At the end of its leg (or leaving a site), a truck on the road arrives if it stands at its destination's road point;
        // otherwise it picks the quickest way on from the current traffic and enters that way's first leg if the segment
        // admits it, or waits where it is. A destination with no route on the network (never in a validated world) finishes
        // as an abstract trip.
        private void ContinueOnRoad(GoodsTruck truck, bool departing)
        {
            var target = SitePoint(truck.DestinationSiteId);
            if (_roads is null || !_roads.TryGetSegment(truck.LegSegmentId, out var segment) || target is null)
            {
                FinishAbstractly(truck);
                return;
            }
            var here = new RoadPoint(segment, truck.LegTo);
            var arrived = here.Equals(target.Value) || (_roads.AtNode(here) >= 0 && _roads.AtNode(here) == _roads.AtNode(target.Value));
            if (arrived && !departing)
            {
                Arrive(truck);
                return;
            }
            var occupancy = Occupancy(truck);
            var now = Now;
            RoadLeg leg;
            bool final;
            // A truck already queued for a leg keeps waiting for it; otherwise it plans the quickest way on.
            if (_roads.TryGetSegment(truck.NextSegmentId, out var waitingFor))
            {
                leg = new RoadLeg(waitingFor, truck.NextFrom, truck.NextTo);
                final = truck.NextFinal;
            }
            else
            {
                var route = _roads.Route(here, target.Value,
                    (index, forward) => RoadTraffic.DriveMillis(_roads.Segments[index], _roads.Segments[index].Length, truck.SpeedMetresPerSecond,
                        RoadTraffic.LoadPercent(_roads.Segments[index], now, occupancy.GetValueOrDefault((index, forward)))),
                    (index, forward) => RoadTraffic.JunctionEstimateMillis(_roads, index, forward, now));
                if (route is null || route.Count == 0)
                {
                    FinishAbstractly(truck);
                    return;
                }
                leg = route[0];
                final = route.Count == 1;
            }
            var road = _roads.Segments[leg.Segment];
            var others = occupancy.GetValueOrDefault((leg.Segment, leg.Forward));
            var load = RoadTraffic.LoadPercent(road, now, others + 1);
            if (!RoadTraffic.Admits(load, others, truck.QueuedSeconds))
            {
                truck.RemainingSeconds = 0;
                truck.NextSegmentId = road.Id;
                truck.NextFrom = leg.From;
                truck.NextTo = leg.To;
                truck.NextFinal = final;
                return;
            }
            ClearNext(truck);
            // A zero-length leg (two sites sharing a road point) still takes a second, like any trip between different sites.
            var drive = Math.Max(1, (RoadTraffic.DriveMillis(road, leg.Length, truck.SpeedMetresPerSecond, load) + 999) / 1000);
            var wait = final ? 0 : RoadTraffic.JunctionWaitSeconds(_roads, leg.Segment, leg.Forward, now + drive);
            SetLeg(truck, leg.Segment, leg.From, leg.To, drive, drive + wait);
            truck.RemainingSeconds = drive + wait;
            truck.QueuedSeconds = 0;
        }

        // A road trip that cannot go on by road ends as an abstract trip from the site it left.
        private void FinishAbstractly(GoodsTruck truck)
        {
            ClearLeg(truck);
            var seconds = RoadSeconds(_state, truck.SiteId, truck.DestinationSiteId, truck.SpeedMetresPerSecond);
            if (seconds == 0) Arrive(truck);
            else truck.RemainingSeconds = seconds;
        }

        // Trucks on each direction of each segment, other than the one deciding.
        private Dictionary<(int Segment, bool Forward), int> Occupancy(GoodsTruck except)
        {
            var counts = new Dictionary<(int, bool), int>();
            foreach (var truck in _state.Trucks)
            {
                if (truck == except || !truck.OnRoad || !_roads.TryGetSegment(truck.LegSegmentId, out var index)) continue;
                var key = (index, truck.LegTo >= truck.LegFrom);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            return counts;
        }

        // Free-flow-plus-traffic estimate of how long a truck on the road needs from the end of its leg to a site; used to
        // choose where a truck parked while driving goes. Long.MaxValue when it cannot get there by road.
        private long EstimateMillis(GoodsTruck truck, string siteId)
        {
            var target = SitePoint(siteId);
            if (_roads is null || target is null || !_roads.TryGetSegment(truck.LegSegmentId, out var segment)) return long.MaxValue;
            var occupancy = Occupancy(truck);
            var now = Now;
            var route = _roads.Route(new RoadPoint(segment, truck.LegTo), target.Value,
                (index, forward) => RoadTraffic.DriveMillis(_roads.Segments[index], _roads.Segments[index].Length, truck.SpeedMetresPerSecond,
                    RoadTraffic.LoadPercent(_roads.Segments[index], now, occupancy.GetValueOrDefault((index, forward)))),
                (index, forward) => RoadTraffic.JunctionEstimateMillis(_roads, index, forward, now));
            if (route is null) return long.MaxValue;
            return route.Sum(x => RoadTraffic.DriveMillis(_roads.Segments[x.Segment], x.Length, truck.SpeedMetresPerSecond,
                RoadTraffic.LoadPercent(_roads.Segments[x.Segment], now, occupancy.GetValueOrDefault((x.Segment, x.Forward)))));
        }

        // For screens: about how many seconds a truck on the road still needs to reach its destination, from what a site view
        // carries (the truck, the company's map records, the clock): the rest of its leg plus the quickest way on at the
        // current city traffic, without other trucks. Null for a truck that is not driving the network.
        public static long? EstimateRoadSeconds(GoodsSnapshot view, RoadNetwork network, GoodsTruck truck)
        {
            if (view is null || network is null || truck is null || !truck.Driving || !truck.OnRoad
                || !network.TryGetSegment(truck.LegSegmentId, out var segment)) return null;
            var site = view.Sites.FirstOrDefault(x => x.Id == truck.DestinationSiteId);
            var target = site is null ? null : network.Locate(site.MapX, site.MapZ);
            if (target is null) return truck.RemainingSeconds;
            var clock = view.ClockSeconds;
            var route = network.Route(new RoadPoint(segment, truck.LegTo), target.Value,
                (index, _) => RoadTraffic.DriveMillis(network.Segments[index], network.Segments[index].Length, truck.SpeedMetresPerSecond,
                    RoadTraffic.LoadPercent(network.Segments[index], clock, 0)),
                (index, forward) => RoadTraffic.JunctionEstimateMillis(network, index, forward, clock));
            if (route is null) return truck.RemainingSeconds;
            var millis = route.Sum(x => RoadTraffic.DriveMillis(network.Segments[x.Segment], x.Length, truck.SpeedMetresPerSecond,
                RoadTraffic.LoadPercent(network.Segments[x.Segment], clock, 0)));
            for (var i = 0; i + 1 < route.Count; i++) millis += RoadTraffic.JunctionEstimateMillis(network, route[i].Segment, route[i].Forward, clock);
            return truck.RemainingSeconds + (millis + 999) / 1000;
        }

        private void SetLeg(GoodsTruck truck, int segment, int from, int to, long driveSeconds, long legSeconds)
        {
            truck.LegSegmentId = _roads.Segments[segment].Id;
            truck.LegFrom = from;
            truck.LegTo = to;
            truck.LegDriveSeconds = driveSeconds;
            truck.LegSeconds = legSeconds;
        }

        private static void ClearNext(GoodsTruck truck)
        {
            truck.NextSegmentId = "";
            truck.NextFrom = 0;
            truck.NextTo = 0;
            truck.NextFinal = false;
        }

        private static void ClearLeg(GoodsTruck truck)
        {
            ClearNext(truck);
            truck.LegSegmentId = "";
            truck.LegFrom = 0;
            truck.LegTo = 0;
            truck.LegDriveSeconds = 0;
            truck.LegSeconds = 0;
            truck.QueuedSeconds = 0;
        }

        // One second at the pickup dock: loads up to the truck's rate of allowed, unreserved goods from the dock's outgoing
        // buffer, most exposed first. With nothing loaded and cargo aboard it leaves for the dropoff. False when it waits.
        private bool LoadSecond(GoodsTruck truck)
        {
            var route = Route(truck);
            var dock = PlacedDock(route.PickupDockId);
            if (dock is null) return false;
            var budget = truck.LoadUnitsPerSecond;
            var candidates = _state.Lots
                .Where(x => x.LocationId == dock.InputLocationId && x.OwnerId == dock.SiteId
                    && (route.AllowedItemIds.Count == 0 || route.AllowedItemIds.Contains(x.ItemId)) && Available(x) > 0)
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
            Drive(truck, DockSite(route.DropoffDockId), TruckState.ToDropoff, TruckState.Unloading);
            return true;
        }

        // One second at the dropoff dock: unloads up to the truck's rate into the dock's incoming buffer, re-owned by that
        // site, most exposed first. Once empty it heads back to the pickup. False when it waits (full buffer, no dock).
        private bool UnloadSecond(GoodsTruck truck)
        {
            var route = Route(truck);
            if (!HasCargo(truck))
            {
                Drive(truck, DockSite(route.PickupDockId), TruckState.ToPickup, TruckState.Loading);
                return true;
            }
            var dock = PlacedDock(route.DropoffDockId);
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
            if (!HasCargo(truck)) Drive(truck, DockSite(route.PickupDockId), TruckState.ToPickup, TruckState.Loading);
            return true;
        }

        // A placed dock the truck can use: on a restaurant's site it must also be reachable from the street (decision 0034).
        private GoodsEquipment PlacedDock(string dockId)
        {
            var dock = _state.Equipment.FirstOrDefault(x => x.Id == dockId && x.Kind == DockKind && x.State == EquipmentState.Placed);
            if (dock is null || !RestaurantRules.IsRestaurantSite(_state, dock.SiteId)) return dock;
            if (!_usableDocks.TryGetValue(dock.Id, out var usable))
                _usableDocks[dock.Id] = usable = RestaurantRules.Touches(RestaurantRules.Reached(RestaurantRules.Walkable(_state, dock.SiteId),
                    RestaurantRules.StreetCells(_state.SiteLayouts.FirstOrDefault(x => x.SiteId == dock.SiteId),
                        _propertyOffers?.Values.FirstOrDefault(x => x.SiteId == dock.SiteId))), dock);
            return usable ? dock : null;
        }

        // Street reachability of restaurant docks during one MoveTrucks call.
        private readonly Dictionary<string, bool> _usableDocks = new(StringComparer.Ordinal);

        // The dock a working truck is at: its route's pickup while loading, its dropoff while unloading.
        private string WorkingDockId(GoodsTruck truck) => truck.State == TruckState.Loading ? Route(truck).PickupDockId : Route(truck).DropoffDockId;

        // True when the truck may work its dock this second: any dock off a restaurant site, or a restaurant dock it holds or can
        // take because no other truck holds it.
        private bool TakeDock(GoodsTruck truck, List<GoodsTruck> trucks)
        {
            if (truck.Docked) return true;
            var dockId = WorkingDockId(truck);
            var dock = _state.Equipment.FirstOrDefault(x => x.Id == dockId);
            if (dock is null || !RestaurantRules.IsRestaurantSite(_state, dock.SiteId)) return true;
            if (trucks.Any(x => x != truck && x.Docked && x.State is TruckState.Loading or TruckState.Unloading && WorkingDockId(x) == dockId)) return false;
            truck.Docked = true;
            return true;
        }

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
            // The part joins an equivalent lot already there (decision 0003: same item, owner, condition and spoilage history,
            // unreserved; GoodsWorld.Merge's rule), so a truck loading a few units a second does not leave a new lot behind every
            // second (decision 0032 scale benchmark). Whole lots above keep their IDs.
            var same = _state.Lots.FirstOrDefault(x => x.LocationId == locationId && x.ItemId == lot.ItemId && x.OwnerId == ownerId
                && x.ExposureSeconds == lot.ExposureSeconds && x.SpoilAfterSeconds == lot.SpoilAfterSeconds && x.Spoiled == lot.Spoiled
                && !_state.Reservations.Any(r => r.Active && r.LotId == x.Id));
            if (same is not null)
            {
                checked { same.Quantity += quantity; }
                return;
            }
            _state.Lots.Add(new GoodsLot
            {
                Id = SplitLotId(lot.Id, locationId), ItemId = lot.ItemId, OwnerId = ownerId, LocationId = locationId, Quantity = quantity,
                ExposureSeconds = lot.ExposureSeconds, SpoilAfterSeconds = lot.SpoilAfterSeconds, Spoiled = lot.Spoiled
            });
        }

        // A split's ID comes from the lot, the destination and the truck second, not from a random GUID: lot IDs break ties in
        // loading order, so random IDs made one long clock step differ from many short ones. A lot moves to one location at
        // most once a truck second, so the inputs are unique; a hash collision still takes the next free suffix.
        private string SplitLotId(string lotId, string locationId)
        {
            using var hash = System.Security.Cryptography.SHA256.Create();
            var bytes = hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{lotId}\n{locationId}\n{_truckSecond}"));
            var id = string.Concat(bytes.Take(16).Select(x => x.ToString("x2")));
            var candidate = id;
            for (var suffix = 1; _state.Lots.Any(x => x.Id == candidate); suffix++) candidate = $"{id}-{suffix}";
            return candidate;
        }

        // The clock second the truck simulation is working through (MoveTrucks).
        private long _truckSecond;

        // A site's baseline carries its company's routes and fleet (with each truck's cargo), and the map records of the
        // company's sites, so the route screen can show every truck wherever it is.
        private void ViewLogistics(GoodsSnapshot view, string siteId)
        {
            var company = _state.Companies.FirstOrDefault(x => x.SiteIds.Contains(siteId));
            view.Sites = _state.Sites.Where(x => x.Id == siteId || company?.SiteIds.Contains(x.Id) == true)
                .Select(x => JsonUtility.FromJson<GoodsSite>(JsonUtility.ToJson(x))).ToList();
            view.Routes = company is null ? new List<GoodsRoute>()
                : _state.Routes.Where(x => x.CompanyId == company.Id).Select(x => JsonUtility.FromJson<GoodsRoute>(JsonUtility.ToJson(x))).ToList();
            view.Trucks = company is null ? new List<GoodsTruck>()
                : _state.Trucks.Where(x => x.CompanyId == company.Id).Select(x => JsonUtility.FromJson<GoodsTruck>(JsonUtility.ToJson(x))).ToList();
            // Every other company's truck on the road is public, like a vehicle seen in the street (decision 0032): where it is
            // and where it goes, never its route or cargo.
            view.RoadTrucks = _state.Trucks.Where(x => x.CompanyId != company?.Id && x.Driving && x.OnRoad)
                .Select(x =>
                {
                    var copy = JsonUtility.FromJson<GoodsTruck>(JsonUtility.ToJson(x));
                    copy.RouteId = "";
                    return copy;
                }).ToList();
            // Appended after the site's own locations: a baseline names its site by its first location.
            var cargoIds = new HashSet<string>(view.Trucks.Select(x => x.CargoLocationId));
            view.Locations.AddRange(_state.Locations.Where(x => cargoIds.Contains(x.Id)).Select(x => JsonUtility.FromJson<GoodsLocation>(JsonUtility.ToJson(x))));
            view.Lots.AddRange(_state.Lots.Where(x => cargoIds.Contains(x.LocationId)).Select(x => JsonUtility.FromJson<GoodsLot>(JsonUtility.ToJson(x))));
        }

        private static bool NoLeg(GoodsTruck truck) => truck.LegSegmentId == "" && truck.LegFrom == 0 && truck.LegTo == 0
            && truck.LegDriveSeconds == 0 && truck.LegSeconds == 0 && truck.QueuedSeconds == 0 && NoNext(truck);

        private static bool NoNext(GoodsTruck truck) => truck.NextSegmentId == "" && truck.NextFrom == 0 && truck.NextTo == 0 && !truck.NextFinal;

        private static void ValidateTrucks(GoodsSnapshot state)
        {
            var siteIds = new HashSet<string>(state.Sites.Where(x => x is not null).Select(x => x.Id));
            if (state.Sites.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || x.Id == RoadSiteId || x.Name is null)
                || state.Sites.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Grants.Any(x => x.SiteId == RoadSiteId)
                || state.Companies.Any(x => x.SiteIds.Contains(RoadSiteId))
                || state.Trucks.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id))
                || state.Trucks.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Routes.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id))
                || state.Routes.GroupBy(x => x.Id).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot violates site, route or truck identity invariants.");
            foreach (var route in state.Routes)
                if (route.AllowedItemIds is null || CargoFilterProblem(route.AllowedItemIds) is not null
                    || RouteProblem(state, route.CompanyId, route.PickupDockId, route.DropoffDockId) is not null)
                    throw new InvalidOperationException($"Route {route.Id} is inconsistent.");
            foreach (var truck in state.Trucks)
            {
                var cargo = state.Locations.FirstOrDefault(x => x.Id == truck.CargoLocationId);
                var route = RouteOf(state, truck);
                var valid = state.Companies.Any(x => x.Id == truck.CompanyId) && truck.Name is not null
                    && truck.CargoSlots >= 1 && truck.SpeedMetresPerSecond >= 1 && truck.LoadUnitsPerSecond >= 1
                    && cargo is not null && cargo.Kind == VehicleLocationKind && cargo.SiteId == RoadSiteId && cargo.Capacity == truck.CargoSlots
                    && !cargo.Refrigerated
                    && siteIds.Contains(truck.SiteId)
                    && truck.State is TruckState.Parked or TruckState.ToPickup or TruckState.Loading or TruckState.ToDropoff
                        or TruckState.Unloading or TruckState.ToPark
                    && (truck.State is TruckState.Parked or TruckState.ToPark
                        ? string.IsNullOrEmpty(truck.RouteId)
                        : route is not null && route.CompanyId == truck.CompanyId)
                    && truck.LegSegmentId is not null
                    && (truck.Driving
                        ? siteIds.Contains(truck.DestinationSiteId)
                          && (truck.OnRoad
                              ? truck.LegFrom >= 0 && truck.LegTo >= 0 && truck.LegDriveSeconds >= 0
                                && truck.LegSeconds >= truck.LegDriveSeconds && truck.RemainingSeconds >= 0
                                && truck.RemainingSeconds <= truck.LegSeconds && truck.QueuedSeconds >= 0 && truck.NextSegmentId is not null
                                && (truck.RemainingSeconds == 0 || NoNext(truck)) && truck.NextFrom >= 0 && truck.NextTo >= 0
                              : truck.RemainingSeconds >= 1 && NoLeg(truck))
                        : string.IsNullOrEmpty(truck.DestinationSiteId) && truck.RemainingSeconds == 0 && NoLeg(truck))
                    && (!truck.Docked || truck.State is TruckState.Loading or TruckState.Unloading);
                if (!valid) throw new InvalidOperationException($"Truck {truck.Id} is inconsistent.");
            }
            // A restaurant dock is held by at most one truck (decision 0034).
            if (state.Trucks.Where(x => x.Docked).GroupBy(x =>
                {
                    var route = RouteOf(state, x);
                    return x.State == TruckState.Loading ? route?.PickupDockId : route?.DropoffDockId;
                }).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot has two trucks holding one dock.");
            // Other companies' trucks reach a view only (ViewLogistics); a world never stores them.
            if (state.RoadTrucks is null || state.RoadTrucks.Count != 0)
                throw new InvalidOperationException("Goods snapshot stores view-only road trucks.");
            // Every vehicle location is one truck's cargo; nothing else stands on the road.
            var cargoIds = new HashSet<string>(state.Trucks.Select(x => x.CargoLocationId));
            if (state.Locations.Any(x => (x.Kind == VehicleLocationKind || x.SiteId == RoadSiteId) && !cargoIds.Contains(x.Id)))
                throw new InvalidOperationException("Goods snapshot has a vehicle location without its truck.");
        }
    }
}
