// Buying generated buildings (decision 0028). Each purchasable building of the stored world layout is listed as a PropertyOffer:
// content derived from the layout, registered by the server at start and never saved. A lot's site is created on its first
// purchase, in the one commit that debits the buyer's company, records ownership as its own GoodsProperty record and grants
// the buyer and every teammate (players granted any of the company's sites; not employees) the new site; a rejected or failed
// purchase leaves no site, property or debit behind. Sites and properties are never removed. Ownership is public: every site
// view carries all properties, so any client can colour the map by owner. The site grid covers exactly the lot; a restaurant or factory shell becomes the site's GoodsBuilding
// (decision 0019) and the rest of the lot is ordinary outdoor cells. A generated restaurant's back door comes with its shell, and
// on purchase the dock that comes with it is placed beside that door (decision 0037).
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FoodFactoryGame.Goods
{
    // "Company owns lot L": ownership is its own record, not a property of the site (decision 0028). GoodsCompany.SiteIds
    // still lists the site and must agree.
    [Serializable] public sealed class GoodsProperty
    {
        public string LotId;
        public string SiteId;
        public string CompanyId;
    }

    // Content, not state: one listed lot of the world layout (WorldLayoutShells.PropertyOffers).
    [Serializable] public sealed class PropertyOffer
    {
        public string LotId;
        // Reserved in the layout; the site is created with this ID when the lot is first bought.
        public string SiteId;
        public string BuildingId;
        // GoodsWorld.RestaurantKind, FactoryKind, FarmCategory or StationCategory.
        public string Category;
        // False for the starting restaurant and competitors' buildings: listed, but not sold to players.
        public bool ForSale;
        // Whole cents.
        public long PriceCents;
        // The lot's world rectangle: the site grid is Width x Depth cells, with site cell (0,0) at world cell (LotX, LotZ).
        public int LotX;
        public int LotZ;
        public int Width;
        public int Depth;
        // The street cell trucks drive to, in world cells: the new site's map position.
        public int AccessX;
        public int AccessZ;
        // The building's footprint and door cells, in site cells.
        public int BuildingX;
        public int BuildingZ;
        public int BuildingWidth;
        public int BuildingDepth;
        public List<GridCell> Doors = new();
        // A generated restaurant's back doors (layout format 4, decision 0037), in site cells; they are also in Doors' perimeter,
        // never among Doors. Empty for older layouts and other buildings.
        public List<GridCell> BackDoors = new();
        // The starter dock that comes with a generated restaurant, in site cells (rotation 0 lies along X); HasDock false otherwise.
        public bool HasDock;
        public int DockX;
        public int DockZ;
        public int DockRotation;
        public int Floors = 1;
        // The lot's district price multiplier in percent (100 = list price); structure orders on the site are scaled by it
        // (decision 0034, PROTOTYPE pricing).
        public int PricePercent = 100;

        public bool IsShell => Category == GoodsWorld.RestaurantKind || Category == GoodsWorld.FactoryKind;
    }

    public sealed partial class GoodsWorld
    {
        public const string FarmCategory = "farm";
        public const string StationCategory = "station";

        // Keyed by lot ID; null until registered, and then nothing is listed.
        private Dictionary<string, PropertyOffer> _propertyOffers;

        // Server content, registered once at start. Every existing property must be a listed lot of the same size.
        public void RegisterPropertyOffers(IEnumerable<PropertyOffer> offers)
        {
            lock (_gate)
            {
                if (offers is null || _propertyOffers is not null) throw new ArgumentException("Missing or duplicate property catalog.");
                var catalog = new Dictionary<string, PropertyOffer>(StringComparer.Ordinal);
                var sites = new HashSet<string>(StringComparer.Ordinal);
                foreach (var offer in offers)
                {
                    var copy = offer is null ? null : JsonUtility.FromJson<PropertyOffer>(JsonUtility.ToJson(offer));
                    if (copy is null || OfferProblem(copy) is not null || catalog.ContainsKey(copy.LotId) || !sites.Add(copy.SiteId))
                        throw new ArgumentException($"Invalid or duplicate property offer '{offer?.LotId}'.");
                    catalog.Add(copy.LotId, copy);
                }
                var problem = PropertyCatalogProblem(_state, catalog);
                if (problem is not null) throw new InvalidOperationException(problem);
                _propertyOffers = catalog;
            }
        }

        // Volatile primitive for tests. Live request handlers must call BuyPropertyDurably.
        // The company that owns the paying site buys a listed lot: it pays the price, and the lot's site is created with its
        // layout and shell, owned by the company and granted to the buyer. Like a floor, only the accepted order (the one that
        // charges) is recorded, so a retried request replays it and never pays twice; a rejection changes and records nothing.
        public GoodsOutcome BuyProperty(string playerId, string requestId, string payingSiteId, string lotId)
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
                if (lotId is null || _propertyOffers is null || !_propertyOffers.TryGetValue(lotId, out var offer)) return Reject("unknown-lot");
                if (!offer.ForSale) return Reject("not-for-sale");
                if (Owned(offer)) return Reject("owned");
                if (!_state.Grants.Any(x => x.PlayerId == playerId && x.SiteId == payingSiteId)) return Reject("no-grant");
                var company = CompanyOfSiteLocked(payingSiteId);
                if (company is null) return Reject("no-company");
                if (_state.Companies.First(x => x.Id == company).Cash < offer.PriceCents) return Reject("insufficient-funds");

                // All checks precede this single locked mutation.
                TryDebit(company, offer.PriceCents);
                var teammates = Teammates(company);
                CreateProperty(offer, company);
                PlaceStarterDock(offer);
                Grant(playerId, offer.SiteId);
                foreach (var teammate in teammates) Grant(teammate, offer.SiteId);
                return Record(requestId, playerId, true, "property-bought", null);
            }
        }

        public GoodsOutcome BuyPropertyDurably(string playerId, string requestId, string payingSiteId, string lotId, string savePath)
        {
            return Commit(playerId, requestId, savePath, () => BuyProperty(playerId, requestId, payingSiteId, lotId));
        }

        // Server-only, for world creation (the starting restaurant): gives a lot to a company without charge or grant. When a
        // catalog is registered the offer must be the listed one. Callers commit.
        public void Bootstrap(PropertyOffer offer, string companyId)
        {
            lock (_gate)
            {
                var copy = offer is null ? null : JsonUtility.FromJson<PropertyOffer>(JsonUtility.ToJson(offer));
                if (copy is null || OfferProblem(copy) is not null || _state.Companies.All(x => x.Id != companyId) || Owned(copy)
                    || (_propertyOffers is not null && (!_propertyOffers.TryGetValue(copy.LotId, out var listed)
                        || JsonUtility.ToJson(listed) != JsonUtility.ToJson(copy))))
                    throw new ArgumentException("Invalid, unlisted or already owned property, or an unknown company.");
                var before = Snapshot();
                try { CreateProperty(copy, companyId); }
                catch
                {
                    _state = before;
                    InvalidateDiners();
                    throw;
                }
            }
        }

        // Call only under _gate. Every player granted any site of the company, in grant order: they act for it (membership is
        // implied by grants, decision 0012), so they are given each site it buys. Employees are site-bound workers and are not.
        private List<string> Teammates(string companyId)
        {
            var sites = _state.Companies.First(x => x.Id == companyId).SiteIds;
            return _state.Grants.Where(x => sites.Contains(x.SiteId) && !x.PlayerId.StartsWith(EmployeePrefix, StringComparison.Ordinal))
                .Select(x => x.PlayerId).Distinct().ToList();
        }

        // The site a baseline from View describes: its own layout's site, or else its first location's (a view lists the site's
        // own locations before any truck cargo). A bought site may have no locations yet, but it always has its layout.
        public static string ViewSiteId(GoodsSnapshot view) =>
            view?.SiteLayouts?.FirstOrDefault()?.SiteId ?? view?.Locations?.FirstOrDefault()?.SiteId;

        // A back door is a perimeter opening with a door record in the service role (decision 0037); it came with the building, so it
        // records nothing paid. The kitchen leaf is its look.
        internal static GoodsBuilding BuildingOf(PropertyOffer offer) => new()
        {
            Id = offer.BuildingId, SiteId = offer.SiteId, Kind = offer.Category, CellX = offer.BuildingX, CellZ = offer.BuildingZ,
            Width = offer.BuildingWidth, Depth = offer.BuildingDepth, Floors = offer.Floors,
            Doors = offer.Doors.Concat(offer.BackDoors ?? new List<GridCell>()).Select(x => new GridCell { X = x.X, Z = x.Z }).ToList(),
            Structures = (offer.BackDoors ?? new List<GridCell>())
                .Select(x => new GoodsStructure { Kind = DoorStructure, X = x.X, Z = x.Z, Style = BackDoorStyle, Role = ServiceDoorRole }).ToList()
        };

        public const string BackDoorStyle = "kitchen";

        // The dock that came with a bought restaurant (decision 0037), from the supplier's dock content, placed free of charge (it
        // came with the building, so selling it refunds nothing). Call only under _gate, after CreateProperty. Nothing is placed
        // when no dock content is registered or something already stands there.
        private void PlaceStarterDock(PropertyOffer offer)
        {
            var template = _equipmentOffers.Values.Select(x => x.Equipment).FirstOrDefault(x => x != null && x.Kind == DockKind);
            if (!offer.HasDock || template == null) return;
            var dock = JsonUtility.FromJson<GoodsEquipment>(JsonUtility.ToJson(template));
            dock.Id = StarterDockId(offer.SiteId);
            dock.SiteId = offer.SiteId;
            dock.State = EquipmentState.Placed;
            dock.HolderId = "";
            dock.CellX = offer.DockX;
            dock.CellZ = offer.DockZ;
            dock.Rotation = offer.DockRotation;
            dock.Level = 0;
            dock.ChargedCents = 0;
            dock.StaffId = "";
            dock.Layer ??= "";
            if (_state.Equipment.Any(x => x.Id == dock.Id) || SiteGrid.PlacementProblem(_state, dock, dock.CellX, dock.CellZ, dock.Rotation) != null) return;
            _state.Equipment.Add(dock);
            AddPlacedParts(dock);
        }

        public static string StarterDockId(string siteId) => "dock:" + siteId;

        // Call only under _gate, after every check. The site's map position is the lot's access point.
        private void CreateProperty(PropertyOffer offer, string companyId)
        {
            Bootstrap(new GoodsSite { Id = offer.SiteId, Name = offer.BuildingId, MapX = offer.AccessX, MapZ = offer.AccessZ });
            Bootstrap(new SiteLayout { SiteId = offer.SiteId, Width = offer.Width, Depth = offer.Depth });
            if (offer.IsShell) Bootstrap(BuildingOf(offer));
            _state.Properties.Add(new GoodsProperty { LotId = offer.LotId, SiteId = offer.SiteId, CompanyId = companyId });
            _state.Companies.First(x => x.Id == companyId).SiteIds.Add(offer.SiteId);
            InvalidateDiners();
            _state.Revision++;
        }

        // A lot is taken once it has a property record or anything already uses its reserved site ID.
        private bool Owned(PropertyOffer offer) =>
            _state.Properties.Any(x => x.LotId == offer.LotId || x.SiteId == offer.SiteId) || SiteExists(_state, offer.SiteId)
            || _state.SiteLayouts.Any(x => x.SiteId == offer.SiteId) || _state.Companies.Any(x => x.SiteIds.Contains(offer.SiteId));

        // Null when the offer is well formed and its shell (if any) is a valid building on an empty lot-sized site.
        private static string OfferProblem(PropertyOffer offer)
        {
            if (string.IsNullOrWhiteSpace(offer.LotId) || string.IsNullOrWhiteSpace(offer.SiteId) || offer.SiteId == RoadSiteId
                || string.IsNullOrWhiteSpace(offer.BuildingId) || offer.PriceCents < 1 || offer.Width < 1 || offer.Depth < 1 || offer.Doors is null
                || (!offer.IsShell && offer.Category != FarmCategory && offer.Category != StationCategory)
                || offer.PricePercent < 1 || offer.BuildingX < 0 || offer.BuildingZ < 0 || offer.BuildingWidth < 1 || offer.BuildingDepth < 1
                || offer.BuildingX + offer.BuildingWidth > offer.Width || offer.BuildingZ + offer.BuildingDepth > offer.Depth)
                return "invalid-offer";
            if (offer.BackDoors is null || (offer.BackDoors.Count > 0 && offer.Category != RestaurantKind)
                || offer.BackDoors.Any(x => x is null || offer.Doors.Any(d => d.X == x.X && d.Z == x.Z))
                || (offer.HasDock && (offer.Category != RestaurantKind || offer.DockRotation < 0 || offer.DockRotation > 3 || offer.DockX < 0 || offer.DockZ < 0
                    || offer.DockX + (offer.DockRotation % 2 == 0 ? 2 : 1) > offer.Width || offer.DockZ + (offer.DockRotation % 2 == 0 ? 1 : 2) > offer.Depth)))
                return "invalid-offer";
            if (!offer.IsShell) return null;
            var probe = new GoodsSnapshot { WorldId = "offer" };
            probe.SiteLayouts.Add(new SiteLayout { SiteId = offer.SiteId, Width = offer.Width, Depth = offer.Depth });
            return BuildingProblem(probe, BuildingOf(offer));
        }

        // Null when every property is a listed lot whose site has the lot's size, and every listed lot whose site exists has its
        // property record: sites are never deleted, so a property can never disappear either.
        private static string PropertyCatalogProblem(GoodsSnapshot state, Dictionary<string, PropertyOffer> catalog)
        {
            foreach (var property in state.Properties)
            {
                if (!catalog.TryGetValue(property.LotId, out var offer) || offer.SiteId != property.SiteId)
                    return $"Property {property.LotId} is not a listed lot.";
                var layout = state.SiteLayouts.FirstOrDefault(x => x.SiteId == property.SiteId);
                if (layout is null || layout.Width != offer.Width || layout.Depth != offer.Depth)
                    return $"The site of property {property.LotId} does not have the lot's size.";
            }
            var used = new HashSet<string>(state.Sites.Select(x => x.Id).Concat(state.Locations.Select(x => x.SiteId))
                .Concat(state.SiteLayouts.Select(x => x.SiteId)), StringComparer.Ordinal);
            var owned = new HashSet<string>(state.Properties.Select(x => x.LotId), StringComparer.Ordinal);
            foreach (var offer in catalog.Values)
                if (used.Contains(offer.SiteId) && !owned.Contains(offer.LotId))
                    return $"Site {offer.SiteId} of lot {offer.LotId} exists without its property record.";
            // A competitor stands on a listed lot that is not for sale (buying competitors is GDD section 11, later).
            foreach (var competitor in state.Competitors.Where(x => x.LotId != ""))
                if (!catalog.TryGetValue(competitor.LotId, out var lot) || lot.ForSale)
                    return $"Competitor {competitor.Id} does not stand on a listed lot that is not for sale.";
            return null;
        }

        // The layout-dependent half of validation, run before every save once a catalog is registered.
        private void ValidatePropertyCatalog()
        {
            if (_propertyOffers is null) return;
            var problem = PropertyCatalogProblem(_state, _propertyOffers);
            if (problem is not null) throw new InvalidOperationException(problem);
        }

        private static void ValidateProperties(GoodsSnapshot state)
        {
            if (state.Properties.Any(x => x is null || string.IsNullOrWhiteSpace(x.LotId) || string.IsNullOrWhiteSpace(x.SiteId)
                    || state.Companies.FirstOrDefault(y => y.Id == x.CompanyId)?.SiteIds.Contains(x.SiteId) != true
                    || state.Sites.All(y => y.Id != x.SiteId) || state.SiteLayouts.All(y => y.SiteId != x.SiteId))
                || state.Properties.GroupBy(x => x.LotId).Any(x => x.Count() != 1)
                || state.Properties.GroupBy(x => x.SiteId).Any(x => x.Count() != 1))
                throw new InvalidOperationException("Goods snapshot violates property invariants.");
        }
    }
}
