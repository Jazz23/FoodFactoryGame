// A building shell is a server-owned rectangle on a site grid whose perimeter cells are walls, except its door cells
// (decision 0019). Walls block equipment and belts through SiteGrid.CellProblem; the interior and doors are ordinary cells.
// Shells are created only by the server (no construction command yet) and are never moved or removed.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    [Serializable] public sealed class GridCell
    {
        public int X;
        public int Z;
    }

    [Serializable] public sealed class GoodsBuilding
    {
        public string Id;
        public string SiteId;
        // Anchor is the footprint's minimum cell; the footprint includes the walls.
        public int CellX;
        public int CellZ;
        public int Width;
        public int Depth;
        // Perimeter cells, never corners, that are openings instead of walls.
        public List<GridCell> Doors = new();
    }

    public sealed partial class GoodsWorld
    {
        // Smallest shell with an interior: one cell inside a ring of walls.
        public const int MinimumBuildingSize = 3;

        // Server-only, like layout bootstrap: the shell must fit the site, overlap no other shell, and its walls must not
        // cover placed equipment or belts.
        public void Bootstrap(GoodsBuilding building)
        {
            lock (_gate)
            {
                var copy = building == null ? null : JsonUtility.FromJson<GoodsBuilding>(JsonUtility.ToJson(building));
                if (copy == null || _state.Buildings.Any(x => x.Id == copy.Id) || BuildingProblem(_state, copy) != null)
                    throw new ArgumentException("Invalid, duplicate or blocked building.");
                _state.Buildings.Add(copy);
                _state.Revision++;
            }
        }

        // Null when the shell is well formed and its walls are clear; checks everything except ID uniqueness.
        private static string BuildingProblem(GoodsSnapshot state, GoodsBuilding building)
        {
            var layout = state.SiteLayouts.FirstOrDefault(x => x.SiteId == building.SiteId);
            if (string.IsNullOrWhiteSpace(building.Id) || layout == null || building.Doors == null
                || building.Width < MinimumBuildingSize || building.Depth < MinimumBuildingSize
                || building.CellX < 0 || building.CellZ < 0
                || building.CellX + building.Width > layout.Width || building.CellZ + building.Depth > layout.Depth)
                return "invalid-building";
            if (building.Doors.Any(x => x == null || !SiteGrid.IsDoorCell(building, x.X, x.Z))
                || building.Doors.GroupBy(x => (x.X, x.Z)).Any(x => x.Count() != 1))
                return "invalid-door";
            if (state.Buildings.Any(x => x.Id != building.Id && x.SiteId == building.SiteId
                    && SiteGrid.Overlaps(building.CellX, building.CellZ, building.Width, building.Depth, x.CellX, x.CellZ, x.Width, x.Depth)))
                return "blocked";
            foreach (var equipment in state.Equipment.Where(x => x.SiteId == building.SiteId && x.State == EquipmentState.Placed))
            {
                var (width, depth) = SiteGrid.Footprint(equipment.Width, equipment.Depth, equipment.Rotation);
                if (SiteGrid.CoversWall(building, equipment.CellX, equipment.CellZ, width, depth)) return "blocked";
            }
            if (state.Belts.Any(x => x.SiteId == building.SiteId && SiteGrid.CoversWall(building, x.CellX, x.CellZ, 1, 1))) return "blocked";
            return null;
        }

        private static void ValidateBuildings(GoodsSnapshot state)
        {
            if (state.Buildings.Any(x => x == null || BuildingProblem(state, x) != null)
                || state.Buildings.GroupBy(x => x.Id).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot violates building invariants.");
        }
    }
}
