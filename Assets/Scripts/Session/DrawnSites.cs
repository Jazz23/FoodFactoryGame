// Client side: which sites this client draws (decisions 0029, 0031). It watches every site its company owns (read from the
// ownership records in each baseline of the current site, so rejoining re-watches them), and draws the current site plus each
// watched owned site whose lot lies within DrawRadius of the local camera. Sites further away still receive baselines for
// remote management. Presentation only: what is drawn never decides whether a site exists or progresses on the server.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    // One drawn site: its latest baseline and grid. Current is the site the player works in (inventory, HUD, placement).
    public sealed class DrawnSite
    {
        public string SiteId;
        public GoodsSnapshot Snapshot;
        public SiteLayout Layout;
        public bool Current;
    }

    public sealed class DrawnSites
    {
        // PROTOTYPE (decision 0029): owned sites are drawn within this distance of the local camera, and dropped only a little
        // further out, so a camera on the boundary does not rebuild them every frame.
        public const float DrawRadius = 300f;
        private const float DropMargin = 20f;

        private readonly ClientSiteSubscription _subscription;
        private readonly List<DrawnSite> _sites = new();
        // The placement the sites were last drawn with: a new one (the map shown or replaced) moves them, so it counts as a change.
        private SitePlacement _placement;

        public DrawnSites(ClientSiteSubscription subscription) => _subscription = subscription;

        // The current site first, then other drawn sites in site ID order.
        public IReadOnlyList<DrawnSite> Sites => _sites;
        // Changes whenever the set of drawn sites or any of their baselines changes, so presenters rebuild only then.
        public int Version { get; private set; }

        public DrawnSite Find(string siteId) => siteId == null ? null : _sites.FirstOrDefault(x => x.SiteId == siteId);

        // The latest crowd near this client's avatar (decision 0033), straight from the subscription; never part of Version.
        public GoodsCrowdView Crowd => _subscription.LatestCrowd;

        // Sites owned by the current site's company, from its latest baseline (ownership records are public, decision 0028).
        public static IEnumerable<string> OwnedSites(GoodsSnapshot current, string currentSiteId)
        {
            var company = current?.Companies.FirstOrDefault(x => x.SiteIds.Contains(currentSiteId))?.Id;
            return company == null ? Enumerable.Empty<string>()
                : current.Properties.Where(x => x.CompanyId == company).Select(x => x.SiteId).Where(x => x != currentSiteId);
        }

        // Call every frame with the local camera's position (null without a camera: only the current site is drawn).
        public void Tick(Vector3? viewPoint)
        {
            var currentId = _subscription.SiteId;
            var latest = _subscription.Latest;
            var next = new List<DrawnSite>();
            if (latest != null)
            {
                next.Add(Entry(currentId, latest, true));
                var owned = OwnedSites(latest, currentId).ToList();
                foreach (var siteId in owned) _subscription.Watch(siteId);
                var placement = SitePlacement.Active;
                if (placement != null && viewPoint.HasValue)
                {
                    var map = placement.ToMap(viewPoint.Value);
                    foreach (var siteId in owned.OrderBy(x => x, System.StringComparer.Ordinal))
                    {
                        var lot = placement.LotOf(siteId);
                        var snapshot = _subscription.Remote(siteId);
                        if (lot == null || snapshot == null) continue;
                        var reach = DrawRadius + (Find(siteId) != null ? DropMargin : 0f);
                        if (SitePlacement.Distance(lot, map) <= reach) next.Add(Entry(siteId, snapshot, false));
                    }
                }
            }
            var changed = !ReferenceEquals(SitePlacement.Active, _placement) || next.Count != _sites.Count || next.Where((x, i) => x.SiteId != _sites[i].SiteId
                || !ReferenceEquals(x.Snapshot, _sites[i].Snapshot) || x.Current != _sites[i].Current).Any();
            if (!changed) return;
            _placement = SitePlacement.Active;
            _sites.Clear();
            _sites.AddRange(next);
            Version++;
        }

        private static DrawnSite Entry(string siteId, GoodsSnapshot snapshot, bool current) => new()
        {
            SiteId = siteId, Snapshot = snapshot, Current = current, Layout = snapshot.SiteLayouts.FirstOrDefault(x => x.SiteId == siteId)
        };
    }
}
