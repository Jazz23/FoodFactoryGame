// Loads the committed world save or creates the DEVELOPMENT seed; the seed is placeholder content, not design data.
// The seed is applied only to a brand-new world: an existing save never gains the layout, oven or storage dough retroactively.
// Belts, lifts, the company, the sell counter and the building shells are the exceptions: a save from before each existed
// gets the dev belt stock (EnsureBeltStock), the dev lift stock (EnsureLiftStock), the dev company with its starting cash (EnsureCompany), the dev counter (EnsureCounter),
// the dev restaurant shell (EnsureBuilding), the dev factory shell (EnsureFactory) or, where the server spawns employees,
// the dev employee (EnsureEmployee) once.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    public static class DevWorld
    {
        public const string WorldId = "dev-world";
        public const string SiteId = "dev-site";
        public const string StorageId = "dev-site-storage";
        public const string OvenId = "dev-oven-1";
        // PROTOTYPE values: a 20x20 one-metre grid, the seeded oven's anchor cell, and the slot counts of each player's
        // inventory and the dev storage (decision 0009: capacity counts slots; a slot holds one stack up to the item's max).
        public const int GridWidth = 20;
        public const int GridDepth = 20;
        public const int OvenCellX = 12;
        public const int OvenCellZ = 13;
        public const int InventoryCapacity = 30;
        public const int StorageCapacity = 30;
        // PROTOTYPE ingredients until something produces dough: stock in the dev storage and a few for each new player.
        public const string DoughItemId = "dough";
        public const int StorageDough = 20;
        public const int StarterDough = 5;
        public const long DoughSpoilAfterSeconds = 7200;
        // PROTOTYPE belts until belts can be bought or made: stock in the dev storage and some for each new player.
        public const string StorageBeltsLotId = "dev-storage-belts";
        public const int StorageBelts = 200;
        public const int StarterBelts = 50;
        // PROTOTYPE conveyor lifts (decision 0021) until they can be bought: stock in the dev storage and some for each new player.
        public const string StorageLiftsLotId = "dev-storage-lifts";
        public const int StorageLifts = 20;
        public const int StarterLifts = 10;
        // PROTOTYPE starting capital (whole cents) of the one company that owns the dev site (decision 0012).
        public const string CompanyId = "dev-company";
        public const long StartingCash = 50000;
        // PROTOTYPE sell counter (decision 0013): one placed near the spawn points, left of the oven.
        public const string CounterKind = "counter";
        public const string CounterId = "dev-counter-1";
        public const int CounterCellX = 6;
        public const int CounterCellZ = 13;
        // PROTOTYPE restaurant shell (decision 0019): 11x9 cells in the south-east of the grid, walls included, with a two-cell
        // doorway in its north wall beside the spawn points. It clears the seeded oven and counter.
        public const string RestaurantId = "dev-restaurant";
        public const int RestaurantCellX = 9;
        public const int RestaurantCellZ = 0;
        public const int RestaurantWidth = 11;
        public const int RestaurantDepth = 9;
        public const int RestaurantDoorX = 13;
        // PROTOTYPE factory shell (decision 0020): 5x10 cells along the north-east edge, walls included (interior 3x8), with a
        // two-cell doorway in its south wall. It clears the seeded oven and counter, the spawn points, every PlayMode test cell
        // and everything placed in the owner's dev save.
        public const string FactoryId = "dev-factory";
        public const int FactoryCellX = 15;
        public const int FactoryCellZ = 10;
        public const int FactoryWidth = 5;
        public const int FactoryDepth = 10;
        public const int FactoryDoorX = 16;
        // PROTOTYPE construction prices (whole cents per interior cell of a new floor) and height limit, ground floor included.
        public const long FloorCentsPerCell = 500;
        public const int MaxFloors = 3;

        // PROTOTYPE employee (the scriptable worker): one per dev site, standing west of the counter, with 4 hand slots.
        public const string EmployeeId = GoodsWorld.EmployeePrefix + "1";
        public const int EmployeeHandSlots = 4;

        public static GoodsEmployee Employee() => new() { Id = EmployeeId, SiteId = SiteId, Name = "Employee", X = -5f, Z = 3f, Yaw = 90f };

        public static FloorOffer FloorOffer => new() { CentsPerCell = FloorCentsPerCell, MaxFloors = MaxFloors };

        public static IReadOnlyList<GoodsLot> StarterGoods => new[]
        {
            new GoodsLot { ItemId = DoughItemId, Quantity = StarterDough, SpoilAfterSeconds = DoughSpoilAfterSeconds },
            new GoodsLot { ItemId = GoodsWorld.BeltItemId, Quantity = StarterBelts, SpoilAfterSeconds = GoodsWorld.NonPerishableSeconds },
            new GoodsLot { ItemId = GoodsWorld.LiftItemId, Quantity = StarterLifts, SpoilAfterSeconds = GoodsWorld.NonPerishableSeconds }
        };

        // Returns a world that matches its committed snapshot, as GoodsNetworkBridge.InitializeServer requires.
        // Without an oven definition the seed has the layout but no equipment. Item max stacks (content) are registered
        // before the seed, because the seed's goods are counted in slots.
        // A pre-SQLite snapshot at legacyWorldPath is imported once, after a dry run, when no database exists yet.
        // Without a counter definition no counter is seeded or added. seedEmployee (a server that spawns employees) adds the
        // dev employee.
        public static GoodsWorld LoadOrCreate(string worldPath, EquipmentDefinition oven = null, IEnumerable<ItemDefinition> items = null,
            string legacyWorldPath = null, EquipmentDefinition counter = null, bool seedEmployee = false)
        {
            if (!File.Exists(worldPath) && !string.IsNullOrWhiteSpace(legacyWorldPath)
                && (File.Exists(legacyWorldPath) || File.Exists(legacyWorldPath + ".previous")))
            {
                GoodsSnapshotStore.ImportLegacy(legacyWorldPath, worldPath, true);
                GoodsSnapshotStore.ImportLegacy(legacyWorldPath, worldPath, false);
                Debug.Log($"[Session] Imported {legacyWorldPath} into {worldPath}; the old file was left in place.");
            }
            if (File.Exists(worldPath))
            {
                var loaded = GoodsSnapshotStore.Load(worldPath);
                Register(loaded, items);
                EnsureBeltStock(loaded, worldPath);
                EnsureLiftStock(loaded, worldPath);
                EnsureCompany(loaded, worldPath);
                EnsureCounter(loaded, worldPath, counter);
                EnsureBuilding(loaded, worldPath);
                EnsureFactory(loaded, worldPath);
                if (seedEmployee) EnsureEmployee(loaded, worldPath);
                return loaded;
            }
            var world = new GoodsWorld(WorldId);
            Register(world, items);
            world.Bootstrap(new GoodsLocation { Id = StorageId, SiteId = SiteId, Kind = "storage", Capacity = StorageCapacity });
            world.Bootstrap(new GoodsLot
            {
                Id = "dev-storage-dough", ItemId = DoughItemId, OwnerId = SiteId, LocationId = StorageId,
                Quantity = StorageDough, SpoilAfterSeconds = DoughSpoilAfterSeconds
            });
            world.Bootstrap(BeltStock());
            world.Bootstrap(LiftStock());
            world.Bootstrap(Company());
            world.Bootstrap(new SiteLayout { SiteId = SiteId, Width = GridWidth, Depth = GridDepth });
            world.Bootstrap(Restaurant());
            world.Bootstrap(Factory());
            if (oven != null) world.Bootstrap(oven.CreatePlaced(OvenId, SiteId, OvenCellX, OvenCellZ, 0));
            if (counter != null) world.Bootstrap(counter.CreatePlaced(CounterId, SiteId, CounterCellX, CounterCellZ, 0));
            if (seedEmployee) world.Bootstrap(Employee(), EmployeeHandSlots);
            GoodsSnapshotStore.Save(world, worldPath);
            return world;
        }

        private static void Register(GoodsWorld world, IEnumerable<ItemDefinition> items)
        {
            foreach (var item in items ?? Enumerable.Empty<ItemDefinition>())
                if (item != null) world.RegisterItem(item.Id, item.MaxStack);
        }

        private static GoodsLot BeltStock() => new()
        {
            Id = StorageBeltsLotId, ItemId = GoodsWorld.BeltItemId, OwnerId = SiteId, LocationId = StorageId,
            Quantity = StorageBelts, SpoilAfterSeconds = GoodsWorld.NonPerishableSeconds
        };

        // PROTOTYPE, one-time: belts are never destroyed (placing turns an item into a belt, removing turns it back), so a
        // world with no belt items and no placed belts has never had belts. Such a save gets the dev storage belt stock,
        // committed before serving; if the storage has no room it is left alone.
        private static void EnsureBeltStock(GoodsWorld world, string worldPath)
        {
            var state = world.Snapshot();
            if (state.Belts.Count > 0 || state.Lots.Any(x => x.ItemId == GoodsWorld.BeltItemId)
                || state.Lots.Any(x => x.Id == StorageBeltsLotId) || state.Locations.All(x => x.Id != StorageId)) return;
            try { world.Bootstrap(BeltStock()); }
            catch (System.ArgumentException)
            {
                Debug.LogWarning("[Session] The dev storage is too full for the belt stock; free two slots and restart the server.");
                return;
            }
            GoodsSnapshotStore.Save(world, worldPath);
            Debug.Log($"[Session] Added {StorageBelts} dev belts to the storage of this older save.");
        }

        private static GoodsLot LiftStock() => new()
        {
            Id = StorageLiftsLotId, ItemId = GoodsWorld.LiftItemId, OwnerId = SiteId, LocationId = StorageId,
            Quantity = StorageLifts, SpoilAfterSeconds = GoodsWorld.NonPerishableSeconds
        };

        // PROTOTYPE, one-time, like EnsureBeltStock: lifts are never destroyed, so a world with no lift items and no placed
        // lifts has never had them. Such a save gets the dev storage lift stock, committed before serving; if the storage has
        // no room it is left alone.
        private static void EnsureLiftStock(GoodsWorld world, string worldPath)
        {
            var state = world.Snapshot();
            if (state.Belts.Any(x => x.Lift != 0) || state.Lots.Any(x => x.ItemId == GoodsWorld.LiftItemId)
                || state.Lots.Any(x => x.Id == StorageLiftsLotId) || state.Locations.All(x => x.Id != StorageId)) return;
            try { world.Bootstrap(LiftStock()); }
            catch (System.ArgumentException)
            {
                Debug.LogWarning("[Session] The dev storage is too full for the lift stock; free a slot and restart the server.");
                return;
            }
            GoodsSnapshotStore.Save(world, worldPath);
            Debug.Log($"[Session] Added {StorageLifts} dev lifts to the storage of this older save.");
        }

        private static GoodsCompany Company() => new() { Id = CompanyId, Cash = StartingCash, SiteIds = new List<string> { SiteId } };

        // One-time for saves from before companies (payload v4 and older): the dev site gets its company and starting cash,
        // committed before serving. A site that already has a company is never given cash again.
        private static void EnsureCompany(GoodsWorld world, string worldPath)
        {
            var state = world.Snapshot();
            if (world.CompanyOfSite(SiteId) != null || state.Companies.Any(x => x.Id == CompanyId)
                || state.Locations.All(x => x.SiteId != SiteId)) return;
            world.Bootstrap(Company());
            GoodsSnapshotStore.Save(world, worldPath);
            Debug.Log($"[Session] Added {CompanyId} with {StartingCash} cents to this older save.");
        }

        // PROTOTYPE, one-time: equipment is never destroyed (pickup only holds it), so a world with no counter of any state has
        // never had one. It gets the dev counter at its seed cell, committed before serving; if that cell is taken the
        // save is left alone with a warning.
        private static void EnsureCounter(GoodsWorld world, string worldPath, EquipmentDefinition counter)
        {
            var state = world.Snapshot();
            if (counter == null || state.Equipment.Any(x => x.Kind == counter.Kind || x.Id == CounterId)
                || state.SiteLayouts.All(x => x.SiteId != SiteId)) return;
            try { world.Bootstrap(counter.CreatePlaced(CounterId, SiteId, CounterCellX, CounterCellZ, 0)); }
            catch (System.ArgumentException)
            {
                Debug.LogWarning($"[Session] Cells ({CounterCellX}, {CounterCellZ}) are taken, so this older save gets no dev counter; clear them and restart the server.");
                return;
            }
            GoodsSnapshotStore.Save(world, worldPath);
            Debug.Log("[Session] Added the dev sell counter to this older save.");
        }

        public static GoodsBuilding Restaurant() => new()
        {
            Id = RestaurantId, SiteId = SiteId, CellX = RestaurantCellX, CellZ = RestaurantCellZ, Width = RestaurantWidth, Depth = RestaurantDepth,
            Doors = new List<GridCell>
            {
                new() { X = RestaurantDoorX, Z = RestaurantCellZ + RestaurantDepth - 1 },
                new() { X = RestaurantDoorX + 1, Z = RestaurantCellZ + RestaurantDepth - 1 }
            }
        };

        // PROTOTYPE, one-time: buildings are never removed, so a save without the dev restaurant has never had it. It gets the
        // dev restaurant shell, committed before serving; if equipment or belts stand where its walls go, the save is left
        // alone with a warning.
        private static void EnsureBuilding(GoodsWorld world, string worldPath)
        {
            var state = world.Snapshot();
            // Keyed by ID: the dev factory may already stand on a site whose restaurant was skipped.
            if (state.Buildings.Any(x => x.Id == RestaurantId) || state.SiteLayouts.All(x => x.SiteId != SiteId)) return;
            try { world.Bootstrap(Restaurant()); }
            catch (System.ArgumentException)
            {
                Debug.LogWarning($"[Session] Something stands where the dev restaurant's walls go (cells {RestaurantCellX}-{RestaurantCellX + RestaurantWidth - 1}, "
                    + $"{RestaurantCellZ}-{RestaurantCellZ + RestaurantDepth - 1}), so this older save gets no building; clear the walls and restart the server.");
                return;
            }
            GoodsSnapshotStore.Save(world, worldPath);
            Debug.Log("[Session] Added the dev restaurant shell to this older save.");
        }

        public static GoodsBuilding Factory() => new()
        {
            Id = FactoryId, SiteId = SiteId, Kind = GoodsWorld.FactoryKind, CellX = FactoryCellX, CellZ = FactoryCellZ,
            Width = FactoryWidth, Depth = FactoryDepth,
            Doors = new List<GridCell> { new() { X = FactoryDoorX, Z = FactoryCellZ }, new() { X = FactoryDoorX + 1, Z = FactoryCellZ } }
        };

        // PROTOTYPE, one-time, like EnsureBuilding: a save without the dev factory (it is never removed) gets it, committed
        // before serving; if equipment or belts stand where its walls go, the save is left alone with a warning.
        private static void EnsureFactory(GoodsWorld world, string worldPath)
        {
            var state = world.Snapshot();
            if (state.Buildings.Any(x => x.Id == FactoryId) || state.SiteLayouts.All(x => x.SiteId != SiteId)) return;
            try { world.Bootstrap(Factory()); }
            catch (System.ArgumentException)
            {
                Debug.LogWarning($"[Session] Something stands where the dev factory's walls go (cells {FactoryCellX}-{FactoryCellX + FactoryWidth - 1}, "
                    + $"{FactoryCellZ}-{FactoryCellZ + FactoryDepth - 1}), so this save gets no factory; clear the walls and restart the server.");
                return;
            }
            GoodsSnapshotStore.Save(world, worldPath);
            Debug.Log("[Session] Added the dev factory shell to this save.");
        }

        // PROTOTYPE, one-time: employees are never removed, so a save without the dev employee record has never had it. It
        // gets one, committed before serving. A grant and carried inventory left by the earlier unsaved prototype are reused.
        private static void EnsureEmployee(GoodsWorld world, string worldPath)
        {
            var state = world.Snapshot();
            if (state.Employees.Any(x => x.Id == EmployeeId) || state.Locations.All(x => x.SiteId != SiteId)) return;
            world.Bootstrap(Employee(), EmployeeHandSlots);
            GoodsSnapshotStore.Save(world, worldPath);
            Debug.Log($"[Session] Added {EmployeeId} to this save.");
        }
    }
}
