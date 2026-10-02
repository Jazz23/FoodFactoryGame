// Pure restaurant rules of decision 0034, shared by the server simulation and the client's build-mode preview.
// Walking: a site's ground cells are walkable unless a wall (perimeter, interior wall or window) or a placed object-layer piece
// covers them; decor on the floor, walls, ceiling or tables never blocks. A piece is reached when a walkable cell beside its
// footprint can be walked to from a source. Customers come from any edge of the site; trucks stop on the street edge of a lot.
// Ambience: one score per restaurant from its placed pieces' ambience points, with diminishing returns and a cap.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodFactoryGame.Goods
{
    public static class RestaurantRules
    {
        // PROTOTYPE (decision 0034 PROPOSAL "Ambience", weights open): score = Cap * (1 - exp(-points / Scale)), rounded; it adds
        // Weight * score / Cap to a customer's score for the restaurant.
        public const int AmbienceCap = 100;
        public const double AmbienceScale = 50.0;
        public const double AmbienceWeight = 0.6;

        // Walkable ground cells of a site, indexed [x, z]; null without a layout. block marks one more footprint as taken (a
        // piece about to be placed).
        public static bool[,] Walkable(GoodsSnapshot state, string siteId, (int X, int Z, int Width, int Depth)? block = null)
        {
            var layout = state.SiteLayouts?.FirstOrDefault(x => x.SiteId == siteId);
            if (layout == null) return null;
            var open = new bool[layout.Width, layout.Depth];
            var buildings = state.Buildings?.Where(x => x.SiteId == siteId).ToList() ?? new List<GoodsBuilding>();
            for (var x = 0; x < layout.Width; x++)
            for (var z = 0; z < layout.Depth; z++)
                open[x, z] = !buildings.Any(b => SiteGrid.IsWall(b, x, z));
            void Close(int cellX, int cellZ, int width, int depth)
            {
                for (var x = Math.Max(0, cellX); x < Math.Min(layout.Width, cellX + width); x++)
                for (var z = Math.Max(0, cellZ); z < Math.Min(layout.Depth, cellZ + depth); z++)
                    open[x, z] = false;
            }
            foreach (var piece in state.Equipment.Where(x => x.SiteId == siteId && x.State == EquipmentState.Placed && x.Level == 0 && string.IsNullOrEmpty(x.Layer)))
            {
                var (width, depth) = SiteGrid.Footprint(piece.Width, piece.Depth, piece.Rotation);
                Close(piece.CellX, piece.CellZ, width, depth);
            }
            if (block is { } extra) Close(extra.X, extra.Z, extra.Width, extra.Depth);
            return open;
        }

        // Every walkable cell reachable (four-way) from the walkable source cells.
        public static HashSet<(int X, int Z)> Reached(bool[,] walkable, IEnumerable<(int X, int Z)> sources)
        {
            var reached = new HashSet<(int X, int Z)>();
            if (walkable == null) return reached;
            var width = walkable.GetLength(0);
            var depth = walkable.GetLength(1);
            var queue = new Queue<(int X, int Z)>();
            foreach (var cell in sources)
                if (cell.X >= 0 && cell.Z >= 0 && cell.X < width && cell.Z < depth && walkable[cell.X, cell.Z] && reached.Add(cell)) queue.Enqueue(cell);
            while (queue.Count > 0)
            {
                var (x, z) = queue.Dequeue();
                foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                    if (nx >= 0 && nz >= 0 && nx < width && nz < depth && walkable[nx, nz] && reached.Add((nx, nz))) queue.Enqueue((nx, nz));
            }
            return reached;
        }

        // The cells along every edge of a site: where customers walk in from.
        public static IEnumerable<(int X, int Z)> EdgeCells(SiteLayout layout)
        {
            if (layout == null) yield break;
            for (var x = 0; x < layout.Width; x++)
            {
                yield return (x, 0);
                yield return (x, layout.Depth - 1);
            }
            for (var z = 1; z < layout.Depth - 1; z++)
            {
                yield return (0, z);
                yield return (layout.Width - 1, z);
            }
        }

        // PROTOTYPE (decision 0034 PROPOSAL "Dock rule"): the lot's edge on its street access side, where a truck stops. A site
        // without a listed lot (dev sites) has no known street, so every edge counts.
        public static IEnumerable<(int X, int Z)> StreetCells(SiteLayout layout, PropertyOffer offer)
        {
            if (layout == null) return Enumerable.Empty<(int, int)>();
            if (offer == null) return EdgeCells(layout);
            if (offer.AccessZ >= offer.LotZ + offer.Depth) return Enumerable.Range(0, layout.Width).Select(x => (x, layout.Depth - 1));
            if (offer.AccessZ < offer.LotZ) return Enumerable.Range(0, layout.Width).Select(x => (x, 0));
            if (offer.AccessX < offer.LotX) return Enumerable.Range(0, layout.Depth).Select(z => (0, z));
            return Enumerable.Range(0, layout.Depth).Select(z => (layout.Width - 1, z));
        }

        // True when a walkable cell beside the footprint was reached.
        public static bool Touches(HashSet<(int X, int Z)> reached, int cellX, int cellZ, int width, int depth)
        {
            for (var x = cellX; x < cellX + width; x++)
                if (reached.Contains((x, cellZ - 1)) || reached.Contains((x, cellZ + depth))) return true;
            for (var z = cellZ; z < cellZ + depth; z++)
                if (reached.Contains((cellX - 1, z)) || reached.Contains((cellX + width, z))) return true;
            return false;
        }

        public static bool Touches(HashSet<(int X, int Z)> reached, GoodsEquipment piece)
        {
            var (width, depth) = SiteGrid.Footprint(piece.Width, piece.Depth, piece.Rotation);
            return Touches(reached, piece.CellX, piece.CellZ, width, depth);
        }

        // A site holds a restaurant when one of its shells is a restaurant; its docks follow the restaurant dock rules.
        public static bool IsRestaurantSite(GoodsSnapshot state, string siteId) =>
            state.Buildings?.Any(x => x.SiteId == siteId && x.Kind == GoodsWorld.RestaurantKind) == true;

        // Null unless a dock on a restaurant site would stand where no walkable path reaches it from the lot's street edge
        // ("no-street-access"). Any other piece, and docks elsewhere (factories, warehouses), are unaffected.
        public static string DockProblem(GoodsSnapshot state, GoodsEquipment dock, int cellX, int cellZ, int rotation, PropertyOffer offer)
        {
            if (dock.Kind != GoodsWorld.DockKind || !IsRestaurantSite(state, dock.SiteId)) return null;
            var (width, depth) = SiteGrid.Footprint(dock.Width, dock.Depth, rotation);
            var walkable = Walkable(state, dock.SiteId, (cellX, cellZ, width, depth));
            var reached = Reached(walkable, StreetCells(state.SiteLayouts.FirstOrDefault(x => x.SiteId == dock.SiteId), offer));
            return Touches(reached, cellX, cellZ, width, depth) ? null : "no-street-access";
        }

        // True when a walkable path reaches a placed dock from its lot's street edge (any site; restaurants require it).
        public static bool ReachesStreet(GoodsSnapshot state, GoodsEquipment dock, PropertyOffer offer) =>
            Touches(Reached(Walkable(state, dock.SiteId), StreetCells(state.SiteLayouts.FirstOrDefault(x => x.SiteId == dock.SiteId), offer)), dock);

        // One restaurant's ambience score from the ambience points of everything placed on its site.
        public static int Ambience(GoodsSnapshot state, string siteId)
        {
            var points = state.Equipment.Where(x => x.SiteId == siteId && x.State == EquipmentState.Placed).Sum(x => (long)Math.Max(0, x.Ambience));
            return (int)Math.Round(AmbienceCap * (1.0 - Math.Exp(-points / AmbienceScale)));
        }
    }
}
