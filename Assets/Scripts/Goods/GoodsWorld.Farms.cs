// Farm orders (owner request 2026-10-13, decision 0047). Every listed farm lot has a farm type that decides which raw ingredients
// it sells; a player orders packs of one of them to a placed dock of a site their company owns and the company pays at once. The
// goods come one of two ways:
// - Delivery: the company also pays a flat fee, and a supplier truck that belongs to the order (GoodsTruck.Supplier, never part of
//   the fleet) starts at the farm's gate with the goods aboard, drives the roads to the dock, unloads and drives back, and is
//   removed there. Its cargo is refrigerated (starting-loop decision 7: supplier goods do not age until unloaded).
// - Pickup: one of the company's parked trucks drives to the farm's gate, loads the goods there (they are created on loading),
//   brings them to the dock, unloads and parks at the dock's site. Its cargo ages as any truck's does.
// A farm's gate is a world-owned site (FarmGateSiteId) at the lot's street access: a map record only, created with the first
// order to that farm, never granted or owned. It is not the lot's own site, so the farm can still be bought.
// Failure behaviour (starting-loop decision 8): a dock that is gone or unreachable when the truck arrives is replaced by another
// usable dock of the same site; a truck that cannot unload (no usable dock, full incoming buffer) waits up to
// FarmDockTimeoutSeconds and then gives up. A supplier truck then drives its remaining goods back to the farm, where they leave
// the world and are refunded (the fee too when nothing was unloaded); a pickup truck parks with the goods still aboard, so they
// stay the company's. Goods are never deleted without a refund, and never duplicated.
// Owning the farm's lot halves the goods price (FarmOwnedPricePercent); the fee is flat. Recurring orders (GoodsFarmSchedule)
// place the same order every few game hours inside the clock step; one that cannot be placed records why and tries again at its
// next hour. All numbers are PROTOTYPE.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    public enum FarmKind
    {
        Dairy,
        Cattle,
        Poultry,
        Vegetable,
        Grain,
        Fishery
    }

    // Content derived from the stored layout: which type each farm is, and what each type sells.
    public static class FarmCatalog
    {
        public static readonly FarmKind[] Kinds = { FarmKind.Dairy, FarmKind.Cattle, FarmKind.Poultry, FarmKind.Vegetable, FarmKind.Grain, FarmKind.Fishery };

        // Item IDs a farm of this type sells (supplier offers of these items give the pack size and list price).
        public static IReadOnlyList<string> Items(FarmKind kind) => kind switch
        {
            FarmKind.Dairy => new[] { "milk", "cheese" },
            FarmKind.Cattle => new[] { "beef" },
            FarmKind.Poultry => new[] { "egg", "chicken" },
            FarmKind.Vegetable => new[] { "potato", "tomato", "lettuce" },
            FarmKind.Grain => new[] { "flour", "dough" },
            _ => new[] { "fish" }
        };

        public static string Name(FarmKind kind) => kind switch
        {
            FarmKind.Dairy => "Dairy farm",
            FarmKind.Cattle => "Cattle farm",
            FarmKind.Poultry => "Poultry farm",
            FarmKind.Vegetable => "Vegetable farm",
            FarmKind.Grain => "Grain farm",
            _ => "Fishery"
        };

        // Farms take the types in turn in lot ID order, so a world with six or more farms has every type, and server and clients
        // derive the same types from the same layout.
        public static Dictionary<string, FarmKind> KindsOf(IEnumerable<PropertyOffer> offers)
        {
            var kinds = new Dictionary<string, FarmKind>(StringComparer.Ordinal);
            var farms = (offers ?? Enumerable.Empty<PropertyOffer>()).Where(x => x != null && x.Category == GoodsWorld.FarmCategory)
                .Select(x => x.LotId).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            for (var index = 0; index < farms.Count; index++) kinds[farms[index]] = Kinds[index % Kinds.Length];
            return kinds;
        }

        public static string GateSiteId(string lotId) => "farm-gate:" + lotId;
    }

    // An open farm order: from placement until its goods are all at the dock (or, for a supplier truck, back at the farm).
    [Serializable] public sealed class GoodsFarmOrder
    {
        // "order:<player>:<request>", or "order:<schedule>:<game hour>" for a recurring order.
        public string Id;
        public string CompanyId;
        // Who placed it (the schedule's owner for a recurring order).
        public string PlayerId;
        public string FarmLotId;
        public string OfferId;
        public string ItemId;
        public int Units;
        public long SpoilAfterSeconds;
        // Whole cents paid for the goods and for delivery (0 for a pickup).
        public long GoodsCents;
        public long FeeCents;
        // The receiving site and dock (the dock may be replaced by another of the site's docks on arrival).
        public string SiteId;
        public string DockId;
        // The supplier truck (delivery) or the company's truck (pickup) serving it.
        public string TruckId;
        public bool Pickup;
        public string ScheduleId = "";
        public long PlacedSeconds;
        // Pickup: true once the goods are aboard. Delivery: always true.
        public bool Loaded;
        public int UnloadedUnits;
        // Seconds the truck has waited at the dock without unloading anything (FarmDockTimeoutSeconds).
        public long WaitSeconds;
        // A supplier truck that gave up or finished is driving back to the farm.
        public bool Returning;
    }

    // A recurring farm order: the same order every EveryHours game hours, from NextHour on.
    [Serializable] public sealed class GoodsFarmSchedule
    {
        // "schedule:<player>:<request>".
        public string Id;
        public string CompanyId;
        public string PlayerId;
        public string FarmLotId;
        public string OfferId;
        public int Packs;
        public string DockId;
        // Empty for delivery; otherwise the company truck that picks the goods up.
        public string TruckId = "";
        public int EveryHours;
        // The game hour (clock / GameHourSeconds) of the next order.
        public long NextHour;
        // Why the last attempt placed nothing, or empty.
        public string LastProblem = "";
    }

    public sealed partial class GoodsWorld
    {
        // PROTOTYPE values (decision 0047).
        public const long FarmDeliveryFeeCents = 2000;
        public const int FarmOwnedPricePercent = 50;
        public const int MaxFarmOrderPacks = 20;
        public const int MaxFarmScheduleHours = 24;
        // Starting-loop decision 15: two game hours.
        public const long FarmDockTimeoutSeconds = 120;
        public const int SupplierTruckSpeed = 15;
        public const int SupplierTruckLoadUnits = 5;
        public const int SupplierTruckMinSlots = 4;
        public const string SupplierTruckName = "Farm truck";
        public const string LedgerFarmGoods = "farm-goods";
        public const string LedgerFarmDelivery = "farm-delivery";
        public const string LedgerFarmRefund = "farm-refund";

        // The goods price of packs of an offer from a farm: halved when the company owns the farm's lot.
        public static long FarmGoodsCents(long packCents, int packs, bool owned) =>
            checked(packCents * packs * (owned ? FarmOwnedPricePercent : 100) / 100);

        // Whether a company owns a farm lot, from a snapshot or view (properties are public).
        public static bool OwnsLot(GoodsSnapshot state, string companyId, string lotId) =>
            state?.Properties?.Any(x => x.LotId == lotId && x.CompanyId == companyId) == true;

        // The dock a truck serving an order or a route loads or unloads at (null for an order truck loading at a farm, or when
        // it is at no dock). For presentation as well as the simulation.
        public static string WorkingDockOf(GoodsSnapshot state, GoodsTruck truck)
        {
            if (truck is null) return null;
            if (!string.IsNullOrEmpty(truck.OrderId))
                return truck.State == TruckState.Unloading ? state.FarmOrders?.FirstOrDefault(x => x.Id == truck.OrderId)?.DockId : null;
            var route = RouteOf(state, truck);
            return truck.State == TruckState.Loading ? route?.PickupDockId : truck.State == TruckState.Unloading ? route?.DropoffDockId : null;
        }

        // Volatile primitive for tests. Live request handlers must call OrderFromFarmDurably.
        // Orders packs of a farm's offer to a dock. truckId empty means delivery (pays the fee); otherwise that parked truck of the
        // dock's company picks the goods up. Checks, in order: identity and replay; the player's grant on the dock's site
        // (forbidden); the farm (unknown-farm); the offer (not-sold-here); packs (invalid-quantity); the dock (invalid-dock); its
        // company (no-company); the dock reachable from the street (dock-unreachable); road map records (no-road); for a pickup
        // the truck (invalid-truck, truck-busy, capacity); cash (insufficient-funds). Like purchases, only the accepted order is
        // recorded.
        public GoodsOutcome OrderFromFarm(string playerId, string requestId, string farmLotId, string offerId, int packs, string dockId, string truckId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                var dock = _state.Equipment.FirstOrDefault(x => x.Id == dockId && x.Kind == DockKind);
                var cents = 0L;
                var problem = dock is not null && !CanView(playerId, dock.SiteId) ? "forbidden"
                    : PlaceFarmOrderLocked($"order:{playerId}:{requestId}", playerId, requestId, farmLotId, offerId, packs, dockId, truckId, "", out cents);
                if (problem is not null)
                    return new GoodsOutcome { RequestId = requestId, PlayerId = playerId, Accepted = false, Reason = problem, Revision = _state.Revision };
                var outcome = Record(requestId, playerId, true, "ordered", null);
                outcome.Cents = cents;
                _state.Outcomes[_state.Outcomes.Count - 1].Cents = cents;
                return outcome;
            }
        }

        public GoodsOutcome OrderFromFarmDurably(string playerId, string requestId, string farmLotId, string offerId, int packs, string dockId,
            string truckId, string savePath) =>
            Commit(playerId, requestId, savePath, () => OrderFromFarm(playerId, requestId, farmLotId, offerId, packs, dockId, truckId));

        // Volatile primitive for tests. Live request handlers must call ScheduleFarmOrderDurably.
        // Repeats an order every everyHours game hours from the next game hour on (the first order is not placed now). The order's
        // checks that do not depend on the moment are made now (same reasons as OrderFromFarm, plus invalid-interval); cash, a
        // busy truck and the dock's state are checked each time it runs.
        public GoodsOutcome ScheduleFarmOrder(string playerId, string requestId, string farmLotId, string offerId, int packs, string dockId,
            string truckId, int everyHours)
        {
            return RouteCommand(playerId, requestId, reject =>
            {
                var dock = _state.Equipment.FirstOrDefault(x => x.Id == dockId && x.Kind == DockKind);
                var problem = FarmOrderProblem(farmLotId, offerId, packs, dock, truckId, false, out var company, out _, out _);
                if (problem is null && !CanView(playerId, dock.SiteId)) problem = "forbidden";
                if (problem is null && (everyHours < 1 || everyHours > MaxFarmScheduleHours)) problem = "invalid-interval";
                if (problem is not null) return reject(problem);
                _state.FarmSchedules.Add(new GoodsFarmSchedule
                {
                    Id = $"schedule:{playerId}:{requestId}", CompanyId = company, PlayerId = playerId, FarmLotId = farmLotId, OfferId = offerId,
                    Packs = packs, DockId = dockId, TruckId = truckId ?? "", EveryHours = everyHours,
                    NextHour = _state.ClockSeconds / GameHourSeconds + 1
                });
                return Record(requestId, playerId, true, "scheduled", null);
            });
        }

        public GoodsOutcome ScheduleFarmOrderDurably(string playerId, string requestId, string farmLotId, string offerId, int packs, string dockId,
            string truckId, int everyHours, string savePath) =>
            Commit(playerId, requestId, savePath, () => ScheduleFarmOrder(playerId, requestId, farmLotId, offerId, packs, dockId, truckId, everyHours));

        // Volatile primitive for tests. Live request handlers must call CancelFarmScheduleDurably. Orders it already placed go on.
        public GoodsOutcome CancelFarmSchedule(string playerId, string requestId, string scheduleId)
        {
            return RouteCommand(playerId, requestId, reject =>
            {
                var schedule = _state.FarmSchedules.FirstOrDefault(x => x.Id == scheduleId);
                var company = schedule is null ? null : _state.Companies.FirstOrDefault(x => x.Id == schedule.CompanyId);
                if (company is null || !company.SiteIds.Any(x => CanView(playerId, x))) return reject("forbidden");
                _state.FarmSchedules.Remove(schedule);
                return Record(requestId, playerId, true, "unscheduled", null);
            });
        }

        public GoodsOutcome CancelFarmScheduleDurably(string playerId, string requestId, string scheduleId, string savePath) =>
            Commit(playerId, requestId, savePath, () => CancelFarmSchedule(playerId, requestId, scheduleId));

        // Call only under _gate. Null when the order can be placed now (company, total cents out), otherwise the first problem.
        private string FarmOrderProblem(string farmLotId, string offerId, int packs, GoodsEquipment dock, string truckId, bool now,
            out string companyId, out PurchaseOffer offer, out long cents)
        {
            companyId = null;
            offer = null;
            cents = 0;
            if (farmLotId is null || _propertyOffers is null || !_propertyOffers.TryGetValue(farmLotId, out var farm) || farm.Category != FarmCategory)
                return "unknown-farm";
            var kind = FarmCatalog.KindsOf(_propertyOffers.Values)[farmLotId];
            if (offerId is null || !_offers.TryGetValue(offerId, out offer) || !FarmCatalog.Items(kind).Contains(offer.ItemId)) return "not-sold-here";
            if (packs < 1 || packs > MaxFarmOrderPacks) return "invalid-quantity";
            if (dock is null) return "invalid-dock";
            companyId = CompanyOfSiteLocked(dock.SiteId);
            if (companyId is null) return "no-company";
            if (_state.Sites.All(x => x.Id != dock.SiteId)) return "no-road";
            var units = checked(offer.Quantity * packs);
            if (!string.IsNullOrEmpty(truckId))
            {
                var truck = _state.Trucks.FirstOrDefault(x => x.Id == truckId);
                if (truck is null || truck.CompanyId != companyId || truck.Supplier) return "invalid-truck";
                if (now && (truck.State != TruckState.Parked || !string.IsNullOrEmpty(truck.OrderId))) return "truck-busy";
                if (now && !Fits(truck.CargoLocationId, offer.ItemId, false, units)) return "capacity";
                if (!now && GoodsSlots.Slots(units, MaxStackLocked(offer.ItemId)) > truck.CargoSlots)
                    return "capacity";
            }
            if (!now) return null;
            if (dock.State != EquipmentState.Placed || PlacedDock(dock.Id) is null) return "dock-unreachable";
            cents = FarmGoodsCents(offer.PriceCents, packs, OwnsLot(_state, companyId, farmLotId)) + (string.IsNullOrEmpty(truckId) ? FarmDeliveryFeeCents : 0);
            var payer = companyId;
            if (_state.Companies.First(x => x.Id == payer).Cash < cents) return "insufficient-funds";
            return null;
        }

        // Call only under _gate. Checks the order and, when nothing stands in the way, pays for it and sends its truck off. The
        // order's ID must be new. Null when placed (cents is what it cost), otherwise the problem and nothing changed.
        private string PlaceFarmOrderLocked(string orderId, string playerId, string requestId, string farmLotId, string offerId, int packs,
            string dockId, string truckId, string scheduleId, out long cents)
        {
            var dock = _state.Equipment.FirstOrDefault(x => x.Id == dockId && x.Kind == DockKind);
            _usableDocks.Clear();
            var problem = FarmOrderProblem(farmLotId, offerId, packs, dock, truckId, true, out var companyId, out var offer, out cents);
            _usableDocks.Clear();
            if (problem is not null) return problem;
            if (_state.FarmOrders.Any(x => x.Id == orderId)) return "duplicate-order";

            // All checks precede the mutation.
            var pickup = !string.IsNullOrEmpty(truckId);
            var units = offer.Quantity * packs;
            var feeCents = pickup ? 0 : FarmDeliveryFeeCents;
            var goodsCents = cents - feeCents;
            var gate = FarmGate(farmLotId);
            TryDebit(companyId, goodsCents, new CashNote(LedgerFarmGoods, dock.SiteId, requestId, offerId: offerId));
            if (feeCents > 0) TryDebit(companyId, feeCents, new CashNote(LedgerFarmDelivery, dock.SiteId, requestId, offerId: offerId));
            var order = new GoodsFarmOrder
            {
                Id = orderId, CompanyId = companyId, PlayerId = playerId, FarmLotId = farmLotId, OfferId = offerId, ItemId = offer.ItemId,
                Units = units, SpoilAfterSeconds = offer.SpoilAfterSeconds, GoodsCents = goodsCents, FeeCents = feeCents, SiteId = dock.SiteId,
                DockId = dock.Id, Pickup = pickup, ScheduleId = scheduleId ?? "", PlacedSeconds = _state.ClockSeconds, Loaded = !pickup
            };
            _state.FarmOrders.Add(order);
            if (pickup)
            {
                var truck = _state.Trucks.First(x => x.Id == truckId);
                order.TruckId = truck.Id;
                truck.OrderId = order.Id;
                Drive(truck, gate, TruckState.ToPickup, TruckState.Loading);
            }
            else
            {
                var truck = new GoodsTruck
                {
                    Id = "supply:" + order.Id, CompanyId = companyId, Name = SupplierTruckName, Supplier = true, OrderId = order.Id,
                    CargoSlots = Math.Max(SupplierTruckMinSlots, GoodsSlots.Slots(units, MaxStackLocked(offer.ItemId))),
                    SpeedMetresPerSecond = SupplierTruckSpeed, LoadUnitsPerSecond = SupplierTruckLoadUnits, State = TruckState.Parked, SiteId = gate
                };
                order.TruckId = truck.Id;
                AddTruck(truck);
                _state.Lots.Add(new GoodsLot
                {
                    Id = "farm:" + order.Id, ItemId = offer.ItemId, OwnerId = dock.SiteId, LocationId = truck.CargoLocationId, Quantity = units,
                    SpoilAfterSeconds = offer.SpoilAfterSeconds
                });
                Drive(truck, dock.SiteId, TruckState.ToDropoff, TruckState.Unloading);
            }
            _state.Revision++;
            return null;
        }

        // Call only under _gate. The farm's gate site, created at its lot's street access on first use.
        private string FarmGate(string farmLotId)
        {
            var id = FarmCatalog.GateSiteId(farmLotId);
            if (_state.Sites.Any(x => x.Id == id)) return id;
            var farm = _propertyOffers[farmLotId];
            _state.Sites.Add(new GoodsSite { Id = id, Name = farm.BuildingId, MapX = farm.AccessX, MapZ = farm.AccessZ });
            _sitePoints.Remove(id);
            return id;
        }

        // Inside the clock step: places every recurring order whose game hour has come (once, however many hours a step covers).
        private void RunFarmSchedules()
        {
            if (_state.FarmSchedules.Count == 0) return;
            var hour = _state.ClockSeconds / GameHourSeconds;
            foreach (var schedule in _state.FarmSchedules.Where(x => x.NextHour <= hour).OrderBy(x => x.Id, StringComparer.Ordinal).ToList())
            {
                var problem = PlaceFarmOrderLocked($"order:{schedule.Id}:{schedule.NextHour}", schedule.PlayerId, "", schedule.FarmLotId, schedule.OfferId,
                    schedule.Packs, schedule.DockId, schedule.TruckId, schedule.Id, out _);
                schedule.LastProblem = problem ?? "";
                schedule.NextHour = Math.Max(schedule.NextHour + schedule.EveryHours, hour + 1);
            }
        }

        // One second of an order truck at the farm gate: the order's goods are created in its cargo, and it leaves for the dock.
        private bool LoadOrderSecond(GoodsTruck truck)
        {
            var order = _state.FarmOrders.First(x => x.Id == truck.OrderId);
            if (!order.Loaded)
            {
                _state.Lots.Add(new GoodsLot
                {
                    Id = "farm:" + order.Id, ItemId = order.ItemId, OwnerId = order.SiteId, LocationId = truck.CargoLocationId, Quantity = order.Units,
                    SpoilAfterSeconds = order.SpoilAfterSeconds
                });
                order.Loaded = true;
            }
            Drive(truck, order.SiteId, TruckState.ToDropoff, TruckState.Unloading);
            return true;
        }

        // One second of an order truck at the receiving site: unloads the order's goods into the dock's incoming buffer (or another
        // usable dock of the site). Waiting counts toward the timeout, after which it gives up; once empty of the order's goods it
        // is done. Always true: a waiting order truck is rechecked every second so its wait is counted.
        private bool UnloadOrderSecond(GoodsTruck truck)
        {
            var order = _state.FarmOrders.First(x => x.Id == truck.OrderId);
            var dock = PlacedDock(order.DockId);
            if (dock is null || dock.SiteId != order.SiteId)
            {
                var other = _state.Equipment.Where(x => x.SiteId == order.SiteId && x.Kind == DockKind && x.State == EquipmentState.Placed)
                    .OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => PlacedDock(x.Id)).FirstOrDefault(x => x is not null);
                if (other is not null)
                {
                    // Redirected: it takes its turn at the other dock from the next second.
                    order.DockId = other.Id;
                    truck.Docked = false;
                    return true;
                }
            }
            var moved = 0;
            if (dock is not null)
            {
                var budget = truck.LoadUnitsPerSecond;
                foreach (var lot in OrderCargo(truck, order).ToList())
                {
                    var take = (int)Math.Min(Math.Min(budget, lot.Quantity), FreeUnits(dock.OutputLocationId, lot));
                    if (take == 0) continue;
                    MoveUnits(lot, dock.OutputLocationId, take, dock.SiteId);
                    budget -= take;
                    moved += take;
                    if (budget == 0) break;
                }
            }
            order.UnloadedUnits += moved;
            if (moved > 0) order.WaitSeconds = 0;
            else order.WaitSeconds++;
            if (order.UnloadedUnits < order.Units && order.WaitSeconds < FarmDockTimeoutSeconds) return true;
            FinishUnloading(truck, order);
            return true;
        }

        // The order's goods aboard: the lot made for it and, after a partial unload, what is left of it.
        private IEnumerable<GoodsLot> OrderCargo(GoodsTruck truck, GoodsFarmOrder order) => _state.Lots
            .Where(x => x.LocationId == truck.CargoLocationId && x.ItemId == order.ItemId)
            .OrderByDescending(x => x.ExposureSeconds).ThenBy(x => x.Id, StringComparer.Ordinal);

        // All unloaded, or given up: a pickup truck parks where it is with anything left aboard (the company's goods) and the order
        // ends; a supplier truck drives back to the farm with what is left.
        private void FinishUnloading(GoodsTruck truck, GoodsFarmOrder order)
        {
            truck.Docked = false;
            if (!truck.Supplier)
            {
                truck.OrderId = "";
                truck.State = TruckState.Parked;
                _state.FarmOrders.Remove(order);
                return;
            }
            order.Returning = true;
            Drive(truck, FarmCatalog.GateSiteId(order.FarmLotId), TruckState.ToPark, TruckState.Parked);
            if (truck.State == TruckState.Parked) ReturnSupplierTruck(truck);
        }

        // A supplier truck back at its farm: what it still carries leaves the world and is refunded (goods pro rata, and the fee
        // when nothing was delivered), and the truck, its cargo location and the order go.
        private void ReturnSupplierTruck(GoodsTruck truck)
        {
            var order = _state.FarmOrders.FirstOrDefault(x => x.Id == truck.OrderId);
            var left = _state.Lots.Where(x => x.LocationId == truck.CargoLocationId).ToList();
            if (order is not null)
            {
                var units = left.Sum(x => (long)x.Quantity);
                var refund = order.Units == 0 ? 0 : order.GoodsCents * Math.Min(units, order.Units) / order.Units;
                if (order.UnloadedUnits == 0) refund += order.FeeCents;
                if (refund > 0) TryCredit(order.CompanyId, refund, new CashNote(LedgerFarmRefund, order.SiteId, offerId: order.OfferId, clockSeconds: Now));
                _state.FarmOrders.Remove(order);
            }
            foreach (var lot in left) _state.Lots.Remove(lot);
            _state.Reservations.RemoveAll(x => left.Any(y => y.Id == x.LotId));
            _state.Trucks.Remove(truck);
            _state.Locations.RemoveAll(x => x.Id == truck.CargoLocationId);
        }

        // A site view carries its company's open orders and schedules, and the gates its orders' trucks drive to or from.
        private void ViewFarms(GoodsSnapshot view, string siteId)
        {
            var company = _state.Companies.FirstOrDefault(x => x.SiteIds.Contains(siteId));
            view.FarmOrders = company is null ? new List<GoodsFarmOrder>()
                : _state.FarmOrders.Where(x => x.CompanyId == company.Id).Select(x => JsonUtility.FromJson<GoodsFarmOrder>(JsonUtility.ToJson(x))).ToList();
            view.FarmSchedules = company is null ? new List<GoodsFarmSchedule>()
                : _state.FarmSchedules.Where(x => x.CompanyId == company.Id).Select(x => JsonUtility.FromJson<GoodsFarmSchedule>(JsonUtility.ToJson(x))).ToList();
            var gates = new HashSet<string>(view.FarmOrders.Select(x => FarmCatalog.GateSiteId(x.FarmLotId)), StringComparer.Ordinal);
            view.Sites.AddRange(_state.Sites.Where(x => gates.Contains(x.Id) && view.Sites.All(y => y.Id != x.Id))
                .Select(x => JsonUtility.FromJson<GoodsSite>(JsonUtility.ToJson(x))));
        }

        private static void ValidateFarms(GoodsSnapshot state)
        {
            if (state.FarmOrders.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.ItemId) || x.Units < 1
                    || x.SpoilAfterSeconds < 1 || x.GoodsCents < 0 || x.FeeCents < 0 || x.UnloadedUnits < 0 || x.UnloadedUnits > x.Units
                    || x.WaitSeconds < 0 || x.ScheduleId is null || string.IsNullOrWhiteSpace(x.FarmLotId)
                    || state.Companies.All(y => y.Id != x.CompanyId) || state.Sites.All(y => y.Id != x.SiteId)
                    || state.Trucks.FirstOrDefault(y => y.Id == x.TruckId) is not { } truck || truck.OrderId != x.Id || truck.Supplier == x.Pickup
                    || truck.CompanyId != x.CompanyId || (!x.Pickup && !x.Loaded) || (x.Returning && x.Pickup))
                || state.FarmOrders.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.FarmSchedules.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || x.Packs < 1 || x.Packs > MaxFarmOrderPacks
                    || x.EveryHours < 1 || x.EveryHours > MaxFarmScheduleHours || x.NextHour < 0 || x.TruckId is null || x.LastProblem is null
                    || string.IsNullOrWhiteSpace(x.DockId) || state.Companies.All(y => y.Id != x.CompanyId))
                || state.FarmSchedules.GroupBy(x => x.Id).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot violates farm order invariants.");
            foreach (var truck in state.Trucks.Where(x => !string.IsNullOrEmpty(x.OrderId)))
            {
                var order = state.FarmOrders.FirstOrDefault(x => x.Id == truck.OrderId);
                var valid = order is not null && string.IsNullOrEmpty(truck.RouteId)
                    && (truck.Supplier
                        ? truck.State is TruckState.ToDropoff or TruckState.Unloading or TruckState.ToPark && order.Returning == (truck.State == TruckState.ToPark)
                        : truck.State is TruckState.ToPickup or TruckState.Loading or TruckState.ToDropoff or TruckState.Unloading
                          && order.Loaded == (truck.State is TruckState.ToDropoff or TruckState.Unloading));
                if (!valid) throw new InvalidOperationException($"Truck {truck.Id} is inconsistent with its farm order.");
            }
            if (state.Trucks.Any(x => x.Supplier && string.IsNullOrEmpty(x.OrderId)))
                throw new InvalidOperationException("Goods snapshot has a supplier truck without an order.");
        }
    }
}
