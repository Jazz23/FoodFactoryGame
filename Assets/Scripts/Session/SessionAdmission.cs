// Server-only join rule: resolve the durable identity first, then commit the site grants before admitting.
// The stores are not one transaction; a failed grant leaves an unprivileged identity that the next join reuses.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;

namespace FoodFactoryGame.Session
{
    public sealed class SessionAdmission
    {
        private readonly PlayerRegistry _registry;
        private readonly GoodsWorld _world;
        private readonly string _siteId;
        private readonly string _savePath;
        private readonly int _inventoryCapacity;
        private readonly IReadOnlyList<GoodsLot> _starterGoods;
        private readonly IReadOnlyList<string> _remoteSiteIds;

        // siteId is the primary site: the dev site, or a generated world's starting restaurant.
        public SessionAdmission(PlayerRegistry registry, GoodsWorld world, string siteId, string savePath, int inventoryCapacity = 0,
            IReadOnlyList<GoodsLot> starterGoods = null, IReadOnlyList<string> remoteSiteIds = null)
        {
            if (registry == null || world == null || string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(savePath))
                throw new ArgumentException("Registry, world, site and save path are required.");
            _registry = registry;
            _world = world;
            _siteId = siteId;
            _savePath = savePath;
            _inventoryCapacity = inventoryCapacity;
            _starterGoods = starterGoods;
            _remoteSiteIds = remoteSiteIds ?? Array.Empty<string>();
        }

        public PlayerRegistry Registry => _registry;
        // Sent to each admitted client, which subscribes to it.
        public string PrimarySiteId => _siteId;

        // PROTOTYPE rule: every authenticated player joins the one company that owns the primary site (GDD ownership is open):
        // it receives the primary site's grant and, when a capacity is configured, an inventory location there (with any
        // starter goods), committed together with the grant. Every other site of that company, and any listed remote site that
        // exists (decision 0022), is granted too, without an inventory, so the player can manage it from afar.
        // isConnected is checked against the existing identity before any write, so a rejected duplicate
        // cannot rename or otherwise touch the player who is already connected.
        public PlayerResolution Admit(string displayName, string secret, Func<string, bool> isConnected = null)
        {
            var existing = _registry.FindBySecret(secret);
            if (existing != null && isConnected != null && isConnected(existing)) return PlayerResolution.Rejected("already-connected");
            var identity = _registry.RegisterOrResolve(displayName, secret);
            if (!identity.Accepted) return identity;
            var others = _world.CompanySiteIds(_siteId).Concat(_remoteSiteIds.Where(_world.HasSite)).Where(x => x != _siteId).Distinct().ToList();
            var granted = _world.TryGrantDurably(identity.PlayerId, _siteId, _savePath, _inventoryCapacity, _starterGoods)
                && others.All(x => _world.TryGrantDurably(identity.PlayerId, x, _savePath));
            return granted ? identity : PlayerResolution.Rejected("persistence-unavailable");
        }
    }
}
