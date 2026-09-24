// Authored equipment content: grid footprint, buffer capacities, visual and inventory icon. Converted to a plain
// GoodsEquipment record when the server creates a piece; the record keeps copies, so saved equipment never depends on this
// asset afterwards.
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
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
        [SerializeField] private GameObject visualPrefab;
        [SerializeField] private Sprite icon;

        public string Kind => kind;
        public int Width => width;
        public int Depth => depth;
        public int InputCapacity => inputCapacity;
        public int OutputCapacity => outputCapacity;
        public bool InputRefrigerated => inputRefrigerated;
        public bool OutputRefrigerated => outputRefrigerated;
        public GameObject VisualPrefab => visualPrefab;
        public Sprite Icon => icon;

        public GoodsEquipment CreatePlaced(string id, string siteId, int cellX, int cellZ, int rotation) => new()
        {
            Id = id, Kind = kind, SiteId = siteId, State = EquipmentState.Placed, HolderId = "",
            CellX = cellX, CellZ = cellZ, Rotation = rotation, Width = width, Depth = depth,
            InputCapacity = inputCapacity, OutputCapacity = outputCapacity, InputRefrigerated = inputRefrigerated,
            OutputRefrigerated = outputRefrigerated
        };

        // Kind, footprint and capacities only: the server gives a bought piece its ID, site and holder (decision 0017).
        public GoodsEquipment CreateTemplate() => new()
        {
            Kind = kind, State = EquipmentState.Held, Width = width, Depth = depth,
            InputCapacity = inputCapacity, OutputCapacity = outputCapacity, InputRefrigerated = inputRefrigerated,
            OutputRefrigerated = outputRefrigerated
        };
    }
}
