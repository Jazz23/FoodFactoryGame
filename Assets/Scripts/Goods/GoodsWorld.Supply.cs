// Supplier purchases (decisions 0014, 0017): a player spends the site company's cash on a content offer and the goods, or a
// new machine held by the buyer, arrive in the player's inventory, debit and delivery in one commit. Offers are content like
// recipes (registered, never saved); goods and equipment offers share one ID space. The accepted outcome is keyed by player
// and request ID, so a retried request replays it and is never charged twice.
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

    // Content, not state: one machine. The template's kind, footprint and buffer capacities are copied into each bought piece,
    // like a placed machine's copy of its definition; identity, site, holder and placement come from the purchase.
    [Serializable] public sealed class EquipmentOffer
    {
        public string Id;
        // Whole cents for one machine.
        public long PriceCents;
        public GoodsEquipment Equipment;
    }

    public sealed partial class GoodsWorld
    {
        private readonly Dictionary<string, PurchaseOffer> _offers = new();
        private readonly Dictionary<string, EquipmentOffer> _equipmentOffers = new();

        public void RegisterOffer(PurchaseOffer offer)
        {
            lock (_gate)
            {
                if (offer is null || string.IsNullOrWhiteSpace(offer.Id) || string.IsNullOrWhiteSpace(offer.ItemId)
                    || offer.Quantity < 1 || offer.PriceCents < 1 || offer.SpoilAfterSeconds < 1 || OfferExistsLocked(offer.Id))
                    throw new ArgumentException("Invalid or duplicate offer.");
                _offers.Add(offer.Id, JsonUtility.FromJson<PurchaseOffer>(JsonUtility.ToJson(offer)));
            }
        }

        public void RegisterEquipmentOffer(EquipmentOffer offer)
        {
            lock (_gate)
            {
                var equipment = offer?.Equipment;
                if (offer is null || string.IsNullOrWhiteSpace(offer.Id) || offer.PriceCents < 1 || OfferExistsLocked(offer.Id)
                    || equipment is null || string.IsNullOrWhiteSpace(equipment.Kind) || equipment.Width < 1 || equipment.Depth < 1
                    || equipment.InputCapacity < 1 || equipment.OutputCapacity < 1)
                    throw new ArgumentException("Invalid or duplicate equipment offer.");
                _equipmentOffers.Add(offer.Id, JsonUtility.FromJson<EquipmentOffer>(JsonUtility.ToJson(offer)));
            }
        }

        private bool OfferExistsLocked(string offerId) => _offers.ContainsKey(offerId) || _equipmentOffers.ContainsKey(offerId);

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
                PurchaseOffer offer = null;
                EquipmentOffer machine = null;
                if (offerId is null || (!_offers.TryGetValue(offerId, out offer) && !_equipmentOffers.TryGetValue(offerId, out machine)))
                    return Reject("invalid-offer");
                var company = CompanyOfSiteLocked(siteId);
                if (company is null) return Reject("no-company");
                var inventory = _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId) && x.SiteId == siteId);
                if (inventory is null) return Reject("no-inventory");
                // A held machine takes no inventory slot (decision 0006), but its site needs a grid for it to be placed on.
                if (machine is not null && !_state.SiteLayouts.Any(x => x.SiteId == siteId)) return Reject("no-layout");
                if (offer is not null && !Fits(inventory.Id, offer.ItemId, false, offer.Quantity)) return Reject("capacity");
                if (_state.Companies.First(x => x.Id == company).Cash < (offer?.PriceCents ?? machine.PriceCents))
                    return Reject("insufficient-funds");

                // All checks precede this single locked mutation: pay, then deliver under a request-derived ID, which is unique
                // because an accepted request replays instead of running again.
                var deliveredId = $"buy:{playerId}:{requestId}";
                if (machine is not null)
                {
                    TryDebit(company, machine.PriceCents);
                    var equipment = JsonUtility.FromJson<GoodsEquipment>(JsonUtility.ToJson(machine.Equipment));
                    equipment.Id = deliveredId;
                    equipment.SiteId = siteId;
                    equipment.State = EquipmentState.Held;
                    equipment.HolderId = playerId;
                    equipment.CellX = equipment.CellZ = equipment.Rotation = equipment.Level = 0;
                    _state.Equipment.Add(equipment);
                    var bought = Record(requestId, playerId, true, "bought", null);
                    bought.EquipmentId = deliveredId;
                    _state.Outcomes[_state.Outcomes.Count - 1].EquipmentId = deliveredId;
                    return bought;
                }
                TryDebit(company, offer.PriceCents);
                var lotId = deliveredId;
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
