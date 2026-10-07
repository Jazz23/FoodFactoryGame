// Authored equipment content: grid footprint, buffer capacities, visual and inventory icon. Converted to a plain
// GoodsEquipment record when the server creates a piece; the record keeps copies, so saved equipment never depends on this
// asset afterwards. Decision 0034 adds the occupancy layer and ambience points (copied into the record), and presentation-only
// build-catalog metadata: display name, category, and how the visual is mounted (on the floor, flush in it, on a wall face, under
// the ceiling, on a table top, or backed against a wall).
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    // Where a placed visual stands (presentation only).
    public enum EquipmentMount
    {
        // Standing on the floor (machines, furniture).
        Floor,
        // A floor finish: its top flush with the floor.
        Flush,
        // Hung on the face of its wall cell, facing the piece's rotation, at mountHeight.
        Wall,
        // Hung from the ceiling (its pivot at ceiling height).
        Ceiling,
        // Standing on the top of the table under it.
        Tabletop,
        // Standing on the floor with its back to the wall behind its footprint (a sink); against no wall it stands at the back of
        // its footprint.
        Backed
    }

    [CreateAssetMenu(menuName = "Food Factory/Equipment Definition")]
    public sealed class EquipmentDefinition : ScriptableObject
    {
        [SerializeField] private string kind;
        [SerializeField, Min(1)] private int width = 1;
        [SerializeField, Min(1)] private int depth = 1;
        [SerializeField, Min(1)] private int inputCapacity = 1;
        [SerializeField, Min(1)] private int outputCapacity = 1;
        [SerializeField] private bool inputRefrigerated;
        [SerializeField] private bool outputRefrigerated;
        // Dining seats (decision 0024): above zero only for a table.
        [SerializeField, Min(0)] private int seats;
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private Sprite icon;
        // Decision 0034: occupancy layer (SiteGrid.ObjectLayer is empty) and PROTOTYPE ambience points.
        [SerializeField] private string layer = "";
        [SerializeField, Min(0)] private int ambience;
        // Build catalog (presentation only): shown name, palette category, mount and height of a wall mount's centre (metres).
        [SerializeField] private string displayName;
        [SerializeField] private string category;
        [SerializeField] private EquipmentMount mount;
        [SerializeField] private float mountHeight = 1.5f;
        // Hidden in the top-down view while the local avatar is inside its building (ceiling panels would cover the room).
        [SerializeField] private bool hideFromAbove;
        // False for decor and furniture without goods or seats: clicking it opens nothing (its unused buffers stay closed).
        [SerializeField] private bool opensScreen = true;
        // Decision 0037: the machine has a power switch and starts and advances batches only while it is on (the oven).
        [SerializeField] private bool manualPower;

        public string Kind => kind;
        public int Width => width;
        public int Depth => depth;
        public int InputCapacity => inputCapacity;
        public int OutputCapacity => outputCapacity;
        public bool InputRefrigerated => inputRefrigerated;
        public bool OutputRefrigerated => outputRefrigerated;
        public int Seats => seats;
        public GameObject VisualPrefab => visualPrefab;
        public Sprite Icon => icon;
        public string Layer => layer ?? "";
        public int Ambience => ambience;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? kind : displayName;
        public string Category => category ?? "";
        public EquipmentMount Mount => mount;
        public float MountHeight => mountHeight;
        public bool HideFromAbove => hideFromAbove;
        public bool OpensScreen => opensScreen;
        public bool ManualPower => manualPower;

        public GoodsEquipment CreatePlaced(string id, string siteId, int cellX, int cellZ, int rotation) => new()
        {
            Id = id, Kind = kind, SiteId = siteId, State = EquipmentState.Placed, HolderId = "",
            CellX = cellX, CellZ = cellZ, Rotation = rotation, Width = width, Depth = depth,
            InputCapacity = inputCapacity, OutputCapacity = outputCapacity, InputRefrigerated = inputRefrigerated,
            OutputRefrigerated = outputRefrigerated, Seats = seats, Layer = Layer, Ambience = ambience
        };

        // Kind, footprint and capacities only: the server gives a bought piece its ID, site and holder (decision 0017).
        public GoodsEquipment CreateTemplate() => new()
        {
            Kind = kind, State = EquipmentState.Held, Width = width, Depth = depth,
            InputCapacity = inputCapacity, OutputCapacity = outputCapacity, InputRefrigerated = inputRefrigerated,
            OutputRefrigerated = outputRefrigerated, Seats = seats, Layer = Layer, Ambience = ambience
        };
    }
}
