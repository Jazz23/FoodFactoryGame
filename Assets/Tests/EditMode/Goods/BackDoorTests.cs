// Verifies back doors and the restaurant dock rule (decision 0037): a back door is a door on an outer wall that customers treat as
// a wall; a restaurant dock must stand outside the interior touching an open back-door doorstep and stay reachable from the
// street, and no other object-layer piece may cover a doorstep; a restaurant keeps at least one back door once it has one, and no
// order takes away a back door or doorstep a dock stands beside; moving the last back door works as "place new, then remove old"
// with exact refunds; a bought generated restaurant comes with its back door and a free dock beside it; v17 saves load with
// customer doors, keep their docks where they stand and get a back door. Owner answers of 2026-10-06: the last customer door
// stays, belts stay off doorsteps, and a restaurant listed or saved without a back door gets one. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class BackDoorTests
    {
        // TEST-ONLY values: a 16x12 site at map (0, 0) with an 8x6 restaurant shell at (2,2) (interior 3..8 x 3..6) and its front door
        // at (5,2); company "co" with $10,000; chef with a 10-slot inventory; offers for a 2x1 dock ($60), a 1x1 plant ($15) and a
        // 2x1 register ($50). Not gameplay content.
        private const long StartCash = 1_000_000;
        // One door order: the door plus the order fee (GoodsWorld.ShellDoorCents + ShellOrderFeeCents at list price).
        private const long DoorOrderCents = 25_000;
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");

        private static GoodsEquipment Template(string kind, int width, int depth, int input = 1, int output = 1) => new()
        {
            Kind = kind, State = EquipmentState.Held, Width = width, Depth = depth, InputCapacity = input, OutputCapacity = output, Layer = ""
        };

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryBackDoorTests", Guid.NewGuid().ToString("N"));
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
            _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "dock", PriceCents = 6000, Equipment = Template(GoodsWorld.DockKind, 2, 1, 8, 8) });
            _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "plant", PriceCents = 1500, Equipment = Template("plant", 1, 1) });
            _world.RegisterEquipmentOffer(new EquipmentOffer { Id = "register", PriceCents = 5000, Equipment = Template(GoodsWorld.CounterKind, 2, 1, 5) });
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private long Cash => _world.Snapshot().Companies.Single().Cash;

        // Cash plus everything recorded as charged: constant across exact charges and refunds.
        private long Ledger
        {
            get
            {
                var state = _world.Snapshot();
                return state.Companies.Single().Cash + state.Equipment.Sum(x => x.ChargedCents) + state.Buildings.SelectMany(x => x.Structures).Sum(x => x.ChargedCents);
            }
        }

        private GoodsBuilding Shop => _world.Snapshot().Buildings.Single(x => x.Id == "shop");

        private GoodsOutcome Shell(string request, string kind, int x = 0, int z = 0, params (int X, int Z)[] cells) => _world.OrderShell("chef", request, new ShellOrder
        {
            Kind = kind, BuildingId = "shop", X = x, Z = z, Style = kind == ShellOrder.Partition ? "plaster" : "kitchen",
            Cells = cells.Select(c => new GridCell { X = c.X, Z = c.Z }).ToList()
        });

        private GoodsOutcome Furnish(string request, string offer, int x, int z, int rotation = 0) => _world.BuyAndPlace("chef", request,
            new FurnishOrder { SiteId = "resto", OfferId = offer, Placements = { new GridPlacement { X = x, Z = z, Rotation = rotation } } });

        private string Placed(string request) => $"buy:chef:{request}:0";

        [Test]
        public void ABackDoorGoesOnAnOuterWallAndCustomersNeverUseIt()
        {
            var ledger = Ledger;
            Assert.That(Shell("corner", ShellOrder.BackDoor, 9, 2).Reason, Is.EqualTo("invalid-cell"), "Never on a corner.");
            Assert.That(Shell("floor", ShellOrder.BackDoor, 5, 4).Reason, Is.EqualTo("invalid-cell"), "Only on a wall.");
            Assert.That(Shell("front", ShellOrder.BackDoor, 5, 2).Reason, Is.EqualTo("invalid-cell"), "Not over another door.");
            var cash = Cash;
            Assert.That(Shell("back", ShellOrder.BackDoor, 9, 4).Accepted, Is.True);
            Assert.That(Cash, Is.EqualTo(cash - DoorOrderCents), "A back door costs what a door costs.");
            var door = Shop.Structures.Single(x => x.Kind == GoodsWorld.DoorStructure && x.X == 9 && x.Z == 4);
            Assert.That((door.Role, SiteGrid.IsServiceDoor(Shop, 9, 4), SiteGrid.Doorstep(Shop, 9, 4)), Is.EqualTo((GoodsWorld.ServiceDoorRole, true, ((int, int)?)(10, 4))));
            Assert.That(SiteGrid.IsServiceDoor(Shop, 5, 2), Is.False, "The front door stays a customer door.");
            // An interior wall with a room on both sides: a back door there would open onto no outside cell.
            Assert.That(Shell("room", ShellOrder.Partition, 0, 0, (3, 5), (4, 5), (5, 5), (6, 5), (7, 5), (8, 5)).Accepted, Is.True);
            Assert.That(Shell("inner", ShellOrder.BackDoor, 5, 5).Reason, Is.EqualTo("invalid-cell"));
            Assert.That(Shell("inner-door", ShellOrder.Door, 5, 5).Accepted, Is.True, "An ordinary door goes there.");

            // A register by the north wall; with the front door's inward cell walled off, a customer cannot reach it, though a
            // player can walk in through the back door.
            Assert.That(Furnish("register", "register", 4, 6).Accepted, Is.True);
            Assert.That(Shell("seal", ShellOrder.Partition, 0, 0, (5, 3)).Accepted, Is.True);
            var state = _world.Snapshot();
            var register = state.Equipment.Single(x => x.Kind == GoodsWorld.CounterKind);
            var edges = RestaurantRules.EdgeCells(state.SiteLayouts.Single());
            Assert.That(RestaurantRules.Touches(RestaurantRules.Reached(RestaurantRules.Walkable(state, "resto"), edges), register), Is.True, "Players reach it.");
            Assert.That(RestaurantRules.Touches(RestaurantRules.Reached(RestaurantRules.Walkable(state, "resto", customers: true), edges), register), Is.False,
                "Customers never pass a back door.");
            Assert.That(Ledger, Is.EqualTo(ledger), "Every order charged exactly once.");
        }

        [Test]
        public void ADockStandsBesideABackDoorStepReachableFromTheStreet()
        {
            Assert.That(Furnish("before", "dock", 12, 9).Reason, Is.EqualTo("not-beside-back-door"), "No back door, no new dock.");
            Assert.That(Shell("back", ShellOrder.BackDoor, 9, 4).Accepted, Is.True);
            Assert.That(Furnish("far", "dock", 12, 9).Reason, Is.EqualTo("not-beside-back-door"));
            Assert.That(Furnish("on-step", "dock", 10, 4).Reason, Is.EqualTo("not-beside-back-door"), "The doorstep stays clear.");
            Assert.That(Furnish("diagonal", "dock", 11, 5).Reason, Is.EqualTo("not-beside-back-door"), "Corner to corner is not beside.");
            Assert.That(Furnish("interior", "dock", 6, 4).Reason, Is.EqualTo("not-beside-back-door"), "Not inside the restaurant.");
            Assert.That(Furnish("plant-on-step", "plant", 10, 4).Reason, Is.EqualTo("doorstep"), "Nothing else covers the doorstep either.");

            // The doorstep and the dock's spot boxed in by plants, and the front door walled off inside: no way from the street.
            foreach (var (x, z) in new[] { (10, 3), (11, 4), (12, 5), (10, 6), (11, 6) })
                Assert.That(Furnish($"plant-{x}-{z}", "plant", x, z).Accepted, Is.True);
            Assert.That(Shell("seal", ShellOrder.Partition, 0, 0, (5, 3)).Accepted, Is.True);
            Assert.That(Furnish("boxed", "dock", 10, 5).Reason, Is.EqualTo("no-street-access"));
            Assert.That(_world.Sell("chef", "clear", Placed("plant-11-4")).Accepted, Is.True);
            Assert.That(Furnish("beside", "dock", 10, 5).Accepted, Is.True, "Beside the doorstep with a way in from the street.");
            Assert.That(RestaurantRules.BesideBackDoor(_world.Snapshot(), _world.Snapshot().Equipment.Single(x => x.Id == Placed("beside"))), Is.True);

            // The same rule for a dock bought from the supplier and placed by hand.
            Assert.That(_world.Buy("chef", "held-dock", "resto", "dock").Accepted, Is.True);
            var held = _world.Snapshot().Equipment.Single(x => x.Kind == GoodsWorld.DockKind && x.State == EquipmentState.Held).Id;
            Assert.That(_world.Place("chef", "place-far", held, 13, 10, 0).Reason, Is.EqualTo("not-beside-back-door"));
        }

        [Test]
        public void BackDoorsStayWhileADockNeedsThemAndTheLastOneStays()
        {
            var ledger = Ledger;
            Assert.That(Shell("back-a", ShellOrder.BackDoor, 9, 4).Accepted, Is.True);
            Assert.That(Furnish("dock", "dock", 10, 5).Accepted, Is.True);
            Assert.That(Shell("remove-a", ShellOrder.Remove, 0, 0, (9, 4)).Reason, Is.EqualTo("no-back-door"), "The only back door, with a dock beside it.");
            Assert.That(Shell("wall-step", ShellOrder.Partition, 0, 0, (10, 4)).Reason, Is.EqualTo("dock-attached"), "Its doorstep stays open.");
            // Taking another outer wall out (the shell is open, with no interior) and drawing it back leaves the back door's doorstep.
            Assert.That(Shell("open", ShellOrder.Remove, 0, 0, (5, 7)).Accepted, Is.True);
            Assert.That(SiteGrid.InteriorCells(Shop), Is.Empty);
            Assert.That(SiteGrid.Doorstep(Shop, 9, 4), Is.EqualTo(((int, int)?)(10, 4)));
            Assert.That(Shell("close", ShellOrder.Partition, 0, 0, (5, 7)).Accepted, Is.True);
            Assert.That(Shell("back-b", ShellOrder.BackDoor, 2, 5).Accepted, Is.True);
            Assert.That(Shell("remove-a-again", ShellOrder.Remove, 0, 0, (9, 4)).Reason, Is.EqualTo("dock-attached"), "The dock stands beside A, not B.");
            Assert.That(Shell("remove-b", ShellOrder.Remove, 0, 0, (2, 5)).Accepted, Is.True, "Nothing needs B.");

            // Without the dock, the last back door may move, but only by placing the new one first.
            Assert.That(_world.Sell("chef", "sell-dock", Placed("dock")).Accepted, Is.True);
            Assert.That(Shell("remove-last", ShellOrder.Remove, 0, 0, (9, 4)).Reason, Is.EqualTo("no-back-door"));
            var cash = Cash;
            Assert.That(Shell("back-c", ShellOrder.BackDoor, 9, 6).Accepted, Is.True);
            Assert.That(Shell("remove-a-moved", ShellOrder.Remove, 0, 0, (9, 4)).Accepted, Is.True);
            Assert.That(Cash, Is.EqualTo(cash - DoorOrderCents + DoorOrderCents), "Moving a back door pays for the new one and refunds the old one in full.");
            Assert.That(SiteGrid.ServiceDoors(Shop), Is.EqualTo(new[] { (9, 6) }));
            Assert.That(Shell("remove-c", ShellOrder.Remove, 0, 0, (9, 6)).Reason, Is.EqualTo("no-back-door"));
            Assert.That(Ledger, Is.EqualTo(ledger), "Every order charged or refunded exactly once.");
        }

        // A bought generated restaurant (layout format 4) comes with its back door in the shell and the dock beside it, free.
        [Test]
        public void ABoughtRestaurantComesWithItsBackDoorAndDock()
        {
            var offer = new PropertyOffer
            {
                LotId = "lot-diner", SiteId = "site-diner", BuildingId = "diner", Category = GoodsWorld.RestaurantKind, ForSale = true, PriceCents = 50_000,
                LotX = 100, LotZ = 0, Width = 18, Depth = 14, AccessX = 108, AccessZ = 14, BuildingX = 4, BuildingZ = 0, BuildingWidth = 14, BuildingDepth = 12,
                Doors = { new GridCell { X = 10, Z = 11 }, new GridCell { X = 11, Z = 11 } },
                BackDoors = { new GridCell { X = 4, Z = 2 } }, HasDock = true, DockX = 3, DockZ = 3, DockRotation = 1
            };
            _world.RegisterPropertyOffers(new[] { offer });
            Assert.That(_world.BuyProperty("chef", "buy", "resto", "lot-diner").Accepted, Is.True);
            var state = _world.Snapshot();
            var shell = state.Buildings.Single(x => x.Id == "diner");
            Assert.That(SiteGrid.ServiceDoors(shell), Is.EqualTo(new[] { (4, 2) }));
            Assert.That(SiteGrid.IsDoor(shell, 4, 2) && !SiteGrid.IsWall(shell, 4, 2), Is.True, "The back door is an opening.");
            var dock = state.Equipment.Single(x => x.SiteId == "site-diner");
            Assert.That((dock.Id, dock.Kind, dock.State, dock.CellX, dock.CellZ, dock.Rotation, dock.ChargedCents),
                Is.EqualTo((GoodsWorld.StarterDockId("site-diner"), GoodsWorld.DockKind, EquipmentState.Placed, 3, 3, 1, 0L)));
            Assert.That(RestaurantRules.BesideBackDoor(state, dock), Is.True);
            Assert.That(state.Locations.Any(x => x.Id == dock.InputLocationId) && state.Locations.Any(x => x.Id == dock.OutputLocationId), Is.True);
            Assert.That(state.Companies.Single().Cash, Is.EqualTo(StartCash - 50_000), "Only the building is paid for.");
            Assert.That(GoodsWorld.Restore(state), Is.Not.Null);
        }

        [Test]
        public void BackDoorsSurviveSavingAndV17SavesLoadWithCustomerDoorsAndTheirDocks()
        {
            // An old dock far from any back door (bootstrapped, as v17 placed it) and a new back door.
            _world.Bootstrap(new GoodsEquipment
            {
                Id = "old-dock", Kind = GoodsWorld.DockKind, SiteId = "resto", State = EquipmentState.Placed, CellX = 12, CellZ = 9, Width = 2, Depth = 1,
                InputCapacity = 8, OutputCapacity = 8
            });
            Assert.That(Shell("back", ShellOrder.BackDoor, 9, 4).Accepted, Is.True);
            GoodsSnapshotStore.Save(_world, PathForSave);
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            Assert.That(JsonUtility.ToJson(loaded.Buildings), Is.EqualTo(JsonUtility.ToJson(_world.Snapshot().Buildings)), "The back door round-trips.");
            Assert.That(SiteGrid.IsServiceDoor(loaded.Buildings.Single(x => x.Id == "shop"), 9, 4), Is.True);

            // The same save shaped as v17: no roles anywhere, so the back door reads as a customer door.
            var v17 = SnapshotDatabase.LatestPayload(PathForSave)
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":17")
                .Replace(",\"Role\":\"service\"", "").Replace(",\"Role\":\"\"", "");
            Assert.That(v17, Does.Not.Contain("\"Role\""), "The payload is shaped like a v17 save.");
            SnapshotDatabase.WritePayload(PathForSave, v17);
            var upgraded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            var shop = upgraded.Buildings.Single(x => x.Id == "shop");
            Assert.That((upgraded.SchemaVersion, SiteGrid.IsDoor(shop, 9, 4), SiteGrid.IsServiceDoor(shop, 9, 4)), Is.EqualTo((GoodsSnapshot.CurrentSchema, true, false)));
            var dock = upgraded.Equipment.Single(x => x.Id == "old-dock");
            Assert.That((dock.State, dock.CellX, dock.CellZ), Is.EqualTo((EquipmentState.Placed, 12, 9)), "Nothing moves.");
            Assert.That(RestaurantRules.ReachesStreet(upgraded, dock, null), Is.True, "Trucks still use it.");
            Assert.That(RestaurantRules.BesideBackDoor(upgraded, dock), Is.False, "It is marked as not beside a back door.");
            // The v19 upgrade gives the shop a back door, free: the west wall cell whose doorstep is farthest from both customer doors
            // ((5,1) and (10,4)), lowest cell first among equals.
            Assert.That(SiteGrid.ServiceDoors(shop), Is.EqualTo(new[] { (2, 6) }));
            Assert.That(shop.Structures.Single(x => x.Role == GoodsWorld.ServiceDoorRole).ChargedCents, Is.Zero);
        }

        // Owner decision 2026-10-06: removing the last customer door is refused, as removing the last back door is.
        [Test]
        public void TheLastCustomerDoorStays()
        {
            var ledger = Ledger;
            Assert.That(Shell("back", ShellOrder.BackDoor, 9, 5).Accepted, Is.True);
            Assert.That(Shell("remove-front", ShellOrder.Remove, 0, 0, (5, 2)).Reason, Is.EqualTo("no-customer-door"), "A back door is no way in for customers.");
            Assert.That(Shell("second", ShellOrder.Door, 2, 4).Accepted, Is.True);
            Assert.That(Shell("remove-front-again", ShellOrder.Remove, 0, 0, (5, 2)).Accepted, Is.True, "Another customer door is left.");
            Assert.That(SiteGrid.CustomerDoors(Shop), Is.EqualTo(new[] { (2, 4) }));
            Assert.That(Shell("remove-second", ShellOrder.Remove, 0, 0, (2, 4)).Reason, Is.EqualTo("no-customer-door"));
            // A door in an interior wall is not a customer door.
            Assert.That(Shell("room", ShellOrder.Partition, 0, 0, (6, 3), (6, 4), (6, 5), (6, 6)).Accepted, Is.True);
            Assert.That(Shell("inner-door", ShellOrder.Door, 6, 4).Accepted, Is.True);
            Assert.That(Shell("remove-second-again", ShellOrder.Remove, 0, 0, (2, 4)).Reason, Is.EqualTo("no-customer-door"));
            Assert.That(Ledger, Is.EqualTo(ledger), "Every order charged or refunded exactly once.");
        }

        // Owner decision 2026-10-06: belts and lifts stay off back-door doorsteps, and a new back door may not open onto a belt.
        [Test]
        public void BeltsStayOffDoorsteps()
        {
            _world.RegisterItem(GoodsWorld.BeltItemId, 100);
            _world.Bootstrap(new GoodsLot
            {
                Id = "belts", ItemId = GoodsWorld.BeltItemId, OwnerId = "resto", LocationId = "carried:chef", Quantity = 5, SpoilAfterSeconds = GoodsWorld.NonPerishableSeconds
            });
            Assert.That(_world.PlaceBelt("chef", "before", "resto", 10, 4, 1).Accepted, Is.True, "No back door yet.");
            Assert.That(Shell("onto-belt", ShellOrder.BackDoor, 9, 4).Reason, Is.EqualTo("doorstep"), "A back door may not open onto a belt.");
            Assert.That(_world.RemoveBelt("chef", "lift-belt", _world.Snapshot().Belts.Single().Id).Accepted, Is.True);
            Assert.That(Shell("back", ShellOrder.BackDoor, 9, 4).Accepted, Is.True);
            Assert.That(_world.PlaceBelt("chef", "on-step", "resto", 10, 4, 1).Reason, Is.EqualTo("doorstep"));
            Assert.That(RestaurantRules.BeltProblem(_world.Snapshot(), "resto", 10, 4, 1, -1), Is.EqualTo("doorstep"), "A lift coming down onto it.");
            Assert.That(RestaurantRules.BeltProblem(_world.Snapshot(), "resto", 10, 4, 1), Is.Null, "Off the ground it is no doorstep.");
            Assert.That(_world.PlaceBelt("chef", "beside", "resto", 11, 4, 1).Accepted, Is.True, "Beside the doorstep is fine.");
        }

        // Owner decision 2026-10-06: a restaurant listed without a back door (a layout made before generator v5) gets one on purchase.
        [Test]
        public void ARestaurantListedWithoutABackDoorGetsOne()
        {
            var offer = new PropertyOffer
            {
                LotId = "lot-old", SiteId = "site-old", BuildingId = "old", Category = GoodsWorld.RestaurantKind, ForSale = true, PriceCents = 50_000,
                LotX = 100, LotZ = 0, Width = 14, Depth = 14, AccessX = 107, AccessZ = 14, BuildingX = 0, BuildingZ = 0, BuildingWidth = 14, BuildingDepth = 12,
                Doors = { new GridCell { X = 6, Z = 11 }, new GridCell { X = 7, Z = 11 } }
            };
            _world.RegisterPropertyOffers(new[] { offer });
            Assert.That(_world.BuyProperty("chef", "buy", "resto", "lot-old").Accepted, Is.True);
            var state = _world.Snapshot();
            var shell = state.Buildings.Single(x => x.Id == "old");
            // The shell fills the lot but for the street apron, so only the front wall has a doorstep the street reaches; the door
            // goes there, as far from the customer doors as it can (both ends tie; the lowest cell wins).
            Assert.That(SiteGrid.ServiceDoors(shell), Is.EqualTo(new[] { (1, 11) }));
            Assert.That(SiteGrid.IsDoor(shell, 1, 11), Is.True, "The back door is an opening.");
            Assert.That(shell.Structures.Single().ChargedCents, Is.Zero);
            Assert.That(state.Companies.Single().Cash, Is.EqualTo(StartCash - 50_000), "Only the building is paid for.");
            Assert.That(GoodsWorld.Restore(state), Is.Not.Null);
            // A dock can now be placed beside it.
            Assert.That(_world.BuyAndPlace("chef", "dock", new FurnishOrder
            {
                SiteId = "site-old", OfferId = "dock", Placements = { new GridPlacement { X = 2, Z = 12 } }
            }).Accepted, Is.True);
        }
    }
}
