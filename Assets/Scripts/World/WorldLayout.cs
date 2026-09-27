// The generated world (GDD section 3): districts, the fixed road graph, rail lines, and every building, farm and station.
// A layout is created once when a world is created and then only read: loading never regenerates it (decision 0026).
// Coordinates are whole metres on the building cell grid (1 m cells), with the city centre at the origin, +X east, +Z north.
// A rectangle's X/Z is its minimum cell and Width/Depth count cells along X/Z. Roads and rails are centrelines with a width.
using System.Collections.Generic;

namespace FoodFactoryGame.World
{
    public enum DistrictKind
    {
        Downtown,
        Residential,
        Wealthy,
        Industrial
    }

    // The side a building's doors face, toward its street. Index = quarter turns clockwise seen from above (yaw / 90).
    public enum Facing
    {
        North,
        East,
        South,
        West
    }

    public enum BuildingCategory
    {
        Restaurant,
        Factory,
        Farm,
        Station,
        House,
        Apartment,
        Office
    }

    public enum Ownership
    {
        // Premade scenery: never sold, holds no population.
        Scenery,
        ForSale,
        Competitor,
        // The player's starting restaurant.
        Player
    }

    public enum RoadKind
    {
        Arterial,
        Local,
        Rural
    }

    public sealed class WorldCell
    {
        public int X;
        public int Z;

        public WorldCell(int x, int z)
        {
            X = x;
            Z = z;
        }
    }

    public sealed class WorldRect
    {
        public int X;
        public int Z;
        public int Width;
        public int Depth;

        public WorldRect(int x, int z, int width, int depth)
        {
            X = x;
            Z = z;
            Width = width;
            Depth = depth;
        }

        public bool Contains(int x, int z) => x >= X && x < X + Width && z >= Z && z < Z + Depth;
    }

    public sealed class CuisineWeight
    {
        public string Cuisine;
        public int Weight;
    }

    // PROTOTYPE demand values; see WorldSettings.
    public sealed class WorldDistrict
    {
        public string Id;
        public DistrictKind Kind;
        public List<WorldRect> Areas = new();
        public List<CuisineWeight> Cuisines = new();
        public int MinRecipeTier;
        public int CustomersPerHour;
        public int TrafficPercent;
        // Building purchase price multiplier, percent of the base price.
        public int PricePercent;
    }

    public sealed class RoadNode
    {
        public string Id;
        public int X;
        public int Z;
    }

    // An axis-aligned piece of road between two adjacent nodes.
    public sealed class RoadSegment
    {
        public string Id;
        public string FromId;
        public string ToId;
        public RoadKind Kind;
        public int Width;
        public int CapacityPerHour;
    }

    // Axis-aligned polyline of track centreline points.
    public sealed class RailLine
    {
        public string Id;
        public int Width;
        public List<WorldCell> Points = new();
    }

    // Restaurants and factories are generated shells (decision 0019: footprint includes the walls, doors are non-corner
    // perimeter cells on the street side). Everything else records a premade model slot: ModelKey plus the footprint and
    // Facing, from which the transform follows (centre of the footprint, yaw = Facing * 90 degrees). Their Doors are the
    // entrance cells used for road access. Stations belong to a rail line and are generated platforms.
    public sealed class WorldBuilding
    {
        public string Id;
        public BuildingCategory Category;
        // Empty outside the city (farms, farmland stations).
        public string DistrictId = "";
        public int X;
        public int Z;
        public int Width;
        public int Depth;
        public Facing Facing;
        public List<WorldCell> Doors = new();
        // Empty for generated shells and stations.
        public string ModelKey = "";
        // Shells: storeys (decision 0020, always 1 when generated). Premade models: scenery storeys, a presentation hint.
        public int Floors = 1;
        public Ownership Ownership;
        // Whole cents; 0 for scenery.
        public long PriceCents;
        // Stations only.
        public string LineId = "";
        // Stub: which site (and SiteGrid) a purchasable building becomes is an open owner decision (decision 0026). Always empty.
        public string SiteId = "";

        public bool IsShell => Category == BuildingCategory.Restaurant || Category == BuildingCategory.Factory;
        public bool IsPurchasable => Ownership == Ownership.ForSale || Ownership == Ownership.Competitor;
        public WorldRect Footprint => new(X, Z, Width, Depth);
        public int YawDegrees => (int)Facing * 90;
    }

    public sealed class WorldLayout
    {
        // Format of WorldLayoutText; the generator version is separate (a new generator may keep the format).
        public const int CurrentFormat = 1;

        public int FormatVersion = CurrentFormat;
        public int GeneratorVersion;
        // As the creator entered it (or the random value's decimal text); Seed is what generation actually used.
        public string RequestedSeed = "";
        public ulong Seed;
        // 0 when the first seed validated; otherwise Seed = WorldRandom.DeriveSeed(requested, Attempt).
        public int Attempt;
        public int CityHalfSize;
        public int MapHalfSize;
        public string StartRestaurantId = "";
        public List<WorldDistrict> Districts = new();
        public List<RoadNode> Nodes = new();
        public List<RoadSegment> Roads = new();
        public List<RailLine> Rails = new();
        public List<WorldBuilding> Buildings = new();
    }
}
