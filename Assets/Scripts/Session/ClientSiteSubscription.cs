// Client side: once authenticated and the bridge is visible, subscribes once to a site, keeps its latest baseline,
// and forwards this client's command results so presentation can send requests through the same bridge. It can also watch
// the company's other sites for remote management (decision 0022); their baselines are kept apart from the primary one.
using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Managing;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Goods.Network;

namespace FoodFactoryGame.Session
{
    public sealed class ClientSiteSubscription
    {
        private readonly NetworkManager _manager;
        private readonly string _siteId;
        private readonly HashSet<string> _watched = new();
        private readonly Dictionary<string, GoodsSnapshot> _remote = new();
        private GoodsNetworkBridge _bridge;

        public ClientSiteSubscription(NetworkManager manager, string siteId)
        {
            _manager = manager;
            _siteId = siteId;
        }

        public GoodsSnapshot Latest { get; private set; }
        // Null until subscribed; requests sent through it are resolved to this connection's player by the server.
        public GoodsNetworkBridge Bridge => _bridge;
        // The site this client subscribes to; requests that name a site (purchases) use it.
        public string SiteId => _siteId;
        public event Action<GoodsOutcome> ResultReceived;
        public string LastRejection { get; private set; }

        // Call every frame; cheap once subscribed. Resets when the client connection or bridge goes away.
        public void Tick()
        {
            if (_bridge != null && (!_manager.IsClientStarted || !_bridge.IsClientStarted)) Reset();
            if (_bridge != null || !_manager.IsClientStarted || !_manager.ClientManager.Connection.IsAuthenticated) return;
            var spawned = _manager.ClientManager.Objects.Spawned.Values
                .Select(x => x.GetComponent<GoodsNetworkBridge>()).FirstOrDefault(x => x != null);
            if (spawned == null) return;
            _bridge = spawned;
            _bridge.SiteReceived += OnSite;
            _bridge.ResultReceived += OnResult;
            _bridge.RequestSite(_siteId);
            foreach (var site in _watched) _bridge.RequestSite(site);
        }

        // Also subscribes to another site (the server refuses one this player has no grant for). Kept across reconnects.
        public void Watch(string siteId)
        {
            if (string.IsNullOrWhiteSpace(siteId) || siteId == _siteId || !_watched.Add(siteId)) return;
            _bridge?.RequestSite(siteId);
        }

        // Latest baseline of a watched site; null until one arrives.
        public GoodsSnapshot Remote(string siteId) => _remote.TryGetValue(siteId ?? "", out var site) ? site : null;

        public void Reset()
        {
            if (_bridge != null)
            {
                _bridge.SiteReceived -= OnSite;
                _bridge.ResultReceived -= OnResult;
            }
            _bridge = null;
            Latest = null;
            _remote.Clear();
            LastRejection = null;
        }

        private void OnSite(GoodsSnapshot site)
        {
            if (site.Locations.Count == 0) return;
            var siteId = site.Locations[0].SiteId;
            if (siteId == _siteId) Latest = site;
            else if (_watched.Contains(siteId)) _remote[siteId] = site;
        }

        private void OnResult(GoodsOutcome outcome)
        {
            if (!outcome.Accepted && outcome.Reason == "subscription-forbidden") LastRejection = outcome.Reason;
            ResultReceived?.Invoke(outcome);
        }
    }
}
