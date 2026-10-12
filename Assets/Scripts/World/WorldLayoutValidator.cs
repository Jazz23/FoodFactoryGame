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
                         .Concat(layout.Rails.Select(x => x.Id)).Concat(layout.Buildings.Select(x => x.Id)).Concat(layout.Rivers.Select(x => x.Id))
                         .Concat(layout.Bridges.Select(x => x.Id)).Concat(layout.Crossings.Select(x => x.Id)).Concat(layout.Lots.Select(x => x.Id)))
                if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsWhiteSpace) || !ids.Add(id)) problems.Add($"id: blank, spaced or duplicate '{id}'");

            Districts(layout, problems);
            var nodes = layout.Nodes.GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var roadIndex = Roads(layout, nodes, problems);
            var railIndex = Rails(layout, problems);
            Buildings(layout, settings, roadIndex, railIndex, problems);
            Lots(layout, roadIndex, railIndex, problems);
            Access(layout, settings, nodes, roadIndex, problems);
            Start(layout, settings, problems);
            FarmsAndStations(layout, problems);
            if (layout.FormatVersion >= 2)
            {
                Land(layout, settings, problems);
                Controls(layout, problems);
                Rivers(layout, nodes, problems);
                LevelCrossings(layout, nodes, problems);
                Trees(layout, nodes, roadIndex, railIndex, problems);
            }

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

        // Decision 0028: exactly one lot per property (format 3; none in formats 1 and 2) and none for scenery, with IDs derived
        // from the building so they stay stable. A lot holds its building and overlaps no other lot or building, road, rail line
        // (or water, in Rivers); its access cell borders its edge on a road (reachability is checked in Access).
        private static void Lots(WorldLayout layout, BoxIndex<RoadSegment> roads, BoxIndex<RailLine> rails, List<string> problems)
        {
            var buildings = layout.Buildings.GroupBy(x => x.Id ?? "").ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
            var footprints = new BoxIndex<WorldBuilding>();
            foreach (var building in layout.Buildings) footprints.Add(WorldGeometry.Of(building), building);
            var byBuilding = new Dictionary<string, WorldLot>(StringComparer.Ordinal);
            var sites = new HashSet<string>(StringComparer.Ordinal);
            var placed = new BoxIndex<WorldLot>();
            if (layout.FormatVersion < 3 && layout.Lots.Count > 0) problems.Add("lot: formats 1 and 2 have no lots");
            foreach (var lot in layout.Lots)
            {
                var name = $"lot {lot.Id}";
                if (!buildings.TryGetValue(lot.BuildingId ?? "", out var building) || !building.HasLot)
                {
                    problems.Add($"{name}: not the lot of a property");
                    continue;
                }
                if (!byBuilding.TryAdd(building.Id, lot)) problems.Add($"{name}: {building.Id} has more than one lot");
                if (lot.Id != WorldLot.IdFor(building.Id) || lot.SiteId != WorldLot.SiteIdFor(building.Id)) problems.Add($"{name}: IDs not derived from its building");
                if (!sites.Add(lot.SiteId ?? "")) problems.Add($"{name}: duplicate site ID '{lot.SiteId}'");
                var rect = lot.Rect;
                var box = WorldGeometry.Of(rect);
                if (lot.Width < 1 || lot.Depth < 1 || !WorldGeometry.Contains(rect, building.Footprint)) problems.Add($"{name}: does not contain its building");
                if (roads.Any(box)) problems.Add($"{name}: overlaps a road");
                if (rails.Any(box)) problems.Add($"{name}: overlaps a rail line");
                foreach (var other in placed.Overlapping(box)) problems.Add($"{name}: overlaps {other.Id}");
                foreach (var other in footprints.Overlapping(box).Where(x => x != building)) problems.Add($"{name}: overlaps building {other.Id}");
                placed.Add(box, lot);
                if (lot.Access == null || !Borders(rect, lot.Access)) problems.Add($"{name}: access is not a cell beside its edge");
                else if (!roads.TryFind(2 * lot.Access.X + 1, 2 * lot.Access.Z + 1, out _)) problems.Add($"{name}: access is not on a road");
            }
            foreach (var building in layout.Buildings)
            {
                byBuilding.TryGetValue(building.Id ?? "", out var lot);
                if (layout.FormatVersion >= 3 && building.HasLot && lot == null) problems.Add($"building {building.Id}: property without a lot");
                if ((building.SiteId ?? "") != (lot?.SiteId ?? "")) problems.Add($"building {building.Id}: site ID must be its lot's, and empty without one");
                ServiceYard(layout, building, lot, problems);
            }
        }

        // Decision 0037 (format 4): every restaurant property has a service yard inside its lot and outside its shell that reaches
        // the lot's street edge; its back door is a non-corner wall cell on the yard side; the doorstep outside it and the 2 x 1
        // starter dock lie in the yard, the dock touching the doorstep edge to edge without covering it. Nothing else has a yard,
        // and formats before 4 have none.
        private static void ServiceYard(WorldLayout layout, WorldBuilding building, WorldLot lot, List<string> problems)
        {
            var name = $"building {building.Id}";
            var wanted = layout.FormatVersion >= 4 && building.Category == BuildingCategory.Restaurant && building.HasLot;
            var has = building.ServiceYard != null || building.BackDoor != null || building.ServiceDock != null;
            if (!wanted)
            {
                if (has) problems.Add($"{name}: only a format 4 restaurant property has a service yard");
                return;
            }
            if (building.ServiceYard == null || building.BackDoor == null || building.ServiceDock == null || lot == null)
            {
                problems.Add($"{name}: restaurant without a service yard, back door and dock");
                return;
            }
            var yard = building.ServiceYard;
            var dock = building.ServiceDock;
            var footprint = building.Footprint;
            var doorstep = WorldGeometry.Doorstep(footprint, building.BackDoor);
            if (!WorldGeometry.Contains(lot.Rect, yard) || Overlaps(yard, footprint)) problems.Add($"{name}: service yard not in its lot beside the shell");
            if (doorstep == null || building.Doors.Any(x => x.X == building.BackDoor.X && x.Z == building.BackDoor.Z))
                problems.Add($"{name}: back door is not a free non-corner wall cell");
            else if (!yard.Contains(doorstep.X, doorstep.Z)) problems.Add($"{name}: back door does not open into the service yard");
            if (!(dock.Width == 2 && dock.Depth == 1 || dock.Width == 1 && dock.Depth == 2) || !WorldGeometry.Contains(yard, dock))
                problems.Add($"{name}: starter dock is not a 2 x 1 piece in the yard");
            else if (doorstep != null && (dock.Contains(doorstep.X, doorstep.Z) || !Borders(dock, doorstep)))
                problems.Add($"{name}: starter dock does not touch the back door's doorstep");
            // The yard runs to the lot's street edge, so trucks and players reach the dock from the street along it.
            var street = WorldGeometry.Step(building.Facing);
            var reaches = street.X > 0 ? yard.X + yard.Width == lot.X + lot.Width : street.X < 0 ? yard.X == lot.X
                : street.Z > 0 ? yard.Z + yard.Depth == lot.Z + lot.Depth : yard.Z == lot.Z;
            if (!reaches) problems.Add($"{name}: service yard does not reach the street edge");
        }

        private static bool Overlaps(WorldRect a, WorldRect b) =>
            a.X < b.X + b.Width && b.X < a.X + a.Width && a.Z < b.Z + b.Depth && b.Z < a.Z + a.Depth;

        // A cell just outside the rectangle, sharing an edge with it (not a corner).
        private static bool Borders(WorldRect rect, WorldCell cell)
        {
            var alongX = cell.X >= rect.X && cell.X < rect.X + rect.Width;
            var alongZ = cell.Z >= rect.Z && cell.Z < rect.Z + rect.Depth;
            return (alongX && (cell.Z == rect.Z - 1 || cell.Z == rect.Z + rect.Depth)) || (alongZ && (cell.X == rect.X - 1 || cell.X == rect.X + rect.Width));
        }

        private static WorldDistrict DistrictAt(WorldLayout layout, int doubledX, int doubledZ) =>
            layout.Districts.FirstOrDefault(x => x.Areas.Any(a =>
                doubledX >= 2 * a.X && doubledX < 2 * (a.X + a.Width) && doubledZ >= 2 * a.Z && doubledZ < 2 * (a.Z + a.Depth)));

        // The road a building's door opens onto: stepping straight out of a door cell must reach pavement within its setback.
        public static RoadSegment AccessRoad(WorldBuilding building, WorldSettings settings, Func<int, int, RoadSegment> roadAt)
        {
            var (dx, dz) = WorldGeometry.Step(building.Facing);
            foreach (var door in building.Doors)
                for (var step = 1; step <= settings.SetbackFor(building.Category) + 2; step++)
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
            foreach (var lot in layout.Lots.Where(x => x.Access != null))
                if (roads.TryFind(2 * lot.Access.X + 1, 2 * lot.Access.Z + 1, out var road) && !reached.Contains(road.FromId))
                    problems.Add($"access: lot {lot.Id} is not reachable by road");
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

        // ------------------------------------------------------------------ format 2: land, rivers, crossings, junctions, trees

        // The land covers the map; every building's entrance is at land height, and every footprint but a farm's is near level.
        private static void Land(WorldLayout layout, WorldSettings settings, List<string> problems)
        {
            var terrain = layout.Terrain;
            if (terrain == null || terrain.IsFlat || terrain.Spacing * (terrain.Samples - 1) != 2 * layout.MapHalfSize
                || terrain.HeightsCm.Length != terrain.Samples * terrain.Samples)
            {
                problems.Add("land: the terrain must cover the map");
                return;
            }
            foreach (var building in layout.Buildings)
            {
                if (building.Doors.Count == 0) continue;
                var level = terrain.CellHeightCm(building.Doors[0].X, building.Doors[0].Z);
                if (building.ElevationCm != level) problems.Add($"building {building.Id}: elevation is not the land height at its entrance");
                if (building.Category == BuildingCategory.Farm) continue;
                var x0 = 2 * building.X;
                var z0 = 2 * building.Z;
                var x1 = 2 * (building.X + building.Width);
                var z1 = 2 * (building.Z + building.Depth);
                foreach (var h in new[] { terrain.HeightCm(x0, z0), terrain.HeightCm(x1, z0), terrain.HeightCm(x0, z1), terrain.HeightCm(x1, z1) })
                    if (Math.Abs(h - level) > settings.MaxFootprintRiseCm)
                    {
                        problems.Add($"building {building.Id}: stands on land too steep");
                        break;
                    }
            }
        }

        private static Dictionary<string, List<RoadSegment>> SegmentsByNode(WorldLayout layout)
        {
            var result = new Dictionary<string, List<RoadSegment>>(StringComparer.Ordinal);
            foreach (var road in layout.Roads)
                foreach (var id in new[] { road.FromId ?? "", road.ToId ?? "" })
                {
                    if (!result.TryGetValue(id, out var list)) result[id] = list = new List<RoadSegment>();
                    list.Add(road);
                }
            return result;
        }

        // A junction (three or more segments) is controlled; a bend or a straight join is not.
        private static void Controls(WorldLayout layout, List<string> problems)
        {
            var segments = SegmentsByNode(layout);
            foreach (var node in layout.Nodes)
            {
                var degree = segments.TryGetValue(node.Id, out var list) ? list.Count : 0;
                if ((degree >= 3) != (node.Control != JunctionControl.None)) problems.Add($"node {node.Id}: control {node.Control} at {degree} segments");
            }
        }

        private static (bool Vertical, int Fixed, int From, int To, int Width)? Line(RoadSegment road, Dictionary<string, RoadNode> nodes)
        {
            if (!nodes.TryGetValue(road.FromId ?? "", out var a) || !nodes.TryGetValue(road.ToId ?? "", out var b)) return null;
            return a.X == b.X ? (true, a.X, Math.Min(a.Z, b.Z), Math.Max(a.Z, b.Z), road.Width) : (false, a.Z, Math.Min(a.X, b.X), Math.Max(a.X, b.X), road.Width);
        }

        private static IEnumerable<(string Id, bool Vertical, int Fixed, int From, int To, int Width)> RailPieces(WorldLayout layout)
        {
            foreach (var line in layout.Rails)
                for (var index = 0; index + 1 < line.Points.Count; index++)
                {
                    var a = line.Points[index];
                    var b = line.Points[index + 1];
                    yield return a.X == b.X
                        ? (line.Id, true, a.X, Math.Min(a.Z, b.Z), Math.Max(a.Z, b.Z), line.Width)
                        : (line.Id, false, a.Z, Math.Min(a.X, b.X), Math.Max(a.X, b.X), line.Width);
                }
        }

        // Rivers stay on the map and off buildings; wherever a road or rail line crosses one, a bridge on that way spans it.
        private static void Rivers(WorldLayout layout, Dictionary<string, RoadNode> nodes, List<string> problems)
        {
            var m = layout.MapHalfSize;
            var ways = layout.Roads.Select(x => (x.Id, Line: Line(x, nodes))).Where(x => x.Line != null)
                .Select(x => (x.Id, x.Line.Value.Vertical, x.Line.Value.Fixed, x.Line.Value.From, x.Line.Value.To, x.Line.Value.Width))
                .Concat(RailPieces(layout)).ToList();
            foreach (var river in layout.Rivers)
            {
                if (river.Width <= 0 || river.Width % 2 != 0 || river.SurfaceDropCm <= 0 || river.Points.Count < 2
                    || river.Points.Any(p => Math.Abs(p.X) > m || Math.Abs(p.Z) > m)
                    || river.Points.Zip(river.Points.Skip(1), (a, b) => a.X == b.X && a.Z == b.Z).Any(x => x))
                {
                    problems.Add($"river {river.Id}: invalid line");
                    continue;
                }
                var half = river.Width / 2;
                var water = new BoxIndex<int>();
                foreach (var box in WorldGeometry.Corridor(river.Points, half)) water.Add(box, 0);
                foreach (var building in layout.Buildings.Where(b => water.Any(WorldGeometry.Of(b))))
                    problems.Add($"building {building.Id}: stands in river {river.Id}");
                foreach (var lot in layout.Lots.Where(l => water.Any(WorldGeometry.Of(l.Rect))))
                    problems.Add($"lot {lot.Id}: lies in river {river.Id}");
                foreach (var way in ways)
                    foreach (var (along, _, _) in WorldGeometry.Crossings(river.Points, way.Vertical, way.Fixed, way.From, way.To))
                    {
                        var covered = layout.Bridges.Any(b => b.CarriesId == way.Id && b.RiverId == river.Id
                            && (way.Vertical ? b.From.X == way.Fixed && b.To.X == way.Fixed : b.From.Z == way.Fixed && b.To.Z == way.Fixed)
                            && Math.Min(way.Vertical ? b.From.Z : b.From.X, way.Vertical ? b.To.Z : b.To.X) <= along - half
                            && Math.Max(way.Vertical ? b.From.Z : b.From.X, way.Vertical ? b.To.Z : b.To.X) >= along + half);
                        if (!covered) problems.Add($"river {river.Id}: {way.Id} crosses it without a bridge");
                    }
            }
            var rivers = new HashSet<string>(layout.Rivers.Select(x => x.Id), StringComparer.Ordinal);
            foreach (var bridge in layout.Bridges)
            {
                var onWay = ways.Any(x => x.Id == bridge.CarriesId && (x.Vertical
                    ? bridge.From.X == x.Fixed && bridge.To.X == x.Fixed && Math.Min(bridge.From.Z, bridge.To.Z) >= x.From && Math.Max(bridge.From.Z, bridge.To.Z) <= x.To
                    : bridge.From.Z == x.Fixed && bridge.To.Z == x.Fixed && Math.Min(bridge.From.X, bridge.To.X) >= x.From && Math.Max(bridge.From.X, bridge.To.X) <= x.To));
                if (!onWay || !rivers.Contains(bridge.RiverId ?? "") || (bridge.From.X == bridge.To.X && bridge.From.Z == bridge.To.Z))
                    problems.Add($"bridge {bridge.Id}: not on the centreline of what it carries, or over no river");
            }
        }

        // Every at-grade meeting of a rail line and a road segment is a level crossing, and every level crossing is one.
        private static void LevelCrossings(WorldLayout layout, Dictionary<string, RoadNode> nodes, List<string> problems)
        {
            var expected = new HashSet<(string, string, int, int)>();
            foreach (var rail in RailPieces(layout))
                foreach (var road in layout.Roads)
                {
                    var line = Line(road, nodes);
                    if (line == null || line.Value.Vertical == rail.Vertical) continue;
                    var (_, fixedAt, from, to, _) = line.Value;
                    if (rail.Fixed > from && rail.Fixed < to && fixedAt > rail.From && fixedAt < rail.To)
                        expected.Add((road.Id, rail.Id, rail.Vertical ? rail.Fixed : fixedAt, rail.Vertical ? fixedAt : rail.Fixed));
                }
            var recorded = new HashSet<(string, string, int, int)>();
            foreach (var crossing in layout.Crossings)
                if (!recorded.Add((crossing.RoadId, crossing.RailId, crossing.X, crossing.Z)) || !expected.Contains((crossing.RoadId, crossing.RailId, crossing.X, crossing.Z)))
                    problems.Add($"crossing {crossing.Id}: not where {crossing.RailId} meets {crossing.RoadId}");
            foreach (var missing in expected.Where(x => !recorded.Contains(x)).OrderBy(x => x.Item1, StringComparer.Ordinal))
                problems.Add($"crossing: {missing.Item2} meets {missing.Item1} without a level crossing");
        }

        // Trees stand on the map, never in a building, on a rail line, in the water, on a carriageway or in a junction; the only
        // trees on a road are sidewalk trees within 1.5 m of a city street's edge.
        private static void Trees(WorldLayout layout, Dictionary<string, RoadNode> nodes, BoxIndex<RoadSegment> roads, BoxIndex<RailLine> rails,
            List<string> problems)
        {
            var m = layout.MapHalfSize;
            var buildings = new BoxIndex<WorldBuilding>();
            foreach (var building in layout.Buildings) buildings.Add(WorldGeometry.Of(building), building);
            // A lot is paved ground for its owner's equipment: no tree stands on one.
            foreach (var lot in layout.Lots) buildings.Add(WorldGeometry.Of(lot.Rect), null);
            var water = layout.Rivers.Select(r => (Points: r.Points.Select(p => new WorldCell(2 * p.X, 2 * p.Z)).ToList(), Radius: r.Width)).ToList();
            var bad = 0;
            string first = null;
            foreach (var tree in layout.Trees)
            {
                var x = 2 * tree.X + 1;
                var z = 2 * tree.Z + 1;
                var point = new Box(x - 1, z - 1, x + 1, z + 1);
                var wrong = Math.Abs(x) > 2 * m || Math.Abs(z) > 2 * m || tree.Scale < 25 || tree.Scale > 400
                            || buildings.Overlapping(point).Count > 0 || rails.Overlapping(point).Count > 0
                            || water.Any(r => WorldGeometry.WithinDistance(r.Points, x, z, r.Radius));
                if (!wrong)
                {
                    var on = roads.Overlapping(point);
                    foreach (var road in on)
                    {
                        var line = Line(road, nodes);
                        if (line == null) continue;
                        var offset = Math.Abs((line.Value.Vertical ? x : z) - 2 * line.Value.Fixed);
                        if (road.Kind == RoadKind.Rural || offset < road.Width - 3) wrong = true;
                    }
                    if (on.Select(r => Line(r, nodes)?.Vertical).Distinct().Count() > 1) wrong = true;
                }
                if (!wrong) continue;
                bad++;
                first ??= $"{tree.X},{tree.Z}";
            }
            if (bad > 0) problems.Add($"trees: {bad} misplaced (first at {first})");
        }
    }
}
