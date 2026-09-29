// The generated world (GDD section 3): districts, the fixed road graph, rail lines, and every building, farm and station.
// A layout is created once when a world is created and then only read: loading never regenerates it (decision 0026).
// Coordinates are whole metres on the building cell grid (1 m cells), with the city centre at the origin, +X east, +Z north.
// A rectangle's X/Z is its minimum cell and Width/Depth count cells along X/Z. Roads and rails are centrelines with a width.
// Format 2 adds the land surface (WorldTerrain), rivers with bridges, level crossings, junction controls, building elevations
// and trees; a format 1 layout (generator v1) reads as flat land with none of them. Format 3 adds lots and reserved site IDs
// (decision 0028); format 1 and 2 layouts have no lots, so nothing in them can be bought.
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

    // How a road node is controlled (format 2). A stop sign stops the approaches of the lowest road kind meeting there, or every
    // approach when all are the same kind (WorldJunctions.Stops).
    public enum JunctionControl
    {
        None,
        StopSign,
        TrafficLight
    }

    public enum TreeKind
    {
        Broadleaf,
        Conifer,
        Poplar,
        Bush
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
        // None where at most two segments meet.
        public JunctionControl Control;
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

    // A river's centreline (points about 20 m apart, any direction) and its water width. The water surface lies SurfaceDropCm
    // below the land surface at the centreline; only bridges may stand over the water.
    public sealed class WorldRiver
    {
        public string Id;
        public int Width;
        public int SurfaceDropCm;
        public List<WorldCell> Points = new();
    }

    // A road segment or rail line carried over a river between two points of its centreline. The deck follows the land surface
    // (WorldTerrain), which runs smoothly over the river's channel.
    public sealed class WorldBridge
    {
        public string Id;
        // A road segment ID or a rail line ID.
        public string CarriesId;
        public string RiverId;
        public WorldCell From;
        public WorldCell To;
    }

    // A rail line crossing a road segment at grade, at the intersection of their centrelines.
    public sealed class LevelCrossing
    {
        public string Id;
        public string RoadId;
        public string RailId;
        public int X;
        public int Z;
    }

    // Scenery only. Scale is a percentage of the model's authored size.
    public sealed class WorldTree
    {
        public int X;
        public int Z;
        public TreeKind Kind;
        public int Scale;
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
        // The reserved site ID of the building's lot (format 3, decision 0028); empty for scenery and in formats 1 and 2.
        public string SiteId = "";
        // Ground-floor level in centimetres: the land height at the entrance (0 in format 1).
        public int ElevationCm;

        public bool IsShell => Category == BuildingCategory.Restaurant || Category == BuildingCategory.Factory;
        public bool IsPurchasable => Ownership == Ownership.ForSale || Ownership == Ownership.Competitor;
        // Property (for sale, a competitor's or the player's) stands on a lot; scenery does not.
        public bool HasLot => Ownership != Ownership.Scenery;
        public WorldRect Footprint => new(X, Z, Width, Depth);
        public int YawDegrees => (int)Facing * 90;
    }

    // A purchasable building's lot (format 3, decision 0028): the footprint extended forward to its street, so the gap in
    // front of the street wall (a factory's or farm's deeper apron) belongs to it. The building's site covers exactly the lot:
    // site cell (x, z) is world cell (X + x, Z + z), translation only. Access is the street cell in front of the lot, level
    // with the first door, that trucks drive to. IDs derive from the building ID, so they are stable for a stored layout.
    public sealed class WorldLot
    {
        public string Id;
        public string BuildingId;
        public string SiteId;
        public int X;
        public int Z;
        public int Width;
        public int Depth;
        public WorldCell Access;

        public WorldRect Rect => new(X, Z, Width, Depth);

        public static string IdFor(string buildingId) => "lot-" + buildingId;

        public static string SiteIdFor(string buildingId) => "site-" + buildingId;
    }

    public sealed class WorldLayout
    {
        // Format of WorldLayoutText; the generator version is separate (a new generator may keep the format).
        public const int CurrentFormat = 3;

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
        // Format 2; flat and empty in format 1.
        public WorldTerrain Terrain = WorldTerrain.Flat();
        public List<WorldRiver> Rivers = new();
        public List<WorldBridge> Bridges = new();
        public List<LevelCrossing> Crossings = new();
        public List<WorldTree> Trees = new();
        // Format 3; empty in formats 1 and 2. One per building that HasLot, in building order.
        public List<WorldLot> Lots = new();
    }
}
