// Server-only join rule: resolve the durable identity first, then commit the site grant before admitting.
// The two stores are not one transaction; a failed grant leaves an unprivileged identity that the next join reuses.
using System;
using FoodFactoryGame.Goods;

namespace FoodFactoryGame.Session
{
    public sealed class SessionAdmission
    {
        private readonly PlayerRegistry _registry;
        private readonly GoodsWorld _world;
        private readonly string _siteId;
        private readonly string _savePath;

        public SessionAdmission(PlayerRegistry registry, GoodsWorld world, string siteId, string savePath)
        {
            if (registry == null || world == null || string.IsNullOrWhiteSpace(siteId) || string.IsNullOrWhiteSpace(savePath))
                throw new ArgumentException("Registry, world, site and save path are required.");
            _registry = registry;
            _world = world;
            _siteId = siteId;
            _savePath = savePath;
        }

        public PlayerRegistry Registry => _registry;

        // PROTOTYPE rule: every authenticated player receives the single dev-site grant (GDD ownership is open).
        // isConnected is checked against the existing identity before any write, so a rejected duplicate
        // cannot rename or otherwise touch the player who is already connected.
        public PlayerResolution Admit(string displayName, string secret, Func<string, bool> isConnected = null)
        {
            var existing = _registry.FindBySecret(secret);
            if (existing != null && isConnected != null && isConnected(existing)) return PlayerResolution.Rejected("already-connected");
            var identity = _registry.RegisterOrResolve(displayName, secret);
            if (!identity.Accepted) return identity;
            return _world.TryGrantDurably(identity.PlayerId, _siteId, _savePath)
                ? identity
                : PlayerResolution.Rejected("persistence-unavailable");
        }
    }
}
