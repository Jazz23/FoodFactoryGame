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
        // Gap between neighbouring lots along a street, inclusive range in metres.
        public int MinGap;
        public int MaxGap;
        // Chance of a street tree at each sidewalk spot, and of a tree at each point of the yard lattice.
        public int StreetTreePercent;
        public int YardTreePercent;
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
        // PROTOTYPE geometry (densified for generator v2, owner request 2026-09-28): a 1000 m city (5x5 superblocks between 6 arterials each way) in a 1900 m map.
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
        // Gap between a road's edge and a building's street wall. It is part of a purchasable building's lot (generator v3).
        public int Setback = 2;
        // Deeper gaps (generator v3) for factories and farms: their lot's apron in front of the building, for docks and trucks.
        public int FactorySetback = 12;
        public int FarmSetback = 8;

        // Land (generator v2): value noise in centimetres, one octave per lattice size, sampled every TerrainSpacing metres
        // (which must divide the map). Relief is damped to CityReliefPercent inside the city (full relief TerrainCityFade m
        // outside it) and to RailReliefPercent along rail lines. A building's footprint may rise or fall at most
        // MaxFootprintRiseCm from its entrance; lots on steeper land are left empty. Farms (fields) are exempt.
        public int TerrainSpacing = 20;
        public int[] TerrainLatticeMetres = { 480, 200, 80 };
        public int[] TerrainAmplitudeCm = { 2400, 650, 150 };
        public int CityReliefPercent = 22;
        public int TerrainCityFade = 220;
        public int RailReliefPercent = 20;
        public int RailReliefFade = 120;
        public int MaxFootprintRiseCm = 150;

        // River (generator v2): crosses the whole map along one row of superblocks (never the industrial row), meandering
        // inside it. Bridges and buildings keep RiverBank metres clear of the water; the valley floor is flattened within
        // RiverValleyFlat of the centreline and blends back to the hills by RiverValleyFade.
        public int RiverWidth = 22;
        public int RiverBank = 12;
        public int RiverSurfaceDropCm = 160;
        public int RiverValleyDropCm = 150;
        public int RiverValleyFlat = 30;
        public int RiverValleyFade = 150;
        public int RiverMeanderStep = 160;
        // Clearance between the water and a parallel street, which is dropped when it would come closer.
        public int RiverStreetClearance = 10;

        // Trees (generator v2, scenery): sidewalk trees every StreetTreeSpacing metres (plus jitter) on city streets, a yard
        // lattice inside city blocks, and a countryside lattice with woodland where the woodland noise is high.
        public int StreetTreeSpacing = 13;
        public int StreetTreeJunctionClear = 14;
        public int YardTreeLattice = 8;
        public int CountryTreeLattice = 10;
        public int WoodlandLatticeMetres = 220;
        public int WoodlandThresholdPercent = 62;
        public int WoodlandTreePercent = 70;
        public int LoneTreePercent = 3;
        public int RiverbankTreePercent = 55;
        public int RiverbankTreeBand = 20;

        // Rail: one line through the city from the farm line, one along the farmland beyond the industrial edge.
        public int RailWidth = 8;
        public int FarmLineOffset = 110;
        public int RailClearance = 25;
        public int FarmLineStations = 3;
        public int StationLength = 30;
        public int StationDepth = 10;

        // Farms along rural spurs.
        public SizeRange Farm = new(40, 70, 40, 70);
        public int FarmChancePercent = 70;
        public int MinFarmGap = 6;
        public int MaxFarmGap = 20;
        public int FarmIndustrialSideBonusPercent = 30;

        public SizeRange Restaurant = new(16, 24, 14, 20);
        public SizeRange Factory = new(20, 40, 18, 35);
        public SizeRange House = new(8, 12, 8, 12);
        public SizeRange LargeHouse = new(12, 18, 12, 18);
        public SizeRange Apartment = new(14, 22, 12, 18);
        public SizeRange Office = new(16, 28, 12, 24);

        // PROTOTYPE ownership and prices (whole cents per footprint cell, before the district multiplier).
        public int RestaurantForSalePercent = 30;
        public long RestaurantCentsPerCell = 15000;
        public long FactoryCentsPerCell = 8000;
        public long FarmCentsPerCell = 1500;
        public long StationCentsPerCell = 12000;
        // Largest footprint the starting restaurant may have.
        public int StartMaxArea = 400;

        public int MaxAttempts = 8;

        public List<DistrictProfile> Districts = new()
        {
            new DistrictProfile
            {
                Kind = DistrictKind.Downtown, Id = "downtown", MinRecipeTier = 1, CustomersPerHour = 900, TrafficPercent = 90,
                PricePercent = 250, MinLocalStreets = 3, MaxLocalStreets = 3, MinGap = 0, MaxGap = 1, StreetTreePercent = 70, YardTreePercent = 15,
                RestaurantWeight = 30, OfficeWeight = 45, ApartmentWeight = 25,
                Cuisines = Weights(("fast-food", 40), ("noodles", 30), ("bakery", 20), ("grill", 10))
            },
            new DistrictProfile
            {
                Kind = DistrictKind.Residential, Id = "residential", MinRecipeTier = 1, CustomersPerHour = 500, TrafficPercent = 40,
                PricePercent = 60, MinLocalStreets = 2, MaxLocalStreets = 3, MinGap = 1, MaxGap = 3, StreetTreePercent = 75, YardTreePercent = 35,
                RestaurantWeight = 20, HouseWeight = 55, ApartmentWeight = 25,
                Cuisines = Weights(("bakery", 30), ("fast-food", 30), ("noodles", 25), ("grill", 15))
            },
            new DistrictProfile
            {
                Kind = DistrictKind.Wealthy, Id = "wealthy", MinRecipeTier = 3, CustomersPerHour = 350, TrafficPercent = 30,
                PricePercent = 300, MinLocalStreets = 2, MaxLocalStreets = 2, MinGap = 2, MaxGap = 4, StreetTreePercent = 90, YardTreePercent = 50,
                RestaurantWeight = 20, HouseWeight = 70, ApartmentWeight = 10,
                Cuisines = Weights(("fine-dining", 40), ("bakery", 30), ("grill", 20), ("noodles", 10))
            },
            new DistrictProfile
            {
                Kind = DistrictKind.Industrial, Id = "industrial", MinRecipeTier = 1, CustomersPerHour = 120, TrafficPercent = 60,
                PricePercent = 40, MinLocalStreets = 1, MaxLocalStreets = 2, MinGap = 2, MaxGap = 4, StreetTreePercent = 15, YardTreePercent = 6,
                RestaurantWeight = 8, FactoryWeight = 92,
                Cuisines = Weights(("fast-food", 60), ("grill", 30), ("noodles", 10))
            }
        };

        public static WorldSettings Default => new();

        public DistrictProfile Profile(DistrictKind kind) => Districts.Find(x => x.Kind == kind);

        public int SetbackFor(BuildingCategory category) => category switch
        {
            BuildingCategory.Factory => FactorySetback,
            BuildingCategory.Farm => FarmSetback,
            _ => Setback
        };

        private static List<CuisineWeight> Weights(params (string Cuisine, int Weight)[] weights)
        {
            var list = new List<CuisineWeight>();
            foreach (var (cuisine, weight) in weights) list.Add(new CuisineWeight { Cuisine = cuisine, Weight = weight });
            return list;
        }
    }
}
