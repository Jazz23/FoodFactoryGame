// Pure restaurant rules of decision 0034, shared by the server simulation and the client's build-mode preview.
// Walking: a site's ground cells are walkable unless a wall (perimeter, interior wall or window) or a placed object-layer piece
// covers them; decor on the floor, walls, ceiling or tables never blocks. A piece is reached when a walkable cell beside its
// footprint can be walked to from a source. Customers come from the street edge of a lot (any edge of a dev site) and never pass
// a back door (decision 0037); trucks stop on the street edge. A restaurant dock stands beside a back door's doorstep;
// no other object-layer piece and no belt covers that doorstep.
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
        // piece about to be placed). For customers (decision 0037) back doors are walls too.
        public static bool[,] Walkable(GoodsSnapshot state, string siteId, (int X, int Z, int Width, int Depth)? block = null, bool customers = false)
        {
            var layout = state.SiteLayouts?.FirstOrDefault(x => x.SiteId == siteId);
            if (layout == null) return null;
            var open = new bool[layout.Width, layout.Depth];
            var buildings = state.Buildings?.Where(x => x.SiteId == siteId).ToList() ?? new List<GoodsBuilding>();
            for (var x = 0; x < layout.Width; x++)
            for (var z = 0; z < layout.Depth; z++)
                open[x, z] = !buildings.Any(b => SiteGrid.IsWall(b, x, z));
            if (customers)
                foreach (var (x, z) in buildings.SelectMany(SiteGrid.ServiceDoors))
                    if (x >= 0 && z >= 0 && x < layout.Width && z < layout.Depth) open[x, z] = false;
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

        // The cells customers reach on a site: from the lot's street edge (every edge of a dev site), never through a back door
        // (decision 0037). The customer simulation and the client's readiness readout both use this.
        public static HashSet<(int X, int Z)> CustomerReach(GoodsSnapshot state, string siteId, PropertyOffer offer) =>
            Reached(Walkable(state, siteId, customers: true), StreetCells(state.SiteLayouts.FirstOrDefault(x => x.SiteId == siteId), offer));

        // A register or table serves customers when it stands on the ground floor beside a cell they reach (decision 0034).
        public static bool ServesCustomers(HashSet<(int X, int Z)> reach, GoodsEquipment piece) => piece.Level == 0 && Touches(reach, piece);

        // A sale recipe sold at registers (decision 0024): the restaurant's menu.
        public static bool IsMenuItem(RecipeDefinition recipe) => recipe != null && recipe.IsSale && recipe.StationKind == GoodsWorld.CounterKind;

        // A site holds a restaurant when one of its shells is a restaurant; its docks follow the restaurant dock rules.
        public static bool IsRestaurantSite(GoodsSnapshot state, string siteId) =>
            state.Buildings?.Any(x => x.SiteId == siteId && x.Kind == GoodsWorld.RestaurantKind) == true;

        // Restaurant placement rules on top of SiteGrid's (decisions 0034, 0037): an object-layer piece may not cover a back door's
        // doorstep ("doorstep"), and a dock must touch one and be reachable from the street (DockProblem).
        public static string PlacementProblem(GoodsSnapshot state, GoodsEquipment piece, int cellX, int cellZ, int rotation, PropertyOffer offer) =>
            DoorstepProblem(state, piece, cellX, cellZ, rotation) ?? DockProblem(state, piece, cellX, cellZ, rotation, offer);

        // Null unless an object-layer piece other than a dock would cover the doorstep of a back door on its site ("doorstep"): the
        // doorstep stays clear so goods can come in. Docks have their own rule; decor layers never block.
        public static string DoorstepProblem(GoodsSnapshot state, GoodsEquipment piece, int cellX, int cellZ, int rotation)
        {
            if (piece.Kind == GoodsWorld.DockKind || !string.IsNullOrEmpty(piece.Layer) || !IsRestaurantSite(state, piece.SiteId)) return null;
            var (width, depth) = SiteGrid.Footprint(piece.Width, piece.Depth, rotation);
            return Doorsteps(state, piece.SiteId).Any(c => SiteGrid.Overlaps(c.X, c.Z, 1, 1, cellX, cellZ, width, depth)) ? "doorstep" : null;
        }

        // Null unless a belt or lift with an end on the ground at the cell would cover the doorstep of a back door on a restaurant
        // site ("doorstep", owner decision 2026-10-06).
        public static string BeltProblem(GoodsSnapshot state, string siteId, int cellX, int cellZ, int level, int lift = 0) =>
            (level == 0 || level + lift == 0) && IsRestaurantSite(state, siteId) && Doorsteps(state, siteId).Contains((cellX, cellZ)) ? "doorstep" : null;

        // True when a placed object-layer piece (a dock included) or a belt with an end on the ground covers the cell.
        public static bool Covered(GoodsSnapshot state, string siteId, int cellX, int cellZ)
        {
            foreach (var piece in state.Equipment.Where(x => x.SiteId == siteId && x.State == EquipmentState.Placed && x.Level == 0 && string.IsNullOrEmpty(x.Layer)))
            {
                var (width, depth) = SiteGrid.Footprint(piece.Width, piece.Depth, piece.Rotation);
                if (SiteGrid.Overlaps(cellX, cellZ, 1, 1, piece.CellX, piece.CellZ, width, depth)) return true;
            }
            return (state.Belts ?? new List<GoodsBelt>()).Any(x => x.SiteId == siteId && x.CellX == cellX && x.CellZ == cellZ && (x.Level == 0 || x.ExitLevel == 0));
        }

        // Null unless a dock on a restaurant site would stand where it does not touch a back door's doorstep
        // ("not-beside-back-door", decision 0037: wholly outside every interior, edge to edge with an open doorstep it leaves
        // clear), or where no walkable path reaches it from the lot's street edge ("no-street-access"). Any other piece, and docks
        // elsewhere (factories, warehouses), are unaffected. Docks placed before decision 0037 keep working where they stand.
        public static string DockProblem(GoodsSnapshot state, GoodsEquipment dock, int cellX, int cellZ, int rotation, PropertyOffer offer)
        {
            if (dock.Kind != GoodsWorld.DockKind || !IsRestaurantSite(state, dock.SiteId)) return null;
            var (width, depth) = SiteGrid.Footprint(dock.Width, dock.Depth, rotation);
            var layout = state.SiteLayouts.FirstOrDefault(x => x.SiteId == dock.SiteId);
            if (!state.Buildings.Where(x => x.SiteId == dock.SiteId && x.Kind == GoodsWorld.RestaurantKind)
                    .Any(b => BesideBackDoor(state, b, layout, cellX, cellZ, width, depth)))
                return "not-beside-back-door";
            var walkable = Walkable(state, dock.SiteId, (cellX, cellZ, width, depth));
            var reached = Reached(walkable, StreetCells(layout, offer));
            return Touches(reached, cellX, cellZ, width, depth) ? null : "no-street-access";
        }

        // True when a footprint lies wholly outside the building's interior and touches, edge to edge without covering it, the
        // doorstep of one of its back doors that is open ground on the site.
        public static bool BesideBackDoor(GoodsSnapshot state, GoodsBuilding building, SiteLayout layout, int cellX, int cellZ, int width, int depth)
        {
            if (layout == null) return false;
            for (var x = cellX; x < cellX + width; x++)
            for (var z = cellZ; z < cellZ + depth; z++)
                if (SiteGrid.IsInterior(building, x, z)) return false;
            foreach (var door in SiteGrid.ServiceDoors(building))
            {
                if (SiteGrid.Doorstep(building, door.X, door.Z) is not { } step || !Open(state, building, layout, step.X, step.Z)) continue;
                if (SiteGrid.Overlaps(step.X, step.Z, 1, 1, cellX, cellZ, width, depth)) continue;
                var alongX = step.X >= cellX && step.X < cellX + width;
                var alongZ = step.Z >= cellZ && step.Z < cellZ + depth;
                if ((alongX && (step.Z == cellZ - 1 || step.Z == cellZ + depth)) || (alongZ && (step.X == cellX - 1 || step.X == cellX + width))) return true;
            }
            return false;
        }

        public static bool BesideBackDoor(GoodsSnapshot state, GoodsBuilding building, GoodsEquipment dock)
        {
            var (width, depth) = SiteGrid.Footprint(dock.Width, dock.Depth, dock.Rotation);
            return BesideBackDoor(state, building, state.SiteLayouts.FirstOrDefault(x => x.SiteId == dock.SiteId), dock.CellX, dock.CellZ, width, depth);
        }

        // A placed restaurant dock that touches a back door's doorstep (decision 0037); docks placed before the rule may not.
        public static bool BesideBackDoor(GoodsSnapshot state, GoodsEquipment dock) =>
            state.Buildings.Any(b => b.SiteId == dock.SiteId && b.Kind == GoodsWorld.RestaurantKind && BesideBackDoor(state, b, dock));

        // The doorsteps of a site's back doors that lie on the site grid.
        public static List<(int X, int Z)> Doorsteps(GoodsSnapshot state, string siteId)
        {
            var layout = state.SiteLayouts?.FirstOrDefault(x => x.SiteId == siteId);
            var steps = new List<(int X, int Z)>();
            if (layout == null) return steps;
            foreach (var building in state.Buildings.Where(x => x.SiteId == siteId))
                foreach (var door in SiteGrid.ServiceDoors(building))
                    if (SiteGrid.Doorstep(building, door.X, door.Z) is { } step && step.X >= 0 && step.Z >= 0 && step.X < layout.Width && step.Z < layout.Depth)
                        steps.Add(step);
            return steps;
        }

        // A doorstep is open ground when it lies on the grid and no wall of any of the site's buildings stands there.
        private static bool Open(GoodsSnapshot state, GoodsBuilding building, SiteLayout layout, int x, int z) =>
            x >= 0 && z >= 0 && x < layout.Width && z < layout.Depth && !SiteGrid.WallAt(state, building.SiteId, x, z);

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
