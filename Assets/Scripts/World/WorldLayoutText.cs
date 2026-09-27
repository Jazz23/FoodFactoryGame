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
            foreach (var n in layout.Nodes) Line("node", n.Id, n.X, n.Z);
            foreach (var r in layout.Roads) Line("road", r.Id, r.FromId, r.ToId, r.Kind, r.Width, r.CapacityPerHour);
            foreach (var l in layout.Rails) Line("rail", l.Id, l.Width, Cells(l.Points));
            foreach (var b in layout.Buildings)
                Line("building", b.Id, b.Category, Escape(b.DistrictId), b.X, b.Z, b.Width, b.Depth, b.Facing, Cells(b.Doors),
                    Escape(b.ModelKey), b.Floors, b.Ownership, b.PriceCents, Escape(b.LineId), Escape(b.SiteId));
            Line("end", layout.Districts.Count, layout.Nodes.Count, layout.Roads.Count, layout.Rails.Count, layout.Buildings.Count);
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
            if (layout.FormatVersion != WorldLayout.CurrentFormat) throw new NotSupportedException($"World layout format {layout.FormatVersion} is not supported.");
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
            while (Peek() == "node")
            {
                var p = Next("node", 3);
                layout.Nodes.Add(new RoadNode { Id = p[1], X = Int(p[2]), Z = Int(p[3]) });
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
                var p = Next("building", 15);
                layout.Buildings.Add(new WorldBuilding
                {
                    Id = p[1], Category = ParseEnum<BuildingCategory>(p[2]), DistrictId = Unescape(p[3]), X = Int(p[4]), Z = Int(p[5]),
                    Width = Int(p[6]), Depth = Int(p[7]), Facing = ParseEnum<Facing>(p[8]), Doors = ReadCells(p[9]), ModelKey = Unescape(p[10]),
                    Floors = Int(p[11]), Ownership = ParseEnum<Ownership>(p[12]), PriceCents = long.Parse(p[13], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture),
                    LineId = Unescape(p[14]), SiteId = Unescape(p[15])
                });
            }
            var end = Next("end", 5);
            if (Int(end[1]) != layout.Districts.Count || Int(end[2]) != layout.Nodes.Count || Int(end[3]) != layout.Roads.Count
                || Int(end[4]) != layout.Rails.Count || Int(end[5]) != layout.Buildings.Count)
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
