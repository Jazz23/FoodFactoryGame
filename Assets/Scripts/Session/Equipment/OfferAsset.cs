// Authored supplier offer (decision 0014): one pack of an item for a price in whole cents. The server registers it with
// GoodsWorld at start; offers are never saved, and a bought lot keeps its own spoil threshold.
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [CreateAssetMenu(menuName = "Food Factory/Supplier Offer")]
    public sealed class OfferAsset : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string itemId;
        [SerializeField, Min(1)] private int quantity = 1;
        [SerializeField, Min(1)] private int priceCents = 1;
        [SerializeField, Min(1)] private long spoilAfterSeconds = 1;

        public string Id => id;
        public string ItemId => itemId;
        public int Quantity => quantity;
        public long PriceCents => priceCents;

        public PurchaseOffer ToDefinition() => new()
        {
            Id = id, ItemId = itemId, Quantity = quantity, PriceCents = priceCents, SpoilAfterSeconds = spoilAfterSeconds
        };
    }
}
