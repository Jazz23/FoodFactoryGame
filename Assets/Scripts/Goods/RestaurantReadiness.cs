// Why a restaurant can or cannot sell right now (decision 0038 readout), read from a snapshot or a client's baseline. Pure and
// read-only: it adds no rule of its own, asking only the questions the customer simulation asks (RestaurantRules.CustomerReach,
// ServesCustomers, IsMenuItem, GoodsWorld.InputPlan), so the client explains exactly what the server does. The first blocker
// that applies is reported, in the simulation's order; warnings and status follow. Presentation decides the wording.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodFactoryGame.Goods
{
    public sealed class RestaurantReadiness
    {
        public const string NoCustomerDoor = "no-customer-door";
        public const string NoReachableRegister = "no-reachable-register";
        public const string NoStaffedRegister = "no-staffed-register";
        public const string NoEdibleMenuItem = "no-edible-menu-item";

        public const string NoReachableSeat = "no-reachable-seat";
        public const string DockNotBesideBackDoor = "dock-not-beside-back-door";
        public const string StockSpoilsSoon = "stock-spoils-soon";

        // PROTOTYPE (decision 0038): register stock that spoils within this many clock seconds is flagged.
        public const long SpoilSoonSeconds = 600;
        // PROTOTYPE (decision 0038): the window, in clock seconds, of "recent" sales and spend read from the ledger.
        public const long RecentSeconds = 600;

        // The first blocker that applies, or null when nothing stops a sale.
        public string Blocker;
        public readonly List<string> Warnings = new();
        // Placed registers on the site, those customers reach, and those of them someone works.
        public int Registers;
        public int ReachableRegisters;
        public int StaffedRegisters;
        public int ReachableSeats;
        // Docks placed before the back-door rule that still stand away from a back door (0037 grandfathered docks).
        public readonly List<string> DocksNotBesideBackDoor = new();
        // The register stock that spoils first, when it spoils within SpoilSoonSeconds.
        public string SpoilingItemId;
        public long SpoilingSeconds;
        // Status: this restaurant's customers, and the company's recent ledger (all its sites).
        public int Queued;
        public int Eating;
        public int RecentSales;
        public long RecentSalesCents;
        public long RecentSpendCents;
        public bool Ready => Blocker is null;

        // Null when the site holds no restaurant or no company owns it. offer: the lot's listing (null for a dev site, whose every
        // edge is street, as on the server). menu: the registered recipes; only menu items count.
        public static RestaurantReadiness Evaluate(GoodsSnapshot state, string siteId, PropertyOffer offer, IEnumerable<RecipeDefinition> menu)
        {
            if (state is null || string.IsNullOrEmpty(siteId) || !RestaurantRules.IsRestaurantSite(state, siteId)) return null;
            var company = state.Companies?.FirstOrDefault(x => x.SiteIds.Contains(siteId));
            if (company is null) return null;
            var items = (menu ?? Enumerable.Empty<RecipeDefinition>()).Where(RestaurantRules.IsMenuItem).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var result = new RestaurantReadiness();
            var placed = state.Equipment.Where(x => x.SiteId == siteId && x.State == EquipmentState.Placed).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var registers = placed.Where(x => x.Kind == GoodsWorld.CounterKind).ToList();
            var reach = RestaurantRules.CustomerReach(state, siteId, offer);
            var reachable = registers.Where(x => RestaurantRules.ServesCustomers(reach, x)).ToList();
            var staffed = reachable.Where(x => !string.IsNullOrEmpty(x.StaffId)).ToList();
            result.Registers = registers.Count;
            result.ReachableRegisters = reachable.Count;
            result.StaffedRegisters = staffed.Count;
            result.ReachableSeats = placed.Where(x => GoodsWorld.IsTable(x) && RestaurantRules.ServesCustomers(reach, x)).Sum(x => x.Seats);

            var restaurants = state.Buildings.Where(x => x.SiteId == siteId && x.Kind == GoodsWorld.RestaurantKind).ToList();
            if (restaurants.All(x => !SiteGrid.CustomerDoors(x).Any())) result.Blocker = NoCustomerDoor;
            else if (reachable.Count == 0) result.Blocker = NoReachableRegister;
            else if (staffed.Count == 0) result.Blocker = NoStaffedRegister;
            else if (!staffed.Any(x => state.Stations.FirstOrDefault(y => y.Id == x.Id) is { } station
                         && items.Any(y => GoodsWorld.InputPlan(state, station, y) != null)))
                result.Blocker = NoEdibleMenuItem;

            if (result.ReachableSeats == 0) result.Warnings.Add(NoReachableSeat);
            result.DocksNotBesideBackDoor.AddRange(placed.Where(x => x.Kind == GoodsWorld.DockKind && !RestaurantRules.BesideBackDoor(state, x)).Select(x => x.Id));
            if (result.DocksNotBesideBackDoor.Count > 0) result.Warnings.Add(DockNotBesideBackDoor);
            var inputs = new HashSet<string>(registers.Select(x => x.InputLocationId));
            var cold = new HashSet<string>(state.Locations.Where(x => x.Refrigerated).Select(x => x.Id));
            var soonest = state.Lots.Where(x => inputs.Contains(x.LocationId) && !cold.Contains(x.LocationId) && !x.Spoiled
                    && x.SpoilAfterSeconds < GoodsWorld.NonPerishableSeconds)
                .OrderBy(x => x.SpoilAfterSeconds - x.ExposureSeconds).ThenBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (soonest is not null && soonest.SpoilAfterSeconds - soonest.ExposureSeconds <= SpoilSoonSeconds)
            {
                result.SpoilingItemId = soonest.ItemId;
                result.SpoilingSeconds = Math.Max(0, soonest.SpoilAfterSeconds - soonest.ExposureSeconds);
                result.Warnings.Add(StockSpoilsSoon);
            }

            result.Queued = state.Customers.Count(x => x.RestaurantId == siteId && x.State == CustomerState.Queued);
            result.Eating = state.Customers.Count(x => x.RestaurantId == siteId && x.State == CustomerState.Eating);
            foreach (var entry in state.Ledger.Where(x => x.CompanyId == company.Id && x.ClockSeconds > state.ClockSeconds - RecentSeconds))
            {
                if (entry.Kind == GoodsWorld.LedgerSale)
                {
                    result.RecentSales++;
                    result.RecentSalesCents += entry.Cents;
                }
                else if (entry.Cents < 0) result.RecentSpendCents -= entry.Cents;
            }
            return result;
        }
    }
}
