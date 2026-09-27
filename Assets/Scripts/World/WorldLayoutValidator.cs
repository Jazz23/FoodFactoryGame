// Independent checks of a finished layout, recomputed from its data rather than trusted from the generator: identity, road
// graph shape, shell rules (decision 0019), overlaps, road access and connectivity, the starting restaurant, farms and stations,
// and district placement (industrial on one city edge facing farmland, farms outside the city). Problems come back sorted.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodFactoryGame.World
{
    public static class WorldLayoutValidator
    {
        // A station counts as near a farm within this straight-line distance between their centres.
        public const int StationNearFarmMetres = 300;

        public static List<string> Validate(WorldLayout layout, WorldSettings settings = null)
        {
            settings ??= WorldSettings.Default;
            var problems = new List<string>();
            if (layout == null) return new List<string> { "no layout" };
            var c = layout.CityHalfSize;
            var m = layout.MapHalfSize;
            if (c <= 0 || m <= c) problems.Add("bounds: map must be larger than the city");

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in layout.Districts.Select(x => x.Id).Concat(layout.Nodes.Select(x => x.Id)).Concat(layout.Roads.Select(x => x.Id))
                         .Concat(layout.Rails.Select(x => x.Id)).Concat(layout.Buildings.Select(x => x.Id)))
                if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsWhiteSpace) || !ids.Add(id)) problems.Add($"id: blank, spaced or duplicate '{id}'");

            Districts(layout, problems);
            var nodes = layout.Nodes.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var roadIndex = Roads(layout, nodes, problems);
            var railIndex = Rails(layout, problems);
            Buildings(layout, settings, roadIndex, railIndex, problems);
            Access(layout, settings, nodes, roadIndex, problems);
            Start(layout, settings, problems);
            FarmsAndStations(layout, problems);

            problems.Sort(StringComparer.Ordinal);
            return problems;
        }

        private static void Districts(WorldLayout layout, List<string> problems)
        {
            var c = layout.CityHalfSize;
            foreach (DistrictKind kind in Enum.GetValues(typeof(DistrictKind)))
                if (layout.Districts.Count(x => x.Kind == kind) != 1) problems.Add($"district: need exactly one {kind}");
            var city = new Box(-2 * c, -2 * c, 2 * c, 2 * c);
            long area = 0;
            var all = new List<(WorldDistrict District, WorldRect Rect)>();
            foreach (var district in layout.Districts)
            {
                if (district.Areas.Count == 0) problems.Add($"district {district.Id}: no area");
                if (district.MinRecipeTier < 1 || district.CustomersPerHour < 0 || district.TrafficPercent < 0 || district.PricePercent <= 0
                    || district.Cuisines.Count == 0 || district.Cuisines.Any(x => string.IsNullOrWhiteSpace(x.Cuisine) || x.Weight <= 0))
                    problems.Add($"district {district.Id}: invalid demand values");
                foreach (var rect in district.Areas)
                {
                    var box = WorldGeometry.Of(rect);
                    if (rect.Width <= 0 || rect.Depth <= 0 || box.X0 < city.X0 || box.Z0 < city.Z0 || box.X1 > city.X1 || box.Z1 > city.Z1)
                        problems.Add($"district {district.Id}: area outside the city");
                    if (all.Any(x => WorldGeometry.Of(x.Rect).Overlaps(box))) problems.Add($"district {district.Id}: areas overlap");
                    all.Add((district, rect));
                    area += (long)rect.Width * rect.Depth;
                }
            }
            if (area != 4L * c * c) problems.Add("district: areas do not cover the city exactly");

            // Industrial must hold one whole city edge, which faces the farmland; downtown holds the centre.
            var industrial = layout.Districts.FirstOrDefault(x => x.Kind == DistrictKind.Industrial);
            if (industrial != null && industrial.Areas.Count > 0)
            {
                var side = IndustrialSide(layout);
                if (side == null) problems.Add("district: industrial does not lie along a single city edge");
                else if (all.Any(x => x.District.Kind != DistrictKind.Industrial && TouchesEdge(x.Rect, side.Value, c)))
                    problems.Add("district: a non-industrial district shares the industrial city edge");
                else if (!layout.Buildings.Any(x => x.Category == BuildingCategory.Farm && Beyond(x.Footprint, side.Value, c)))
                    problems.Add("district: no farmland beyond the industrial edge");
            }
            var downtown = layout.Districts.FirstOrDefault(x => x.Kind == DistrictKind.Downtown);
            if (downtown != null && !downtown.Areas.Any(x => WorldGeometry.Of(x).ContainsDoubled(0, 0)))
                problems.Add("district: downtown does not hold the city centre");
        }

        // The city edge that every industrial area touches, or null when there is no single such edge.
        public static Facing? IndustrialSide(WorldLayout layout)
        {
            var industrial = layout.Districts.FirstOrDefault(x => x.Kind == DistrictKind.Industrial);
            if (industrial == null || industrial.Areas.Count == 0) return null;
            Facing? found = null;
            foreach (Facing side in Enum.GetValues(typeof(Facing)))
                if (industrial.Areas.All(x => TouchesEdge(x, side, layout.CityHalfSize)))
                {
                    if (found != null) return null;
                    found = side;
                }
            return found;
        }

        private static bool TouchesEdge(WorldRect rect, Facing side, int c) => side switch
        {
            Facing.North => rect.Z + rect.Depth == c,
            Facing.South => rect.Z == -c,
            Facing.East => rect.X + rect.Width == c,
            _ => rect.X == -c
        };

        public static bool Beyond(WorldRect rect, Facing side, int c) => side switch
        {
            Facing.North => rect.Z >= c,
            Facing.South => rect.Z + rect.Depth <= -c,
            Facing.East => rect.X >= c,
            _ => rect.X + rect.Width <= -c
        };

        private static BoxIndex<RoadSegment> Roads(WorldLayout layout, Dictionary<string, RoadNode> nodes, List<string> problems)
        {
            var index = new BoxIndex<RoadSegment>();
            foreach (var road in layout.Roads)
            {
                if (!nodes.TryGetValue(road.FromId ?? "", out var from) || !nodes.TryGetValue(road.ToId ?? "", out var to))
                {
                    problems.Add($"road {road.Id}: unknown node");
                    continue;
                }
                if ((from.X != to.X && from.Z != to.Z) || (from.X == to.X && from.Z == to.Z))
                    problems.Add($"road {road.Id}: not an axis-aligned segment");
                if (road.Width <= 0 || road.Width % 2 != 0 || road.CapacityPerHour <= 0) problems.Add($"road {road.Id}: invalid width or capacity");
                if (Math.Max(Math.Max(Math.Abs(from.X), Math.Abs(from.Z)), Math.Max(Math.Abs(to.X), Math.Abs(to.Z))) > layout.MapHalfSize)
                    problems.Add($"road {road.Id}: outside the map");
                index.Add(WorldGeometry.Strip(from.X, from.Z, to.X, to.Z, road.Width), road);
            }
            return index;
        }

        private static BoxIndex<RailLine> Rails(WorldLayout layout, List<string> problems)
        {
            var index = new BoxIndex<RailLine>();
            if (layout.Rails.Count == 0) problems.Add("rail: no lines");
            foreach (var line in layout.Rails)
            {
                if (line.Points.Count < 2 || line.Width <= 0 || line.Width % 2 != 0) problems.Add($"rail {line.Id}: invalid line");
                for (var point = 0; point + 1 < line.Points.Count; point++)
                {
                    var a = line.Points[point];
                    var b = line.Points[point + 1];
                    if ((a.X != b.X && a.Z != b.Z) || (a.X == b.X && a.Z == b.Z)) problems.Add($"rail {line.Id}: not axis-aligned");
                    index.Add(WorldGeometry.Strip(a.X, a.Z, b.X, b.Z, line.Width), line);
                }
            }
            return index;
        }

        private static void Buildings(WorldLayout layout, WorldSettings settings, BoxIndex<RoadSegment> roads, BoxIndex<RailLine> rails, List<string> problems)
        {
            var c = layout.CityHalfSize;
            var m = layout.MapHalfSize;
            var city = new Box(-2 * c, -2 * c, 2 * c, 2 * c);
            var placed = new BoxIndex<WorldBuilding>();
            var lines = new HashSet<string>(layout.Rails.Select(x => x.Id), StringComparer.Ordinal);
            foreach (var building in layout.Buildings)
            {
                var name = $"building {building.Id}";
                var box = WorldGeometry.Of(building);
                var minimum = building.IsShell ? 3 : 1;
                if (building.Width < minimum || building.Depth < minimum) problems.Add($"{name}: too small");
                if (building.X < -m || building.Z < -m || building.X + building.Width > m || building.Z + building.Depth > m)
                    problems.Add($"{name}: outside the map");
                if (building.Doors.Count == 0 || (building.IsShell && building.Doors.Count < 1)) problems.Add($"{name}: no door");
                if (building.Doors.Any(x => !WorldGeometry.IsDoorOnFacing(building, x))) problems.Add($"{name}: door not a non-corner cell of the street wall");
                if (building.Doors.Select(x => (x.X, x.Z)).Distinct().Count() != building.Doors.Count) problems.Add($"{name}: duplicate door");
                if (building.IsShell != string.IsNullOrEmpty(building.ModelKey) && building.Category != BuildingCategory.Station)
                    problems.Add($"{name}: shells have no model key and premade buildings need one");
                if (building.Category == BuildingCategory.Station && (!lines.Contains(building.LineId ?? "") || !string.IsNullOrEmpty(building.ModelKey)))
                    problems.Add($"{name}: station without a rail line");
                if (building.IsShell && building.Floors != 1) problems.Add($"{name}: a generated shell has one storey");
                if (building.Floors < 1) problems.Add($"{name}: no storeys");
                var expected = building.Category switch
                {
                    BuildingCategory.Restaurant => building.Ownership == Ownership.ForSale || building.Ownership == Ownership.Competitor
                        || (building.Ownership == Ownership.Player && building.Id == layout.StartRestaurantId),
                    BuildingCategory.Factory or BuildingCategory.Farm or BuildingCategory.Station => building.Ownership == Ownership.ForSale,
                    _ => building.Ownership == Ownership.Scenery
                };
                if (!expected) problems.Add($"{name}: ownership {building.Ownership} not allowed for a {building.Category}");
                if ((building.Ownership == Ownership.Scenery) != (building.PriceCents == 0) || building.PriceCents < 0)
                    problems.Add($"{name}: price must be positive exactly for property");
                if (!string.IsNullOrEmpty(building.SiteId)) problems.Add($"{name}: site link is undecided and must stay empty");

                var district = DistrictAt(layout, 2 * building.X + building.Width, 2 * building.Z + building.Depth);
                if ((district?.Id ?? "") != (building.DistrictId ?? "")) problems.Add($"{name}: district does not match its position");
                if (building.Category == BuildingCategory.Farm ? box.Overlaps(city) : building.Category != BuildingCategory.Station && string.IsNullOrEmpty(building.DistrictId))
                    problems.Add($"{name}: farms lie outside the city and city buildings inside a district");

                if (roads.Any(box)) problems.Add($"{name}: overlaps a road");
                if (rails.Any(box)) problems.Add($"{name}: overlaps a rail line");
                foreach (var other in placed.Overlapping(box)) problems.Add($"{name}: overlaps {other.Id}");
                placed.Add(box, building);
            }
        }

        private static WorldDistrict DistrictAt(WorldLayout layout, int doubledX, int doubledZ) =>
            layout.Districts.FirstOrDefault(x => x.Areas.Any(a =>
                doubledX >= 2 * a.X && doubledX < 2 * (a.X + a.Width) && doubledZ >= 2 * a.Z && doubledZ < 2 * (a.Z + a.Depth)));

        // The road a building's door opens onto: stepping straight out of a door cell must reach pavement within the setback.
        public static RoadSegment AccessRoad(WorldBuilding building, WorldSettings settings, Func<int, int, RoadSegment> roadAt)
        {
            var (dx, dz) = WorldGeometry.Step(building.Facing);
            foreach (var door in building.Doors)
                for (var step = 1; step <= settings.Setback + 2; step++)
                {
                    var road = roadAt(2 * (door.X + dx * step) + 1, 2 * (door.Z + dz * step) + 1);
                    if (road != null) return road;
                }
            return null;
        }

        private static void Access(WorldLayout layout, WorldSettings settings, Dictionary<string, RoadNode> nodes, BoxIndex<RoadSegment> roads,
            List<string> problems)
        {
            RoadSegment RoadAt(int x, int z) => roads.TryFind(x, z, out var road) ? road : null;
            var start = layout.Buildings.FirstOrDefault(x => x.Id == layout.StartRestaurantId);
            var startRoad = start == null ? null : AccessRoad(start, settings, RoadAt);
            if (startRoad == null)
            {
                problems.Add("access: the starting restaurant has no road");
                return;
            }
            // Breadth-first over the road graph from the starting restaurant's street, in node-ID order.
            var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var road in layout.Roads.Where(x => nodes.ContainsKey(x.FromId ?? "") && nodes.ContainsKey(x.ToId ?? "")))
            {
                if (!edges.TryGetValue(road.FromId, out var a)) edges[road.FromId] = a = new List<string>();
                if (!edges.TryGetValue(road.ToId, out var b)) edges[road.ToId] = b = new List<string>();
                a.Add(road.ToId);
                b.Add(road.FromId);
            }
            var reached = new HashSet<string>(StringComparer.Ordinal) { startRoad.FromId };
            var queue = new Queue<string>();
            queue.Enqueue(startRoad.FromId);
            while (queue.Count > 0)
                foreach (var next in edges.TryGetValue(queue.Dequeue(), out var list) ? list : new List<string>())
                    if (reached.Add(next)) queue.Enqueue(next);
            foreach (var road in layout.Roads.Where(x => !reached.Contains(x.FromId ?? "")))
                problems.Add($"access: road {road.Id} is not connected to the network");
            foreach (var building in layout.Buildings)
            {
                var road = AccessRoad(building, settings, RoadAt);
                if (road == null) problems.Add($"access: building {building.Id} has no road at its door");
                else if (!reached.Contains(road.FromId)) problems.Add($"access: building {building.Id} is not reachable by road");
            }
        }

        private static void Start(WorldLayout layout, WorldSettings settings, List<string> problems)
        {
            var start = layout.Buildings.FirstOrDefault(x => x.Id == layout.StartRestaurantId);
            var residential = layout.Districts.FirstOrDefault(x => x.Kind == DistrictKind.Residential)?.Id;
            if (start == null) problems.Add("start: no starting restaurant");
            else if (start.Category != BuildingCategory.Restaurant || start.DistrictId != residential || start.Ownership != Ownership.Player
                     || start.Width * start.Depth > settings.StartMaxArea)
                problems.Add("start: must be a small player-owned restaurant shell in the residential district");
            if (layout.Buildings.Count(x => x.Ownership == Ownership.Player) != 1) problems.Add("start: exactly one player-owned building");
            var restaurants = layout.Buildings.Where(x => x.Category == BuildingCategory.Restaurant).ToList();
            if (!restaurants.Any(x => x.Ownership == Ownership.ForSale)) problems.Add("property: no restaurant for sale");
            if (!restaurants.Any(x => x.Ownership == Ownership.Competitor)) problems.Add("property: no competitor restaurant");
            if (!layout.Buildings.Any(x => x.Category == BuildingCategory.Factory)) problems.Add("property: no factory");
        }

        private static void FarmsAndStations(WorldLayout layout, List<string> problems)
        {
            var farms = layout.Buildings.Where(x => x.Category == BuildingCategory.Farm).ToList();
            var stations = layout.Buildings.Where(x => x.Category == BuildingCategory.Station).ToList();
            if (farms.Count == 0) problems.Add("farms: none");
            if (stations.Count == 0) problems.Add("stations: none");
            var limit = 4L * StationNearFarmMetres * StationNearFarmMetres;
            if (!stations.Any(s => farms.Any(f => Squared(2 * s.X + s.Width - 2 * f.X - f.Width) + Squared(2 * s.Z + s.Depth - 2 * f.Z - f.Depth) <= limit)))
                problems.Add($"stations: none within {StationNearFarmMetres} m of a farm");
        }

        private static long Squared(long value) => value * value;
    }
}
