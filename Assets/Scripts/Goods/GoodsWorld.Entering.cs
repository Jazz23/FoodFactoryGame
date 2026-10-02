// Carrying goods between owned sites (decisions 0029, 0031). A player's carried location and every machine the player holds are
// on one site, which the player is granted; entering another listed lot moves all of it there in one commit, re-owning the
// carried lots by the new site as a truck delivery does. The server checks the player's own position (map metres, supplied by
// the session from its copy of the avatar) against the lot plus a margin; avatar movement is client-controlled, so this is a
// sanity check, not anti-cheat. Nothing is created or lost: a rejection records and changes nothing, and a retried accepted
// request replays its outcome.
using System;
using System.Linq;

namespace FoodFactoryGame.Goods
{
    public sealed partial class GoodsWorld
    {
        // PROTOTYPE (decision 0031): how far outside its lot (metres) a player may stand and still enter it.
        public const float EnterMarginMetres = 2f;

        // Volatile primitive for tests. Live request handlers must call EnterSiteDurably.
        public GoodsOutcome EnterSite(string playerId, string requestId, string siteId, float mapX, float mapZ)
        {
            lock (_gate)
            {
                if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(requestId))
                    return new GoodsOutcome { Accepted = false, Reason = "invalid-identity" };
                var replay = Replay(playerId, requestId);
                if (replay is not null) return replay;
                GoodsOutcome Reject(string reason) => new()
                {
                    RequestId = requestId, PlayerId = playerId, Accepted = false, Reason = reason, Revision = _state.Revision
                };
                if (!_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == siteId)) return Reject("forbidden");
                var lot = _propertyOffers?.Values.FirstOrDefault(x => x.SiteId == siteId);
                if (lot is null || !SiteExists(_state, siteId)) return Reject("unknown-site");
                if (float.IsNaN(mapX) || float.IsNaN(mapZ) || mapX < lot.LotX - EnterMarginMetres || mapX > lot.LotX + lot.Width + EnterMarginMetres
                    || mapZ < lot.LotZ - EnterMarginMetres || mapZ > lot.LotZ + lot.Depth + EnterMarginMetres)
                    return Reject("not-on-lot");
                var inventory = _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId));
                if (inventory is null) return Reject("no-inventory");
                if (inventory.SiteId == siteId) return Reject("already-there");
                var carried = _state.Lots.Where(x => x.LocationId == inventory.Id).ToList();
                if (carried.Any(x => _state.Reservations.Any(y => y.Active && y.LotId == x.Id))) return Reject("reserved");

                // All checks precede this single locked mutation. Lots keep their IDs, quantities and exposure.
                inventory.SiteId = siteId;
                foreach (var carriedLot in carried) carriedLot.OwnerId = siteId;
                foreach (var held in _state.Equipment.Where(x => x.State == EquipmentState.Held && x.HolderId == playerId)) held.SiteId = siteId;
                // A player who leaves no longer staffs a register on the old site (decision 0034).
                if (ReleaseStaffLocked(playerId)) InvalidateDiners();
                return Record(requestId, playerId, true, "entered", null);
            }
        }

        public GoodsOutcome EnterSiteDurably(string playerId, string requestId, string siteId, float mapX, float mapZ, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => EnterSite(playerId, requestId, siteId, mapX, mapZ));
        }

        // The site holding a player's carried location, or null without one: where the player works (joining names it).
        public string CarriedSiteOf(string playerId)
        {
            lock (_gate) return _state.Locations.FirstOrDefault(x => x.Id == InventoryLocationId(playerId))?.SiteId;
        }

        // Invariant (decision 0029): a carried location belongs to an actor granted its site, and every held machine is on its
        // holder's carried location's site.
        private static void ValidateCarrying(GoodsSnapshot state)
        {
            foreach (var location in state.Locations.Where(x => x.Id.StartsWith(CarriedPrefix, StringComparison.Ordinal)))
            {
                var holder = location.Id.Substring(CarriedPrefix.Length);
                if (!state.Grants.Any(x => x.PlayerId == holder && x.SiteId == location.SiteId))
                    throw new InvalidOperationException($"{location.Id} is on a site its holder is not granted.");
            }
            foreach (var held in state.Equipment.Where(x => x.State == EquipmentState.Held))
                if (!state.Locations.Any(x => x.Id == InventoryLocationId(held.HolderId) && x.SiteId == held.SiteId))
                    throw new InvalidOperationException($"Held equipment {held.Id} is not on its holder's carried site.");
        }
    }
}
