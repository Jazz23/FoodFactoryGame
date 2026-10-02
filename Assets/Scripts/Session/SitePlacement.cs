// Where sites stand in the scene (decision 0031): the one rule shared by the map presenter, every site presenter, placement
// and the server's enter-site check, so the map and the sites can never disagree. In a generated world (format 3 layout with a
// starting restaurant) the starting lot's grid centre stands at the scene origin with its building's ground floor at y = 0, and
// every other lot stands where it is on the map: its grid centre at its layout position relative to the starting lot's, at its
// building's ground-floor elevation minus the starting building's. Lots never rotate (decision 0028), so a site cell maps to the
// scene by a shift only. Sites without a lot (dev worlds, remote dev sites) stand at the origin. Presentation only: nothing in
// the simulation reads scene positions except that check, which converts a scene point back to map metres.
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.World;
using UnityEngine;

namespace FoodFactoryGame.Session
{
    public sealed class SitePlacement
    {
        private readonly Dictionary<string, WorldLot> _lots = new();
        private readonly Dictionary<string, Vector3> _origins = new();
        private readonly Dictionary<string, PropertyOffer> _offers = new();

        // The placement presenters use while a generated world's layout is shown on this client (set by WorldLayoutPresenter);
        // null otherwise, when every site stands at the origin. SiteGridSpace reads it, so all site presentation follows it.
        public static SitePlacement Active { get; private set; }

        public static void Use(SitePlacement placement) => Active = placement;

        // Scene position of the layout's origin (map point 0,0 at elevation 0).
        public Vector3 LayoutOrigin { get; }
        public string StartSiteId { get; }
        public IReadOnlyDictionary<string, WorldLot> Lots => _lots;
        public WorldLayout Layout { get; }
        // The drivable road graph of the layout (decision 0032), shared with the server's truck simulation.
        public RoadNetwork Roads => RoadNetwork.For(Layout);
        // Height of the drawn ground (layout-local metres) at a map point: set by WorldLayoutPresenter, which levels lots; the raw
        // terrain until then. Vehicles stand on it, as the road tiles do.
        public System.Func<float, float, float> Ground { get; set; }

        private SitePlacement(WorldLayout layout, WorldLot start)
        {
            var elevations = layout.Buildings.ToDictionary(x => x.Id, x => x.ElevationCm / 100f);
            var startElevation = elevations[start.BuildingId];
            LayoutOrigin = new Vector3(-(start.X + start.Width / 2f), -startElevation, -(start.Z + start.Depth / 2f));
            StartSiteId = start.SiteId;
            Layout = layout;
            foreach (var offer in WorldLayoutShells.PropertyOffers(layout)) _offers[offer.LotId] = offer;
            foreach (var lot in layout.Lots)
            {
                _lots[lot.SiteId] = lot;
                _origins[lot.SiteId] = LayoutOrigin + new Vector3(lot.X + lot.Width / 2f, elevations[lot.BuildingId], lot.Z + lot.Depth / 2f);
            }
        }

        // Null for a layout that is not a generated world (no layout, format 1 or 2, or no starting restaurant lot).
        public static SitePlacement For(WorldLayout layout)
        {
            var start = layout?.Lots.FirstOrDefault(x => x.BuildingId == layout.StartRestaurantId);
            return layout != null && layout.FormatVersion >= GeneratedWorld.FirstFormat && start != null ? new SitePlacement(layout, start) : null;
        }

        // Scene position of a site's grid centre at its ground floor; the origin for a site without a lot.
        public Vector3 SiteOrigin(string siteId) => siteId != null && _origins.TryGetValue(siteId, out var origin) ? origin : Vector3.zero;

        public WorldLot LotOf(string siteId) => siteId != null && _lots.TryGetValue(siteId, out var lot) ? lot : null;

        // A lot's building and doors in its site cells (WorldLayoutShells), for any lot, owned or not; null for an unknown lot.
        public PropertyOffer OfferOf(string lotId) => lotId != null && _offers.TryGetValue(lotId, out var offer) ? offer : null;

        // Scene point of a map point (metres) at a layout height.
        public Vector3 ToScene(float mapX, float mapZ, float height = 0f) => LayoutOrigin + new Vector3(mapX, height, mapZ);

        // Scene point on the drawn ground at a map point (metres).
        public Vector3 OnGround(float mapX, float mapZ) =>
            ToScene(mapX, mapZ, Ground?.Invoke(mapX, mapZ) ?? (Layout.Terrain ?? WorldTerrain.Flat()).Height(mapX, mapZ));

        // Map point (metres) under a scene point.
        public Vector2 ToMap(Vector3 scene) => new(scene.x - LayoutOrigin.x, scene.z - LayoutOrigin.z);

        // Horizontal distance (metres) from a map point to a lot's rectangle; 0 inside it.
        public static float Distance(WorldLot lot, Vector2 map)
        {
            var dx = Mathf.Max(0f, Mathf.Max(lot.X - map.x, map.x - (lot.X + lot.Width)));
            var dz = Mathf.Max(0f, Mathf.Max(lot.Z - map.y, map.y - (lot.Z + lot.Depth)));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // The lot containing a scene point (its site ID), or null on the street or off every lot.
        public string SiteAt(Vector3 scene)
        {
            var map = ToMap(scene);
            foreach (var (siteId, lot) in _lots)
                if (map.x >= lot.X && map.x < lot.X + lot.Width && map.y >= lot.Z && map.y < lot.Z + lot.Depth) return siteId;
            return null;
        }

        // The origin SiteGridSpace applies to a site's grid: the active placement's, or zero.
        public static Vector3 OriginOf(string siteId) => Active?.SiteOrigin(siteId) ?? Vector3.zero;
    }
}
