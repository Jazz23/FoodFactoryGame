// Supplier purchases (decision 0014): a player spends the site company's cash on a content offer and the goods arrive in the
// player's inventory, debit and delivery in one commit. Offers are content like recipes (registered, never saved). The
// accepted outcome is keyed by player and request ID, so a retried request replays it and is never charged twice.
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
                if (replay is not null) return replay;
                // Rejections are answered, not recorded: they change nothing, so they cost no commit and cannot grow the saved
                // outcomes (repeated failing clicks). Only the accepted purchase, the one that charges, is stored for replay;
                // a rejected request retried later is evaluated afresh and can be accepted at most once.
                GoodsOutcome Reject(string reason) => new()
                {
                    RequestId = requestId, PlayerId = playerId, Accepted = false, Reason = reason, Revision = _state.Revision
                };
                if (!_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == siteId)) return Reject("forbidden");
                if (offerId is null || !_offers.TryGetValue(offerId, out var offer)) return Reject("invalid-offer");
                var company = CompanyOfSiteLocked(siteId);
                if (company is null) return Reject("no-company");
                var inventory = _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId) && x.SiteId == siteId);
                if (inventory is null) return Reject("no-inventory");
                if (!Fits(inventory.Id, offer.ItemId, false, offer.Quantity)) return Reject("capacity");
                if (_state.Companies.First(x => x.Id == company).Cash < offer.PriceCents) return Reject("insufficient-funds");

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
