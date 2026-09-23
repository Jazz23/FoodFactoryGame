// Client side: once authenticated and the bridge is visible, subscribes once to a site and keeps its latest baseline.
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
        }
    }
}
