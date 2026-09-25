// Customers (decision 0024): districts create customers at their own rate; each customer scores the restaurants in range once,
// when it appears or after walking out of a queue, travels there, queues at the counter, buys a menu item (a sale recipe)
// once it can be served (and, to dine in, once a seat is free), then eats at a table or leaves with takeaway. A purchase
// takes the goods from a counter's input and pays the site's company inside the clock tick, so goods, cash, the seat and the
// customer's state commit at one revision and a restart never repeats a sale. Competitors are fixed AI restaurant records
// with infinite stock and no cash. Customers are server records only; nothing here depends on a client, camera or scene.
// Customers advance in one-second sub-steps after the rest of a clock step, so a long step matches many one-second steps.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    // An area of the region map that creates customers. Map data like GoodsSite; SpawnProgress is simulation state.
    [Serializable] public sealed class GoodsDistrict
    {
        public string Id;
        public string Name;
        public int MapX;
        public int MapZ;
        public int CustomersPerHour;
        // 0 poor to 100 wealthy: wealthier customers value higher-tier food and mind prices less.
        public int WealthPercent;
        // Presentation look set of this district's customers, and how many variants it has.
        public string Appearance = "";
        public int AppearanceVariants = 1;
        public List<string> LikedCuisines = new();
        // Share of customers who prefer to dine in; the rest take away.
        public int DineInPercent;
        // Restaurants farther than this (Manhattan metres, like roads) are not considered.
        public int RangeMetres;
        // Customer-seconds accumulated toward the next customer; below 3600.
        public long SpawnProgress;
    }

    // A fixed AI restaurant (PROTOTYPE of GDD section 11): one menu item, servers and seats, unlimited stock, no cash.
    [Serializable] public sealed class GoodsCompetitor
    {
        public string Id;
        public string Name;
        public int MapX;
        public int MapZ;
        public string Cuisine = "";
        public int Tier = 1;
        public long PriceCents;
        public int Servers;
        public long ServiceSeconds;
        public int Seats;
    }

    public enum CustomerState
    {
        Travelling,
        Queued,
        // Paid; being served at a counter (or by a competitor's server).
        Ordering,
        // Seated at a table until done.
        Eating
    }

    [Serializable] public sealed class GoodsCustomer
    {
        public string Id;
        public string DistrictId;
        public int Appearance;
        public bool DineIn;
        public long PatienceSeconds;
        public CustomerState State;
        // A site ID (a player restaurant) or a competitor ID.
        public string RestaurantId;
        // The chosen menu item at a player restaurant; empty at a competitor.
        public string RecipeId = "";
        // The restaurant this customer walked out of; never chosen again. Empty until then.
        public string LeftRestaurantId = "";
        // Seconds of travel, service or eating left; 0 while queued.
        public long RemainingSeconds;
        public long QueuedAtSeconds;
        // Queue order, from GoodsSnapshot.NextCustomerNumber.
        public long Ticket;
        // Counter serving this customer (player restaurant, Ordering only).
        public string CounterId = "";
        // Table (player restaurant) or competitor ID the dine-in customer's seat belongs to, from purchase until they leave.
        public string TableId = "";
        // Price paid at purchase; 0 before.
        public long PaidCents;
    }

    // A restaurant's standing with customers: reputation, and what it last published (decision 0024: customers see a periodic
    // estimate, not the live queue).
    [Serializable] public sealed class GoodsDiner
    {
        public string RestaurantId;
        // -1000 to 1000; 0 is neutral.
        public int Reputation;
        public long PublishedWaitSeconds;
        public int PublishedFreeSeats;
        public long Served;
        public long WalkedOut;
    }

    public sealed partial class GoodsWorld
    {
        public const string CounterKind = "counter";
        public const string TableKind = "table";
        // PROTOTYPE tuning (decision 0024 leaves these open).
        public const int WalkMetresPerSecond = 2;
        public const int PublishIntervalSeconds = 5;
        public const int ReputationLimit = 1000;
        private const int ReputationDriftSeconds = 60;
        private const int WalkOutReputation = -40;
        private const long MinPatienceSeconds = 90, MaxPatienceSeconds = 240;
        private const long MinEatSeconds = 120, MaxEatSeconds = 300;

        // A restaurant as customers see it during one sub-step.
        private sealed class Diner
        {
            public string Id;
            public bool Player;
            public int MapX, MapZ;
            public GoodsCompetitor Competitor;
            public List<RecipeDefinition> Menu = new();
            public List<GoodsEquipment> Counters = new();
            public List<GoodsEquipment> Tables = new();
            public int Servers;
            public long ServiceSeconds;
            public int Seats;
        }

        // Server-only map data, like a site's map record.
        public void Bootstrap(GoodsDistrict district)
        {
            lock (_gate)
            {
                var copy = district is null ? null : JsonUtility.FromJson<GoodsDistrict>(JsonUtility.ToJson(district));
                if (copy is null || _state.Districts.Any(x => x.Id == copy.Id)) throw new ArgumentException("Invalid or duplicate district.");
                var before = Snapshot();
                _state.Districts.Add(copy);
                _state.Revision++;
                ValidateOrRestore(before, "district");
            }
        }

        // Server-only: a fixed AI competitor.
        public void Bootstrap(GoodsCompetitor competitor)
        {
            lock (_gate)
            {
                var copy = competitor is null ? null : JsonUtility.FromJson<GoodsCompetitor>(JsonUtility.ToJson(competitor));
                if (copy is null || _state.Competitors.Any(x => x.Id == copy.Id)) throw new ArgumentException("Invalid or duplicate competitor.");
                var before = Snapshot();
                _state.Competitors.Add(copy);
                _state.Revision++;
                ValidateOrRestore(before, "competitor");
            }
        }

        // Server-only, for tests and tools: puts a customer in a given state. The result must satisfy Validate or nothing changes;
        // a paying customer's goods and cash are not touched (the caller sets up whatever it already bought).
        public void Bootstrap(GoodsCustomer customer)
        {
            lock (_gate)
            {
                var copy = customer is null ? null : JsonUtility.FromJson<GoodsCustomer>(JsonUtility.ToJson(customer));
                if (copy is null || _state.Customers.Any(x => x.Id == copy.Id)) throw new ArgumentException("Invalid or duplicate customer.");
                var before = Snapshot();
                _state.Customers.Add(copy);
                _state.Revision++;
                ValidateOrRestore(before, "customer");
            }
        }

        private void AdvanceCustomers(long seconds)
        {
            if (_state.Districts.Count == 0 && _state.Customers.Count == 0) return;
            var start = _state.ClockSeconds - seconds;
            for (var second = 1; second <= seconds; second++) CustomerSecond(start + second);
        }

        private void CustomerSecond(long now)
        {
            var diners = Diners();
            foreach (var customer in _state.Customers.ToList())
            {
                switch (customer.State)
                {
                    case CustomerState.Travelling:
                        if (--customer.RemainingSeconds > 0) break;
                        customer.State = CustomerState.Queued;
                        customer.QueuedAtSeconds = now;
                        customer.Ticket = _state.NextCustomerNumber++;
                        break;
                    case CustomerState.Ordering:
                        if (--customer.RemainingSeconds > 0) break;
                        customer.CounterId = "";
                        if (!customer.DineIn)
                        {
                            _state.Customers.Remove(customer);
                            break;
                        }
                        customer.State = CustomerState.Eating;
                        customer.RemainingSeconds = Between(MinEatSeconds, MaxEatSeconds);
                        break;
                    case CustomerState.Eating:
                        if (--customer.RemainingSeconds <= 0) _state.Customers.Remove(customer);
                        break;
                    case CustomerState.Queued:
                        if (now - customer.QueuedAtSeconds < customer.PatienceSeconds) break;
                        var left = DinerRecord(customer.RestaurantId);
                        left.WalkedOut++;
                        AdjustReputation(left, WalkOutReputation);
                        var again = string.IsNullOrEmpty(customer.LeftRestaurantId);
                        customer.LeftRestaurantId = customer.RestaurantId;
                        // Walking out once means choosing again, without this restaurant; a second walk-out goes home.
                        if (!again || !Decide(customer, diners)) _state.Customers.Remove(customer);
                        break;
                }
            }

            foreach (var district in _state.Districts)
            {
                district.SpawnProgress += district.CustomersPerHour;
                while (district.SpawnProgress >= 3600)
                {
                    district.SpawnProgress -= 3600;
                    Spawn(district, diners);
                }
            }

            foreach (var diner in diners) Serve(diner, now);

            if (now % PublishIntervalSeconds == 0)
                foreach (var diner in diners) Publish(diner);
            if (now % ReputationDriftSeconds == 0)
                foreach (var record in _state.Diners)
                    record.Reputation -= Math.Sign(record.Reputation);
        }

        // Every restaurant customers may choose, by ID. A player restaurant is a mapped site with a company, a placed counter and
        // a menu; its servers are its counters and its seats are those of its placed tables.
        private List<Diner> Diners()
        {
            var menu = _recipes.Values.Where(x => x.IsSale && x.StationKind == CounterKind).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var diners = new List<Diner>();
            foreach (var site in _state.Sites)
            {
                var placed = _state.Equipment.Where(x => x.SiteId == site.Id && x.State == EquipmentState.Placed).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
                var counters = placed.Where(x => x.Kind == CounterKind).ToList();
                if (menu.Count == 0 || counters.Count == 0 || CompanyOfSiteLocked(site.Id) is null) continue;
                var tables = placed.Where(x => x.Kind == TableKind).ToList();
                diners.Add(new Diner
                {
                    Id = site.Id, Player = true, MapX = site.MapX, MapZ = site.MapZ, Menu = menu, Counters = counters, Tables = tables,
                    Servers = counters.Count, ServiceSeconds = menu[0].DurationSeconds, Seats = tables.Sum(x => x.Seats)
                });
            }
            foreach (var competitor in _state.Competitors)
                diners.Add(new Diner
                {
                    Id = competitor.Id, MapX = competitor.MapX, MapZ = competitor.MapZ, Competitor = competitor,
                    Servers = competitor.Servers, ServiceSeconds = competitor.ServiceSeconds, Seats = competitor.Seats
                });
            diners.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return diners;
        }

        private void Spawn(GoodsDistrict district, List<Diner> diners)
        {
            var customer = new GoodsCustomer
            {
                Id = $"customer:{district.Id}:{_state.NextCustomerNumber++}", DistrictId = district.Id,
                Appearance = (int)(NextCustomerRandom() * district.AppearanceVariants),
                DineIn = NextCustomerRandom() * 100 < district.DineInPercent,
                PatienceSeconds = Between(MinPatienceSeconds, MaxPatienceSeconds)
            };
            // A customer who prefers staying home to every restaurant in range never enters the world.
            if (Decide(customer, diners)) _state.Customers.Add(customer);
        }

        // Scores the restaurants in range and picks one at random weighted by exp(score), against staying home (score 0).
        // Returns false for staying home; otherwise the customer sets off for the chosen restaurant.
        private bool Decide(GoodsCustomer customer, List<Diner> diners)
        {
            var district = _state.Districts.First(x => x.Id == customer.DistrictId);
            var options = new List<(Diner Diner, string RecipeId, double Weight)>();
            var total = 1.0;
            foreach (var diner in diners)
            {
                var distance = Math.Abs(diner.MapX - district.MapX) + Math.Abs(diner.MapZ - district.MapZ);
                if (diner.Id == customer.LeftRestaurantId || distance > district.RangeMetres) continue;
                var record = _state.Diners.FirstOrDefault(x => x.RestaurantId == diner.Id);
                var recipeId = "";
                double food;
                if (diner.Player)
                {
                    var best = diner.Menu.Select(x => (x.Id, Score: FoodScore(district, x.Tier, x.Cuisine, x.SaleCents)))
                        .OrderByDescending(x => x.Score).ThenBy(x => x.Id, StringComparer.Ordinal).First();
                    recipeId = best.Id;
                    food = best.Score;
                }
                else food = FoodScore(district, diner.Competitor.Tier, diner.Competitor.Cuisine, diner.Competitor.PriceCents);
                var freeSeats = record?.PublishedFreeSeats ?? diner.Seats;
                var score = 1.0 + food
                    - 0.5 * distance / 100.0
                    - 0.5 * (record?.PublishedWaitSeconds ?? 0) / 60.0
                    + (record?.Reputation ?? 0) / (double)ReputationLimit
                    + (customer.DineIn ? (freeSeats > 0 ? 0.5 : -1.0) : 0.0);
                var weight = Math.Exp(score);
                options.Add((diner, recipeId, weight));
                total += weight;
            }

            // The first 1.0 of the draw is staying home; a remainder lost to rounding goes to the last option.
            var pick = NextCustomerRandom() * total - 1.0;
            if (pick < 0) return false;
            for (var index = 0; index < options.Count; index++)
            {
                var option = options[index];
                pick -= option.Weight;
                if (pick >= 0 && index < options.Count - 1) continue;
                var distance = Math.Abs(option.Diner.MapX - district.MapX) + Math.Abs(option.Diner.MapZ - district.MapZ);
                customer.State = CustomerState.Travelling;
                customer.RestaurantId = option.Diner.Id;
                customer.RecipeId = option.RecipeId;
                customer.RemainingSeconds = Math.Max(1, distance / WalkMetresPerSecond);
                customer.QueuedAtSeconds = 0;
                return true;
            }
            return false;
        }

        // PROTOTYPE weights: cuisine fit, tier valued by wealth, price minded less by wealth.
        private static double FoodScore(GoodsDistrict district, int tier, string cuisine, long priceCents)
        {
            var wealth = district.WealthPercent / 100.0;
            return (district.LikedCuisines.Contains(cuisine) ? 1.0 : 0.0)
                + 0.8 * tier * wealth
                - 0.15 * priceCents / 100.0 * (1.0 - 0.7 * wealth);
        }

        // Serves the queue in ticket order. A customer who cannot be served yet (no free counter with their item, or no free
        // seat to dine in) does not hold up those behind who can.
        private void Serve(Diner diner, long now)
        {
            var queue = _state.Customers.Where(x => x.State == CustomerState.Queued && x.RestaurantId == diner.Id).OrderBy(x => x.Ticket).ToList();
            foreach (var customer in queue)
            {
                if (_state.Customers.Count(x => x.State == CustomerState.Ordering && x.RestaurantId == diner.Id) >= diner.Servers) return;
                var table = "";
                if (customer.DineIn)
                {
                    table = diner.Player
                        ? diner.Tables.FirstOrDefault(x => _state.Customers.Count(y => y.TableId == x.Id) < x.Seats)?.Id
                        : _state.Customers.Count(y => y.TableId == diner.Id) < diner.Seats ? diner.Id : null;
                    if (table is null) continue;
                }
                long price, service;
                var tier = 1;
                var counterId = "";
                if (diner.Player)
                {
                    if (!_recipes.TryGetValue(customer.RecipeId, out var recipe)) continue;
                    var company = _state.Companies.First(x => x.SiteIds.Contains(diner.Id));
                    if (company.Cash > long.MaxValue - recipe.SaleCents) continue;
                    var counter = diner.Counters.FirstOrDefault(x => _state.Customers.All(y => y.CounterId != x.Id)
                        && InputPlan(_state.Stations.First(y => y.Id == x.Id), recipe) != null);
                    if (counter is null) continue;
                    // The purchase: the item leaves the world and the company is paid, in this same commit.
                    foreach (var (lot, take) in InputPlan(_state.Stations.First(x => x.Id == counter.Id), recipe))
                    {
                        lot.Quantity -= take;
                        if (lot.Quantity == 0) _state.Lots.Remove(lot);
                    }
                    TryCredit(company.Id, recipe.SaleCents);
                    price = recipe.SaleCents;
                    service = recipe.DurationSeconds;
                    tier = recipe.Tier;
                    counterId = counter.Id;
                }
                else
                {
                    price = diner.Competitor.PriceCents;
                    service = diner.Competitor.ServiceSeconds;
                    tier = diner.Competitor.Tier;
                }
                customer.State = CustomerState.Ordering;
                customer.PaidCents = price;
                customer.RemainingSeconds = service;
                customer.CounterId = counterId;
                customer.TableId = table;
                var record = DinerRecord(diner.Id);
                record.Served++;
                AdjustReputation(record, (int)(5 * tier - Math.Min(now - customer.QueuedAtSeconds, 600) / 30));
            }
        }

        private void Publish(Diner diner)
        {
            var record = DinerRecord(diner.Id);
            var queued = _state.Customers.Count(x => x.State == CustomerState.Queued && x.RestaurantId == diner.Id);
            record.PublishedWaitSeconds = queued * diner.ServiceSeconds / Math.Max(1, diner.Servers);
            var seated = diner.Player
                ? _state.Customers.Count(x => diner.Tables.Any(y => y.Id == x.TableId))
                : _state.Customers.Count(x => x.TableId == diner.Id);
            record.PublishedFreeSeats = Math.Max(0, diner.Seats - seated);
        }

        private GoodsDiner DinerRecord(string restaurantId)
        {
            var record = _state.Diners.FirstOrDefault(x => x.RestaurantId == restaurantId);
            if (record is not null) return record;
            record = new GoodsDiner { RestaurantId = restaurantId };
            _state.Diners.Add(record);
            return record;
        }

        private static void AdjustReputation(GoodsDiner record, int delta) =>
            record.Reputation = Math.Clamp(record.Reputation + delta, -ReputationLimit, ReputationLimit);

        private long Between(long min, long max) => min + (long)(NextCustomerRandom() * (max - min + 1));

        // xorshift64* in the snapshot, so a restored world makes the same choices as one that never stopped.
        private double NextCustomerRandom()
        {
            var x = (ulong)_state.CustomerRandom;
            if (x == 0) x = 0x9E3779B97F4A7C15UL;
            x ^= x >> 12;
            x ^= x << 25;
            x ^= x >> 27;
            _state.CustomerRandom = (long)x;
            return ((x * 2685821657736338717UL) >> 11) * (1.0 / (1UL << 53));
        }

        // A site baseline carries the customers at that site and its diner record; district, competitor and random state stay
        // on the server.
        private static void ViewCustomers(GoodsSnapshot view, string siteId)
        {
            view.Customers = view.Customers.Where(x => x.RestaurantId == siteId).ToList();
            view.Diners = view.Diners.Where(x => x.RestaurantId == siteId).ToList();
            view.Districts.Clear();
            view.Competitors.Clear();
            view.CustomerRandom = 0;
        }

        private static void ValidateCustomers(GoodsSnapshot state)
        {
            var siteIds = new HashSet<string>(state.Locations.Select(x => x.SiteId));
            var equipmentIds = new HashSet<string>(state.Equipment.Where(x => x is not null).Select(x => x.Id));
            if (state.NextCustomerNumber < 0
                || state.Districts.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || x.Name is null || x.CustomersPerHour < 0
                    || x.WealthPercent is < 0 or > 100 || x.DineInPercent is < 0 or > 100 || x.AppearanceVariants < 1
                    || x.Appearance is null || x.LikedCuisines is null || x.RangeMetres < 1 || x.SpawnProgress is < 0 or >= 3600)
                || state.Districts.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Competitors.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || x.Name is null || x.Cuisine is null
                    || x.Tier < 1 || x.PriceCents < 1 || x.Servers < 1 || x.ServiceSeconds < 1 || x.Seats < 0
                    || siteIds.Contains(x.Id) || equipmentIds.Contains(x.Id))
                || state.Competitors.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Diners.Any(x => x is null || string.IsNullOrWhiteSpace(x.RestaurantId)
                    || Math.Abs(x.Reputation) > ReputationLimit || x.PublishedWaitSeconds < 0 || x.PublishedFreeSeats < 0
                    || x.Served < 0 || x.WalkedOut < 0)
                || state.Diners.GroupBy(x => x.RestaurantId).Any(x => x.Count() != 1)
                || state.Customers.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id) || state.Districts.All(y => y.Id != x.DistrictId)
                    || x.PatienceSeconds < 1 || x.Appearance < 0 || x.RecipeId is null || x.LeftRestaurantId is null
                    || x.CounterId is null || x.TableId is null
                    || !(siteIds.Contains(x.RestaurantId) || state.Competitors.Any(y => y.Id == x.RestaurantId)) || !ValidCustomer(state, x))
                || state.Customers.GroupBy(x => x.Id).Any(x => x.Count() != 1)
                || state.Customers.Where(x => x.CounterId != "").GroupBy(x => x.CounterId).Any(x => x.Count() != 1)
                || state.Customers.Where(x => x.TableId != "").GroupBy(x => x.TableId)
                    .Any(x => x.Count() > (state.Equipment.FirstOrDefault(y => y.Id == x.Key)?.Seats
                        ?? state.Competitors.FirstOrDefault(y => y.Id == x.Key)?.Seats ?? 0))
                || state.Competitors.Any(x => state.Customers.Count(y => y.State == CustomerState.Ordering && y.RestaurantId == x.Id) > x.Servers))
                throw new InvalidOperationException("Goods snapshot violates customer invariants.");
        }

        // Per-state shape: only a paying customer (Ordering or Eating) holds a price, a counter (Ordering at a player counter)
        // or a seat (dine-in, at a placed table of the restaurant's site or at the competitor).
        private static bool ValidCustomer(GoodsSnapshot state, GoodsCustomer customer)
        {
            var competitor = state.Competitors.Any(x => x.Id == customer.RestaurantId);
            bool Placed(string id, string kind) => state.Equipment.Any(x => x.Id == id && x.Kind == kind && x.SiteId == customer.RestaurantId
                && x.State == EquipmentState.Placed);
            var seat = customer.DineIn && (competitor ? customer.TableId == customer.RestaurantId : Placed(customer.TableId, TableKind));
            return customer.State switch
            {
                CustomerState.Travelling or CustomerState.Queued => customer.PaidCents == 0 && customer.CounterId == "" && customer.TableId == ""
                    && (customer.State == CustomerState.Queued ? customer.RemainingSeconds == 0 : customer.RemainingSeconds >= 1),
                CustomerState.Ordering => customer.PaidCents >= 1 && customer.RemainingSeconds >= 1
                    && (competitor ? customer.CounterId == "" : Placed(customer.CounterId, CounterKind))
                    && (customer.DineIn ? seat : customer.TableId == ""),
                CustomerState.Eating => customer.PaidCents >= 1 && customer.RemainingSeconds >= 1 && customer.CounterId == "" && seat,
                _ => false
            };
        }
    }
}
