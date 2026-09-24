// Authored supplier offer: one pack of an item (decision 0014) or, when an equipment definition is set, one machine (decision
// 0017), for a price in whole cents. The server registers it with GoodsWorld at start; offers are never saved, and a bought lot
// or machine keeps its own copy of what it needs.
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [CreateAssetMenu(menuName = "Food Factory/Supplier Offer")]
    public sealed class OfferAsset : ScriptableObject
    {
        [SerializeField] private string id;
        // Set for a machine offer; the item fields are then unused.
        [SerializeField] private EquipmentDefinition equipment;
        [SerializeField] private string itemId;
        [SerializeField, Min(1)] private int quantity = 1;
        [SerializeField, Min(1)] private int priceCents = 1;
        [SerializeField, Min(1)] private long spoilAfterSeconds = 1;

        public string Id => id;
        public EquipmentDefinition Equipment => equipment;
        public string ItemId => itemId;
        public int Quantity => equipment != null ? 1 : quantity;
        public long PriceCents => priceCents;

        public PurchaseOffer ToDefinition() => new()
        {
            Id = id, ItemId = itemId, Quantity = quantity, PriceCents = priceCents, SpoilAfterSeconds = spoilAfterSeconds
        };

        public EquipmentOffer ToEquipmentOffer() => new() { Id = id, PriceCents = priceCents, Equipment = equipment.CreateTemplate() };

        // Registers the offer as whichever kind it is.
        public void RegisterWith(GoodsWorld world)
        {
            if (equipment != null) world.RegisterEquipmentOffer(ToEquipmentOffer());
            else world.RegisterOffer(ToDefinition());
        }
    }
}
