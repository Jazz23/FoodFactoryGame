// Client side of walking into another owned building (decisions 0029, 0031): when the local avatar stands on an owned lot other
// than the current site, one enter request is sent; on acceptance the current site switches, so the HUD, screens and placement
// follow it. Walking onto the street keeps the last site. A refused lot is not asked for again until the avatar has left it.
// The server decides: it checks the grant and its own copy of the avatar's position, and moves the carried goods itself.
using System;
using System.Linq;
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    public sealed class SiteEntry
    {
        private readonly ClientSiteSubscription _subscription;
        private (string RequestId, string SiteId)? _pending;
        private string _refused;

        public SiteEntry(ClientSiteSubscription subscription)
        {
            _subscription = subscription;
            _subscription.ResultReceived += OnResult;
        }

        // The last refusal's reason (for diagnostics and tests); null after an accepted entry.
        public string LastRejection { get; private set; }
        public bool Pending => _pending != null;
        // Raised on the client after the current site switched.
        public event Action<string> Entered;

        // Call every frame with the local avatar's position (null without one).
        public void Tick(Vector3? avatar)
        {
            var placement = SitePlacement.Active;
            var bridge = _subscription.Bridge;
            if (placement == null || bridge == null || avatar == null || _subscription.Latest == null)
            {
                if (bridge == null) _pending = null;
                return;
            }
            var here = placement.SiteAt(avatar.Value);
            if (here != _refused) _refused = null;
            if (here == null || here == _subscription.SiteId || here == _refused || _pending != null) return;
            if (!DrawnSites.OwnedSites(_subscription.Latest, _subscription.SiteId).Contains(here)) return;
            var requestId = Guid.NewGuid().ToString("N");
            _pending = (requestId, here);
            bridge.RequestEnterSite(requestId, here);
        }

        public void Reset() => _pending = null;

        private void OnResult(GoodsOutcome outcome)
        {
            if (_pending == null || outcome.RequestId != _pending.Value.RequestId) return;
            var siteId = _pending.Value.SiteId;
            _pending = null;
            if (!outcome.Accepted)
            {
                LastRejection = outcome.Reason;
                _refused = siteId;
                Debug.Log($"[Session] Entering {siteId} was refused: {outcome.Reason}.");
                return;
            }
            LastRejection = null;
            _subscription.SwitchTo(siteId);
            Entered?.Invoke(siteId);
        }
    }
}
