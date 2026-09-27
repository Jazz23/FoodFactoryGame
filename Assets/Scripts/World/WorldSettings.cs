// Generator inputs. Every number here is a PROTOTYPE placeholder chosen by the implementer, not design data; the GDD fixes only
// the city size (about 1 km), the four districts and their character (section 3). Changing any value that affects output
// requires a new WorldGenerator.Version, because stored worlds are never regenerated.
using System.Collections.Generic;

namespace FoodFactoryGame.World
{
    public sealed class DistrictProfile
    {
        public DistrictKind Kind;
        public string Id;
        public List<CuisineWeight> Cuisines = new();
        public int MinRecipeTier;
        public int CustomersPerHour;
        public int TrafficPercent;
        public int PricePercent;
        // Local streets added across each superblock in each direction.
        public int MinLocalStreets;
        public int MaxLocalStreets;
        // Relative chance of each lot category along this district's streets.
        public int RestaurantWeight;
        public int FactoryWeight;
        public int HouseWeight;
        public int ApartmentWeight;
        public int OfficeWeight;
    }

    // Street-parallel (Along) and perpendicular (Deep) extent ranges, inclusive, in cells.
    public sealed class SizeRange
    {
        public int MinAlong, MaxAlong, MinDeep, MaxDeep;

        public SizeRange(int minAlong, int maxAlong, int minDeep, int maxDeep)
        {
            MinAlong = minAlong;
            MaxAlong = maxAlong;
            MinDeep = minDeep;
            MaxDeep = maxDeep;
        }
    }

    public sealed class WorldSettings
    {
        // PROTOTYPE geometry: a 1000 m city (5x5 superblocks between 6 arterials each way) in a 1900 m map.
        public int CityHalfSize = 500;
        public int MapHalfSize = 950;
        public int Superblocks = 5;
        public int ArterialJitter = 24;
        public int LocalStreetJitter = 8;
        // The outer ring road runs this far inside the map edge; rural spurs continue every arterial out to it.
        public int OuterRingInset = 40;
        public int ArterialWidth = 14;
        public int LocalWidth = 10;
        public int RuralWidth = 8;
        // PROTOTYPE capacities, vehicles per hour per segment.
        public int ArterialCapacity = 1800;
        public int LocalCapacity = 600;
        public int RuralCapacity = 400;
        // Gap between a road's edge and a building's street wall.
        public int Setback = 2;
        public int MinBuildingGap = 2;
        public int MaxBuildingGap = 6;

        // Rail: one line through the city from the farm line, one along the farmland beyond the industrial edge.
        public int RailWidth = 8;
        public int FarmLineOffset = 110;
        public int RailClearance = 25;
        public int FarmLineStations = 3;
        public int StationLength = 30;
        public int StationDepth = 10;

        // Farms along rural spurs.
        public SizeRange Farm = new(40, 70, 40, 70);
        public int FarmChancePercent = 45;
        public int MinFarmGap = 10;
        public int MaxFarmGap = 40;
        public int FarmIndustrialSideBonusPercent = 30;

        public SizeRange Restaurant = new(9, 16, 8, 14);
        public SizeRange Factory = new(20, 40, 18, 35);
        public SizeRange House = new(8, 12, 8, 12);
        public SizeRange LargeHouse = new(12, 18, 12, 18);
        public SizeRange Apartment = new(14, 22, 12, 18);
        public SizeRange Office = new(16, 28, 14, 24);

        // PROTOTYPE ownership and prices (whole cents per footprint cell, before the district multiplier).
        public int RestaurantForSalePercent = 30;
        public long RestaurantCentsPerCell = 15000;
        public long FactoryCentsPerCell = 8000;
        public long FarmCentsPerCell = 1500;
        public long StationCentsPerCell = 12000;
        // Largest footprint the starting restaurant may have.
        public int StartMaxArea = 160;

        public int MaxAttempts = 8;

        public List<DistrictProfile> Districts = new()
        {
            new DistrictProfile
            {
                Kind = DistrictKind.Downtown, Id = "downtown", MinRecipeTier = 1, CustomersPerHour = 900, TrafficPercent = 90,
                PricePercent = 250, MinLocalStreets = 2, MaxLocalStreets = 2,
                RestaurantWeight = 30, OfficeWeight = 45, ApartmentWeight = 25,
                Cuisines = Weights(("fast-food", 40), ("noodles", 30), ("bakery", 20), ("grill", 10))
            },
            new DistrictProfile
            {
                Kind = DistrictKind.Residential, Id = "residential", MinRecipeTier = 1, CustomersPerHour = 500, TrafficPercent = 40,
                PricePercent = 60, MinLocalStreets = 1, MaxLocalStreets = 2,
                RestaurantWeight = 20, HouseWeight = 55, ApartmentWeight = 25,
                Cuisines = Weights(("bakery", 30), ("fast-food", 30), ("noodles", 25), ("grill", 15))
            },
            new DistrictProfile
            {
                Kind = DistrictKind.Wealthy, Id = "wealthy", MinRecipeTier = 3, CustomersPerHour = 350, TrafficPercent = 30,
                PricePercent = 300, MinLocalStreets = 1, MaxLocalStreets = 1,
                RestaurantWeight = 20, HouseWeight = 70, ApartmentWeight = 10,
                Cuisines = Weights(("fine-dining", 40), ("bakery", 30), ("grill", 20), ("noodles", 10))
            },
            new DistrictProfile
            {
                Kind = DistrictKind.Industrial, Id = "industrial", MinRecipeTier = 1, CustomersPerHour = 120, TrafficPercent = 60,
                PricePercent = 40, MinLocalStreets = 0, MaxLocalStreets = 1,
                RestaurantWeight = 8, FactoryWeight = 92,
                Cuisines = Weights(("fast-food", 60), ("grill", 30), ("noodles", 10))
            }
        };

        public static WorldSettings Default => new();

        public DistrictProfile Profile(DistrictKind kind) => Districts.Find(x => x.Kind == kind);

        private static List<CuisineWeight> Weights(params (string Cuisine, int Weight)[] weights)
        {
            var list = new List<CuisineWeight>();
            foreach (var (cuisine, weight) in weights) list.Add(new CuisineWeight { Cuisine = cuisine, Weight = weight });
            return list;
        }
    }
}
