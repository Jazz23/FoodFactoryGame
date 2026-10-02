// Verifies restaurant furnishing (decision 0034, slices 3-5): buy-and-place orders charge once for all pieces or change nothing;
// selling refunds exactly what was charged and moves a piece's goods to the seller's inventory first; decor layers (floor, wall,
// ceiling, tabletop) overlap only their own kind; a register sells only while staffed and reachable, and only reachable tables
// give seats; ambience is one capped score per restaurant that customers weigh; restaurant docks need a path from the street
// and serve one truck at a time while other sites' docks keep their old rules. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class RestaurantFurnishingTests
    {
        // TEST-ONLY values: a 16x12 site at map (0, 0) with an 8x6 restaurant shell at (2,2) (interior 3..8 x 3..6) and its door at
        // (5,2); company "co" with $10,000; chef with a 10-slot inventory; offers for a 1x1 two-seat table ($40), a 2x1 register
        // ($50), a 2x3 rug (floor, 4 points, $30), wall art (wall, 3 points, $20), a pendant (ceiling, 2 points, $25), a vase
        // (tabletop, 1 point, $5), a 1x1 plant (6 points, $15) and a 2x1 dock ($60); one bread menu item. Not gameplay content.
        private const long StartCash = 1_000_000;
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");

        private static GoodsEquipment Template(string kind, int width, int depth, string layer = "", int ambience = 0, int seats = 0,
            int input = 1, int output = 1) => new()
        {
            Kind = kind, State = EquipmentState.Held, Width = width, Depth = depth, InputCapacity = input, OutputCapacity = output,
            Seats = seats, Layer = layer, Ambience = ambience
        };

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryRestaurantFurnishingTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            _world = new GoodsWorld("test-world");
            _world.Bootstrap(new SiteLayout { SiteId = "resto", Width = 16, Depth = 12 });
            _world.Bootstrap(new GoodsSite { Id = "resto", Name = "Resto" });
            _world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "resto", Kind = "carried", Capacity = 10 });
            _world.Bootstrap(new GoodsBuilding
            {
                Id = "shop", SiteId = "resto", CellX = 2, CellZ = 2, Width = 8, Depth = 6, Doors = new List<GridCell> { new() { X = 5, Z = 2 } }
            });
            _world.Bootstrap(new GoodsCompany { Id = "co", Cash = StartCash, SiteIds = { "resto" } });
            _world.Grant("chef", "resto");
            void Offer(string id, long price, GoodsEquipment template) => _world.RegisterEquipmentOffer(new EquipmentOffer { Id = id, PriceCents = price, Equipment = template });
            Offer("table", 4000, Template(GoodsWorld.TableKind, 1, 1, seats: 2));
            Offer("register", 5000, Template(GoodsWorld.CounterKind, 2, 1, input: 5));
            Offer("rug", 3000, Template("rug", 2, 3, SiteGrid.FloorLayer, 4));
            Offer("art", 2000, Template("wall-art", 1, 1, SiteGrid.WallLayer, 3));
            Offer("pendant", 2500, Template("pendant", 1, 1, SiteGrid.CeilingLayer, 2));
            Offer("vase", 500, Template("vase", 1, 1, SiteGrid.TabletopLayer, 1));
            Offer("plant", 1500, Template("plant", 1, 1, ambience: 6));
            Offer("dock", 6000, Template(GoodsWorld.DockKind, 2, 1, input: 8, output: 8));
            _world.RegisterRecipe(new RecipeDefinition
            {
                Id = "sell-bread", StationKind = GoodsWorld.CounterKind, DurationSeconds = 4, Tier = 1, Cuisine = "bakery",
                Inputs = { new RecipeInput { ItemId = "bread", Quantity = 1 } }, OutputItemId = "", SaleCents = 250
            });
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private long Cash => _world.Snapshot().Companies.Single().Cash;
        private long Ledger
        {
            get
            {
                var state = _world.Snapshot();
                return state.Companies.Single().Cash + state.Equipment.Sum(x => x.ChargedCents)
                    + state.Buildings.SelectMany(x => x.Structures).Sum(x => x.ChargedCents);
            }
        }

        private GoodsOutcome Furnish(string request, string offer, params (int X, int Z, int Rotation)[] cells) => _world.BuyAndPlace("chef", request,
            new FurnishOrder { SiteId = "resto", OfferId = offer, Placements = cells.Select(c => new GridPlacement { X = c.X, Z = c.Z, Rotation = c.Rotation }).ToList() });

        private string Placed(string request, int index = 0) => $"buy:chef:{request}:{index}";

        [Test]
        public void BuyAndPlaceChargesOnceForAllPiecesOrChangesNothing()
        {
            var bought = Furnish("tables", "table", (3, 5, 0), (5, 5, 0), (7, 5, 0));
            Assert.That((bought.Accepted, bought.Cents, bought.EquipmentId), Is.EqualTo((true, 12000L, Placed("tables"))));
            Assert.That(_world.Snapshot().Equipment.Where(x => x.Kind == GoodsWorld.TableKind).Select(x => x.ChargedCents), Is.All.EqualTo(4000));
            Assert.That(Cash, Is.EqualTo(StartCash - 12000));

            var before = JsonUtility.ToJson(_world.Snapshot());
            Assert.That(Furnish("overlap", "table", (4, 4, 0), (4, 4, 0)).Reason, Is.EqualTo("blocked"), "Pieces of one order see each other.");
            Assert.That(Furnish("wall", "table", (4, 4, 0), (2, 4, 0)).Reason, Is.EqualTo("blocked"), "The second piece is on the west wall.");
            Assert.That(Furnish("none", "table").Reason, Is.EqualTo("invalid-placement"));
            Assert.That(Furnish("unknown", "spaceship", (4, 4, 0)).Reason, Is.EqualTo("invalid-offer"));
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before), "Rejections change and record nothing.");
            var replay = Furnish("tables", "table", (9, 9, 0));
            Assert.That((replay.Accepted, replay.Cents, Cash), Is.EqualTo((true, 12000L, StartCash - 12000)), "A retried order replays.");
        }

        [Test]
        public void DecorLayersOverlapOnlyTheirOwnKind()
        {
            Assert.That(Furnish("rug", "rug", (3, 3, 0)).Accepted, Is.True);
            Assert.That(Furnish("table", "table", (4, 4, 0)).Accepted, Is.True, "A table stands on the rug.");
            Assert.That(Furnish("rug-2", "rug", (4, 3, 0)).Reason, Is.EqualTo("blocked"), "Rugs do not overlap.");
            Assert.That(Furnish("rug-wall", "rug", (1, 3, 0)).Reason, Is.EqualTo("blocked"), "A rug may not lie on the wall.");
            Assert.That(Furnish("vase", "vase", (4, 4, 0)).Accepted, Is.True, "A vase stands on the table.");
            Assert.That(Furnish("vase-floor", "vase", (6, 4, 0)).Reason, Is.EqualTo("no-table"));
            Assert.That(Furnish("art", "art", (6, 7, 2)).Accepted, Is.True, "Art hangs on the north wall.");
            Assert.That(Furnish("art-door", "art", (5, 2, 0)).Reason, Is.EqualTo("no-wall"), "Not on a doorway.");
            Assert.That(Furnish("art-floor", "art", (6, 5, 0)).Reason, Is.EqualTo("no-wall"));
            Assert.That(Furnish("pendant", "pendant", (4, 4, 0)).Accepted, Is.True, "A pendant hangs over the table.");
            Assert.That(Furnish("pendant-out", "pendant", (12, 9, 0)).Reason, Is.EqualTo("no-ceiling"));
            Assert.That(_world.PickUp("chef", "lift-table", Placed("table")).Reason, Is.EqualTo("blocked"), "The vase must come off first.");
            Assert.That(_world.Sell("chef", "sell-vase", Placed("vase")).Accepted, Is.True);
            Assert.That(_world.PickUp("chef", "lift-table-2", Placed("table")).Accepted, Is.True);
            Assert.That(GoodsWorld.Restore(_world.Snapshot()), Is.Not.Null, "The result is valid.");
        }

        [Test]
        public void SellingRefundsExactlyAndMovesTheGoodsToTheSellersInventory()
        {
            Assert.That(Furnish("register", "register", (4, 4, 0)).Accepted, Is.True);
            var register = Placed("register");
            _world.Bootstrap(new GoodsLot { Id = "bread", ItemId = "bread", OwnerId = "resto", LocationId = register + ":in", Quantity = 3, SpoilAfterSeconds = 900 });
            var sold = _world.Sell("chef", "sell", register);
            Assert.That((sold.Accepted, sold.Reason, sold.Cents), Is.EqualTo((true, "sold", -5000L)));
            var state = _world.Snapshot();
            Assert.That(state.Equipment.Any(x => x.Id == register), Is.False);
            Assert.That(state.Lots.Single(x => x.Id == "bread").LocationId, Is.EqualTo("carried:chef"), "The goods keep their ID and move to the seller.");
            Assert.That(state.Locations.Any(x => x.Id.StartsWith(register, StringComparison.Ordinal)), Is.False);
            Assert.That(Cash, Is.EqualTo(StartCash), "A full refund.");

            // Goods that do not fit block the sale.
            Assert.That(Furnish("register-2", "register", (4, 4, 0)).Accepted, Is.True);
            _world.Bootstrap(new GoodsLot { Id = "junk", ItemId = "junk", OwnerId = "resto", LocationId = "carried:chef", Quantity = 6, SpoilAfterSeconds = 900 });
            _world.Bootstrap(new GoodsLot { Id = "more", ItemId = "more", OwnerId = "resto", LocationId = Placed("register-2") + ":in", Quantity = 2, SpoilAfterSeconds = 900 });
            var before = JsonUtility.ToJson(_world.Snapshot());
            Assert.That(_world.Sell("chef", "sell-2", Placed("register-2")).Reason, Is.EqualTo("capacity"));
            Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before), "Nothing changed; no goods were deleted.");
            Assert.That(Ledger, Is.EqualTo(StartCash));
        }

        [Test]
        public void ARegisterSellsOnlyWhileStaffedAndReachableAndOnlyReachableTablesGiveSeats()
        {
            Assert.That(Furnish("register", "register", (4, 4, 0)).Accepted, Is.True);
            Assert.That(Furnish("table", "table", (7, 5, 0)).Accepted, Is.True);
            var register = Placed("register");
            _world.Bootstrap(new GoodsLot { Id = "bread", ItemId = "bread", OwnerId = "resto", LocationId = register + ":in", Quantity = 5, SpoilAfterSeconds = 9000 });
            _world.Bootstrap(new GoodsDistrict
            {
                Id = "district", Name = "Test", MapZ = 20, CustomersPerHour = 0, WealthPercent = 50, AppearanceVariants = 1,
                LikedCuisines = { "bakery" }, DineInPercent = 100, RangeMetres = 100
            });
            _world.Bootstrap(new GoodsCustomer
            {
                Id = "c1", DistrictId = "district", DineIn = true, PatienceSeconds = 600, State = CustomerState.Queued, RestaurantId = "resto",
                RecipeId = "sell-bread", Ticket = 1
            });
            _world.Advance(3);
            Assert.That(_world.Snapshot().Customers.Single().State, Is.EqualTo(CustomerState.Queued), "An unstaffed register makes no sales.");

            Assert.That(_world.Staff("chef", "nobody", register, "stranger").Reason, Is.EqualTo("forbidden"));
            Assert.That(_world.Staff("chef", "staff", register, "chef").Reason, Is.EqualTo("staffed"));
            _world.Advance(1);
            var customer = _world.Snapshot().Customers.Single();
            Assert.That((customer.State, customer.CounterId, customer.TableId), Is.EqualTo((CustomerState.Ordering, register, Placed("table"))));

            // Walling the table off from the door: its seats stop counting, the register still sells takeaway.
            var walls = new ShellOrder
            {
                Kind = ShellOrder.Partition, BuildingId = "shop", Style = "plaster",
                Cells = new[] { (6, 3), (6, 4), (6, 5), (6, 6) }.Select(c => new GridCell { X = c.Item1, Z = c.Item2 }).ToList()
            };
            Assert.That(_world.OrderShell("chef", "walls", walls).Accepted, Is.True);
            _world.Advance(400);
            _world.Bootstrap(new GoodsCustomer
            {
                Id = "c2", DistrictId = "district", DineIn = true, PatienceSeconds = 600, State = CustomerState.Queued, RestaurantId = "resto",
                RecipeId = "sell-bread", Ticket = 5
            });
            _world.Advance(2);
            Assert.That(_world.Snapshot().Customers.Single(x => x.Id == "c2").State, Is.EqualTo(CustomerState.Queued),
                "No reachable seat for a dine-in customer.");
            // A door in the wall reopens the path.
            Assert.That(_world.OrderShell("chef", "door", new ShellOrder { Kind = ShellOrder.Door, BuildingId = "shop", X = 6, Z = 5, Style = "panel" }).Accepted, Is.True);
            _world.Advance(1);
            Assert.That(_world.Snapshot().Customers.Single(x => x.Id == "c2").State, Is.EqualTo(CustomerState.Ordering));

            // Leaving clears the register; entering another site would too.
            Assert.That(_world.Staff("chef", "leave", register, "").Reason, Is.EqualTo("unstaffed"));
            Assert.That(_world.Staff("chef", "staff-2", register, "chef").Accepted, Is.True);
            GoodsSnapshotStore.Save(_world, PathForSave);
            Assert.That(_world.ReleaseStaffDurably(null, PathForSave), Is.True, "A server start clears every player's register.");
            Assert.That(_world.Snapshot().Equipment.Single(x => x.Id == register).StaffId, Is.Empty);
        }

        [Test]
        public void AmbienceIsOneCappedScorePerRestaurantThatCustomersWeigh()
        {
            Assert.That(RestaurantRules.Ambience(_world.Snapshot(), "resto"), Is.Zero);
            Assert.That(Furnish("plants", "plant", (3, 3, 0), (3, 4, 0), (3, 5, 0)).Accepted, Is.True);
            var three = RestaurantRules.Ambience(_world.Snapshot(), "resto");
            Assert.That(three, Is.EqualTo((int)Math.Round(100 * (1 - Math.Exp(-18 / 50.0)))));
            Assert.That(Furnish("more", "plant", (4, 3, 0), (4, 5, 0), (5, 3, 0)).Accepted, Is.True);
            var six = RestaurantRules.Ambience(_world.Snapshot(), "resto");
            Assert.That(six - three, Is.LessThan(three), "Diminishing returns.");
            var many = new GoodsSnapshot { Equipment = Enumerable.Range(0, 500).Select(i => new GoodsEquipment
                { SiteId = "x", State = EquipmentState.Placed, Ambience = 10 }).ToList() };
            Assert.That(RestaurantRules.Ambience(many, "x"), Is.EqualTo(RestaurantRules.AmbienceCap), "Capped.");

            // The same restaurant with and without decor: decor wins customers from a competitor at the same distance.
            int Chosen(bool decor)
            {
                var world = new GoodsWorld("choice");
                world.Bootstrap(new SiteLayout { SiteId = "resto", Width = 8, Depth = 8 });
                world.Bootstrap(new GoodsSite { Id = "resto", Name = "Resto" });
                world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "resto", Kind = "carried", Capacity = 1 });
                world.Bootstrap(new GoodsEquipment { Id = "reg", Kind = GoodsWorld.CounterKind, SiteId = "resto", CellX = 3, CellZ = 3, Width = 2, Depth = 1, InputCapacity = 1, OutputCapacity = 1 });
                for (var i = 0; decor && i < 8; i++)
                    world.Bootstrap(new GoodsEquipment { Id = $"plant{i}", Kind = "plant", SiteId = "resto", CellX = i, CellZ = 6, Width = 1, Depth = 1, InputCapacity = 1, OutputCapacity = 1, Ambience = 25 });
                world.Bootstrap(new GoodsCompany { Id = "co", Cash = 0, SiteIds = { "resto" } });
                world.Grant("chef", "resto");
                world.Staff("chef", "s", "reg", "chef");
                world.RegisterRecipe(new RecipeDefinition
                {
                    Id = "sell", StationKind = GoodsWorld.CounterKind, DurationSeconds = 4, Tier = 1, Cuisine = "bakery",
                    Inputs = { new RecipeInput { ItemId = "bread", Quantity = 1 } }, OutputItemId = "", SaleCents = 250
                });
                world.Bootstrap(new GoodsCompetitor { Id = "rival", Name = "Rival", Cuisine = "bakery", Tier = 1, PriceCents = 250, Servers = 1, ServiceSeconds = 4, Seats = 4 });
                world.Bootstrap(new GoodsDistrict
                {
                    Id = "d", Name = "D", MapZ = 10, CustomersPerHour = 3600, WealthPercent = 50, AppearanceVariants = 1, LikedCuisines = { "bakery" },
                    DineInPercent = 0, RangeMetres = 100, SpawnProgress = 0
                });
                world.Advance(400);
                var state = world.Snapshot();
                return state.Customers.Count(x => x.RestaurantId == "resto")
                    + (int)state.Diners.Where(x => x.RestaurantId == "resto").Sum(x => x.Served + x.WalkedOut);
            }
            Assert.That(Chosen(true), Is.GreaterThan(Chosen(false)), "Ambience is one more term in customer choice.");
        }

        [Test]
        public void RestaurantDocksNeedAStreetPathAndServeOneTruckAtATime()
        {
            // The dock inside the shell with the only door bricked up: no path from the street.
            Assert.That(Furnish("dock-in", "dock", (4, 5, 0)).Accepted, Is.True);
            Assert.That(_world.OrderShell("chef", "door-2", new ShellOrder { Kind = ShellOrder.Door, BuildingId = "shop", X = 9, Z = 4, Style = "panel" }).Accepted, Is.True);
            Assert.That(_world.OrderShell("chef", "close", new ShellOrder { Kind = ShellOrder.Remove, BuildingId = "shop", Cells = { new GridCell { X = 9, Z = 4 } } }).Accepted,
                Is.True);
            var walled = new ShellOrder { Kind = ShellOrder.Partition, BuildingId = "shop", Style = "plaster", Cells = { new GridCell { X = 5, Z = 3 } } };
            Assert.That(_world.OrderShell("chef", "wall", walled).Accepted, Is.True, "Seals the doorway's inward cell.");
            Assert.That(Furnish("dock-sealed", "dock", (6, 4, 0)).Reason, Is.EqualTo("no-street-access"));
            Assert.That(Furnish("dock-yard", "dock", (12, 9, 0), (12, 0, 0)).Accepted, Is.True, "Anywhere in the lot outside the shell.");

            // Two trucks on one route to the yard dock: one unloads, the other waits its turn.
            var world = _world;
            world.Bootstrap(new SiteLayout { SiteId = "farm", Width = 6, Depth = 6 });
            world.Bootstrap(new GoodsSite { Id = "farm", Name = "Farm", MapX = 100 });
            world.Bootstrap(new GoodsEquipment { Id = "farm-dock", Kind = GoodsWorld.DockKind, SiteId = "farm", Width = 2, Depth = 1, InputCapacity = 8, OutputCapacity = 8 });
            world.AddCompanySite("co", "farm");
            world.Grant("chef", "farm");
            var yard = Placed("dock-yard");
            world.Bootstrap(new GoodsRoute { Id = "route", CompanyId = "co", PickupDockId = "farm-dock", DropoffDockId = yard });
            foreach (var id in new[] { "truck-a", "truck-b" })
            {
                world.Bootstrap(new GoodsTruck
                {
                    Id = id, CompanyId = "co", Name = id, CargoSlots = 4, SpeedMetresPerSecond = 100, LoadUnitsPerSecond = 1,
                    RouteId = "route", State = TruckState.Unloading, SiteId = "resto"
                });
                world.Bootstrap(new GoodsLot { Id = id + "-crate", ItemId = "crate", OwnerId = "farm", LocationId = id + ":cargo", Quantity = 3, SpoilAfterSeconds = 99999 });
            }
            world.Advance(1);
            var state = world.Snapshot();
            Assert.That(state.Trucks.Where(x => x.Docked).Select(x => x.Id), Is.EqualTo(new[] { "truck-a" }), "The first truck holds the dock.");
            Assert.That(state.Lots.Single(x => x.Id == "truck-b-crate").Quantity, Is.EqualTo(3), "The second waits.");
            world.Advance(2);
            state = world.Snapshot();
            Assert.That(state.Lots.Where(x => x.LocationId == yard + ":out").Sum(x => x.Quantity), Is.EqualTo(4),
                "Truck A unloaded 3 at one unit a second, then truck B took the dock.");
            Assert.That(state.Trucks.Single(x => x.Id == "truck-b").Docked, Is.True);
            Assert.That(GoodsWorld.Restore(state), Is.Not.Null);
            Assert.That(state.Lots.Where(x => x.ItemId == "crate").Sum(x => x.Quantity), Is.EqualTo(6), "No crate lost or duplicated.");
        }
    }
}
