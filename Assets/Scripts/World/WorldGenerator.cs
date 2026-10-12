// Seed + settings -> WorldLayout, with no Unity APIs (GDD section 3, decision 0026). Generation is deterministic: every random
// draw comes from WorldRandom streams forked from the seed, every list is built in a fixed order, and all geometry and land
// heights are integers. A layout that fails WorldLayoutValidator is discarded and generation retries with
// WorldRandom.DeriveSeed(seed, attempt); every attempt and its problems are reported. Bump Version whenever output for a given
// seed can change: stored worlds keep what they were given.
//
// Build steps (canonical frame, industrial to the south; the finished layout is then turned a seeded number of quarter turns):
// arterial grid (5x5 superblocks) -> districts -> river along one superblock row -> local streets (none running along the
// river) -> road graph -> rail lines -> land heights -> junction controls -> bridges -> level crossings -> stations at level
// crossings -> buildings along every block edge, doors facing the street, on land flat enough -> farms along rural spurs ->
// trees -> IDs -> starting restaurant -> turn -> lots. Every purchasable building is placed together with its lot (its
// footprint plus the setback to its street, deeper for factories and farms, plus a restaurant's service yard beside it), and
// nothing else may stand on a lot.
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
        // 2 (2026-09-28): denser city, land heights, a river with bridges, level crossings, junction controls and trees.
        // 3 (2026-09-29): lots with reserved site IDs (decision 0028); factories and farms stand behind deeper aprons.
        // 4 (2026-10-02): larger restaurants (decision 0036).
        // 5 (2026-10-06): every restaurant lot has a service yard beside the shell with a back door and a starter dock (layout
        // format 4, decision 0037).
        // 6 (2026-10-07): district customer rates count game hours (layout format 5, decision 0039); nothing else changes.
        public const int Version = 6;

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
            private const int One = 1024;

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
            // Rails and stations: nothing else may stand there.
            private readonly BoxIndex<int> _reserved = new();
            // The river widened by its bank: no building may stand there.
            private readonly BoxIndex<int> _water = new();
            // Every building and farm, for trees.
            private readonly BoxIndex<int> _placed = new();
            // Doorways, bridges and level crossings: no street trees.
            private readonly BoxIndex<int> _clear = new();
            private readonly List<bool> _roadVertical = new();
            private readonly WorldLayout _layout = new();
            private readonly List<WorldCell> _river = new();
            private int _railX;
            private int _farmZ;
            private ulong _terrainSalt;

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

            private int RiverHalf => _s.RiverWidth / 2;

            public WorldLayout Build()
            {
                _layout.GeneratorVersion = Version;
                _layout.Seed = _seed;
                _layout.CityHalfSize = _c;
                _layout.MapHalfSize = _m;
                Arterials();
                Districts();
                River();
                LocalStreets();
                RoadGraph();
                Rails();
                Terrain();
                Controls();
                Bridges();
                LevelCrossings();
                Stations();
                Blocks();
                Farms();
                Trees();
                AssignIds();
                PickStart();
                var turns = WorldRandom.Fork(_seed, "orientation").Next(4);
                for (var turn = 0; turn < turns; turn++) Turn();
                Lots();
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

            // The river runs west to east across the whole map inside one superblock row (never the industrial row), far enough
            // from that row's two arterials that they, and the rural spurs continuing them, never meet it. Only north-south
            // roads and the city rail line cross it, each on a bridge.
            private void River()
            {
                var rng = WorldRandom.Fork(_seed, "river");
                var row = rng.Range(1, _n - 1);
                var clearance = _s.ArterialWidth / 2 + RiverHalf + _s.RiverBank + _s.RiverStreetClearance + 20;
                var low = _zs[row] + clearance;
                var high = _zs[row + 1] - clearance;
                if (high < low) low = high = (_zs[row] + _zs[row + 1]) / 2;
                var step = _s.RiverMeanderStep;
                var controls = new List<int>();
                for (var index = 0; index <= 2 * _m / step + 1; index++) controls.Add(rng.Range(low, high));
                for (var x = -_m; x <= _m; x += 20)
                {
                    var k = (x + _m) / step;
                    var t = (long)(x + _m - k * step) * One / step;
                    _river.Add(new WorldCell(x, controls[k] + (int)WorldGeometry.FloorDiv((controls[k + 1] - controls[k]) * Smooth(t), One)));
                }
                var river = new WorldRiver { Id = "river-1", Width = _s.RiverWidth, SurfaceDropCm = _s.RiverSurfaceDropCm };
                river.Points.AddRange(_river.Select(p => new WorldCell(p.X, p.Z)));
                _layout.Rivers.Add(river);
                foreach (var box in WorldGeometry.Corridor(_river, RiverHalf + _s.RiverBank)) _water.Add(box, 0);
            }

            // The river's centreline Z at x (the canonical river runs west to east, one point every 20 m).
            private int RiverZ(int x)
            {
                var index = Math.Clamp((x + _m) / 20, 0, _river.Count - 2);
                var a = _river[index];
                var b = _river[index + 1];
                var t = Math.Clamp(x - a.X, 0, 20);
                return a.Z + (int)WorldGeometry.FloorDiv((long)(b.Z - a.Z) * t, 20);
            }

            private void LocalStreets()
            {
                var rng = WorldRandom.Fork(_seed, "rail-position");
                var column = rng.Next(2) == 0 ? 1 : _n - 2;
                _railX = (_xs[column] + _xs[column + 1]) / 2 + rng.Range(-10, 10);
                _farmZ = -_c - _s.FarmLineOffset;
                rng = WorldRandom.Fork(_seed, "streets");
                var riverGap = RiverHalf + _s.RiverBank + _s.LocalWidth / 2 + _s.RiverStreetClearance;
                for (var j = 0; j < _n; j++)
                for (var i = 0; i < _n; i++)
                {
                    var profile = _s.Profile(_kinds[i, j]);
                    var (west, east) = (_xs[i], _xs[i + 1]);
                    _localX[i, j] = Locals(rng, profile, _xs[i], _xs[i + 1], x => Math.Abs(x - _railX) >= _s.RailClearance);
                    // East-west streets never run beside the river: one that would come near it is dropped.
                    _localZ[i, j] = Locals(rng, profile, _zs[j], _zs[j + 1], z =>
                    {
                        for (var x = west; x <= east; x += 10)
                            if (Math.Abs(z - RiverZ(x)) < riverGap) return false;
                        return Math.Abs(z - RiverZ(east)) >= riverGap;
                    });
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
                    _roadVertical.Add(piece.Span.Vertical);
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

            // ------------------------------------------------------------------ land

            // Value noise hills, damped in the city and along the rail lines, with a flat valley floor along the river.
            private void Terrain()
            {
                _terrainSalt = WorldRandom.Fork(_seed, "terrain").NextULong();
                var spacing = _s.TerrainSpacing;
                if (spacing <= 0 || 2 * _m % spacing != 0) throw new ArgumentException("TerrainSpacing must divide the map size.");
                var terrain = new WorldTerrain { Spacing = spacing, Samples = 2 * _m / spacing + 1 };
                terrain.HeightsCm = new int[terrain.Samples * terrain.Samples];
                // The valley floor along the river: the damped land a little upstream and downstream, averaged, then lowered.
                var floor = new Dictionary<int, int>();
                for (var z = 0; z < terrain.Samples; z++)
                for (var x = 0; x < terrain.Samples; x++)
                {
                    var px = terrain.Origin + x * spacing;
                    var pz = terrain.Origin + z * spacing;
                    if (!floor.TryGetValue(px, out var level))
                    {
                        long sum = 0;
                        for (var k = -2; k <= 2; k++) sum += Land(px + 40 * k, RiverZ(px + 40 * k));
                        floor[px] = level = (int)WorldGeometry.FloorDiv(sum, 5) - _s.RiverValleyDropCm;
                    }
                    var land = Land(px, pz);
                    var distance = Math.Abs(pz - RiverZ(px)) - RiverHalf - _s.RiverValleyFlat;
                    var weight = distance <= 0 ? One : distance >= _s.RiverValleyFade ? 0 : One - Smooth((long)distance * One / _s.RiverValleyFade);
                    terrain.HeightsCm[z * terrain.Samples + x] = land + (int)WorldGeometry.FloorDiv((long)(level - land) * weight, One);
                }
                _layout.Terrain = terrain;
            }

            private int Land(int x, int z)
            {
                long relief = 0;
                for (var octave = 0; octave < _s.TerrainLatticeMetres.Length; octave++)
                    relief += Noise(_terrainSalt + (ulong)octave, x, z, _s.TerrainLatticeMetres[octave], _s.TerrainAmplitudeCm[octave]);
                var outside = Math.Max(Math.Abs(x), Math.Abs(z)) - _c;
                var percent = Fade(outside, _s.CityReliefPercent, _s.TerrainCityFade);
                var railDistance = Math.Min(z >= _farmZ && z <= _m ? Math.Abs(x - _railX) : int.MaxValue / 2, Math.Abs(z - _farmZ));
                percent = Math.Min(percent, Fade(railDistance - _s.RailWidth, _s.RailReliefPercent, _s.RailReliefFade));
                return (int)WorldGeometry.FloorDiv(relief * percent, 100);
            }

            // `damped` percent at distance 0 or less, rising smoothly to 100 at `fade`.
            private static int Fade(int distance, int damped, int fade) =>
                distance <= 0 ? damped : distance >= fade ? 100 : damped + (int)((100 - damped) * Smooth((long)distance * One / fade) / One);

            // Smoothstep on 0..One.
            private static long Smooth(long t) => t * t * (3 * One - 2 * t) / ((long)One * One);

            // Value noise in [-amplitude, amplitude] on a lattice of `cell` metres, smoothly interpolated.
            private static int Noise(ulong salt, int x, int z, int cell, int amplitude)
            {
                var ix = WorldGeometry.FloorDiv(x, cell);
                var iz = WorldGeometry.FloorDiv(z, cell);
                var tx = Smooth((x - ix * cell) * One / cell);
                var tz = Smooth((z - iz * cell) * One / cell);
                long Corner(long cx, long cz) =>
                    (long)(WorldRandom.Mix(salt * 0x9E3779B97F4A7C15UL ^ (ulong)cx * 0xC2B2AE3D27D4EB4FUL ^ (ulong)cz * 0x165667B19E3779F9UL)
                           % (ulong)(2 * amplitude + 1)) - amplitude;
                var south = Corner(ix, iz) * One + (Corner(ix + 1, iz) - Corner(ix, iz)) * tx;
                var north = Corner(ix, iz + 1) * One + (Corner(ix + 1, iz + 1) - Corner(ix, iz + 1)) * tx;
                return (int)WorldGeometry.FloorDiv(south * One + (north - south) * tz, (long)One * One);
            }

            private int Elevation(WorldCell cell) => _layout.Terrain.CellHeightCm(cell.X, cell.Z);

            // The entrance height, or null when the footprint rises or falls too far from it.
            private int? LevelFor(WorldRect rect, WorldCell entrance)
            {
                var level = Elevation(entrance);
                var t = _layout.Terrain;
                var x0 = 2 * rect.X;
                var z0 = 2 * rect.Z;
                var x1 = 2 * (rect.X + rect.Width);
                var z1 = 2 * (rect.Z + rect.Depth);
                foreach (var h in new[] { t.HeightCm(x0, z0), t.HeightCm(x1, z0), t.HeightCm(x0, z1), t.HeightCm(x1, z1), t.HeightCm((x0 + x1) / 2, (z0 + z1) / 2) })
                    if (Math.Abs(h - level) > _s.MaxFootprintRiseCm) return null;
                return level;
            }

            // ------------------------------------------------------------------ road features

            // Lights where arterials cross (and where a downtown street crosses an arterial), stop signs at every other junction.
            private void Controls()
            {
                var kinds = new Dictionary<string, List<RoadKind>>();
                foreach (var road in _layout.Roads)
                    foreach (var id in new[] { road.FromId, road.ToId })
                    {
                        if (!kinds.TryGetValue(id, out var list)) kinds[id] = list = new List<RoadKind>();
                        list.Add(road.Kind);
                    }
                foreach (var node in _layout.Nodes)
                {
                    var list = kinds.TryGetValue(node.Id, out var found) ? found : new List<RoadKind>();
                    var arterials = list.Count(x => x == RoadKind.Arterial);
                    var downtown = DistrictAt(2 * node.X, 2 * node.Z) == DistrictKind.Downtown;
                    node.Control = list.Count <= 2 ? JunctionControl.None
                        : arterials >= 3 || (downtown && list.Count == 4 && arterials >= 2) ? JunctionControl.TrafficLight
                        : JunctionControl.StopSign;
                }
            }

            // A bridge carries every road segment and rail line over the river, spanning the water and its banks.
            private void Bridges()
            {
                var nodes = _layout.Nodes.ToDictionary(x => x.Id);
                var river = _layout.Rivers[0];
                foreach (var road in _layout.Roads)
                {
                    var a = nodes[road.FromId];
                    var b = nodes[road.ToId];
                    Bridge(road.Id, river, a.X == b.X, a.X == b.X ? a.X : a.Z, a.X == b.X ? Math.Min(a.Z, b.Z) : Math.Min(a.X, b.X),
                        a.X == b.X ? Math.Max(a.Z, b.Z) : Math.Max(a.X, b.X), road.Width);
                }
                foreach (var line in _layout.Rails)
                    for (var index = 0; index + 1 < line.Points.Count; index++)
                    {
                        var a = line.Points[index];
                        var b = line.Points[index + 1];
                        Bridge(line.Id, river, a.X == b.X, a.X == b.X ? a.X : a.Z, a.X == b.X ? Math.Min(a.Z, b.Z) : Math.Min(a.X, b.X),
                            a.X == b.X ? Math.Max(a.Z, b.Z) : Math.Max(a.X, b.X), line.Width);
                    }
            }

            private void Bridge(string carriesId, WorldRiver river, bool vertical, int fixedAt, int from, int to, int width)
            {
                foreach (var (along, dx, dz) in WorldGeometry.Crossings(river.Points, vertical, fixedAt, from, to))
                {
                    // How far the water plus its banks, and the carried way's own width, reach along the carried line.
                    long across = vertical ? Math.Abs(dx) : Math.Abs(dz);
                    long slant = vertical ? Math.Abs(dz) : Math.Abs(dx);
                    var length = WorldGeometry.CeilSqrt((long)dx * dx + (long)dz * dz);
                    var reach = (int)(((RiverHalf + _s.RiverBank) * length + width / 2 * slant + across - 1) / across);
                    var start = Math.Max(from, along - reach);
                    var end = Math.Min(to, along + reach);
                    var bridge = new WorldBridge
                    {
                        Id = $"bridge-{_layout.Bridges.Count + 1:D4}", CarriesId = carriesId, RiverId = river.Id,
                        From = vertical ? new WorldCell(fixedAt, start) : new WorldCell(start, fixedAt),
                        To = vertical ? new WorldCell(fixedAt, end) : new WorldCell(end, fixedAt)
                    };
                    _layout.Bridges.Add(bridge);
                    _clear.Add(WorldGeometry.Strip(bridge.From.X, bridge.From.Z, bridge.To.X, bridge.To.Z, width + 4), 0);
                }
            }

            // Every place a rail line crosses a road segment at grade.
            private void LevelCrossings()
            {
                var nodes = _layout.Nodes.ToDictionary(x => x.Id);
                foreach (var line in _layout.Rails)
                    for (var index = 0; index + 1 < line.Points.Count; index++)
                    {
                        var p = line.Points[index];
                        var q = line.Points[index + 1];
                        var railVertical = p.X == q.X;
                        var found = new List<(int Along, RoadSegment Road)>();
                        foreach (var road in _layout.Roads)
                        {
                            var a = nodes[road.FromId];
                            var b = nodes[road.ToId];
                            if ((a.X == b.X) == railVertical) continue;
                            if (railVertical)
                            {
                                if (a.Z > Math.Min(p.Z, q.Z) && a.Z < Math.Max(p.Z, q.Z) && p.X > Math.Min(a.X, b.X) && p.X < Math.Max(a.X, b.X))
                                    found.Add((a.Z, road));
                            }
                            else if (a.X > Math.Min(p.X, q.X) && a.X < Math.Max(p.X, q.X) && p.Z > Math.Min(a.Z, b.Z) && p.Z < Math.Max(a.Z, b.Z))
                                found.Add((a.X, road));
                        }
                        foreach (var (along, road) in found.OrderBy(x => x.Along))
                        {
                            var crossing = new LevelCrossing
                            {
                                Id = $"crossing-{_layout.Crossings.Count + 1:D4}", RoadId = road.Id, RailId = line.Id,
                                X = railVertical ? p.X : along, Z = railVertical ? along : p.Z
                            };
                            _layout.Crossings.Add(crossing);
                            _clear.Add(new Box(2 * crossing.X - 24, 2 * crossing.Z - 24, 2 * crossing.X + 24, 2 * crossing.Z + 24), 0);
                        }
                    }
            }

            // ------------------------------------------------------------------ stations and buildings

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
                    var box = WorldGeometry.Of(LotRect(rect, facing, BuildingCategory.Station));
                    if (!InsideRing(rect) || _roadIndex.Any(box) || _reserved.Any(box) || _water.Any(box)) continue;
                    var doors = WorldGeometry.Doors(rect, facing, 1);
                    var level = LevelFor(rect, doors[0]);
                    if (level == null) continue;
                    var district = DistrictAt(2 * rect.X + rect.Width, 2 * rect.Z + rect.Depth);
                    var percent = district == null ? 100 : _s.Profile(district.Value).PricePercent;
                    Add(new WorldBuilding
                    {
                        Category = BuildingCategory.Station, DistrictId = district == null ? "" : _s.Profile(district.Value).Id,
                        X = rect.X, Z = rect.Z, Width = rect.Width, Depth = rect.Depth, Facing = facing,
                        Doors = doors, Ownership = Ownership.ForSale, LineId = line.Id,
                        PriceCents = Price(rect, _s.StationCentsPerCell, percent), ElevationCm = level.Value
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
            // east and west lots take at most half the width and run the full depth, skipping ground the north and south lots
            // already hold (generator v2; v1 left the east and west edges nearly empty).
            private void Block(WorldRandom rng, DistrictKind kind, (int Position, int Width) west, (int Position, int Width) east,
                (int Position, int Width) south, (int Position, int Width) north)
            {
                var profile = _s.Profile(kind);
                var x0 = west.Position + west.Width / 2 + _s.Setback;
                var x1 = east.Position - east.Width / 2 - _s.Setback;
                var z0 = south.Position + south.Width / 2 + _s.Setback;
                var z1 = north.Position - north.Width / 2 - _s.Setback;
                if (x1 - x0 < 12 || z1 - z0 < 12) return;
                var split = Math.Max(2, profile.MaxGap);
                var deepNorthSouth = (z1 - z0 - split) / 2;
                var deepEastWest = (x1 - x0 - split) / 2;
                Edge(rng, kind, Facing.North, x0, x1, z1, deepNorthSouth);
                Edge(rng, kind, Facing.South, x0, x1, z0, deepNorthSouth);
                Edge(rng, kind, Facing.East, z0, z1, x1, deepEastWest);
                Edge(rng, kind, Facing.West, z0, z1, x0, deepEastWest);
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
                    // A factory stands this much further back than the street line, behind its lot's deeper apron.
                    var back = _s.SetbackFor(category) - _s.Setback;
                    var deepMax = Math.Min(size.MaxDeep, deepLimit - back);
                    if (deepMax < size.MinDeep)
                    {
                        cursor += 3;
                        continue;
                    }
                    // A restaurant's lot also holds its service yard beside the shell (generator v5).
                    var yard = category == BuildingCategory.Restaurant ? _s.ServiceYardWidth : 0;
                    var along = Math.Min(rng.Range(size.MinAlong, size.MaxAlong), to - cursor - yard);
                    if (along < size.MinAlong) break;
                    var deep = rng.Range(size.MinDeep, deepMax);
                    var yardLow = yard > 0 && rng.Chance(50);
                    var start = cursor + (yardLow ? yard : 0);
                    var line = facing is Facing.North or Facing.East ? edge - back : edge + back;
                    var rect = facing switch
                    {
                        Facing.North => new WorldRect(start, line - deep, along, deep),
                        Facing.South => new WorldRect(start, line, along, deep),
                        Facing.East => new WorldRect(line - deep, start, deep, along),
                        _ => new WorldRect(line, start, deep, along)
                    };
                    var building = Lot(rng, category, kind, rect, facing);
                    if (yard > 0) WorldGeometry.AddServiceYard(building, yardLow, yard, _s.SetbackFor(category));
                    // Property is checked with its whole lot, scenery with its footprint.
                    var box = WorldGeometry.Of(building.HasLot ? LotRect(building) : rect);
                    if (_placed.Any(Grow(box, 2 * profile.MinGap)))
                    {
                        cursor += 2;
                        continue;
                    }
                    var level = _reserved.Any(box) || _water.Any(box) || _roadIndex.Any(box) ? null : LevelFor(rect, building.Doors[0]);
                    if (level != null)
                    {
                        building.ElevationCm = level.Value;
                        Add(building);
                    }
                    cursor += along + yard + rng.Range(profile.MinGap, profile.MaxGap);
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
                        building.Floors = kind == DistrictKind.Downtown ? rng.Range(4, 8) : rng.Range(3, 5);
                        break;
                    default:
                        building.Doors = WorldGeometry.Doors(rect, facing, 1);
                        building.ModelKey = "office/tower-" + Variant(rng, 2);
                        building.Floors = rng.Range(6, 16);
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
                            var near = spur.Fixed + side * (half + _s.FarmSetback);
                            var rect = spur.Vertical
                                ? new WorldRect(side > 0 ? near : near - deep, cursor, deep, along)
                                : new WorldRect(cursor, side > 0 ? near : near - deep, along, deep);
                            var facing = spur.Vertical ? (side > 0 ? Facing.West : Facing.East) : (side > 0 ? Facing.South : Facing.North);
                            var box = WorldGeometry.Of(LotRect(rect, facing, BuildingCategory.Farm));
                            var variant = Variant(rng, 2);
                            if (place && InsideRing(rect) && !box.Overlaps(city) && !_roadIndex.Any(box) && !_reserved.Any(box) && !_water.Any(box)
                                && !farms.Any(box))
                            {
                                farms.Add(box, 0);
                                var doors = WorldGeometry.Doors(rect, facing, 1);
                                Add(new WorldBuilding
                                {
                                    Category = BuildingCategory.Farm, X = rect.X, Z = rect.Z, Width = rect.Width, Depth = rect.Depth,
                                    Facing = facing, Doors = doors, ModelKey = "farm/barn-" + variant,
                                    Ownership = Ownership.ForSale, PriceCents = Price(rect, _s.FarmCentsPerCell, 100), ElevationCm = Elevation(doors[0])
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

            private WorldRect LotRect(WorldRect footprint, Facing facing, BuildingCategory category) =>
                WorldGeometry.LotRect(footprint, facing, _s.SetbackFor(category));

            // A property's whole lot, its service yard included.
            private WorldRect LotRect(WorldBuilding building) => WorldGeometry.LotRect(building, _s.SetbackFor(building.Category));

            private void Add(WorldBuilding building)
            {
                _layout.Buildings.Add(building);
                // A property's whole lot is taken, so no building or tree stands on its apron.
                var box = WorldGeometry.Of(building.HasLot ? LotRect(building) : building.Footprint);
                _placed.Add(box, 0);
                if (building.Category == BuildingCategory.Station) _reserved.Add(box, -1);
                // The way in: no street tree in front of a door.
                var (dx, dz) = WorldGeometry.Step(building.Facing);
                foreach (var door in building.Doors)
                {
                    var reach = _s.Setback + 4;
                    var ex = door.X + dx * reach;
                    var ez = door.Z + dz * reach;
                    _clear.Add(new Box(2 * Math.Min(door.X, ex) - 4, 2 * Math.Min(door.Z, ez) - 4, 2 * Math.Max(door.X, ex) + 6, 2 * Math.Max(door.Z, ez) + 6), 0);
                }
            }

            // ------------------------------------------------------------------ trees

            // Street trees on city sidewalks, yard trees in the free space of city blocks, woodland and lone trees in the
            // countryside, and trees along the river banks. Positions are whole-metre cells; the tree stands at the cell's centre.
            private void Trees()
            {
                var rng = WorldRandom.Fork(_seed, "trees");
                var woodlandSalt = WorldRandom.Fork(_seed, "woodland").NextULong();
                var nodes = _layout.Nodes.ToDictionary(x => x.Id);
                for (var index = 0; index < _layout.Roads.Count; index++)
                {
                    var road = _layout.Roads[index];
                    if (road.Kind == RoadKind.Rural) continue;
                    var a = nodes[road.FromId];
                    var b = nodes[road.ToId];
                    var vertical = _roadVertical[index];
                    var from = (vertical ? Math.Min(a.Z, b.Z) : Math.Min(a.X, b.X)) + _s.StreetTreeJunctionClear;
                    var to = (vertical ? Math.Max(a.Z, b.Z) : Math.Max(a.X, b.X)) - _s.StreetTreeJunctionClear;
                    var fixedAt = vertical ? a.X : a.Z;
                    var half = road.Width / 2;
                    foreach (var side in new[] { 1, -1 })
                        for (var along = from + rng.Range(0, 4); along <= to; along += _s.StreetTreeSpacing + rng.Range(-2, 2))
                        {
                            var offset = side > 0 ? fixedAt + half - 1 : fixedAt - half;
                            var x = vertical ? offset : along;
                            var z = vertical ? along : offset;
                            var roll = rng.Next(100);
                            var kindRoll = rng.Next(100);
                            var scale = rng.Range(85, 115);
                            var district = DistrictAt(2 * x + 1, 2 * z + 1);
                            if (district == null || roll >= _s.Profile(district.Value).StreetTreePercent) continue;
                            var point = new Box(2 * x + 1, 2 * z + 1, 2 * x + 1, 2 * z + 1);
                            if (_clear.Any(Grow(point, 1)) || _placed.Any(Grow(point, 2)) || _reserved.Any(Grow(point, 2)) || NearWater(x, z)) continue;
                            if (_roadIndex.Overlapping(Grow(point, 1)).Any(r => _roadVertical[r] != vertical)) continue;
                            var kind = district switch
                            {
                                DistrictKind.Wealthy => kindRoll < 70 ? TreeKind.Broadleaf : TreeKind.Poplar,
                                DistrictKind.Industrial => kindRoll < 50 ? TreeKind.Broadleaf : TreeKind.Poplar,
                                DistrictKind.Residential => kindRoll < 85 ? TreeKind.Broadleaf : TreeKind.Poplar,
                                _ => TreeKind.Broadleaf
                            };
                            _layout.Trees.Add(new WorldTree { X = x, Z = z, Kind = kind, Scale = scale });
                        }
                }
                // Yards and parks inside the city, then the countryside; both add riverbank trees.
                var yard = _s.YardTreeLattice;
                for (var gz = -_c; gz < _c; gz += yard)
                for (var gx = -_c; gx < _c; gx += yard)
                {
                    var x = gx + rng.Next(yard);
                    var z = gz + rng.Next(yard);
                    var roll = rng.Next(100);
                    var kindRoll = rng.Next(100);
                    var scale = rng.Range(75, 120);
                    var district = DistrictAt(2 * x + 1, 2 * z + 1);
                    if (district == null) continue;
                    var bank = Riverbank(x, z);
                    if (roll >= (bank ? Math.Max(_s.RiverbankTreePercent, _s.Profile(district.Value).YardTreePercent) : _s.Profile(district.Value).YardTreePercent)) continue;
                    if (!FreeGround(x, z, 2)) continue;
                    var kind = bank ? (kindRoll < 50 ? TreeKind.Broadleaf : kindRoll < 85 ? TreeKind.Poplar : TreeKind.Bush)
                        : kindRoll < 55 ? TreeKind.Broadleaf : kindRoll < 75 ? TreeKind.Conifer : TreeKind.Bush;
                    _layout.Trees.Add(new WorldTree { X = x, Z = z, Kind = kind, Scale = scale });
                }
                var country = _s.CountryTreeLattice;
                var edge = _m - 3;
                for (var gz = -edge; gz < edge - country; gz += country)
                for (var gx = -edge; gx < edge - country; gx += country)
                {
                    var x = gx + rng.Next(country);
                    var z = gz + rng.Next(country);
                    var roll = rng.Next(100);
                    var kindRoll = rng.Next(100);
                    var scale = rng.Range(80, 130);
                    if (Math.Max(Math.Abs(2 * x + 1), Math.Abs(2 * z + 1)) < 2 * _c) continue;
                    var woodland = Noise(woodlandSalt, x, z, _s.WoodlandLatticeMetres, 50) + 50 >= _s.WoodlandThresholdPercent;
                    var bank = Riverbank(x, z);
                    var chance = Math.Max(woodland ? _s.WoodlandTreePercent : _s.LoneTreePercent, bank ? _s.RiverbankTreePercent : 0);
                    if (roll >= chance || !FreeGround(x, z, 3)) continue;
                    var kind = bank && !woodland ? (kindRoll < 50 ? TreeKind.Broadleaf : kindRoll < 85 ? TreeKind.Poplar : TreeKind.Bush)
                        : woodland ? (kindRoll < 45 ? TreeKind.Conifer : kindRoll < 90 ? TreeKind.Broadleaf : TreeKind.Bush)
                        : kindRoll < 80 ? TreeKind.Broadleaf : TreeKind.Conifer;
                    _layout.Trees.Add(new WorldTree { X = x, Z = z, Kind = kind, Scale = scale });
                }
            }

            private static Box Grow(Box box, int doubled) => new(box.X0 - doubled, box.Z0 - doubled, box.X1 + doubled, box.Z1 + doubled);

            // Clear of buildings, roads, rails, stations, level crossings and the water by `metres`.
            private bool FreeGround(int x, int z, int metres)
            {
                var point = new Box(2 * x + 1, 2 * z + 1, 2 * x + 1, 2 * z + 1);
                var grown = Grow(point, 2 * metres);
                return !_placed.Any(Grow(point, 2)) && !_roadIndex.Any(grown) && !_reserved.Any(grown) && !_clear.Any(point) && !NearWater(x, z);
            }

            // Within the river's water or the wet foot of its bank (a tree's cell centre, exactly); the drawn waterline lies about
            // 5 m past the nominal water width, where the channel is cut into the bank.
            private bool NearWater(int x, int z) => WithinRiver(x, z, RiverHalf + 8);

            private bool Riverbank(int x, int z) => WithinRiver(x, z, RiverHalf + _s.RiverbankTreeBand);

            private bool WithinRiver(int x, int z, int metres)
            {
                for (var index = 0; index + 1 < _river.Count; index++)
                {
                    var a = _river[index];
                    var b = _river[index + 1];
                    if (2 * x + 1 < 2 * Math.Min(a.X, b.X) - 2 * metres || 2 * x + 1 > 2 * Math.Max(a.X, b.X) + 2 * metres) continue;
                    if (WorldGeometry.WithinDistance(2 * x + 1, 2 * z + 1, 2 * a.X, 2 * a.Z, 2 * b.X, 2 * b.Z, 2 * metres)) return true;
                }
                return false;
            }

            // ------------------------------------------------------------------ finishing

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

            // Every property's lot and reserved site ID (decision 0028), from its final (turned) footprint, in building order.
            private void Lots()
            {
                foreach (var building in _layout.Buildings.Where(x => x.HasLot))
                {
                    var rect = LotRect(building);
                    building.SiteId = WorldLot.SiteIdFor(building.Id);
                    _layout.Lots.Add(new WorldLot
                    {
                        Id = WorldLot.IdFor(building.Id), BuildingId = building.Id, SiteId = building.SiteId,
                        X = rect.X, Z = rect.Z, Width = rect.Width, Depth = rect.Depth,
                        Access = WorldGeometry.AccessCell(rect, building.Facing, building.Doors[0])
                    });
                }
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

            private static WorldCell TurnedPoint(WorldCell point)
            {
                var (x, z) = WorldGeometry.TurnPoint(point.X, point.Z);
                return new WorldCell(x, z);
            }

            private void Turn()
            {
                foreach (var district in _layout.Districts)
                    district.Areas = district.Areas.Select(WorldGeometry.TurnRect).ToList();
                foreach (var node in _layout.Nodes) (node.X, node.Z) = WorldGeometry.TurnPoint(node.X, node.Z);
                foreach (var line in _layout.Rails) line.Points = line.Points.Select(TurnedPoint).ToList();
                foreach (var building in _layout.Buildings)
                {
                    var rect = WorldGeometry.TurnRect(building.Footprint);
                    building.X = rect.X;
                    building.Z = rect.Z;
                    building.Width = rect.Width;
                    building.Depth = rect.Depth;
                    building.Facing = WorldGeometry.TurnFacing(building.Facing);
                    building.Doors = building.Doors.Select(WorldGeometry.TurnCell).ToList();
                    if (building.ServiceYard != null) building.ServiceYard = WorldGeometry.TurnRect(building.ServiceYard);
                    if (building.BackDoor != null) building.BackDoor = WorldGeometry.TurnCell(building.BackDoor);
                    if (building.ServiceDock != null) building.ServiceDock = WorldGeometry.TurnRect(building.ServiceDock);
                }
                _layout.Terrain = _layout.Terrain.Turned();
                foreach (var river in _layout.Rivers) river.Points = river.Points.Select(TurnedPoint).ToList();
                foreach (var bridge in _layout.Bridges)
                {
                    bridge.From = TurnedPoint(bridge.From);
                    bridge.To = TurnedPoint(bridge.To);
                }
                foreach (var crossing in _layout.Crossings) (crossing.X, crossing.Z) = WorldGeometry.TurnPoint(crossing.X, crossing.Z);
                foreach (var tree in _layout.Trees)
                {
                    var cell = WorldGeometry.TurnCell(new WorldCell(tree.X, tree.Z));
                    tree.X = cell.X;
                    tree.Z = cell.Z;
                }
            }
        }
    }
}
