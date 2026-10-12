// Authored supplier offer: one pack of an item (decision 0014), or, when an equipment definition is set, one machine (decision
// 0017), or, when a truck definition is set, one truck (decision 0023), for a price in whole cents. The server registers it
// with GoodsWorld at start; offers are never saved, and a bought lot, machine or truck keeps its own copy of what it needs.
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Logistics;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [CreateAssetMenu(menuName = "Food Factory/Supplier Offer")]
    public sealed class OfferAsset : ScriptableObject
    {
        [SerializeField] private string id;
        // Set for a machine offer; the item fields are then unused.
        [SerializeField] private EquipmentDefinition equipment;
        // Set for a truck offer; the item fields are then unused.
        [SerializeField] private TruckDefinition truck;
        [SerializeField] private string itemId;
        [SerializeField, Min(1)] private int quantity = 1;
        [SerializeField, Min(1)] private int priceCents = 1;
        [SerializeField, Min(1)] private long spoilAfterSeconds = 1;

        public string Id => id;
        public EquipmentDefinition Equipment => equipment;
        public TruckDefinition Truck => truck;
        public string ItemId => itemId;
        public int Quantity => equipment != null || truck != null ? 1 : quantity;
        public long PriceCents => priceCents;

        // A pack of an ingredient made at runtime from a FoodBalance supply row (decision 0043), never saved as an asset.
        public static OfferAsset CreateSupply(string id, string itemId, int quantity, int priceCents, long spoilAfterSeconds)
        {
            var offer = CreateInstance<OfferAsset>();
            offer.hideFlags = HideFlags.DontSave;
            offer.name = id;
            offer.id = id;
            offer.itemId = itemId;
            offer.quantity = System.Math.Max(1, quantity);
            offer.priceCents = System.Math.Max(1, priceCents);
            offer.spoilAfterSeconds = System.Math.Max(1, spoilAfterSeconds);
            return offer;
        }

        public PurchaseOffer ToDefinition() => new()
        {
            Id = id, ItemId = itemId, Quantity = quantity, PriceCents = priceCents, SpoilAfterSeconds = spoilAfterSeconds
        };

        public EquipmentOffer ToEquipmentOffer() => new() { Id = id, PriceCents = priceCents, Equipment = equipment.CreateTemplate() };

        public TruckOffer ToTruckOffer() => new()
        {
            Id = id, PriceCents = priceCents, Name = truck.DisplayName, CargoSlots = truck.CargoSlots,
            SpeedMetresPerSecond = truck.SpeedMetresPerSecond, LoadUnitsPerSecond = truck.LoadUnitsPerSecond
        };

        // Registers the offer as whichever kind it is.
        public void RegisterWith(GoodsWorld world)
        {
            if (truck != null) world.RegisterTruckOffer(ToTruckOffer());
            else if (equipment != null) world.RegisterEquipmentOffer(ToEquipmentOffer());
            else world.RegisterOffer(ToDefinition());
        }
    }
}
