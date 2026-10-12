// Draws a restaurant's ground storey from the restaurant art kit (decision 0034); presentation only, built from replicated
// building data. Every perimeter and interior-wall cell is a node of one wall graph. A plain wall cell draws a full 1 m piece
// when it continues a straight line (two such cells side by side share one 2 m piece) and otherwise an end post with a half
// piece toward each neighbour. A door cell draws a doorway bay with its frame and leaf; two door cells in line make a double
// door with the 3 m bay. Bays reach half a cell into the neighbouring wall cells, so those leave out their half toward the door.
// A window draws the 2 m opening bay over its two cells with the glazed unit at sill height (a serving hatch is all bay).
// Thin kit walls stand on each cell's centreline; grid occupancy stays whole cells (decision 0019). Wall colliders are added by
// BuildingPresenter, not here; each door leaf gets a DoorSwing, which opens it for people nearby and blocks while it is shut.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;

namespace FoodFactoryGame.Session.Buildings
{
    public static class RestaurantShellModel
    {
        // Window sill height of the kit's glazed units (art kit README).
        private const float SillHeight = 0.85f;
        // Hinge offset of a leaf in a single frame (art kit README) and in each half of a double frame.
        private const float SingleHinge = 0.475f;
        private const float DoubleHinge = 0.995f;

