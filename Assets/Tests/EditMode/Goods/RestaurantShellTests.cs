// Verifies restaurant shell editing (decision 0034, slice 1): resizing within the site, interior walls, doors, windows, removal
// and wall finish; that every order charges or refunds exactly once (company cash plus everything recorded as charged never
// changes), that rejected and failed orders change nothing, that a retried request replays, that walls never cover equipment
// or decor, and that shells and their charges survive a save, a restart and the v15 upgrade. Isolated saves only.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FoodFactoryGame.Goods.Tests
{
    public sealed class RestaurantShellTests
    {
        // TEST-ONLY values: a 16x12 site with an 8x6 restaurant shell at (2,2) (interior 3..8 x 3..6), one door at (5,2) in the
        // south wall, company "co" with $10,000, a chef with an inventory and a grant, and a 4x4 factory on a second site.
        private const long StartCash = 1_000_000;
        private GoodsWorld _world;
        private string _saveDirectory;

        private string PathForSave => Path.Combine(_saveDirectory, "world.db");

        [SetUp]
        public void SetUp()
        {
            _saveDirectory = Path.Combine(Path.GetTempPath(), "FoodFactoryRestaurantShellTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            _world = new GoodsWorld("test-world");
            _world.Bootstrap(new SiteLayout { SiteId = "resto", Width = 16, Depth = 12 });
            _world.Bootstrap(new GoodsSite { Id = "resto", Name = "Resto" });
            _world.Bootstrap(new GoodsLocation { Id = "carried:chef", SiteId = "resto", Kind = "carried", Capacity = 10 });
            _world.Bootstrap(new GoodsBuilding
            {
                Id = "shop", SiteId = "resto", CellX = 2, CellZ = 2, Width = 8, Depth = 6, Doors = new List<GridCell> { new() { X = 5, Z = 2 } }
            });
            _world.Bootstrap(new SiteLayout { SiteId = "works", Width = 8, Depth = 8 });
            _world.Bootstrap(new GoodsSite { Id = "works", Name = "Works" });
            _world.Bootstrap(new GoodsBuilding
            {
                Id = "mill", SiteId = "works", Kind = GoodsWorld.FactoryKind, CellX = 0, CellZ = 0, Width = 4, Depth = 4,
                Doors = new List<GridCell> { new() { X = 1, Z = 0 } }
            });
            _world.Bootstrap(new GoodsCompany { Id = "co", Cash = StartCash, SiteIds = { "resto", "works" } });
            _world.Grant("chef", "resto");
            _world.Grant("chef", "works");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_saveDirectory, true);

        private GoodsBuilding Shop => _world.Snapshot().Buildings.Single(x => x.Id == "shop");
        private long Cash => _world.Snapshot().Companies.Single().Cash;
        // Money is only ever exchanged for recorded structure or equipment, so this never changes.
        private long Ledger
        {
            get
            {
                var state = _world.Snapshot();
                return state.Companies.Single().Cash + state.Buildings.SelectMany(x => x.Structures).Sum(x => x.ChargedCents)
                    + state.Equipment.Sum(x => x.ChargedCents);
            }
        }

        private static ShellOrder Resize(int x, int z, int width, int depth) => new()
        {
            Kind = ShellOrder.Resize, BuildingId = "shop", X = x, Z = z, Width = width, Depth = depth
        };

        private static ShellOrder Cells(string kind, string style, params (int X, int Z)[] cells) => new()
        {
            Kind = kind, BuildingId = "shop", Style = style, Cells = cells.Select(c => new GridCell { X = c.X, Z = c.Z }).ToList()
        };

        [Test]
        public void GrowingChargesTheNewFloorAndWallsAndShrinkingRefundsExactlyWhatWasCharged()
        {
            var plan = GoodsWorld.PlanShell(_world.Snapshot(), Resize(2, 2, 10, 6));
            Assert.That(plan.Problem, Is.Null);
            // Two new columns of 6 cells: 12 floor cells, 6 new east wall cells plus the top and bottom of the new column (2 more),
            // and the fee.
            var newWalls = 6 + 2;
            Assert.That(plan.ChargeCents, Is.EqualTo(12 * GoodsWorld.ShellFloorCellCents + newWalls * GoodsWorld.ShellWallCellCents
                + GoodsWorld.ShellOrderFeeCents));
            Assert.That(plan.RefundCents, Is.Zero, "The old east wall came with the building and was never paid for.");

            var grown = _world.OrderShell("chef", "grow", Resize(2, 2, 10, 6));
            Assert.That((grown.Accepted, grown.Reason, grown.Cents), Is.EqualTo((true, "shell-changed", plan.ChargeCents)));
            Assert.That(Cash, Is.EqualTo(StartCash - plan.ChargeCents));
            Assert.That((Shop.Width, Shop.Depth), Is.EqualTo((10, 6)));
            Assert.That(Ledger, Is.EqualTo(StartCash));

            // Back to the original size: every paid floor and wall of the growth goes back; the old east wall is built anew.
            var back = GoodsWorld.PlanShell(_world.Snapshot(), Resize(2, 2, 8, 6));
            Assert.That(back.RefundCents, Is.EqualTo(plan.ChargeCents), "Everything the growth charged, the fee included, is refunded.");
            var shrunk = _world.OrderShell("chef", "shrink", Resize(2, 2, 8, 6));
            Assert.That((shrunk.Accepted, shrunk.Cents), Is.EqualTo((true, back.ChargeCents - back.RefundCents)));
            Assert.That(Shop.Structures.Where(x => x.Kind == GoodsWorld.FloorStructure), Is.Empty, "No paid floor is left outside.");
            Assert.That(Ledger, Is.EqualTo(StartCash));
        }

        [Test]
        public void InteriorWallsDoorsAndWindowsAreChargedAndRemovingThemRefundsInFull()
        {
            Assert.That(_world.OrderShell("chef", "wall", Cells(ShellOrder.Partition, "brick", (6, 3), (6, 4), (6, 5), (6, 6))).Accepted, Is.True);
            var state = _world.Snapshot();
            Assert.That(SiteGrid.CellProblem(state, "resto", 6, 4, 1, 1, null), Is.EqualTo("blocked"), "An interior wall blocks placement.");
            Assert.That(_world.OrderShell("chef", "door", new ShellOrder { Kind = ShellOrder.Door, BuildingId = "shop", X = 6, Z = 4, Style = "glazed" }).Accepted, Is.True);
            Assert.That(SiteGrid.IsWall(Shop, 6, 4), Is.False, "A door makes the interior wall cell an opening.");
            Assert.That(_world.OrderShell("chef", "window", new ShellOrder { Kind = ShellOrder.Window, BuildingId = "shop", X = 6, Z = 5, Axis = 1, Style = "hatch" }).Accepted,
                Is.True);
            Assert.That(_world.OrderShell("chef", "front", new ShellOrder { Kind = ShellOrder.Window, BuildingId = "shop", X = 3, Z = 7, Axis = 0, Style = "picture" }).Accepted,
                Is.True, "A two-cell window on the north wall.");
            Assert.That(_world.OrderShell("chef", "finish", new ShellOrder { Kind = ShellOrder.WallFinish, BuildingId = "shop", Style = "wainscot" }).Cents,
                Is.Zero, "PROTOTYPE: the perimeter finish is free.");
            Assert.That(Shop.WallStyle, Is.EqualTo("wainscot"));
            Assert.That(Cash, Is.LessThan(StartCash));
            Assert.That(Ledger, Is.EqualTo(StartCash));

            // Windows and the door first, then the walls under them.
            Assert.That(_world.OrderShell("chef", "remove-1", Cells(ShellOrder.Remove, "", (6, 4), (6, 5), (6, 6), (3, 7), (4, 7))).Accepted, Is.True);
            Assert.That(Shop.Structures.Count(x => x.Kind == GoodsWorld.PartitionStructure), Is.EqualTo(4), "Openings went; the walls stay.");
            Assert.That(_world.OrderShell("chef", "remove-2", Cells(ShellOrder.Remove, "", (6, 3), (6, 4), (6, 5), (6, 6))).Accepted, Is.True);
            Assert.That(Shop.Structures, Is.Empty);
            Assert.That(Cash, Is.EqualTo(StartCash), "Everything built was refunded in full.");
        }

        [Test]
        public void DoorsAndWindowsMoveWithTheirSideAndLostOnesAreRefunded()
        {
            Assert.That(_world.OrderShell("chef", "door-2", new ShellOrder { Kind = ShellOrder.Door, BuildingId = "shop", X = 2, Z = 4, Style = "panel" }).Accepted, Is.True);
            Assert.That(_world.OrderShell("chef", "window", new ShellOrder { Kind = ShellOrder.Window, BuildingId = "shop", X = 6, Z = 2, Axis = 0, Style = "mullioned" }).Accepted,
                Is.True);
            // The south side moves down one row and the north side down to row 4, so the paid west door at row 4 becomes a corner.
            Assert.That(_world.OrderShell("chef", "resize", Resize(2, 1, 8, 4)).Accepted, Is.True);
            var shop = Shop;
            Assert.That(shop.Doors.Select(x => (x.X, x.Z)), Is.EqualTo(new[] { (5, 1) }), "The south door moved with its side.");
            Assert.That(shop.Structures.Single(x => x.Kind == GoodsWorld.WindowStructure).Z, Is.EqualTo(1), "The window moved with the south wall.");
            Assert.That(shop.Structures.Any(x => x.Kind == GoodsWorld.DoorStructure), Is.False, "The west door fell off the side and was refunded.");
            Assert.That(Ledger, Is.EqualTo(StartCash));
        }

        [Test]
        public void RejectedOrdersChangeNothing()
        {
            _world.Bootstrap(new GoodsEquipment
            {
                Id = "table", Kind = GoodsWorld.TableKind, SiteId = "resto", CellX = 4, CellZ = 4, Width = 1, Depth = 1, InputCapacity = 1,
                OutputCapacity = 1, Seats = 2
            });
            _world.Bootstrap(new GoodsEquipment
            {
                Id = "art", Kind = "wall-art", SiteId = "resto", CellX = 7, CellZ = 7, Width = 1, Depth = 1, InputCapacity = 1, OutputCapacity = 1,
                Layer = SiteGrid.WallLayer, Ambience = 3
            });
            var before = JsonUtility.ToJson(_world.Snapshot());
            void Rejected(string id, ShellOrder order, string reason, string player = "chef")
            {
                var outcome = _world.OrderShell(player, id, order);
                Assert.That((outcome.Accepted, outcome.Reason), Is.EqualTo((false, reason)), id);
                Assert.That(JsonUtility.ToJson(_world.Snapshot()), Is.EqualTo(before), $"{id} changed nothing and recorded nothing.");
            }
            Rejected("cover-table", Cells(ShellOrder.Partition, "plaster", (4, 4)), "blocked");
            Rejected("wall-onto-table", Resize(2, 2, 8, 3), "blocked");
            Rejected("lose-wall-art", new ShellOrder { Kind = ShellOrder.Door, BuildingId = "shop", X = 7, Z = 7, Style = "panel" }, "blocked");
            Rejected("move-wall-art", Resize(2, 2, 8, 7), "blocked");
            Rejected("off-site", Resize(10, 2, 8, 6), "out-of-bounds");
            Rejected("tiny", Resize(2, 2, 2, 6), "too-small");
            Rejected("last-door", Cells(ShellOrder.Remove, "", (5, 2)), "no-door");
            Rejected("corner-door", new ShellOrder { Kind = ShellOrder.Door, BuildingId = "shop", X = 2, Z = 2, Style = "panel" }, "invalid-cell");
            Rejected("bad-style", Cells(ShellOrder.Partition, "marble", (6, 3)), "invalid-style");
            Rejected("factory", new ShellOrder { Kind = ShellOrder.Resize, BuildingId = "mill", X = 0, Z = 0, Width = 5, Depth = 4 }, "not-a-restaurant");
            Rejected("stranger", Resize(2, 2, 10, 6), "forbidden", "stranger");
            Rejected("same", Resize(2, 2, 8, 6), "unchanged");
            var poor = new GoodsWorld("poor-world");
            poor.Bootstrap(new SiteLayout { SiteId = "resto", Width = 16, Depth = 12 });
            poor.Bootstrap(new GoodsSite { Id = "resto", Name = "Resto" });
            poor.Bootstrap(new GoodsBuilding { Id = "shop", SiteId = "resto", CellX = 2, CellZ = 2, Width = 8, Depth = 6, Doors = { new GridCell { X = 5, Z = 2 } } });
            poor.Bootstrap(new GoodsCompany { Id = "co", Cash = 100, SiteIds = { "resto" } });
            poor.Grant("chef", "resto");
            Assert.That(poor.OrderShell("chef", "grow", Resize(2, 2, 10, 6)).Reason, Is.EqualTo("insufficient-funds"));
            Assert.That(poor.Snapshot().Companies.Single().Cash, Is.EqualTo(100));
        }

        [Test]
        public void ARetriedRequestReplaysAndPaysOnce()
        {
            var first = _world.OrderShell("chef", "grow", Resize(2, 2, 10, 6));
            var cash = Cash;
            var again = _world.OrderShell("chef", "grow", Resize(2, 2, 12, 6));
            Assert.That((again.Accepted, again.Reason, again.Cents, again.Revision), Is.EqualTo((true, "shell-changed", first.Cents, first.Revision)));
            Assert.That((Cash, Shop.Width), Is.EqualTo((cash, 10)), "The replay did not run again.");
        }

        [Test]
        public void ShellsAndChargesSurviveASaveARestartAndAFailedCommit()
        {
            Assert.That(_world.OrderShellDurably("chef", "grow", Resize(1, 1, 10, 8), PathForSave).Accepted, Is.True);
            Assert.That(_world.OrderShellDurably("chef", "wall", Cells(ShellOrder.Partition, "tile", (5, 3), (5, 4)), PathForSave).Accepted, Is.True);
            var saved = JsonUtility.ToJson(_world.Snapshot());
            var loaded = GoodsSnapshotStore.Load(PathForSave);
            Assert.That(JsonUtility.ToJson(loaded.Snapshot()), Is.EqualTo(saved), "Stable IDs, structure and charges recover exactly.");

            var bad = Path.Combine(_saveDirectory, "missing", "world.db");
            var failed = loaded.OrderShellDurably("chef", "door", new ShellOrder { Kind = ShellOrder.Door, BuildingId = "shop", X = 5, Z = 3, Style = "panel" }, bad);
            Assert.That((failed.Accepted, failed.Reason), Is.EqualTo((false, "persistence-unavailable")));
            Assert.That(JsonUtility.ToJson(loaded.Snapshot()), Is.EqualTo(saved), "A failed commit restores everything, cash included.");
        }

        [Test]
        public void V15SavesUpgradeWithPlainShellsAndUnstaffedUnpricedEquipment()
        {
            _world.Bootstrap(new GoodsEquipment
            {
                Id = "counter-old", Kind = GoodsWorld.CounterKind, SiteId = "resto", CellX = 4, CellZ = 4, Width = 2, Depth = 1, InputCapacity = 2, OutputCapacity = 1
            });
            GoodsSnapshotStore.Save(_world, PathForSave);
            var v15 = SnapshotDatabase.LatestPayload(PathForSave)
                .Replace($"\"SchemaVersion\":{GoodsSnapshot.CurrentSchema}", "\"SchemaVersion\":15")
                .Replace(",\"Structures\":[],\"WallStyle\":\"\"", "")
                .Replace(",\"Layer\":\"\",\"Ambience\":0,\"ChargedCents\":0,\"StaffId\":\"\"", "")
                .Replace(",\"Docked\":false", "");
            Assert.That(v15, Does.Not.Contain("Structures").And.Not.Contain("StaffId"), "The payload is shaped like a v15 save.");
            SnapshotDatabase.WritePayload(PathForSave, v15);
            var loaded = GoodsSnapshotStore.Load(PathForSave).Snapshot();
            var counter = loaded.Equipment.Single(x => x.Id == "counter-old");
            Assert.That((loaded.SchemaVersion, counter.Kind, counter.StaffId, counter.Layer, counter.ChargedCents),
                Is.EqualTo((GoodsSnapshot.CurrentSchema, GoodsWorld.CounterKind, "", "", 0L)), "The counter is the same register, unstaffed.");
            Assert.That(loaded.Buildings.Select(x => (x.Id, x.Structures.Count, x.WallStyle)), Is.EquivalentTo(new[] { ("shop", 0, ""), ("mill", 0, "") }));
        }
    }
}
