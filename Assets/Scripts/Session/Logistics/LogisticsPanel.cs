// Local player's logistics screen (decisions 0022, 0023), drawn from the Logistics UXML and USS under Assets/UI/Resources/UI
// (decision 0042): the company's routes (pickup dock, dropoff dock, cargo, Apply, Delete, and the trucks on each) with a
// draft for a new route; its trucks with their live status, destination, route and
// cargo, a route chooser (a route or Parked) with Assign, Sell (asks for confirmation first; the server sells only an empty truck
// serving no farm order, GoodsWorld.SellTruck), and a Buy button per truck offer; and its remote sites with their
// storage and dock stock and buttons that move one stack at a time between them. Opened with L (EquipmentInteraction), or shown
// inside the phone's Trucks app (Embed, decision 0047), where the same window moves into the phone's page. Every
// button is a request the server checks; nothing changes here until the next baseline. Rows are rebuilt only when the set of
// routes, trucks, docks, stacks or drafts changes, and their text is refreshed every frame, so a click is never lost to a
// rebuild. Presentation only: drafts are this client's edits until applied.
// It also feeds the phone's map (PhoneMapView.SetTrucks) with the fleet's positions every frame while the Map app shows (road
// trucks where RoadPose has them on their leg, others at their site), moving icons in place; clicking an icon opens the Trucks
// app with that truck selected (outlined and scrolled into view), and the selected truck is highlighted on the map.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.WorldMap;
using FoodFactoryGame.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Logistics
{
    [DisallowMultipleComponent]
    public sealed class LogisticsPanel : MonoBehaviour
    {
        public const string PickupField = "pickup";
        public const string DropoffField = "dropoff";
        public const string CargoField = "cargo";
        // Draft key of the route not created yet.
        public const string NewRouteKey = "new";

        private const string Templates = "Logistics/";

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;

        // This client's unapplied docks and cargo for one route; ItemId null means any cargo.
        private sealed class RouteDraft
        {
            public string PickupDockId;
            public string DropoffDockId;
            public string ItemId;
        }

        private readonly Dictionary<string, RouteDraft> _drafts = new();
        // This client's unassigned route choice per truck; empty means Parked.
        private readonly Dictionary<string, string> _truckDrafts = new();
        private readonly List<(Label Label, Func<string> Text)> _live = new();
        private readonly List<(VisualElement Element, Func<bool> Enabled)> _liveEnabled = new();
        private readonly HashSet<string> _pending = new();
        private readonly Dictionary<string, VisualElement> _truckCards = new();
        private ClientSiteSubscription _subscription;
        private VisualElement _window;
        private VisualElement _dim;
        private ScrollView _scroll;
        private VisualElement _routes;
        private VisualElement _trucks;
        private VisualElement _sites;
        private Label _status;
        private string _signature;
        // The truck whose Sell is waiting for confirmation, or null.
        private string _confirmSell;
        // A truck card to scroll into view once it has been laid out, or null.
        private string _scrollTo;
        // The phone whose map shows the fleet (found once it exists), the map whose clicks are hooked, and the baseline the
        // icons extrapolate road positions from.
        private PhonePanel _phone;
        private float _nextPhoneSearch;
        private PhoneMapView _hookedMap;
        private GoodsSnapshot _mapBaseline;
        private float _mapReceivedAt;
        private readonly List<PhoneMapView.MapTruck> _mapTrucks = new();
        private readonly Dictionary<string, int> _mapStacks = new();
        private List<TruckOffer> _truckOfferDefs;
        private int _truckOfferCount = -1;

        public VisualElement Window => _window;
        // True while the window is shown inside the phone (decision 0047).
        public bool Embedded { get; private set; }
        private VisualElement _home;
        public string LastRejection { get; private set; }
        public bool HasPendingRequests => _pending.Count > 0;
        // The truck chosen in the list or on the phone map (outlined in the list, highlighted on the map), or null.
        public string SelectedTruckId { get; private set; }
        // The truck whose Sell asks for confirmation, or null.
        public string ConfirmingSellTruckId => _confirmSell;

        private SessionRoot Session => interaction.Session;
        private GoodsSnapshot Primary => Session.ClientSite;

        private void Start()
        {
            var root = document.rootVisualElement;
            root.Clear();
            UiTemplates.Fill(root, Templates + "Logistics");
            _window = root.Part("logistics");
            _dim = root.Part("logistics-dim");
            _scroll = root.Part<ScrollView>("logistics-scroll");
            _routes = root.Part("logistics-routes");
            _trucks = root.Part("logistics-trucks");
            _sites = root.Part("logistics-sites");
            _status = root.Part<Label>("logistics-status");
            root.Part<Button>("logistics-close").clicked += interaction.CloseScreen;
            _window.Show(false);
            _dim.Show(false);
        }

        // Moves the window into a host element of the same panel (the phone's Trucks page), or back to its own screen with null.
        public void Embed(VisualElement host)
        {
            if (_window == null) return;
            _home ??= _window.parent;
            if (host == null)
            {
                if (!Embedded) return;
                _home.Add(_window);
                Embedded = false;
            }
            else
            {
                if (_window.parent != host) host.Add(_window);
                Embedded = true;
            }
            _window.EnableInClassList("logi--embedded", Embedded);
        }

        private void Update()
        {
            if (_window == null) return;
            Subscribe(Session.ClientSubscription);
            UpdatePhoneMap();
            var site = Primary;
            // Remote sites are watched as soon as the baseline names them, so their stock is there when the screen opens.
            if (site != null)
                foreach (var remote in CompanySites(site).Where(x => x.Id != _subscription?.SiteId)) _subscription?.Watch(remote.Id);
            var open = site != null && Session.IsRunning
                && (interaction.Screen == InteractionScreen.Logistics || (Embedded && interaction.Screen == InteractionScreen.Phone));
            _window.Show(open);
            _dim.Show(open && !Embedded);
            if (!open) return;
            _drafts.TryAdd(NewRouteKey, new RouteDraft());
            foreach (var route in site.Routes.Where(x => !_drafts.ContainsKey(x.Id)))
                _drafts[route.Id] = new RouteDraft
                {
                    PickupDockId = route.PickupDockId, DropoffDockId = route.DropoffDockId, ItemId = route.AllowedItemIds.FirstOrDefault()
                };
            // A truck's choice starts at its route and falls back to it when the chosen route is gone.
            foreach (var truck in Fleet(site))
                if (!_truckDrafts.TryGetValue(truck.Id, out var chosen) || (chosen != "" && site.Routes.All(x => x.Id != chosen)))
                    _truckDrafts[truck.Id] = truck.RouteId ?? "";
            var signature = Signature(site);
            if (signature != _signature)
            {
                _signature = signature;
                Build(site);
            }
            foreach (var (label, text) in _live) label.SetText(text());
            foreach (var (element, enabled) in _liveEnabled) element.SetEnabled(enabled());
            foreach (var (truckId, card) in _truckCards) card.EnableInClassList("logi-card--selected", truckId == SelectedTruckId);
            ScrollToSelected();
            _status.text = HasPendingRequests ? "Waiting for the server..." : string.IsNullOrEmpty(LastRejection) ? " " : $"Rejected: {LastRejection}";
        }

        // Moves a route draft's pickup, dropoff or cargo one option forward (step 1) or back (-1); NewRouteKey is the route not
        // created yet. Public so tests can drive the same path as the arrow buttons.
        public void CycleDraft(string routeKey, string field, int step)
        {
            if (!_drafts.TryGetValue(routeKey, out var draft)) return;
            if (field == CargoField)
            {
                var items = CargoOptions();
                draft.ItemId = items[Wrap(items.IndexOf(draft.ItemId) + step, items.Count)];
                return;
            }
            var docks = Docks().Select(x => x.Id).ToList();
            if (docks.Count == 0) return;
            var current = field == PickupField ? draft.PickupDockId : draft.DropoffDockId;
            var next = docks[Wrap(docks.IndexOf(current) + step, docks.Count)];
            if (field == PickupField) draft.PickupDockId = next;
            else draft.DropoffDockId = next;
        }

        // Sends the new-route draft, like the Create route button.
        public void CreateRoute()
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null || !_drafts.TryGetValue(NewRouteKey, out var draft)) return;
            LastRejection = null;
            bridge.RequestCreateRoute(Track(), draft.PickupDockId ?? "", draft.DropoffDockId ?? "", Items(draft));
        }

        // Sends a route's draft, like its Apply button.
        public void ApplyRoute(string routeId)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null || routeId == NewRouteKey || !_drafts.TryGetValue(routeId, out var draft)) return;
            LastRejection = null;
            bridge.RequestSetRoute(Track(), routeId, draft.PickupDockId ?? "", draft.DropoffDockId ?? "", Items(draft));
        }

        // Removes a route and parks its trucks, like its Delete button.
        public void DeleteRoute(string routeId)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null) return;
            LastRejection = null;
            bridge.RequestDeleteRoute(Track(), routeId);
        }

        // Moves a truck's route choice one option forward or back through Parked and the company's routes.
        public void CycleTruckRoute(string truckId, int step)
        {
            if (!_truckDrafts.TryGetValue(truckId, out var chosen)) return;
            var options = TruckRouteOptions();
            _truckDrafts[truckId] = options[Wrap(options.IndexOf(chosen) + step, options.Count)];
        }

        // Sends a truck's route choice, like its Assign button; Parked takes it off its route.
        public void AssignTruck(string truckId)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null || !_truckDrafts.TryGetValue(truckId, out var chosen)) return;
            LastRejection = null;
            bridge.RequestAssignTruck(Track(), truckId, chosen);
        }

        // Chooses a truck in the list (outlined, scrolled into view) and on the phone map; null clears the choice.
        public void SelectTruck(string truckId)
        {
            SelectedTruckId = truckId;
            _scrollTo = truckId;
        }

        // First step of selling: the truck's card asks for confirmation (ConfirmSellTruck sends the request). Null cancels.
        public void AskSellTruck(string truckId)
        {
            _confirmSell = truckId;
            if (truckId != null) SelectedTruckId = truckId;
        }

        // Sells a truck after confirmation, like the card's confirming Sell button. The server checks ownership, an empty cargo and
        // no farm order, and pays TruckResalePercent of the truck's list price.
        public void ConfirmSellTruck(string truckId)
        {
            var bridge = _subscription?.Bridge;
            _confirmSell = null;
            if (bridge == null || string.IsNullOrEmpty(truckId)) return;
            LastRejection = null;
            bridge.RequestSellTruck(Track(), truckId);
        }

        // What selling this truck would pay, from the session's truck offers (the content the server uses).
        public long ResaleCents(GoodsTruck truck) => GoodsWorld.TruckResaleCents(truck, TruckOfferDefs());

        private List<TruckOffer> TruckOfferDefs()
        {
            var count = Session.Offers.Count;
            if (_truckOfferDefs == null || count != _truckOfferCount)
            {
                _truckOfferCount = count;
                _truckOfferDefs = TruckOffers().Select(x => x.ToTruckOffer()).ToList();
            }
            return _truckOfferDefs;
        }

        // Buys a truck offer with the company's cash; it arrives parked at this site.
        public void BuyTruck(string offerId)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null) return;
            LastRejection = null;
            bridge.RequestPurchase(Track(), _subscription.SiteId, offerId);
        }

        // Moves up to one max stack of an item from one location of a site to another, as the site's buttons do: ordinary
        // server-checked transfers, most exposed goods first, limited to the room the destination has in the latest baseline.
        public void MoveStack(string siteId, string fromLocationId, string itemId, bool spoiled, string toLocationId)
        {
            var site = SiteView(siteId);
            var bridge = _subscription?.Bridge;
            if (site == null || bridge == null || toLocationId == null) return;
            LastRejection = null;
            var free = Math.Min(Session.MaxStack(itemId), GoodsSlots.FreeUnits(site, toLocationId, itemId, spoiled, Session.MaxStack));
            if (free <= 0)
            {
                LastRejection = "capacity";
                return;
            }
            foreach (var lot in site.Lots.Where(x => x.LocationId == fromLocationId && x.ItemId == itemId && x.Spoiled == spoiled)
                         .OrderByDescending(x => x.ExposureSeconds).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                if (free <= 0) break;
                var take = (int)Math.Min(free, lot.Quantity);
                free -= take;
                bridge.RequestTransfer(Track(), lot.Id, toLocationId, take);
            }
        }

        // Every dock of the company, on this site and on the remote sites whose baselines have arrived, by site then ID.
        public IReadOnlyList<GoodsEquipment> Docks()
        {
            var site = Primary;
            if (site == null) return Array.Empty<GoodsEquipment>();
            return site.Sites.Select(x => SiteView(x.Id)).Where(x => x != null).SelectMany(x => x.Equipment)
                .Where(x => x.Kind == GoodsWorld.DockKind)
                .OrderBy(x => x.SiteId == GoodsWorld.ViewSiteId(site) ? 0 : 1).ThenBy(x => x.SiteId, StringComparer.Ordinal)
                .ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        }

        private GoodsSnapshot SiteView(string siteId) =>
            siteId == _subscription?.SiteId ? Primary : _subscription?.Remote(siteId);

        private List<string> CargoOptions() =>
            new[] { (string)null }.Concat(Session.Items.Where(x => x != null).Select(x => x.Id)).ToList();

        private static string[] Items(RouteDraft draft) => draft.ItemId == null ? Array.Empty<string>() : new[] { draft.ItemId };

        private IReadOnlyList<GoodsRoute> Routes() =>
            Primary?.Routes.OrderBy(x => x.Id, StringComparer.Ordinal).ToList() ?? new List<GoodsRoute>();

        private List<string> TruckRouteOptions() => new[] { "" }.Concat(Routes().Select(x => x.Id)).ToList();

        private IEnumerable<OfferAsset> TruckOffers() => Session.Offers.Where(x => x != null && x.Truck != null);

        private string Signature(GoodsSnapshot site)
        {
            var text = new StringBuilder();
            foreach (var (key, draft) in _drafts.OrderBy(x => x.Key, StringComparer.Ordinal))
                text.Append(key).Append('/').Append(draft.PickupDockId).Append('/').Append(draft.DropoffDockId).Append('/').Append(draft.ItemId).Append('|');
            foreach (var route in site.Routes)
                text.Append(route.Id).Append('/').Append(route.PickupDockId).Append('/').Append(route.DropoffDockId).Append('/')
                    .Append(string.Join(",", route.AllowedItemIds)).Append('|');
            foreach (var truck in Fleet(site))
                text.Append(truck.Id).Append('/').Append(truck.Name).Append('/').Append(truck.RouteId).Append('/').Append(_truckDrafts[truck.Id]).Append('|');
            text.Append("sell:").Append(_confirmSell).Append('|');
            foreach (var dock in Docks()) text.Append(dock.Id).Append(dock.State).Append('|');
            foreach (var remote in RemoteSites(site))
            {
                text.Append('#').Append(remote.Id);
                var view = SiteView(remote.Id);
                if (view == null) continue;
                foreach (var group in view.Lots.Where(x => view.Locations.Any(y => y.Id == x.LocationId && y.SiteId == remote.Id))
                             .Select(x => (x.LocationId, x.ItemId, x.Spoiled)).Distinct().OrderBy(x => x.ToString(), StringComparer.Ordinal))
                    text.Append('|').Append(group);
            }
            return text.ToString();
        }

        private IEnumerable<GoodsSite> RemoteSites(GoodsSnapshot site) =>
            CompanySites(site).Where(x => x.Id != GoodsWorld.ViewSiteId(site)).OrderBy(x => x.Id, StringComparer.Ordinal);

        // The company's own sites: a baseline also carries the map records of farm gates its orders' trucks drive to (decision 0047).
        private static IEnumerable<GoodsSite> CompanySites(GoodsSnapshot site)
        {
            var owned = site.Companies.FirstOrDefault()?.SiteIds;
            return owned == null ? site.Sites.Where(x => x.Id == GoodsWorld.ViewSiteId(site)) : site.Sites.Where(x => owned.Contains(x.Id));
        }

        // The company's fleet: a farm order's supplier truck is not one of its trucks (decision 0047).
        private static IEnumerable<GoodsTruck> Fleet(GoodsSnapshot site) => site.Trucks.Where(x => !x.Supplier);

        private void Build(GoodsSnapshot site)
        {
            _live.Clear();
            _liveEnabled.Clear();
            _truckCards.Clear();
            _routes.Clear();
            _trucks.Clear();
            _sites.Clear();
            var routes = Routes();
            if (routes.Count == 0) _routes.Add(Empty("The company has no routes."));
            foreach (var route in routes) _routes.Add(RouteCard(site, route));
            _routes.Add(NewRouteCard());
            if (!Fleet(site).Any()) _trucks.Add(Empty("The company has no trucks."));
            foreach (var truck in Fleet(site).OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var card = TruckCard(truck);
                _truckCards[truck.Id] = card;
                _trucks.Add(card);
            }
            foreach (var offer in TruckOffers())
            {
                var id = offer.Id;
                var buy = new Button(() => BuyTruck(id))
                {
                    name = $"logistics-buy-{id}", text = $"Buy {offer.Truck.DisplayName}  {PlayerHud.FormatCash(offer.PriceCents)}", focusable = false
                };
                buy.AddToClassList("ff-button");
                buy.AddToClassList("ff-button--primary");
                buy.AddToClassList("logi__buy");
                _trucks.Add(buy);
            }
            var remotes = RemoteSites(site).ToList();
            if (remotes.Count == 0) _sites.Add(Empty("The company has no other sites."));
            foreach (var remote in remotes) _sites.Add(SiteCard(site, remote));
        }

        private VisualElement RouteCard(GoodsSnapshot site, GoodsRoute route)
        {
            var id = route.Id;
            var card = UiTemplates.Clone(Templates + "LogisticsRouteCard");
            card.name = $"logistics-route-{id}";
            card.Part<Label>("title").text = RouteName(route);
            var trucks = site.Trucks.Where(x => x.RouteId == id).OrderBy(x => x.Id, StringComparer.Ordinal).Select(x => x.Name).ToList();
            var assigned = card.Part<Label>("trucks");
            assigned.name = $"logistics-route-trucks-{id}";
            assigned.text = trucks.Count == 0 ? "No trucks: assign one on the right." : "Trucks: " + string.Join(", ", trucks);
            AddChoosers(card.Part("choosers"), id);
            var draft = _drafts[id];
            var applied = route.PickupDockId == draft.PickupDockId && route.DropoffDockId == draft.DropoffDockId
                && route.AllowedItemIds.FirstOrDefault() == draft.ItemId && route.AllowedItemIds.Count <= 1;
            var apply = card.Part<Button>("apply");
            apply.name = $"logistics-apply-{id}";
            apply.text = applied ? "Route set" : "Apply route";
            apply.EnableInClassList("ff-button--primary", !applied);
            apply.SetEnabled(!applied && draft.PickupDockId != null && draft.DropoffDockId != null);
            apply.clicked += () => ApplyRoute(id);
            var delete = card.Part<Button>("delete");
            delete.name = $"logistics-delete-{id}";
            delete.clicked += () => DeleteRoute(id);
            card.Part("create").RemoveFromHierarchy();
            return card;
        }

        private VisualElement NewRouteCard()
        {
            var card = UiTemplates.Clone(Templates + "LogisticsRouteCard");
            card.name = "logistics-route-new";
            card.Part<Label>("title").text = "New route";
            card.Part("trucks").RemoveFromHierarchy();
            AddChoosers(card.Part("choosers"), NewRouteKey);
            var draft = _drafts[NewRouteKey];
            card.Part("apply").RemoveFromHierarchy();
            card.Part("delete").RemoveFromHierarchy();
            var create = card.Part<Button>("create");
            create.name = "logistics-create-route";
            create.SetEnabled(draft.PickupDockId != null && draft.DropoffDockId != null);
            create.clicked += CreateRoute;
            return card;
        }

        private void AddChoosers(VisualElement choosers, string routeKey)
        {
            var draft = _drafts[routeKey];
            choosers.Add(Chooser(routeKey, PickupField, "Load at", DockName(draft.PickupDockId), step => CycleDraft(routeKey, PickupField, step)));
            choosers.Add(Chooser(routeKey, DropoffField, "Deliver to", DockName(draft.DropoffDockId), step => CycleDraft(routeKey, DropoffField, step)));
            choosers.Add(Chooser(routeKey, CargoField, "Cargo", draft.ItemId == null ? "Any" : ItemName(draft.ItemId),
                step => CycleDraft(routeKey, CargoField, step)));
        }

        private VisualElement TruckCard(GoodsTruck truck)
        {
            var id = truck.Id;
            var card = UiTemplates.Clone(Templates + "LogisticsTruckCard");
            card.name = $"logistics-truck-{id}";
            card.Part<Label>("title").text = truck.Name;
            Live(card.Part<Label>("status"), $"logistics-truck-status-{id}", () => TruckStatus(Current(id)));
            Live(card.Part<Label>("destination"), $"logistics-truck-destination-{id}", () => DestinationText(Current(id)));
            Live(card.Part<Label>("route"), $"logistics-truck-route-{id}", () => RouteText(Current(id)));
            Live(card.Part<Label>("cargo"), $"logistics-truck-cargo-{id}", () => CargoText(Current(id)));
            // Clicking anywhere on the card chooses the truck (and highlights it on the phone map).
            card.RegisterCallback<ClickEvent>(_ => SelectedTruckId = id);
            var chosen = _truckDrafts[id];
            var route = Routes().FirstOrDefault(x => x.Id == chosen);
            var chooser = Chooser(id, "route", "Route", route == null ? "Parked" : RouteName(route), step => CycleTruckRoute(id, step));
            chooser.AddToClassList("logi-chooser--narrow");
            card.Part("choosers").Add(chooser);
            var applied = chosen == (truck.RouteId ?? "");
            var assign = card.Part<Button>("assign");
            assign.name = $"logistics-assign-{id}";
            assign.text = applied ? (chosen == "" ? "Parked" : "Assigned") : chosen == "" ? "Park" : "Assign";
            assign.EnableInClassList("ff-button--primary", !applied);
            assign.SetEnabled(!applied);
            assign.clicked += () => AssignTruck(id);
            AddSell(card, truck);
            return card;
        }

        // Sell, its reason when the truck cannot be sold now, and the confirmation row while this truck's sale is being confirmed.
        private void AddSell(VisualElement card, GoodsTruck truck)
        {
            var id = truck.Id;
            var sell = card.Part<Button>("sell");
            sell.name = $"logistics-sell-{id}";
            sell.text = $"Sell  {PlayerHud.FormatCash(ResaleCents(truck))}";
            sell.clicked += () => AskSellTruck(id);
            LiveEnabled(sell, () => _confirmSell != id && SaleProblem(Current(id)) == null && !HasPendingRequests);
            Live(card.Part<Label>("sell-note"), $"logistics-sell-note-{id}", () => SaleNote(SaleProblem(Current(id))));
            var confirm = card.Part("confirm");
            confirm.name = $"logistics-sell-confirm-{id}";
            if (_confirmSell != id)
            {
                confirm.RemoveFromHierarchy();
                return;
            }
            Live(confirm.Part<Label>("confirm-text"), $"logistics-sell-confirm-text-{id}", () => ConfirmText(Current(id)));
            var yes = confirm.Part<Button>("confirm-sell");
            yes.name = $"logistics-sell-yes-{id}";
            yes.clicked += () => ConfirmSellTruck(id);
            LiveEnabled(yes, () => SaleProblem(Current(id)) == null && !HasPendingRequests);
            var keep = confirm.Part<Button>("confirm-keep");
            keep.name = $"logistics-sell-keep-{id}";
            keep.clicked += () => AskSellTruck(null);
        }

        private string SaleProblem(GoodsTruck truck) => truck == null ? "gone" : GoodsWorld.TruckSaleProblem(Primary, truck);

        private static string SaleNote(string problem) => problem switch
        {
            null => " ",
            "on-order" => "Serving a farm order: it can be sold once the order is done.",
            "has-cargo" => "Carrying goods: let it unload (or park it and empty it) before selling. Goods are never thrown away.",
            _ => "Cannot be sold."
        };

        private string ConfirmText(GoodsTruck truck)
        {
            if (truck == null) return "";
            var offer = GoodsWorld.TruckOfferOf(truck, TruckOfferDefs());
            var price = offer == null ? "no listed price, so it sells for nothing"
                : $"{GoodsWorld.TruckResalePercent}% of its {PlayerHud.FormatCash(offer.PriceCents)} list price";
            var schedules = Primary?.FarmSchedules.Count(x => x.TruckId == truck.Id) ?? 0;
            var cancelled = schedules == 0 ? "" : $" {schedules} repeating farm order{(schedules == 1 ? "" : "s")} picked up by it will be cancelled.";
            var route = RouteOf(truck) == null ? "" : " Its route stays for the company's other trucks.";
            return $"Sell {truck.Name} for {PlayerHud.FormatCash(ResaleCents(truck))} ({price})? It leaves at once.{route}{cancelled}";
        }

        private GoodsTruck Current(string truckId) => Primary?.Trucks.FirstOrDefault(x => x.Id == truckId);

        private GoodsRoute RouteOf(GoodsTruck truck) => Primary == null ? null : GoodsWorld.RouteOf(Primary, truck);

        private VisualElement Chooser(string key, string field, string label, string value, Action<int> cycle)
        {
            var row = UiTemplates.Clone(Templates + "LogisticsChooser");
            row.name = $"logistics-{field}-{key}";
            row.Part<Label>("caption").text = label;
            row.Part<Button>("previous").clicked += () => cycle(-1);
            row.Part<Button>("next").clicked += () => cycle(1);
            var shown = row.Part<Label>("value");
            shown.name = $"logistics-{field}-value-{key}";
            shown.text = value;
            return row;
        }

        // "<pickup site> to <dropoff site>", with the cargo when the route has a filter.
        private string RouteName(GoodsRoute route)
        {
            var from = Dock(route.PickupDockId);
            var to = Dock(route.DropoffDockId);
            var cargo = route.AllowedItemIds.Count == 0 ? "" : $" ({string.Join(", ", route.AllowedItemIds.Select(ItemName))})";
            return $"{SiteName(from?.SiteId ?? "")} to {SiteName(to?.SiteId ?? "")}{cargo}";
        }

        private VisualElement SiteCard(GoodsSnapshot primary, GoodsSite remote)
        {
            var card = UiTemplates.Clone(Templates + "LogisticsSiteCard");
            card.name = $"logistics-site-{remote.Id}";
            card.Part<Label>("title").text = remote.Name;
            var metres = GoodsWorld.RoadMetres(primary, GoodsWorld.ViewSiteId(primary), remote.Id);
            var distance = card.Part<Label>("distance");
            if (metres is { } road) distance.text = $"{road.ToString("N0", CultureInfo.InvariantCulture)} m by road";
            else distance.RemoveFromHierarchy();
            var stock = card.Part("stock");
            var view = SiteView(remote.Id);
            if (view == null)
            {
                stock.Add(Note("Loading...", "logi-stock__none"));
                return card;
            }
            var storage = view.Locations.FirstOrDefault(x => x.SiteId == remote.Id && x.Kind == "storage");
            var dock = view.Equipment.Where(x => x.Kind == GoodsWorld.DockKind && x.State == EquipmentState.Placed)
                .OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (storage != null) Stock(stock, view, storage.Id, "Storage", dock == null ? null : ("Ship", dock.InputLocationId));
            if (dock != null)
            {
                Stock(stock, view, dock.InputLocationId, "Dock outgoing", storage == null ? null : ("Unstage", storage.Id));
                Stock(stock, view, dock.OutputLocationId, "Dock incoming", storage == null ? null : ("Store", storage.Id));
            }
            else stock.Add(Note("No dock here: trucks cannot serve this site.", "logi-stock__heading"));
            return card;
        }

        // One location's stacks, each with its live count and a button that moves one stack to target.
        private void Stock(VisualElement parent, GoodsSnapshot view, string locationId, string title, (string Label, string LocationId)? target)
        {
            var siteId = GoodsWorld.ViewSiteId(view);
            var location = view.Locations.First(x => x.Id == locationId);
            var heading = Note("", "logi-stock__heading");
            parent.Add(heading);
            Live(heading, null, () => $"{title}  {Slots(SiteView(siteId), locationId)}/{location.Capacity}");
            var groups = view.Lots.Where(x => x.LocationId == locationId).Select(x => (x.ItemId, x.Spoiled)).Distinct()
                .OrderBy(x => x.ItemId, StringComparer.Ordinal).ThenBy(x => x.Spoiled).ToList();
            if (groups.Count == 0) parent.Add(Note("empty", "logi-stock__none"));
            foreach (var (itemId, spoiled) in groups)
            {
                var row = UiTemplates.Clone(Templates + "LogisticsStockRow");
                row.name = $"logistics-stock-{locationId}-{itemId}{(spoiled ? "-spoiled" : "")}";
                Live(row.Part<Label>("count"), null, () => $"{ItemName(itemId)}{(spoiled ? " (spoiled)" : "")} ×{Units(SiteView(siteId), locationId, itemId, spoiled)}");
                var button = row.Part<Button>("move");
                if (target is { } move)
                {
                    button.name = $"logistics-move-{locationId}-{itemId}{(spoiled ? "-spoiled" : "")}";
                    button.text = move.Label;
                    button.clicked += () => MoveStack(siteId, locationId, itemId, spoiled, move.LocationId);
                }
                else button.RemoveFromHierarchy();
                parent.Add(row);
            }
        }

        private string TruckStatus(GoodsTruck truck)
        {
            if (truck == null) return "";
            var here = SiteName(truck.SiteId);
            var there = SiteName(truck.DestinationSiteId);
            // On the generated roads (decision 0032) the time left is an estimate at the current traffic.
            var estimate = SitePlacement.Active == null ? null : GoodsWorld.EstimateRoadSeconds(Primary, SitePlacement.Active.Roads, truck);
            var left = estimate is { } seconds
                ? $"about {PlayerHud.FormatDuration(seconds, false)}{(truck.RemainingSeconds == 0 ? " (in traffic)" : "")}"
                : PlayerHud.FormatDuration(truck.RemainingSeconds, false);
            return truck.State switch
            {
                TruckState.Parked => $"Parked at {here}: no route",
                _ when !string.IsNullOrEmpty(truck.OrderId) && truck.State == TruckState.ToPickup => $"Driving to {there} for a farm order: {left}",
                _ when !string.IsNullOrEmpty(truck.OrderId) && truck.State == TruckState.ToDropoff => $"Bringing a farm order to {there}: {left}",
                TruckState.ToPickup => $"Driving to {there} to load: {left}",
                TruckState.ToDropoff => $"Driving to {there} with cargo: {left}",
                TruckState.ToPark => $"Driving to {there} to park: {left}",
                TruckState.Loading when !string.IsNullOrEmpty(truck.OrderId) => $"Loading a farm order at {here}",
                TruckState.Loading => $"Loading at {here}" + DockNote(truck, GoodsWorld.WorkingDockOf(Primary, truck)),
                TruckState.Unloading => $"Unloading at {here}" + DockNote(truck, GoodsWorld.WorkingDockOf(Primary, truck)),
                _ => ""
            };
        }

        // Why a truck at a dock may be idle: the dock is not placed, or (a restaurant dock, decision 0034) another truck holds it.
        private string DockNote(GoodsTruck truck, string dockId)
        {
            if (Dock(dockId)?.State != EquipmentState.Placed) return " (dock not placed)";
            var others = Primary?.Trucks.Any(x => x.Id != truck.Id && x.Docked && x.State is TruckState.Loading or TruckState.Unloading
                && GoodsWorld.WorkingDockOf(Primary, x) == dockId) == true;
            return !truck.Docked && others ? " (waiting for the dock)" : "";
        }

        // Where the truck is headed: the site it drives to, or where its work sends it next.
        private string DestinationText(GoodsTruck truck)
        {
            if (truck == null) return "";
            if (truck.Driving) return $"Destination: {SiteName(truck.DestinationSiteId)}";
            var order = Order(truck);
            var route = RouteOf(truck);
            return truck.State switch
            {
                TruckState.Loading when order != null => $"Next: {SiteName(order.SiteId)}, after loading",
                TruckState.Unloading when order != null => "Next: parks here once unloaded",
                TruckState.Loading when route != null => $"Next: {SiteName(Dock(route.DropoffDockId)?.SiteId)}, after loading",
                TruckState.Unloading when route != null => $"Next: back to {SiteName(Dock(route.PickupDockId)?.SiteId)}, once empty",
                _ => "Destination: none"
            };
        }

        // The route the truck repeats, the farm order it serves, or none.
        private string RouteText(GoodsTruck truck)
        {
            if (truck == null) return "";
            if (Order(truck) is { } order)
                return $"Farm order: {order.Units} {ItemName(order.ItemId)} from {SiteName(FarmCatalog.GateSiteId(order.FarmLotId))} to {SiteName(order.SiteId)}";
            var route = RouteOf(truck);
            return route == null ? "Route: none (parked)" : $"Route: {RouteName(route)}";
        }

        private GoodsFarmOrder Order(GoodsTruck truck) =>
            string.IsNullOrEmpty(truck?.OrderId) ? null : Primary?.FarmOrders.FirstOrDefault(x => x.Id == truck.OrderId);

        private string CargoText(GoodsTruck truck)
        {
            var site = Primary;
            if (truck == null || site == null) return "";
            var lots = site.Lots.Where(x => x.LocationId == truck.CargoLocationId).ToList();
            var slots = GoodsSlots.SlotsUsed(lots, Session.MaxStack);
            var goods = lots.GroupBy(x => (x.ItemId, x.Spoiled)).OrderBy(x => x.Key.ItemId, StringComparer.Ordinal)
                .Select(x => $"{x.Sum(y => y.Quantity)} {ItemName(x.Key.ItemId)}{(x.Key.Spoiled ? " (spoiled)" : "")}");
            return $"Cargo {slots}/{truck.CargoSlots} slots: {(lots.Count == 0 ? "empty" : string.Join(", ", goods))}";
        }

        private GoodsEquipment Dock(string dockId) => Docks().FirstOrDefault(x => x.Id == dockId);

        private string DockName(string dockId)
        {
            var dock = Dock(dockId);
            if (dock == null) return string.IsNullOrEmpty(dockId) ? "(choose a dock)" : "(unknown dock)";
            var siblings = Docks().Where(x => x.SiteId == dock.SiteId).ToList();
            var suffix = siblings.Count > 1 ? $" {siblings.IndexOf(dock) + 1}" : "";
            return $"{SiteName(dock.SiteId)} dock{suffix}{(dock.State == EquipmentState.Placed ? "" : " (not placed)")}";
        }

        private string SiteName(string siteId) => Primary?.Sites.FirstOrDefault(x => x.Id == siteId)?.Name ?? siteId ?? "";

        private string ItemName(string itemId) => Session.Items.FirstOrDefault(x => x != null && x.Id == itemId)?.DisplayName ?? itemId;

        private int Slots(GoodsSnapshot site, string locationId) =>
            site == null ? 0 : GoodsSlots.SlotsUsed(site.Lots.Where(x => x.LocationId == locationId), Session.MaxStack);

        private static int Units(GoodsSnapshot site, string locationId, string itemId, bool spoiled) => site == null ? 0
            : site.Lots.Where(x => x.LocationId == locationId && x.ItemId == itemId && x.Spoiled == spoiled).Sum(x => x.Quantity);

        private string Track()
        {
            var requestId = Guid.NewGuid().ToString("N");
            _pending.Add(requestId);
            return requestId;
        }

        private void OnResult(GoodsOutcome outcome)
        {
            if (!_pending.Remove(outcome.RequestId)) return;
            if (!outcome.Accepted) LastRejection = string.IsNullOrEmpty(outcome.Reason) ? "rejected" : outcome.Reason;
        }

        private void Subscribe(ClientSiteSubscription subscription)
        {
            if (ReferenceEquals(subscription, _subscription)) return;
            if (_subscription != null) _subscription.ResultReceived -= OnResult;
            _subscription = subscription;
            if (_subscription != null) _subscription.ResultReceived += OnResult;
            _pending.Clear();
        }

        private void OnDestroy()
        {
            Subscribe(null);
            if (_hookedMap != null) _hookedMap.TruckClicked -= OnMapTruckClicked;
            _hookedMap = null;
        }

        // A label whose text is refreshed every frame from text.
        private void Live(Label label, string name, Func<string> text)
        {
            if (name != null) label.name = name;
            _live.Add((label, text));
        }

        // A button whose enabled state is refreshed every frame.
        private void LiveEnabled(VisualElement element, Func<bool> enabled)
        {
            element.SetEnabled(enabled());
            _liveEnabled.Add((element, enabled));
        }

        // Scrolls the selected truck's card into view once it is laid out (the Trucks app may have just opened).
        private void ScrollToSelected()
        {
            if (_scrollTo == null || _scroll == null) return;
            if (!_truckCards.TryGetValue(_scrollTo, out var card))
            {
                if (Primary == null || Fleet(Primary).All(x => x.Id != _scrollTo)) _scrollTo = null;
                return;
            }
            if (float.IsNaN(card.layout.height) || card.layout.height <= 0f || _scroll.contentViewport.layout.height <= 0f) return;
            _scroll.ScrollTo(card);
            _scrollTo = null;
        }

        // ---------- Phone map ----------

        // Feeds the phone map the fleet's positions while its Map app is showing, and hooks its truck clicks once.
        private void UpdatePhoneMap()
        {
            if (_phone == null && Time.unscaledTime >= _nextPhoneSearch)
            {
                _nextPhoneSearch = Time.unscaledTime + 1f;
                _phone = FindAnyObjectByType<PhonePanel>();
            }
            var view = _phone != null ? _phone.MapView : null;
            if (view == null) return;
            if (!ReferenceEquals(view, _hookedMap))
            {
                if (_hookedMap != null) _hookedMap.TruckClicked -= OnMapTruckClicked;
                _hookedMap = view;
                view.TruckClicked += OnMapTruckClicked;
            }
            view.HighlightedTruckId = SelectedTruckId;
            var site = Primary;
            var showing = site != null && Session.IsRunning && interaction.Screen == InteractionScreen.Phone && _phone.Tab == PhonePanel.MapTab
                && view.Layout != null;
            _mapTrucks.Clear();
            if (!showing)
            {
                if (view.TruckIds.Count > 0) view.SetTrucks(_mapTrucks);
                return;
            }
            if (!ReferenceEquals(site, _mapBaseline))
            {
                _mapBaseline = site;
                _mapReceivedAt = Time.time;
            }
            var since = Time.time - _mapReceivedAt;
            var network = SitePlacement.Active?.Roads;
            _mapStacks.Clear();
            foreach (var truck in Fleet(site).OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (TruckOnMap(site, truck, network, since) is not { } map) continue;
                var stack = 0;
                if (!truck.Driving)
                {
                    stack = _mapStacks.GetValueOrDefault(truck.SiteId);
                    _mapStacks[truck.SiteId] = stack + 1;
                }
                _mapTrucks.Add(new PhoneMapView.MapTruck(truck.Id, truck.Name, map, stack));
            }
            view.SetTrucks(_mapTrucks);
        }

        private void OnMapTruckClicked(string truckId)
        {
            if (_phone == null) return;
            _phone.ShowTab(PhonePanel.TrucksTab);
            SelectTruck(truckId);
        }

        // Where a truck is on the map: on its road leg (extrapolated since the baseline, like TruckPresenter), part way along an
        // abstract trip, or at the site it stands at. Null when the site has no place on the map.
        private static Vector2? TruckOnMap(GoodsSnapshot site, GoodsTruck truck, RoadNetwork network, float since)
        {
            if (truck.Driving && truck.OnRoad && network != null && network.TryGetSegment(truck.LegSegmentId, out var segment))
                return RoadPose.Along(network, segment, RoadPose.LegOffset(truck, since), truck.LegTo >= truck.LegFrom, 0).Position;
            var from = SiteOnMap(site, truck.SiteId);
            if (truck.Driving && from is { } start && SiteOnMap(site, truck.DestinationSiteId) is { } end)
            {
                var total = GoodsWorld.RoadSeconds(site, truck.SiteId, truck.DestinationSiteId, truck.SpeedMetresPerSecond);
                var done = total <= 0 ? 1f
                    : Mathf.Clamp01((total - truck.RemainingSeconds + Mathf.Clamp(since, 0f, RoadPose.MaxExtrapolationSeconds)) / total);
                return Vector2.Lerp(start, end, done);
            }
            return from;
        }

        // A site's place on the map: the middle of its lot, or its map record (a farm gate, a dev site).
        private static Vector2? SiteOnMap(GoodsSnapshot site, string siteId)
        {
            var lot = SitePlacement.Active?.LotOf(siteId);
            if (lot != null) return new Vector2(lot.X + lot.Width * 0.5f, lot.Z + lot.Depth * 0.5f);
            var record = site.Sites.FirstOrDefault(x => x.Id == siteId);
            return record == null ? null : new Vector2(record.MapX, record.MapZ);
        }

        private static int Wrap(int index, int count) => count == 0 ? 0 : ((index % count) + count) % count;

        // A muted line in an empty column.
        private static Label Empty(string text) => Note(text, "logi__empty");

        private static Label Note(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(className);
            return label;
        }
    }
}