        private static readonly (int X, int Z)[] Directions = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        public static void Build(Transform parent, SiteLayout layout, GoodsBuilding building, RestaurantStyleCatalog catalog)
        {
            var cells = new HashSet<(int X, int Z)>();
            for (var x = building.CellX; x < building.CellX + building.Width; x++)
            for (var z = building.CellZ; z < building.CellZ + building.Depth; z++)
                if (SiteGrid.OnPerimeter(building, x, z)) cells.Add((x, z));
            foreach (var partition in building.Structures.Where(s => s.Kind == GoodsWorld.PartitionStructure)) cells.Add((partition.X, partition.Z));
            bool IsDoor((int X, int Z) c) => SiteGrid.IsDoor(building, c.X, c.Z);
            bool IsWindow((int X, int Z) c) => SiteGrid.WindowAt(building, c.X, c.Z) != null;
            string Style((int X, int Z) c) => building.Structures.FirstOrDefault(s => s.Kind == GoodsWorld.PartitionStructure && s.X == c.X && s.Z == c.Z)?.Style
                ?? building.WallStyle;

            // Plain walls: straight cells pair up into 2 m pieces along their line.
            var paired = new HashSet<(int, int)>();
            foreach (var cell in cells.OrderBy(c => c.X).ThenBy(c => c.Z))
            {
                if (IsDoor(cell) || IsWindow(cell) || paired.Contains(cell)) continue;
                var finish = catalog.Finish(Style(cell));
                if (finish == null) continue;
                var linked = Directions.Where(d => cells.Contains((cell.X + d.X, cell.Z + d.Z))).ToList();
                var halves = linked.Where(d => !IsDoor((cell.X + d.X, cell.Z + d.Z))).ToList();
                var straightX = linked.Count == 2 && linked.Contains((1, 0)) && linked.Contains((-1, 0));
                var straightZ = linked.Count == 2 && linked.Contains((0, 1)) && linked.Contains((0, -1));
                if ((straightX || straightZ) && halves.Count == 2)
                {
                    var step = straightX ? (1, 0) : (0, 1);
                    var next = (cell.X + step.Item1, cell.Z + step.Item2);
                    var nextLinked = Directions.Where(d => cells.Contains((next.Item1 + d.X, next.Item2 + d.Z))).ToList();
                    var nextStraight = cells.Contains(next) && !IsDoor(next) && !IsWindow(next) && !paired.Contains(next) && Style(next) == Style(cell)
                        && nextLinked.Count == 2 && nextLinked.All(d => d.X == -step.Item1 && d.Z == -step.Item2 || d.X == step.Item1 && d.Z == step.Item2)
                        && !IsDoor((next.Item1 + step.Item1, next.Item2 + step.Item2));
                    if (nextStraight && finish.wall2m != null)
                    {
                        paired.Add(cell);
                        paired.Add(next);
                        Place(finish.wall2m, parent, Between(layout, cell, next), straightX ? 0f : 90f);
                    }
                    else Place(finish.wall1m, parent, Center(layout, cell), straightX ? 0f : 90f);
                    continue;
                }
                if (linked.Count == 0)
                {
                    Place(finish.wall1m, parent, Center(layout, cell), 0f);
                    continue;
                }
                Place(finish.end, parent, Center(layout, cell), 0f);
                foreach (var (dx, dz) in halves)
                {
                    var half = Place(finish.wall1m, parent, Center(layout, cell) + new Vector3(dx, 0f, dz) * (SiteGrid.CellSize * 0.25f), dx != 0 ? 0f : 90f);
                    if (half != null) half.transform.localScale = new Vector3(0.5f, 1f, 1f);
                }
            }

            // Doors: a single doorway, or a double one over two door cells in line.
            var doorsDone = new HashSet<(int, int)>();
            foreach (var cell in cells.Where(IsDoor).OrderBy(c => c.X).ThenBy(c => c.Z))
            {
                if (doorsDone.Contains(cell)) continue;
                doorsDone.Add(cell);
                var alongX = cells.Contains((cell.X + 1, cell.Z)) || cells.Contains((cell.X - 1, cell.Z));
                var next = alongX ? (cell.X + 1, cell.Z) : (cell.X, cell.Z + 1);
                var record = building.Structures.FirstOrDefault(s => s.Kind == GoodsWorld.DoorStructure && s.X == cell.X && s.Z == cell.Z);
                var leaf = catalog.Leaf(record?.Style)?.leaf;
                // A back door (decision 0037) never opens for customers.
                var service = record?.Role == GoodsWorld.ServiceDoorRole;
                var yaw = alongX ? 0f : 90f;
                if (cells.Contains(next) && IsDoor(next) && !doorsDone.Contains(next))
                {
                    doorsDone.Add(next);
                    var center = Between(layout, cell, next);
                    Place(catalog.DoubleDoorway, parent, center, yaw);
                    Place(catalog.DoubleFrame, parent, center, yaw);
                    var rotation = Quaternion.Euler(0f, yaw, 0f);
                    Swing(Place(leaf, parent, center + rotation * new Vector3(DoubleHinge, 0.005f, 0f), yaw), parent, center, service);
                    Swing(Place(leaf, parent, center + rotation * new Vector3(-DoubleHinge, 0.005f, 0f), yaw + 180f), parent, center, service);
                }
                else
                {
                    var center = Center(layout, cell);
                    Place(catalog.SingleDoorway, parent, center, yaw);
                    Place(catalog.SingleFrame, parent, center, yaw);
                    Swing(Place(leaf, parent, center + Quaternion.Euler(0f, yaw, 0f) * new Vector3(SingleHinge, 0.005f, 0f), yaw), parent, center, service);
                }
            }

            // Windows span their two cells.
            foreach (var window in building.Structures.Where(s => s.Kind == GoodsWorld.WindowStructure))
            {
                var style = catalog.Window(window.Style);
                if (style == null) continue;
                var second = window.Axis == 0 ? (window.X + 1, window.Z) : (window.X, window.Z + 1);
                var center = Between(layout, (window.X, window.Z), second);
                var yaw = window.Axis == 0 ? 0f : 90f;
                Place(style.bay, parent, center, yaw);
                if (style.window != null) Place(style.window, parent, center + Vector3.up * SillHeight, yaw);
            }
        }

        private static void Swing(GameObject leaf, Transform parent, Vector3 doorway, bool service)
        {
            if (leaf == null) return;
            leaf.AddComponent<DoorSwing>().Init(parent.TransformPoint(doorway), service);
        }

        private static Vector3 Center(SiteLayout layout, (int X, int Z) cell) => SiteGridSpace.FootprintCenter(layout, cell.X, cell.Z, 1, 1);

        private static Vector3 Between(SiteLayout layout, (int X, int Z) a, (int X, int Z) b) => (Center(layout, a) + Center(layout, b)) * 0.5f;

        private static GameObject Place(GameObject prefab, Transform parent, Vector3 position, float yaw)
        {
            if (prefab == null) return null;
            var instance = Object.Instantiate(prefab, parent, false);
            instance.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return instance;
        }
    }
}
