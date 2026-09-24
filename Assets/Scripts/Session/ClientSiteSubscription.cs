// Client side: once authenticated and the bridge is visible, subscribes once to a site, keeps its latest baseline,
// and forwards this client's command results so presentation can send requests through the same bridge.
using System;
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
        }

        public void Reset()
        {
            if (_bridge != null)
            {
                _bridge.SiteReceived -= OnSite;
                _bridge.ResultReceived -= OnResult;
            }
            _bridge = null;
            Latest = null;
            LastRejection = null;
        }

        private void OnSite(GoodsSnapshot site)
        {
            if (site.Locations.Count > 0 && site.Locations[0].SiteId == _siteId) Latest = site;
        }

        private void OnResult(GoodsOutcome outcome)
        {
            if (!outcome.Accepted && outcome.Reason == "subscription-forbidden") LastRejection = outcome.Reason;
            ResultReceived?.Invoke(outcome);
        }
    }
}
