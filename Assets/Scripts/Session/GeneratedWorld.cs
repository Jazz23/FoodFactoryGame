// Loads or creates a generated world's goods save (decision 0028, piece 2; customers by decision 0030): the layout's property
// catalog is registered first, then a new world gets one company with the PROTOTYPE starting cash, which is given the starting
// restaurant's lot (its site, layout and shell) without charge, a placed counter and table inside that shell (owner decision
// 2026-09-30: pre-equipped), and the customer districts and competitors derived from the layout, and the first save is
// committed. The layout's road network is registered with the catalog, so trucks drive the generated roads (decision 0032).
// A world made before these existed gains each missing part once, by ID or by kind, committed before serving.
// Nothing else is seeded: no storage, belts, machines, employees or warehouse. A save made in this scene before piece 2 (a
// format 3 layout beside a dev-world snapshot, which never got its starting restaurant) is left untouched and not opened as a
// generated world.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    public static class GeneratedWorld
    {
        // PROTOTYPE starting capital of the one company every player joins (owner, 2026-09-29: $1,000,000), in whole cents.
        public const string CompanyId = "company-1";
        public const long StartingCash = 100_000_000;
        // Layout format that has lots (decision 0028); older formats keep the dev site beside the map.
        public const int FirstFormat = 3;
        public const string StartCounterId = "start-counter";
        public const string StartTableId = "start-table";

        public static bool Supports(StoredWorldLayout stored) => stored?.Layout != null && stored.Layout.FormatVersion >= FirstFormat;

        // The listed lot of the layout's starting restaurant.
        public static PropertyOffer StartOffer(WorldLayout layout)
        {
            var lot = layout.Lots.FirstOrDefault(x => x.BuildingId == layout.StartRestaurantId)
                ?? throw new System.ArgumentException("The layout has no lot for its starting restaurant.");
            return WorldLayoutShells.PropertyOffers(layout).First(x => x.LotId == lot.Id);
        }

        // Returns a world that matches its committed snapshot (GoodsNetworkBridge.InitializeServer requires it), with the
        // layout's property catalog registered; null for a pre-piece-2 save that must keep the dev world. Item max stacks
        // (content) are registered before anything is counted in slots. Without a counter or table definition that piece is
        // not placed.
        public static GoodsWorld LoadOrCreate(string worldPath, StoredWorldLayout stored, IEnumerable<ItemDefinition> items = null,
            EquipmentDefinition counter = null, EquipmentDefinition table = null)
        {
            if (!Supports(stored)) throw new System.ArgumentException("A generated world needs a layout with lots.");
            var start = StartOffer(stored.Layout);
            var catalog = WorldLayoutShells.PropertyOffers(stored.Layout);
            if (GoodsSnapshotStore.HasSnapshots(worldPath))
            {
                var loaded = GoodsSnapshotStore.Load(worldPath);
                if (loaded.Snapshot().Properties.All(x => x.LotId != start.LotId))
                {
                    Debug.Log("[Session] This world was saved before generated worlds had their own restaurant; it keeps the dev site.");
                    return null;
                }
                Register(loaded, items);
                loaded.RegisterPropertyOffers(catalog);
                loaded.RegisterRoads(RoadNetwork.For(stored.Layout));
                var revision = loaded.Snapshot().Revision;
                Equip(loaded, start, counter, table);
                AddCustomers(loaded, stored.Layout);
                if (loaded.Snapshot().Revision != revision)
                {
                    GoodsSnapshotStore.Save(loaded, worldPath);
                    Debug.Log("[Session] Added the starting counter, table, districts or competitors this older generated world lacked.");
                }
                return loaded;
            }
            var world = new GoodsWorld(stored.WorldId);
            Register(world, items);
            world.RegisterPropertyOffers(catalog);
            world.RegisterRoads(RoadNetwork.For(stored.Layout));
            world.Bootstrap(new GoodsCompany { Id = CompanyId, Cash = StartingCash });
            world.Bootstrap(start, CompanyId);
            Equip(world, start, counter, table);
            AddCustomers(world, stored.Layout);
            GoodsSnapshotStore.Save(world, worldPath);
            var state = world.Snapshot();
            Debug.Log($"[Session] Created generated world {stored.WorldId}: {CompanyId} owns {start.BuildingId} (site {start.SiteId}); " +
                $"{state.Districts.Count} districts, {state.Competitors.Count} competitors.");
            return world;
        }

        // One-time, by ID: every district and competitor the layout derives that the world does not have yet.
        public static void AddCustomers(GoodsWorld world, WorldLayout layout)
        {
            var state = world.Snapshot();
            var districts = new HashSet<string>(state.Districts.Select(x => x.Id));
            var competitors = new HashSet<string>(state.Competitors.Select(x => x.Id));
            foreach (var district in WorldLayoutCustomers.Districts(layout))
                if (!districts.Contains(district.Id)) world.Bootstrap(district);
            foreach (var competitor in WorldLayoutCustomers.Competitors(layout))
                if (!competitors.Contains(competitor.Id)) world.Bootstrap(competitor);
        }

        // One-time, like DevWorld's counter and table: equipment is never destroyed (pickup only holds it), so a world with no
        // counter (or table) of any state has never had one. Each is placed at the first free cell PlaceIn finds.
        public static void Equip(GoodsWorld world, PropertyOffer start, EquipmentDefinition counter, EquipmentDefinition table)
        {
            var state = world.Snapshot();
            GoodsEquipment placedCounter = null;
            if (counter != null && state.Equipment.All(x => x.Kind != counter.Kind && x.Id != StartCounterId))
                placedCounter = PlaceIn(world, start, counter, StartCounterId, CounterCells(start), null);
            if (table != null && state.Equipment.All(x => x.Kind != table.Kind && x.Id != StartTableId))
                PlaceIn(world, start, table, StartTableId, TableCells(start), placedCounter);
        }

        // Interior bounds of the starting shell in site cells: (x0, z0, x1, z1), inclusive.
        private static (int X0, int Z0, int X1, int Z1) Interior(PropertyOffer start) =>
            (start.BuildingX + 1, start.BuildingZ + 1, start.BuildingX + start.BuildingWidth - 2, start.BuildingZ + start.BuildingDepth - 2);

        // The counter stands two rows in from the south wall, centred, so the ordering spot south of it (where customer figures
        // stand) stays inside and walkable: the wall and the counter each take half a cell off the NavMesh (agent radius), which
        // would close a single row. Then the rows further north.
        private static IEnumerable<(int X, int Z)> CounterCells(PropertyOffer start)
        {
            var (x0, z0, x1, z1) = Interior(start);
            var centre = x0 + (x1 - x0 - 1) / 2;
            for (var z = z0 + 2; z <= z1; z++)
            for (var step = 0; step <= x1 - x0; step++)
            {
                var x = centre + (step % 2 == 0 ? step / 2 : -(step + 1) / 2);
                if (x >= x0 && x + 1 <= x1) yield return (x, z);
            }
        }

        // The table goes in an interior corner, north corners first, leaving the middle free for an oven.
        private static IEnumerable<(int X, int Z)> TableCells(PropertyOffer start)
        {
            var (x0, z0, x1, z1) = Interior(start);
            return new[] { (x1 - 1, z1), (x0, z1), (x1 - 1, z0), (x0, z0) };
        }

        private static GoodsEquipment PlaceIn(GoodsWorld world, PropertyOffer start, EquipmentDefinition definition, string id,
            IEnumerable<(int X, int Z)> cells, GoodsEquipment avoid)
        {
            // A door's inward neighbour stays clear so the way in is never blocked.
            var inward = new HashSet<(int, int)>(start.Doors.Select(door => (
                System.Math.Clamp(door.X, start.BuildingX + 1, start.BuildingX + start.BuildingWidth - 2),
                System.Math.Clamp(door.Z, start.BuildingZ + 1, start.BuildingZ + start.BuildingDepth - 2))));
            foreach (var (x, z) in cells)
            {
                var piece = definition.CreatePlaced(id, start.SiteId, x, z, 0);
                if (Enumerable.Range(0, piece.Width).Any(dx => Enumerable.Range(0, piece.Depth).Any(dz =>
                        inward.Contains((x + dx, z + dz))
                        || (avoid != null && x + dx >= avoid.CellX && x + dx < avoid.CellX + avoid.Width
                            && z + dz >= avoid.CellZ - 2 && z + dz < avoid.CellZ + avoid.Depth)))) continue;
                try
                {
                    world.Bootstrap(piece);
                    return piece;
                }
                catch (System.ArgumentException) { }
            }
            Debug.LogWarning($"[Session] No free cell in {start.BuildingId} for {id}; this world starts without it.");
            return null;
        }

        private static void Register(GoodsWorld world, IEnumerable<ItemDefinition> items)
        {
            foreach (var item in items ?? Enumerable.Empty<ItemDefinition>())
                if (item != null) world.RegisterItem(item.Id, item.MaxStack);
        }
    }
}
