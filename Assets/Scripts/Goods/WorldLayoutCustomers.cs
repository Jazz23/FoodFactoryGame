// Derives a generated world's customer map records from its stored layout (decision 0030): one spawning district per block
// (area) of each layout district and one competitor per competitor-owned restaurant lot. Everything is a pure function of the
// layout, so IDs are stable and never stored separately; the server adds any missing record once, by ID. Every tuning value
// here is a PROTOTYPE placeholder listed in decision 0030. Hashes use FNV-1a of the building ID, never string.GetHashCode.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.World;

namespace FoodFactoryGame.Goods
{
    public static class WorldLayoutCustomers
    {
        // A cuisine a district weights at least this much is one its customers like.
        public const int LikedCuisineWeight = 25;
        public const int AppearanceVariants = 4;

        // PROTOTYPE per district kind: wealth (0-100), dine-in share (%), walking range (Manhattan metres).
        private static (int Wealth, int DineIn, int Range) Tuning(DistrictKind kind) => kind switch
        {
            DistrictKind.Downtown => (55, 50, 350),
            DistrictKind.Residential => (40, 70, 400),
            DistrictKind.Wealthy => (85, 80, 450),
            DistrictKind.Industrial => (25, 40, 300),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        public static string DistrictId(WorldDistrict district, int block) => $"district-{district.Id}-{block}";

        public static string CompetitorId(string buildingId) => "competitor-" + buildingId;

        // One district record per block, spawning at the block's centre; the layout district's customers per game hour are shared
        // between its blocks by area (largest remainder, so the blocks add up to the district's rate exactly).
        public static List<GoodsDistrict> Districts(WorldLayout layout)
        {
            var result = new List<GoodsDistrict>();
            foreach (var district in layout.Districts)
            {
                var (wealth, dineIn, range) = Tuning(district.Kind);
                var liked = district.Cuisines.Where(x => x.Weight >= LikedCuisineWeight)
                    .OrderByDescending(x => x.Weight).ThenBy(x => x.Cuisine, StringComparer.Ordinal).Select(x => x.Cuisine).ToList();
                var areas = district.Areas.Select(x => (long)x.Width * x.Depth).ToList();
                var shares = Share(layout.RatePerGameHour(district), areas);
                for (var block = 0; block < district.Areas.Count; block++)
                {
                    var area = district.Areas[block];
                    result.Add(new GoodsDistrict
                    {
                        Id = DistrictId(district, block), Name = $"{district.Kind} {block + 1}",
                        MapX = area.X + area.Width / 2, MapZ = area.Z + area.Depth / 2, CustomersPerHour = shares[block],
                        WealthPercent = wealth, Appearance = district.Id, AppearanceVariants = AppearanceVariants,
                        LikedCuisines = liked.ToList(), DineInPercent = dineIn, RangeMetres = range
                    });
                }
            }
            return result;
        }

        // One competitor per competitor-owned building with a lot, at the lot's access point (the same rule as a site's map
        // position). Cuisine is drawn from its district's weights and tier is the district's minimum; price, servers, service
        // time and seats come from the building ID's hash.
        public static List<GoodsCompetitor> Competitors(WorldLayout layout)
        {
            var lots = layout.Lots.ToDictionary(x => x.BuildingId, StringComparer.Ordinal);
            var result = new List<GoodsCompetitor>();
            foreach (var building in layout.Buildings.Where(x => x.Ownership == Ownership.Competitor))
            {
                if (!lots.TryGetValue(building.Id, out var lot)) continue;
                var district = layout.Districts.FirstOrDefault(x => x.Id == building.DistrictId) ?? layout.Districts[0];
                var tier = Math.Max(1, district.MinRecipeTier);
                result.Add(new GoodsCompetitor
                {
                    Id = CompetitorId(building.Id), Name = building.Id, LotId = lot.Id, MapX = lot.Access.X, MapZ = lot.Access.Z,
                    Cuisine = Cuisine(district, Hash(building.Id, "cuisine")), Tier = tier,
                    PriceCents = 300 + (long)(Hash(building.Id, "price") % 7) * 100 + (tier - 1) * 300,
                    Servers = 1 + (int)(Hash(building.Id, "servers") % 3),
                    ServiceSeconds = 10 + (long)(Hash(building.Id, "service") % 21),
                    Seats = 4 + (int)(Hash(building.Id, "seats") % 5) * 4
                });
            }
            return result;
        }

        private static string Cuisine(WorldDistrict district, ulong hash)
        {
            var total = district.Cuisines.Sum(x => Math.Max(0, x.Weight));
            if (total == 0) return "";
            var pick = (long)(hash % (ulong)total);
            foreach (var weight in district.Cuisines)
            {
                pick -= Math.Max(0, weight.Weight);
                if (pick < 0) return weight.Cuisine;
            }
            return district.Cuisines[^1].Cuisine;
        }

        private static List<int> Share(int total, List<long> areas)
        {
            var sum = areas.Sum();
            if (sum == 0) return areas.Select(_ => 0).ToList();
            var shares = areas.Select(x => (int)(total * x / sum)).ToList();
            var order = Enumerable.Range(0, areas.Count).OrderByDescending(x => total * areas[x] % sum).ThenBy(x => x).ToList();
            var remainder = total - shares.Sum();
            for (var index = 0; index < remainder; index++) shares[order[index]]++;
            return shares;
        }

        private static ulong Hash(string id, string salt)
        {
            var hash = 14695981039346656037UL;
            foreach (var character in id + ":" + salt)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return hash;
        }
    }
}
