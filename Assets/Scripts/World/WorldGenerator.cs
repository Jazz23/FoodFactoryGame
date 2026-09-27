// Seed + settings -> WorldLayout, with no Unity APIs (GDD section 3, decision 0026). Generation is deterministic: every random
// draw comes from WorldRandom streams forked from the seed, and every list is built in a fixed order. A layout that fails
// WorldLayoutValidator is discarded and generation retries with WorldRandom.DeriveSeed(seed, attempt); every attempt and its
// problems are reported. Bump Version whenever output for a given seed can change: stored worlds keep what they were given.
//
// Build steps (canonical frame, industrial to the south; the finished layout is then turned a seeded number of quarter turns):
// arterial grid (5x5 superblocks) -> districts -> local streets -> road graph -> rail lines -> stations at level crossings ->
// buildings along every block edge, doors facing the street -> farms along rural spurs -> IDs -> starting restaurant -> turn.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodFactoryGame.World
{
    public sealed class GenerationAttempt
    {
        public int Attempt;
        public ulong Seed;
        public List<string> Problems = new();
    }

    public sealed class WorldGenerationResult
    {
        public WorldLayout Layout;
        public List<GenerationAttempt> Attempts = new();
    }

    public sealed class WorldGenerationException : InvalidOperationException
    {
        public WorldGenerationException(List<GenerationAttempt> attempts)
            : base($"No valid world after {attempts.Count} attempts; last problems: {string.Join("; ", attempts[attempts.Count - 1].Problems.Take(5))}")
        {
            Attempts = attempts;
        }

        public IReadOnlyList<GenerationAttempt> Attempts { get; }
    }

    public static class WorldGenerator
    {
        public const int Version = 1;

        // extraRule adds problems of its own (tests use it to force retries); it cannot waive validator problems.
        public static WorldGenerationResult Generate(string requestedSeed, ulong seed, WorldSettings settings = null,
            Func<WorldLayout, IEnumerable<string>> extraRule = null)
        {
            settings ??= WorldSettings.Default;
            var result = new WorldGenerationResult();
            for (var attempt = 0; attempt < settings.MaxAttempts; attempt++)
            {
                var attemptSeed = WorldRandom.DeriveSeed(seed, attempt);
                var layout = Build(attemptSeed, settings);
                layout.RequestedSeed = requestedSeed ?? "";
                layout.Attempt = attempt;
                var problems = WorldLayoutValidator.Validate(layout, settings);
                if (extraRule != null) problems.AddRange(extraRule(layout) ?? Enumerable.Empty<string>());
                result.Attempts.Add(new GenerationAttempt { Attempt = attempt, Seed = attemptSeed, Problems = problems });
                if (problems.Count > 0) continue;
                result.Layout = layout;
                return result;
            }
            throw new WorldGenerationException(result.Attempts);
        }

        // One attempt, unvalidated.
        public static WorldLayout Build(ulong seed, WorldSettings settings) => new Builder(seed, settings ?? WorldSettings.Default).Build();

        private struct Span
        {
            public bool Vertical;
            public int Fixed;
            public int From;
            public int To;
            public RoadKind Kind;
            public int Width;
        }

        private sealed class Builder
        {
            private readonly ulong _seed;
            private readonly WorldSettings _s;
            private readonly int _c;
            private readonly int _m;
            private readonly int _ring;
            private readonly int _n;
            private readonly int[] _xs;
            private readonly int[] _zs;
            private readonly DistrictKind[,] _kinds;
            private readonly List<int>[,] _localX;
            private readonly List<int>[,] _localZ;
            private readonly List<Span> _spans = new();
            private readonly BoxIndex<int> _roadIndex = new();
            private readonly BoxIndex<int> _reserved = new();
            private readonly WorldLayout _layout = new();
            private int _railX;
            private int _farmZ;

            public Builder(ulong seed, WorldSettings settings)
            {
                _seed = seed;
                _s = settings;
                _c = settings.CityHalfSize;
                _m = settings.MapHalfSize;
                _ring = settings.MapHalfSize - settings.OuterRingInset;
                _n = settings.Superblocks;
                _xs = new int[_n + 1];
                _zs = new int[_n + 1];
                _kinds = new DistrictKind[_n, _n];
                _localX = new List<int>[_n, _n];
                _localZ = new List<int>[_n, _n];
            }

            public WorldLayout Build()
            {
                _layout.GeneratorVersion = Version;
                _layout.Seed = _seed;
                _layout.CityHalfSize = _c;
                _layout.MapHalfSize = _m;
                Arterials();
                Districts();
                LocalStreets();
                RoadGraph();
                Rails();
                Stations();
                Blocks();
                Farms();
                AssignIds();
                PickStart();
                var turns = WorldRandom.Fork(_seed, "orientation").Next(4);
                for (var turn = 0; turn < turns; turn++) Turn();
                return _layout;
            }

            private void Arterials()
            {
                var rng = WorldRandom.Fork(_seed, "arterials");
                var spacing = 2 * _c / _n;
                for (var index = 0; index <= _n; index++)
                {
                    var edge = index == 0 || index == _n;
                    _xs[index] = -_c + index * spacing + (edge ? 0 : rng.Range(-_s.ArterialJitter, _s.ArterialJitter));
                    _zs[index] = -_c + index * spacing + (edge ? 0 : rng.Range(-_s.ArterialJitter, _s.ArterialJitter));
                }
            }

            // Industrial is the whole row on the south edge (between the city and the southern farmland and farm line);
            // downtown is the centre superblock plus one neighbour; wealthy spreads from a northern corner; the rest is residential.
            private void Districts()
            {
                var rng = WorldRandom.Fork(_seed, "districts");
                var centre = _n / 2;
                var neighbours = new[] { (centre - 1, centre), (centre + 1, centre), (centre, centre + 1), (centre, centre - 1) };
                var extra = neighbours[rng.Next(neighbours.Length)];
                var anchor = rng.Next(2) == 0 ? (0, _n - 1) : (_n - 1, _n - 1);
                for (var j = 0; j < _n; j++)
                for (var i = 0; i < _n; i++)
                {
                    if (j == 0) _kinds[i, j] = DistrictKind.Industrial;
                    else if ((i, j) == (centre, centre) || (i, j) == extra) _kinds[i, j] = DistrictKind.Downtown;
                    else if (Math.Abs(i - anchor.Item1) + Math.Abs(j - anchor.Item2) <= 2) _kinds[i, j] = DistrictKind.Wealthy;
                    else _kinds[i, j] = DistrictKind.Residential;
                }
                foreach (var profile in _s.Districts)
                {
                    var district = new WorldDistrict
                    {
                        Id = profile.Id, Kind = profile.Kind, MinRecipeTier = profile.MinRecipeTier, CustomersPerHour = profile.CustomersPerHour,
                        TrafficPercent = profile.TrafficPercent, PricePercent = profile.PricePercent,
                        Cuisines = profile.Cuisines.Select(x => new CuisineWeight { Cuisine = x.Cuisine, Weight = x.Weight }).ToList()
                    };
                    for (var j = 0; j < _n; j++)
                    for (var i = 0; i < _n; i++)
                        if (_kinds[i, j] == profile.Kind)
                            district.Areas.Add(new WorldRect(_xs[i], _zs[j], _xs[i + 1] - _xs[i], _zs[j + 1] - _zs[j]));
                    _layout.Districts.Add(district);
                }
            }

            private void LocalStreets()
            {
                var rng = WorldRandom.Fork(_seed, "rail-position");
                var column = rng.Next(2) == 0 ? 1 : _n - 2;
                _railX = (_xs[column] + _xs[column + 1]) / 2 + rng.Range(-10, 10);
                _farmZ = -_c - _s.FarmLineOffset;
                rng = WorldRandom.Fork(_seed, "streets");
                for (var j = 0; j < _n; j++)
                for (var i = 0; i < _n; i++)
                {
                    var profile = _s.Profile(_kinds[i, j]);
                    _localX[i, j] = Locals(rng, profile, _xs[i], _xs[i + 1], x => Math.Abs(x - _railX) >= _s.RailClearance);
                    _localZ[i, j] = Locals(rng, profile, _zs[j], _zs[j + 1], _ => true);
                }
            }

            private List<int> Locals(WorldRandom rng, DistrictProfile profile, int from, int to, Func<int, bool> allowed)
            {
                var count = rng.Range(profile.MinLocalStreets, profile.MaxLocalStreets);
                var result = new List<int>();
                for (var index = 1; index <= count; index++)
                {
                    var position = from + (to - from) * index / (count + 1) + rng.Range(-_s.LocalStreetJitter, _s.LocalStreetJitter);
                    if (allowed(position)) result.Add(position);
                }
                return result;
            }

            private void RoadGraph()
            {
                for (var index = 0; index <= _n; index++)
                {
                    AddSpan(true, _xs[index], -_c, _c, RoadKind.Arterial);
                    AddSpan(false, _zs[index], -_c, _c, RoadKind.Arterial);
                }
                for (var j = 0; j < _n; j++)
                for (var i = 0; i < _n; i++)
                {
                    foreach (var x in _localX[i, j]) AddSpan(true, x, _zs[j], _zs[j + 1], RoadKind.Local);
                    foreach (var z in _localZ[i, j]) AddSpan(false, z, _xs[i], _xs[i + 1], RoadKind.Local);
                }
                // Rural spurs continue every arterial from the city edge out to the outer ring.
                for (var index = 0; index <= _n; index++)
                {
                    AddSpan(true, _xs[index], _c, _ring, RoadKind.Rural);
                    AddSpan(true, _xs[index], -_ring, -_c, RoadKind.Rural);
                    AddSpan(false, _zs[index], _c, _ring, RoadKind.Rural);
                    AddSpan(false, _zs[index], -_ring, -_c, RoadKind.Rural);
                }
                AddSpan(false, _ring, -_ring, _ring, RoadKind.Rural);
                AddSpan(false, -_ring, -_ring, _ring, RoadKind.Rural);
                AddSpan(true, _ring, -_ring, _ring, RoadKind.Rural);
                AddSpan(true, -_ring, -_ring, _ring, RoadKind.Rural);

                // Every crossing or touching of two spans is a node; each span is cut into segments between its nodes.
                var cuts = _spans.Select(x => new List<int> { x.From, x.To }).ToList();
                for (var a = 0; a < _spans.Count; a++)
                for (var b = 0; b < _spans.Count; b++)
                {
                    var vertical = _spans[a];
                    var horizontal = _spans[b];
                    if (!vertical.Vertical || horizontal.Vertical) continue;
                    if (horizontal.From <= vertical.Fixed && vertical.Fixed <= horizontal.To && vertical.From <= horizontal.Fixed && horizontal.Fixed <= vertical.To)
                    {
                        cuts[a].Add(horizontal.Fixed);
                        cuts[b].Add(vertical.Fixed);
                    }
                }
                var points = new SortedSet<(int X, int Z)>();
                var pieces = new List<((int X, int Z) From, (int X, int Z) To, Span Span)>();
                for (var index = 0; index < _spans.Count; index++)
                {
                    var span = _spans[index];
                    var stops = cuts[index].Distinct().OrderBy(x => x).ToList();
                    for (var stop = 0; stop + 1 < stops.Count; stop++)
                    {
                        var from = span.Vertical ? (span.Fixed, stops[stop]) : (stops[stop], span.Fixed);
                        var to = span.Vertical ? (span.Fixed, stops[stop + 1]) : (stops[stop + 1], span.Fixed);
                        points.Add(from);
                        points.Add(to);
                        pieces.Add((from, to, span));
                    }
                }
                var ids = new Dictionary<(int, int), string>();
                foreach (var point in points)
                {
                    var id = $"node-{_layout.Nodes.Count + 1:D4}";
                    ids[point] = id;
                    _layout.Nodes.Add(new RoadNode { Id = id, X = point.X, Z = point.Z });
                }
                foreach (var piece in pieces.OrderBy(x => x.From.X).ThenBy(x => x.From.Z).ThenBy(x => x.To.X).ThenBy(x => x.To.Z))
                {
                    var segment = new RoadSegment
                    {
                        Id = $"road-{_layout.Roads.Count + 1:D4}", FromId = ids[piece.From], ToId = ids[piece.To], Kind = piece.Span.Kind,
                        Width = piece.Span.Width, CapacityPerHour = Capacity(piece.Span.Kind)
                    };
                    _roadIndex.Add(WorldGeometry.Strip(piece.From.X, piece.From.Z, piece.To.X, piece.To.Z, segment.Width), _layout.Roads.Count);
                    _layout.Roads.Add(segment);
                }
            }

            private void AddSpan(bool vertical, int position, int from, int to, RoadKind kind) => _spans.Add(new Span
            {
                Vertical = vertical, Fixed = position, From = from, To = to, Kind = kind,
                Width = kind == RoadKind.Arterial ? _s.ArterialWidth : kind == RoadKind.Local ? _s.LocalWidth : _s.RuralWidth
            });

            private int Capacity(RoadKind kind) =>
                kind == RoadKind.Arterial ? _s.ArterialCapacity : kind == RoadKind.Local ? _s.LocalCapacity : _s.RuralCapacity;

            // The city line runs from the farm line north through the city to the northern farmland; the farm line runs across
            // the southern farmland beyond the industrial edge. Both cross roads only at level crossings.
            private void Rails()
            {
                var city = new RailLine { Id = "rail-city", Width = _s.RailWidth };
                city.Points.Add(new WorldCell(_railX, _farmZ));
                city.Points.Add(new WorldCell(_railX, _m - 20));
                var farm = new RailLine { Id = "rail-farm", Width = _s.RailWidth };
                farm.Points.Add(new WorldCell(-_ring + 60, _farmZ));
                farm.Points.Add(new WorldCell(_ring - 60, _farmZ));
                _layout.Rails.Add(city);
                _layout.Rails.Add(farm);
                foreach (var line in _layout.Rails)
                    for (var index = 0; index + 1 < line.Points.Count; index++)
                        _reserved.Add(WorldGeometry.Strip(line.Points[index].X, line.Points[index].Z, line.Points[index + 1].X, line.Points[index + 1].Z, line.Width), -1);
            }

            private void Stations()
            {
                var rng = WorldRandom.Fork(_seed, "stations");
                foreach (var line in _layout.Rails)
                {
                    var crossings = Crossings(line);
                    if (line.Id == "rail-farm")
                    {
                        // Spread along the farm line: one station per equal share of its crossings.
                        var groups = _s.FarmLineStations;
                        for (var group = 0; group < groups; group++)
                            PlaceInGroup(rng, line, crossings.Where((_, index) => index * groups / crossings.Count == group).ToList());
                    }
                    else
                    {
                        // One station per zone the city line passes through (each district crossed, and the farmland).
                        var zones = new List<string>();
                        foreach (var crossing in crossings)
                            if (!zones.Contains(Zone(crossing.X, crossing.Z))) zones.Add(Zone(crossing.X, crossing.Z));
                        foreach (var zone in zones)
                            PlaceInGroup(rng, line, crossings.Where(x => Zone(x.X, x.Z) == zone).ToList());
                    }
                }
            }

            private void PlaceInGroup(WorldRandom rng, RailLine line, List<(int X, int Z, bool RailVertical, int RoadWidth)> group)
            {
                if (group.Count == 0) return;
                var start = rng.Next(group.Count);
                for (var offset = 0; offset < group.Count; offset++)
                    if (TryStation(rng, line, group[(start + offset) % group.Count])) return;
            }

            // Level crossings strictly inside the line, in order along it.
            private List<(int X, int Z, bool RailVertical, int RoadWidth)> Crossings(RailLine line)
            {
                var result = new List<(int, int, bool, int)>();
                for (var index = 0; index + 1 < line.Points.Count; index++)
                {
                    var a = line.Points[index];
                    var b = line.Points[index + 1];
                    var vertical = a.X == b.X;
                    var low = vertical ? Math.Min(a.Z, b.Z) : Math.Min(a.X, b.X);
                    var high = vertical ? Math.Max(a.Z, b.Z) : Math.Max(a.X, b.X);
                    var fixedAt = vertical ? a.X : a.Z;
                    foreach (var span in _spans.Where(x => x.Vertical != vertical && x.Fixed > low && x.Fixed < high
                                 && x.From <= fixedAt && fixedAt <= x.To).OrderBy(x => x.Fixed))
                        result.Add(vertical ? (fixedAt, span.Fixed, true, span.Width) : (span.Fixed, fixedAt, false, span.Width));
                }
                return result;
            }

            private bool TryStation(WorldRandom rng, RailLine line, (int X, int Z, bool RailVertical, int RoadWidth) crossing)
            {
                var length = _s.StationLength;
                var depth = _s.StationDepth;
                var half = crossing.RoadWidth / 2;
                var railHalf = line.Width / 2;
                var sides = new List<(int, int)> { (1, 1), (1, -1), (-1, 1), (-1, -1) };
                var first = rng.Next(sides.Count);
                for (var offset = 0; offset < sides.Count; offset++)
                {
                    var (alongSide, acrossSide) = sides[(first + offset) % sides.Count];
                    WorldRect rect;
                    Facing facing;
                    if (crossing.RailVertical)
                    {
                        var z = alongSide > 0 ? crossing.Z + half + _s.Setback : crossing.Z - half - _s.Setback - length;
                        var x = acrossSide > 0 ? crossing.X + railHalf + 1 : crossing.X - railHalf - 1 - depth;
                        rect = new WorldRect(x, z, depth, length);
                        facing = alongSide > 0 ? Facing.South : Facing.North;
                    }
                    else
                    {
                        var x = alongSide > 0 ? crossing.X + half + _s.Setback : crossing.X - half - _s.Setback - length;
                        var z = acrossSide > 0 ? crossing.Z + railHalf + 1 : crossing.Z - railHalf - 1 - depth;
                        rect = new WorldRect(x, z, length, depth);
                        facing = alongSide > 0 ? Facing.West : Facing.East;
                    }
                    var box = WorldGeometry.Of(rect);
                    if (!InsideRing(rect) || _roadIndex.Any(box) || _reserved.Any(box)) continue;
                    var district = DistrictAt(2 * rect.X + rect.Width, 2 * rect.Z + rect.Depth);
                    var percent = district == null ? 100 : _s.Profile(district.Value).PricePercent;
                    Add(new WorldBuilding
                    {
                        Category = BuildingCategory.Station, DistrictId = district == null ? "" : _s.Profile(district.Value).Id,
                        X = rect.X, Z = rect.Z, Width = rect.Width, Depth = rect.Depth, Facing = facing,
                        Doors = WorldGeometry.Doors(rect, facing, 1), Ownership = Ownership.ForSale, LineId = line.Id,
                        PriceCents = Price(rect, _s.StationCentsPerCell, percent)
                    });
                    return true;
                }
                return false;
            }

            private void Blocks()
            {
                var rng = WorldRandom.Fork(_seed, "buildings");
                for (var j = 0; j < _n; j++)
                for (var i = 0; i < _n; i++)
                {
                    var verticals = new List<(int Position, int Width)> { (_xs[i], _s.ArterialWidth) };
                    verticals.AddRange(_localX[i, j].Select(x => (x, _s.LocalWidth)));
                    verticals.Add((_xs[i + 1], _s.ArterialWidth));
                    var horizontals = new List<(int Position, int Width)> { (_zs[j], _s.ArterialWidth) };
                    horizontals.AddRange(_localZ[i, j].Select(x => (x, _s.LocalWidth)));
                    horizontals.Add((_zs[j + 1], _s.ArterialWidth));
                    for (var row = 0; row + 1 < horizontals.Count; row++)
                    for (var column = 0; column + 1 < verticals.Count; column++)
                        Block(rng, _kinds[i, j], verticals[column], verticals[column + 1], horizontals[row], horizontals[row + 1]);
                }
            }

            // Lots line all four street edges of a block. North and south lots take the full width and at most half the depth;
            // east and west lots fill the band between them, so no two lots of one block can overlap.
            private void Block(WorldRandom rng, DistrictKind kind, (int Position, int Width) west, (int Position, int Width) east,
                (int Position, int Width) south, (int Position, int Width) north)
            {
                var x0 = west.Position + west.Width / 2 + _s.Setback;
                var x1 = east.Position - east.Width / 2 - _s.Setback;
                var z0 = south.Position + south.Width / 2 + _s.Setback;
                var z1 = north.Position - north.Width / 2 - _s.Setback;
                if (x1 - x0 < 12 || z1 - z0 < 12) return;
                var deepNorthSouth = (z1 - z0 - _s.MaxBuildingGap) / 2;
                var deepEastWest = (x1 - x0 - _s.MaxBuildingGap) / 2;
                Edge(rng, kind, Facing.North, x0, x1, z1, deepNorthSouth);
                Edge(rng, kind, Facing.South, x0, x1, z0, deepNorthSouth);
                var band0 = z0 + deepNorthSouth + _s.MinBuildingGap;
                var band1 = z1 - deepNorthSouth - _s.MinBuildingGap;
                Edge(rng, kind, Facing.East, band0, band1, x1, deepEastWest);
                Edge(rng, kind, Facing.West, band0, band1, x0, deepEastWest);
            }

            private void Edge(WorldRandom rng, DistrictKind kind, Facing facing, int from, int to, int edge, int deepLimit)
            {
                var profile = _s.Profile(kind);
                var weights = new[] { profile.RestaurantWeight, profile.FactoryWeight, profile.HouseWeight, profile.ApartmentWeight, profile.OfficeWeight };
                var categories = new[] { BuildingCategory.Restaurant, BuildingCategory.Factory, BuildingCategory.House, BuildingCategory.Apartment, BuildingCategory.Office };
                var cursor = from + rng.Range(0, 2);
                while (cursor < to)
                {
                    var category = categories[rng.Weighted(weights)];
                    var size = SizeOf(category, kind);
                    var deepMax = Math.Min(size.MaxDeep, deepLimit);
                    if (deepMax < size.MinDeep)
                    {
                        cursor += 3;
                        continue;
                    }
                    var along = Math.Min(rng.Range(size.MinAlong, size.MaxAlong), to - cursor);
                    if (along < size.MinAlong) break;
                    var deep = rng.Range(size.MinDeep, deepMax);
                    var rect = facing switch
                    {
                        Facing.North => new WorldRect(cursor, edge - deep, along, deep),
                        Facing.South => new WorldRect(cursor, edge, along, deep),
                        Facing.East => new WorldRect(edge - deep, cursor, deep, along),
                        _ => new WorldRect(edge, cursor, deep, along)
                    };
                    var building = Lot(rng, category, kind, rect, facing);
                    if (!_reserved.Any(WorldGeometry.Of(rect))) Add(building);
                    cursor += along + rng.Range(_s.MinBuildingGap, _s.MaxBuildingGap);
                }
            }

            private SizeRange SizeOf(BuildingCategory category, DistrictKind kind) => category switch
            {
                BuildingCategory.Restaurant => _s.Restaurant,
                BuildingCategory.Factory => _s.Factory,
                BuildingCategory.House => kind == DistrictKind.Wealthy ? _s.LargeHouse : _s.House,
                BuildingCategory.Apartment => _s.Apartment,
                _ => _s.Office
            };

            private WorldBuilding Lot(WorldRandom rng, BuildingCategory category, DistrictKind kind, WorldRect rect, Facing facing)
            {
                var profile = _s.Profile(kind);
                var building = new WorldBuilding
                {
                    Category = category, DistrictId = profile.Id, X = rect.X, Z = rect.Z, Width = rect.Width, Depth = rect.Depth,
                    Facing = facing, Ownership = Ownership.Scenery
                };
                switch (category)
                {
                    case BuildingCategory.Restaurant:
                        building.Doors = WorldGeometry.Doors(rect, facing, 2);
                        building.Ownership = rng.Chance(_s.RestaurantForSalePercent) ? Ownership.ForSale : Ownership.Competitor;
                        building.PriceCents = Price(rect, _s.RestaurantCentsPerCell, profile.PricePercent);
                        break;
                    case BuildingCategory.Factory:
                        building.Doors = WorldGeometry.Doors(rect, facing, 2);
                        building.Ownership = Ownership.ForSale;
                        building.PriceCents = Price(rect, _s.FactoryCentsPerCell, profile.PricePercent);
                        break;
                    case BuildingCategory.House:
                        building.Doors = WorldGeometry.Doors(rect, facing, 1);
                        building.ModelKey = (kind == DistrictKind.Wealthy ? "house/large-" : "house/small-") + Variant(rng, 3);
                        building.Floors = rng.Range(1, 2);
                        break;
                    case BuildingCategory.Apartment:
                        building.Doors = WorldGeometry.Doors(rect, facing, 1);
                        building.ModelKey = "apartment/block-" + Variant(rng, 2);
                        building.Floors = rng.Range(3, 6);
                        break;
                    default:
                        building.Doors = WorldGeometry.Doors(rect, facing, 1);
                        building.ModelKey = "office/tower-" + Variant(rng, 2);
                        building.Floors = rng.Range(4, 12);
                        break;
                }
                return building;
            }

            private static string Variant(WorldRandom rng, int count) => ((char)('a' + rng.Next(count))).ToString();

            private static long Price(WorldRect rect, long centsPerCell, int percent) =>
                checked((long)rect.Width * rect.Depth * centsPerCell * percent / 100);

            // Farms line both sides of every rural spur between the city and the outer ring; the industrial side is denser.
            private void Farms()
            {
                var rng = WorldRandom.Fork(_seed, "farms");
                var farms = new BoxIndex<int>();
                var city = new Box(-2 * _c, -2 * _c, 2 * _c, 2 * _c);
                foreach (var spur in _spans.Where(x => x.Kind == RoadKind.Rural && Math.Abs(x.Fixed) != _ring))
                {
                    var industrialSide = spur.Vertical && spur.To <= -_c;
                    var chance = _s.FarmChancePercent + (industrialSide ? _s.FarmIndustrialSideBonusPercent : 0);
                    var low = spur.From + 12;
                    var high = spur.To - 12;
                    var half = spur.Width / 2;
                    foreach (var side in new[] { 1, -1 })
                    {
                        var cursor = low + rng.Range(0, 20);
                        while (true)
                        {
                            var along = rng.Range(_s.Farm.MinAlong, _s.Farm.MaxAlong);
                            var deep = rng.Range(_s.Farm.MinDeep, _s.Farm.MaxDeep);
                            if (cursor + along > high) break;
                            var place = rng.Chance(chance);
                            var near = spur.Fixed + side * (half + _s.Setback);
                            var rect = spur.Vertical
                                ? new WorldRect(side > 0 ? near : near - deep, cursor, deep, along)
                                : new WorldRect(cursor, side > 0 ? near : near - deep, along, deep);
                            var facing = spur.Vertical ? (side > 0 ? Facing.West : Facing.East) : (side > 0 ? Facing.South : Facing.North);
                            var box = WorldGeometry.Of(rect);
                            var variant = Variant(rng, 2);
                            if (place && InsideRing(rect) && !box.Overlaps(city) && !_roadIndex.Any(box) && !_reserved.Any(box) && !farms.Any(box))
                            {
                                farms.Add(box, 0);
                                Add(new WorldBuilding
                                {
                                    Category = BuildingCategory.Farm, X = rect.X, Z = rect.Z, Width = rect.Width, Depth = rect.Depth,
                                    Facing = facing, Doors = WorldGeometry.Doors(rect, facing, 1), ModelKey = "farm/barn-" + variant,
                                    Ownership = Ownership.ForSale, PriceCents = Price(rect, _s.FarmCentsPerCell, 100)
                                });
                            }
                            cursor += along + rng.Range(_s.MinFarmGap, _s.MaxFarmGap);
                        }
                    }
                }
            }

            private bool InsideRing(WorldRect rect)
            {
                var limit = _ring - _s.RuralWidth / 2;
                return rect.X >= -limit && rect.Z >= -limit && rect.X + rect.Width <= limit && rect.Z + rect.Depth <= limit;
            }

            private void Add(WorldBuilding building)
            {
                _layout.Buildings.Add(building);
                if (building.Category == BuildingCategory.Station) _reserved.Add(WorldGeometry.Of(building), -1);
            }

            private void AssignIds()
            {
                var counts = new Dictionary<BuildingCategory, int>();
                foreach (var building in _layout.Buildings)
                {
                    counts.TryGetValue(building.Category, out var count);
                    counts[building.Category] = ++count;
                    building.Id = $"{building.Category.ToString().ToLowerInvariant()}-{count:D4}";
                }
            }

            // The smallest (then cheapest, then first) residential restaurant shell that is small enough becomes the player's.
            private void PickStart()
            {
                var start = _layout.Buildings
                    .Where(x => x.Category == BuildingCategory.Restaurant && x.DistrictId == _s.Profile(DistrictKind.Residential).Id
                        && x.Width * x.Depth <= _s.StartMaxArea)
                    .OrderBy(x => x.Width * x.Depth).ThenBy(x => x.PriceCents).FirstOrDefault();
                if (start == null) return;
                start.Ownership = Ownership.Player;
                _layout.StartRestaurantId = start.Id;
            }

            private DistrictKind? DistrictAt(int doubledX, int doubledZ)
            {
                for (var j = 0; j < _n; j++)
                for (var i = 0; i < _n; i++)
                    if (doubledX >= 2 * _xs[i] && doubledX < 2 * _xs[i + 1] && doubledZ >= 2 * _zs[j] && doubledZ < 2 * _zs[j + 1])
                        return _kinds[i, j];
                return null;
            }

            private string Zone(int x, int z)
            {
                var district = DistrictAt(2 * x, 2 * z);
                return district == null ? (z > 0 ? "farmland-north" : "farmland-south") : district.Value.ToString();
            }

            private void Turn()
            {
                foreach (var district in _layout.Districts)
                    district.Areas = district.Areas.Select(WorldGeometry.TurnRect).ToList();
                foreach (var node in _layout.Nodes) (node.X, node.Z) = WorldGeometry.TurnPoint(node.X, node.Z);
                foreach (var line in _layout.Rails)
                    line.Points = line.Points.Select(x =>
                    {
                        var (px, pz) = WorldGeometry.TurnPoint(x.X, x.Z);
                        return new WorldCell(px, pz);
                    }).ToList();
                foreach (var building in _layout.Buildings)
                {
                    var rect = WorldGeometry.TurnRect(building.Footprint);
                    building.X = rect.X;
                    building.Z = rect.Z;
                    building.Width = rect.Width;
                    building.Depth = rect.Depth;
                    building.Facing = WorldGeometry.TurnFacing(building.Facing);
                    building.Doors = building.Doors.Select(WorldGeometry.TurnCell).ToList();
                }
            }
        }
    }
}
