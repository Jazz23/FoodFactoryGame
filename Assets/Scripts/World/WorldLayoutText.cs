// Canonical line format of a layout: the one representation that is hashed, stored in world.db and replicated to clients.
// Every value is an integer or an escaped token and every list keeps its layout order, so the same layout always writes the
// same text on any machine or culture. Read is strict: a missing, extra or malformed line fails instead of guessing.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FoodFactoryGame.World
{
    public static class WorldLayoutText
    {
        private const string Magic = "food-factory-world-layout";
        private const string Empty = "%";
        private const int TreesPerLine = 200;

        public static string Write(WorldLayout layout)
        {
            var text = new StringBuilder();
            void Line(params object[] parts) => text.Append(string.Join(" ", parts.Select(Token))).Append('\n');
            Line(Magic, layout.FormatVersion);
            Line("generator", layout.GeneratorVersion);
            Line("seed", layout.Seed, Escape(layout.RequestedSeed), layout.Attempt);
            Line("bounds", layout.CityHalfSize, layout.MapHalfSize);
            Line("start", Escape(layout.StartRestaurantId));
            foreach (var d in layout.Districts)
                Line("district", d.Id, d.Kind, d.MinRecipeTier, d.CustomersPerHour, d.TrafficPercent, d.PricePercent,
                    JoinList(d.Cuisines.Select(x => Escape(x.Cuisine) + ":" + Number(x.Weight))),
                    JoinList(d.Areas.Select(x => string.Join(",", Number(x.X), Number(x.Z), Number(x.Width), Number(x.Depth)))));
            // Format 1 is written exactly as generator v1 wrote it, so stored v1 layouts keep their hash.
            var v2 = layout.FormatVersion >= 2;
            foreach (var n in layout.Nodes)
                if (v2) Line("node", n.Id, n.X, n.Z, n.Control);
                else Line("node", n.Id, n.X, n.Z);
            foreach (var r in layout.Roads) Line("road", r.Id, r.FromId, r.ToId, r.Kind, r.Width, r.CapacityPerHour);
            foreach (var l in layout.Rails) Line("rail", l.Id, l.Width, Cells(l.Points));
            foreach (var b in layout.Buildings)
                if (v2)
                    Line("building", b.Id, b.Category, Escape(b.DistrictId), b.X, b.Z, b.Width, b.Depth, b.Facing, Cells(b.Doors),
                        Escape(b.ModelKey), b.Floors, b.Ownership, b.PriceCents, Escape(b.LineId), Escape(b.SiteId), b.ElevationCm);
                else
                    Line("building", b.Id, b.Category, Escape(b.DistrictId), b.X, b.Z, b.Width, b.Depth, b.Facing, Cells(b.Doors),
                        Escape(b.ModelKey), b.Floors, b.Ownership, b.PriceCents, Escape(b.LineId), Escape(b.SiteId));
            if (!v2)
            {
                Line("end", layout.Districts.Count, layout.Nodes.Count, layout.Roads.Count, layout.Rails.Count, layout.Buildings.Count);
                return text.ToString();
            }
            var terrain = layout.Terrain ?? WorldTerrain.Flat();
            Line("terrain", terrain.Spacing, terrain.Samples);
            for (var row = 0; row < terrain.Samples; row++)
                Line("heights", row, JoinList(Enumerable.Range(0, terrain.Samples).Select(x => Number(terrain.Sample(x, row)))));
            foreach (var r in layout.Rivers) Line("river", r.Id, r.Width, r.SurfaceDropCm, Cells(r.Points));
            foreach (var b in layout.Bridges) Line("bridge", b.Id, b.CarriesId, b.RiverId, Cells(new[] { b.From }), Cells(new[] { b.To }));
            foreach (var c in layout.Crossings) Line("crossing", c.Id, c.RoadId, c.RailId, c.X, c.Z);
            for (var first = 0; first < layout.Trees.Count; first += TreesPerLine)
                Line("trees", JoinList(layout.Trees.Skip(first).Take(TreesPerLine)
                    .Select(t => string.Join(",", Number(t.X), Number(t.Z), Number((int)t.Kind), Number(t.Scale)))));
            Line("end", layout.Districts.Count, layout.Nodes.Count, layout.Roads.Count, layout.Rails.Count, layout.Buildings.Count,
                layout.Rivers.Count, layout.Bridges.Count, layout.Crossings.Count, layout.Trees.Count);
            return text.ToString();
        }

        public static WorldLayout Read(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new FormatException("Empty world layout.");
            var lines = text.Split('\n');
            var row = 0;
            string[] Next(string keyword, int count)
            {
                if (row >= lines.Length) throw new FormatException($"World layout ended before '{keyword}'.");
                var parts = lines[row].Split(' ');
                if (parts[0] != keyword || parts.Length != count + 1) throw new FormatException($"World layout line {row + 1}: expected '{keyword}'.");
                row++;
                return parts;
            }
            string Peek() => row < lines.Length ? lines[row].Split(' ')[0] : "";

            var head = Next(Magic, 1);
            var layout = new WorldLayout { FormatVersion = Int(head[1]) };
            if (layout.FormatVersion < 1 || layout.FormatVersion > WorldLayout.CurrentFormat) throw new NotSupportedException($"World layout format {layout.FormatVersion} is not supported.");
            layout.GeneratorVersion = Int(Next("generator", 1)[1]);
            var seed = Next("seed", 3);
            layout.Seed = ulong.Parse(seed[1], NumberStyles.None, CultureInfo.InvariantCulture);
            layout.RequestedSeed = Unescape(seed[2]);
            layout.Attempt = Int(seed[3]);
            var bounds = Next("bounds", 2);
            layout.CityHalfSize = Int(bounds[1]);
            layout.MapHalfSize = Int(bounds[2]);
            layout.StartRestaurantId = Unescape(Next("start", 1)[1]);
            while (Peek() == "district")
            {
                var p = Next("district", 8);
                layout.Districts.Add(new WorldDistrict
                {
                    Id = p[1], Kind = ParseEnum<DistrictKind>(p[2]), MinRecipeTier = Int(p[3]), CustomersPerHour = Int(p[4]), TrafficPercent = Int(p[5]),
                    PricePercent = Int(p[6]),
                    Cuisines = Items(p[7]).Select(x =>
                    {
                        var pair = x.Split(':');
                        if (pair.Length != 2) throw new FormatException("Bad cuisine weight.");
                        return new CuisineWeight { Cuisine = Unescape(pair[0]), Weight = Int(pair[1]) };
                    }).ToList(),
                    Areas = Items(p[8]).Select(x =>
                    {
                        var v = Ints(x, 4);
                        return new WorldRect(v[0], v[1], v[2], v[3]);
                    }).ToList()
                });
            }
            var v2 = layout.FormatVersion >= 2;
            while (Peek() == "node")
            {
                var p = Next("node", v2 ? 4 : 3);
                layout.Nodes.Add(new RoadNode
                {
                    Id = p[1], X = Int(p[2]), Z = Int(p[3]), Control = v2 ? ParseEnum<JunctionControl>(p[4]) : JunctionControl.None
                });
            }
            while (Peek() == "road")
            {
                var p = Next("road", 6);
                layout.Roads.Add(new RoadSegment
                {
                    Id = p[1], FromId = p[2], ToId = p[3], Kind = ParseEnum<RoadKind>(p[4]), Width = Int(p[5]), CapacityPerHour = Int(p[6])
                });
            }
            while (Peek() == "rail")
            {
                var p = Next("rail", 3);
                layout.Rails.Add(new RailLine { Id = p[1], Width = Int(p[2]), Points = ReadCells(p[3]) });
            }
            while (Peek() == "building")
            {
                var p = Next("building", v2 ? 16 : 15);
                layout.Buildings.Add(new WorldBuilding
                {
                    Id = p[1], Category = ParseEnum<BuildingCategory>(p[2]), DistrictId = Unescape(p[3]), X = Int(p[4]), Z = Int(p[5]),
                    Width = Int(p[6]), Depth = Int(p[7]), Facing = ParseEnum<Facing>(p[8]), Doors = ReadCells(p[9]), ModelKey = Unescape(p[10]),
                    Floors = Int(p[11]), Ownership = ParseEnum<Ownership>(p[12]), PriceCents = long.Parse(p[13], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                    LineId = Unescape(p[14]), SiteId = Unescape(p[15]), ElevationCm = v2 ? Int(p[16]) : 0
                });
            }
            if (!v2)
            {
                var end1 = Next("end", 5);
                if (Int(end1[1]) != layout.Districts.Count || Int(end1[2]) != layout.Nodes.Count || Int(end1[3]) != layout.Roads.Count
                    || Int(end1[4]) != layout.Rails.Count || Int(end1[5]) != layout.Buildings.Count)
                    throw new FormatException("World layout counts do not match.");
                if (row != lines.Length - 1 || lines[row] != "") throw new FormatException("Unexpected text after the world layout.");
                return layout;
            }
            var terrainLine = Next("terrain", 2);
            var terrain = new WorldTerrain { Spacing = Int(terrainLine[1]), Samples = Int(terrainLine[2]) };
            if (terrain.Spacing < 0 || terrain.Samples < 0 || (long)terrain.Samples * terrain.Samples > 4_000_000) throw new FormatException("Bad terrain size.");
            terrain.HeightsCm = new int[terrain.Samples * terrain.Samples];
            for (var z = 0; z < terrain.Samples; z++)
            {
                var p = Next("heights", 2);
                var values = Items(p[2]).ToArray();
                if (Int(p[1]) != z || values.Length != terrain.Samples) throw new FormatException("Bad terrain row.");
                for (var x = 0; x < terrain.Samples; x++) terrain.HeightsCm[z * terrain.Samples + x] = Int(values[x]);
            }
            layout.Terrain = terrain;
            while (Peek() == "river")
            {
                var p = Next("river", 4);
                layout.Rivers.Add(new WorldRiver { Id = p[1], Width = Int(p[2]), SurfaceDropCm = Int(p[3]), Points = ReadCells(p[4]) });
            }
            while (Peek() == "bridge")
            {
                var p = Next("bridge", 5);
                var from = ReadCells(p[4]);
                var to = ReadCells(p[5]);
                if (from.Count != 1 || to.Count != 1) throw new FormatException("Bad bridge ends.");
                layout.Bridges.Add(new WorldBridge { Id = p[1], CarriesId = p[2], RiverId = p[3], From = from[0], To = to[0] });
            }
            while (Peek() == "crossing")
            {
                var p = Next("crossing", 5);
                layout.Crossings.Add(new LevelCrossing { Id = p[1], RoadId = p[2], RailId = p[3], X = Int(p[4]), Z = Int(p[5]) });
            }
            while (Peek() == "trees")
            {
                var p = Next("trees", 1);
                foreach (var item in Items(p[1]))
                {
                    var v = Ints(item, 4);
                    if (!System.Enum.IsDefined(typeof(TreeKind), v[2])) throw new FormatException($"Unknown tree kind {v[2]}.");
                    layout.Trees.Add(new WorldTree { X = v[0], Z = v[1], Kind = (TreeKind)v[2], Scale = v[3] });
                }
            }
            var end = Next("end", 9);
            if (Int(end[1]) != layout.Districts.Count || Int(end[2]) != layout.Nodes.Count || Int(end[3]) != layout.Roads.Count
                || Int(end[4]) != layout.Rails.Count || Int(end[5]) != layout.Buildings.Count || Int(end[6]) != layout.Rivers.Count
                || Int(end[7]) != layout.Bridges.Count || Int(end[8]) != layout.Crossings.Count || Int(end[9]) != layout.Trees.Count)
                throw new FormatException("World layout counts do not match.");
            if (row != lines.Length - 1 || lines[row] != "") throw new FormatException("Unexpected text after the world layout.");
            return layout;
        }

        // Lower-case hex SHA-256 of the UTF-8 canonical text: the layout's identity for tests, storage and replication.
        public static string Hash(string text)
        {
            using var sha = SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text)).Select(x => x.ToString("x2", CultureInfo.InvariantCulture)));
        }

        public static string Hash(WorldLayout layout) => Hash(Write(layout));

        private static string Token(object value) => value switch
        {
            null => Empty,
            string text => text.Length == 0 ? Empty : text,
            int number => Number(number),
            long number => number.ToString(CultureInfo.InvariantCulture),
            ulong number => number.ToString(CultureInfo.InvariantCulture),
            Enum member => member.ToString(),
            _ => throw new ArgumentException($"Unsupported layout value {value.GetType()}.")
        };

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Escape(string value) => string.IsNullOrEmpty(value) ? Empty : Uri.EscapeDataString(value);

        private static string Unescape(string token) => token == Empty ? "" : Uri.UnescapeDataString(token);

        private static string JoinList(IEnumerable<string> items)
        {
            var joined = string.Join(";", items);
            return joined.Length == 0 ? Empty : joined;
        }

        private static IEnumerable<string> Items(string token) => token == Empty ? Array.Empty<string>() : token.Split(';');

        private static string Cells(IEnumerable<WorldCell> cells) => JoinList(cells.Select(x => Number(x.X) + "," + Number(x.Z)));

        private static List<WorldCell> ReadCells(string token) => Items(token).Select(x =>
        {
            var v = Ints(x, 2);
            return new WorldCell(v[0], v[1]);
        }).ToList();

        private static int[] Ints(string token, int count)
        {
            var parts = token.Split(',');
            if (parts.Length != count) throw new FormatException("Bad number list in world layout.");
            return parts.Select(Int).ToArray();
        }

        private static int Int(string token) => int.Parse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        private static T ParseEnum<T>(string token) where T : struct =>
            System.Enum.TryParse<T>(token, false, out var value) && System.Enum.IsDefined(typeof(T), value) && !char.IsDigit(token[0]) && token[0] != '-'
                ? value
                : throw new FormatException($"Unknown {typeof(T).Name} '{token}' in world layout.");
    }
}
