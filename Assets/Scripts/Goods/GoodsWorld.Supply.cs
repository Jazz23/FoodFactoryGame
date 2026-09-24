// Supplier purchases (decision 0014): a player spends the site company's cash on a content offer and the goods arrive in the
// player's inventory, debit and delivery in one commit. Offers are content like recipes (registered, never saved). The
// outcome is keyed by player and request ID, so a retried request replays the first result and is never charged twice.
// PROTOTYPE: delivery is immediate, standing in for supplier logistics (GDD section 9).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    // Content, not state: one purchasable pack of an item.
    [Serializable] public sealed class PurchaseOffer
    {
        public string Id;
        public string ItemId;
        public int Quantity;
        // Whole cents for the whole pack.
        public long PriceCents;
        public long SpoilAfterSeconds;
    }

    public sealed partial class GoodsWorld
    {
        private readonly Dictionary<string, PurchaseOffer> _offers = new();

        public void RegisterOffer(PurchaseOffer offer)
        {
            lock (_gate)
            {
                if (offer is null || string.IsNullOrWhiteSpace(offer.Id) || string.IsNullOrWhiteSpace(offer.ItemId)
                    || offer.Quantity < 1 || offer.PriceCents < 1 || offer.SpoilAfterSeconds < 1 || _offers.ContainsKey(offer.Id))
                    throw new ArgumentException("Invalid or duplicate offer.");
                _offers.Add(offer.Id, JsonUtility.FromJson<PurchaseOffer>(JsonUtility.ToJson(offer)));
            }
        }

        // Volatile primitive for tests. Live request handlers must call BuyDurably.
        public GoodsOutcome Buy(string playerId, string requestId, string siteId, string offerId)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay != null) return replay;
                if (!_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == siteId))
                    return Record(requestId, playerId, false, "forbidden", null);
                if (offerId is null || !_offers.TryGetValue(offerId, out var offer))
                    return Record(requestId, playerId, false, "invalid-offer", null);
                var company = CompanyOfSiteLocked(siteId);
                if (company is null) return Record(requestId, playerId, false, "no-company", null);
                var inventory = _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId) && x.SiteId == siteId);
                if (inventory is null) return Record(requestId, playerId, false, "no-inventory", null);
                if (!Fits(inventory.Id, offer.ItemId, false, offer.Quantity)) return Record(requestId, playerId, false, "capacity", null);
                if (_state.Companies.First(x => x.Id == company).Cash < offer.PriceCents)
                    return Record(requestId, playerId, false, "insufficient-funds", null);

                // All checks precede this single locked mutation: pay, then deliver a fresh lot under a request-derived ID.
                TryDebit(company, offer.PriceCents);
                var lotId = $"buy:{playerId}:{requestId}";
                _state.Lots.Add(new GoodsLot
                {
                    Id = lotId, ItemId = offer.ItemId, OwnerId = siteId, LocationId = inventory.Id,
                    Quantity = offer.Quantity, SpoilAfterSeconds = offer.SpoilAfterSeconds
                });
                return Record(requestId, playerId, true, "bought", lotId);
            }
        }

        public GoodsOutcome BuyDurably(string playerId, string requestId, string siteId, string offerId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => Buy(playerId, requestId, siteId, offerId));
        }
    }
}
