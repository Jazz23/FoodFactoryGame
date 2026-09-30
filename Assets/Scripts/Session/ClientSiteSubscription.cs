// Client side: once authenticated and the bridge is visible, subscribes once to this client's primary site, keeps its latest
// baseline, and forwards this client's command results so presentation can send requests through the same bridge. The server
// names the primary site in its join answer (the dev site, or a generated world's starting restaurant); nothing is subscribed
// before it is known. It can also watch the company's other sites for remote management (decision 0022); their baselines are
// kept apart from the primary one.
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
        private readonly HashSet<string> _watched = new();
        private readonly Dictionary<string, GoodsSnapshot> _remote = new();
        private string _siteId;
        private GoodsNetworkBridge _bridge;

        public ClientSiteSubscription(NetworkManager manager, string siteId = null)
        {
            _manager = manager;
            _siteId = string.IsNullOrWhiteSpace(siteId) ? null : siteId;
        }

        public GoodsSnapshot Latest { get; private set; }
        // Null until subscribed; requests sent through it are resolved to this connection's player by the server.
        public GoodsNetworkBridge Bridge => _bridge;
        // The site this client subscribes to (null until the server names it); requests that name a site (purchases) use it.
        public string SiteId => _siteId;
        public event Action<GoodsOutcome> ResultReceived;
        public string LastRejection { get; private set; }

        // Sets the primary site from the server's join answer. A different site (another world) drops the old baselines and
        // watched sites, which belonged to that world.
        public void SetPrimary(string siteId)
        {
            if (string.IsNullOrWhiteSpace(siteId) || siteId == _siteId) return;
            Reset();
            _watched.Clear();
            _siteId = siteId;
        }

        // Makes another subscribed site the current one after the server accepted entering it (decision 0031): the old current
        // site becomes a watched one, and each keeps its latest baseline. The server keeps sending both.
        public void SwitchTo(string siteId)
        {
            if (string.IsNullOrWhiteSpace(siteId) || siteId == _siteId) return;
            var oldId = _siteId;
            var oldLatest = Latest;
            _watched.Remove(siteId);
            Latest = _remote.TryGetValue(siteId, out var known) ? known : null;
            _remote.Remove(siteId);
            _siteId = siteId;
            if (oldId != null)
            {
                _watched.Add(oldId);
                if (oldLatest != null) _remote[oldId] = oldLatest;
            }
            if (Latest == null) _bridge?.RequestSite(siteId);
            UnityEngine.Debug.Log($"[Session] Current site is now {siteId}.");
        }

        // Call every frame; cheap once subscribed. Resets when the client connection or bridge goes away.
        public void Tick()
        {
            if (_bridge != null && (!_manager.IsClientStarted || !_bridge.IsClientStarted)) Reset();
            if (_bridge != null || _siteId == null || !_manager.IsClientStarted || !_manager.ClientManager.Connection.IsAuthenticated) return;
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

        public bool IsWatching(string siteId) => siteId != null && _watched.Contains(siteId);

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
            var siteId = GoodsWorld.ViewSiteId(site);
            if (siteId == null) return;
            if (siteId == _siteId) Latest = site;
            else if (_watched.Contains(siteId))
            {
                if (!_remote.ContainsKey(siteId)) UnityEngine.Debug.Log($"[Session] Receiving site {siteId} for remote management.");
                _remote[siteId] = site;
            }
        }

        private void OnResult(GoodsOutcome outcome)
        {
            if (!outcome.Accepted && outcome.Reason == "subscription-forbidden") LastRejection = outcome.Reason;
            ResultReceived?.Invoke(outcome);
        }
    }
}
