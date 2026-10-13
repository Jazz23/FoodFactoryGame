// Local player's phone (owner request 2026-10-13, decision 0047), drawn from the Phone UXML and USS under Assets/UI/Resources/UI
// and opened with C (EquipmentInteraction.TogglePhone). It has five apps:
// - Map: the world from above (PhoneMapView) with every lot coloured by what it is and who owns it. Tapping a lot shows its
//   price, owner and a Buy button (the same server request as PropertyPanel), a rename box for the company's own places and,
//   for a farm, its type and the goods it sells: choose one, how many packs, delivery (flat fee) or pickup by a parked company
//   truck, and the dock it goes to, then Order, or Repeat every few game hours. Owning the farm halves the goods price.
// - Orders: the company's open farm orders with their trucks' progress, and its repeating orders with Cancel.
// - Staff: this site's employees with Hire, Fire and Tasks (the employee screen, which returns to the phone on Esc).
// - Trucks: the logistics screen (routes, the fleet, buying trucks, stock at other sites), moved into the phone while this app
//   is open (LogisticsPanel.Embed), so it is the same screen L opens.
// - Places: every site of the company with a rename box.
// Every button is a request the server checks; nothing changes here until the next baseline. The farm types and prices come from
// the replicated layout (FarmCatalog) and the session's supplier offers, the same content the server uses, so nothing extra is
// sent. Pages are rebuilt only when what they list changes; their live text and button states refresh every frame, so a click
// is never lost to a rebuild. Choices on the order form are this client's presentation only until ordered.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Employees;
using FoodFactoryGame.Session.Equipment;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.WorldMap
{
    [DisallowMultipleComponent]
    public sealed class PhonePanel : MonoBehaviour
    {
        public const string MapTab = "map";
        public const string OrdersTab = "orders";
        public const string StaffTab = "staff";
        public const string TrucksTab = "trucks";
        public const string PlacesTab = "places";
        private static readonly string[] Tabs = { MapTab, OrdersTab, StaffTab, TrucksTab, PlacesTab };

        private static readonly Color Yours = new(0.31f, 0.77f, 0.42f);
        private static readonly Color ForSale = new(0.95f, 0.64f, 0.23f);
        private static readonly Color Farm = new(0.85f, 0.76f, 0.35f);
        private static readonly Color Taken = new(0.54f, 0.29f, 0.27f);

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;
        // The generated world's layout; null in a dev world, which has no map.
        [SerializeField] private WorldLayoutPresenter map;
        // Shown inside the Trucks app.
        [SerializeField] private Logistics.LogisticsPanel logistics;

        private readonly HashSet<string> _pending = new();
        // Requests whose acceptance makes this client watch a new site (a bought lot's site).
        private readonly Dictionary<string, string> _watchOnAccept = new();
        private readonly List<(TextElement Element, Func<string> Text)> _live = new();
        private readonly List<(VisualElement Element, Func<bool> Enabled)> _liveEnabled = new();
        // This client's unsent names, by site.
        private readonly Dictionary<string, string> _names = new();
        private readonly Dictionary<string, string> _signatures = new();
        private Dictionary<string, FarmKind> _farmKinds = new();
        private int _farmOfferCount = -1;
        private ClientSiteSubscription _subscription;
        private PhoneMapView _mapView;
        private VisualElement _window;
        private VisualElement _dim;
        private Label _clock;
        private Label _cash;
        private Label _status;
        private Label _mapEmpty;
        private VisualElement _detail;
        private VisualElement _orders;
        private VisualElement _schedules;
        private VisualElement _staff;
        private VisualElement _places;
        private Label _staffSite;
        private Label _staffCount;
        private Button _hire;
        private readonly Dictionary<string, VisualElement> _pages = new();
        private readonly Dictionary<string, Button> _tabButtons = new();
        private bool _wasOpen;

        public VisualElement Window => _window;
        public string Tab { get; private set; } = MapTab;
        public string SelectedLotId { get; private set; }
        // The order form: the chosen offer, packs, delivery or pickup, the truck and dock, and the repeat interval.
        public string GoodOfferId { get; private set; }
        public int Packs { get; private set; } = 1;
        public bool Pickup { get; private set; }
        public string TruckId { get; private set; }
        public string DockId { get; private set; }
        public int EveryHours { get; private set; } = 4;
        public string LastRejection { get; private set; }
        public string LastAccepted { get; private set; }
        public bool HasPendingRequests => _pending.Count > 0;
        public PhoneMapView MapView => _mapView;

        private SessionRoot Session => interaction.Session;
        private GoodsSnapshot Primary => Session.ClientSite;
        private GoodsCompany Company => Primary?.Companies.FirstOrDefault(x => x.SiteIds.Contains(Session.ClientSiteId));
        private IReadOnlyDictionary<string, PropertyOffer> LotOffers =>
            map != null ? map.Offers : (IReadOnlyDictionary<string, PropertyOffer>)new Dictionary<string, PropertyOffer>();

        private void Start()
        {
            var root = document.rootVisualElement;
            root.Clear();
            UiTemplates.Fill(root, "Phone/Phone");
            _window = root.Part("phone");
            _dim = root.Part("phone-dim");
            _clock = root.Part<Label>("phone-clock");
            _cash = root.Part<Label>("phone-cash");
            _status = root.Part<Label>("phone-status");
            _mapEmpty = root.Part<Label>("phone-map-empty");
            _detail = root.Part("phone-detail");
            _orders = root.Part("phone-orders");
            _schedules = root.Part("phone-schedules");
            _staff = root.Part("phone-staff");
            _places = root.Part("phone-places");
            _staffSite = root.Part<Label>("phone-staff-site");
            _staffCount = root.Part<Label>("phone-staff-count");
            _hire = root.Part<Button>("phone-hire");
            _hire.clicked += Hire;
            root.Part<Button>("phone-close").clicked += interaction.CloseScreen;
            foreach (var tab in Tabs)
            {
                _pages[tab] = root.Part("phone-page-" + tab);
                var button = root.Part<Button>("phone-tab-" + tab);
                var chosen = tab;
                button.clicked += () => ShowTab(chosen);
                _tabButtons[tab] = button;
            }
            _mapView = new PhoneMapView { LotColour = LotColour };
            _mapView.LotClicked += SelectLot;
            root.Part("phone-map-frame").Insert(0, _mapView);
            ShowTab(MapTab);
            _window.Show(false);
            _dim.Show(false);
        }

        private void Update()
        {
            if (_window == null) return;
            Subscribe(Session.ClientSubscription);
            var site = Primary;
            var open = site != null && Session.IsRunning && interaction.Screen == InteractionScreen.Phone;
            _window.Show(open);
            _dim.Show(open);
            // The logistics window lives in the phone only while the Trucks app is showing.
            logistics.Embed(open && Tab == TrucksTab ? _pages[TrucksTab] : null);
            if (!open)
            {
                _wasOpen = false;
                return;
            }
            WatchCompanySites(site);
            var focused = document.rootVisualElement.panel?.focusController?.focusedElement as VisualElement;
            interaction.PhoneTextFocused = focused is TextField || focused?.GetFirstAncestorOfType<TextField>() != null;
            RefreshFarmKinds();
            _mapView.Layout = map != null ? map.Shown : null;
            _mapEmpty.Show(_mapView.Layout == null);
            _mapView.Player = PlayerOnMap();
            if (!_wasOpen)
            {
                _wasOpen = true;
                if (SelectedLotId == null && _mapView.Player is { } player) _mapView.Focus(player);
            }
            _mapView.MarkDirtyRepaint();

            var time = GoodsWorld.GameTime(site.ClockSeconds);
            _clock.SetText($"Day {time.Day}  {time.Hour:00}:{time.Minute:00}");
            _cash.SetText(Company == null ? "" : PlayerHud.FormatCash(Company.Cash));
            _status.SetText(HasPendingRequests ? "Waiting for the server..." : !string.IsNullOrEmpty(LastRejection) ? $"Rejected: {LastRejection}"
                : string.IsNullOrEmpty(LastAccepted) ? " " : LastAccepted);
            _status.EnableInClassList("phone__status--ok", !HasPendingRequests && string.IsNullOrEmpty(LastRejection) && !string.IsNullOrEmpty(LastAccepted));

            // A text box being typed in is never rebuilt under the typist.
            if (!interaction.PhoneTextFocused)
            {
                if (Tab == MapTab) Rebuild("detail", DetailSignature(site), BuildDetail);
                if (Tab == OrdersTab) Rebuild("orders", OrdersSignature(site), BuildOrders);
                if (Tab == StaffTab) Rebuild("staff", StaffSignature(site), BuildStaff);
                if (Tab == PlacesTab) Rebuild("places", PlacesSignature(site), BuildPlaces);
            }
            foreach (var (element, text) in _live) element.SetText(text());
            foreach (var (element, enabled) in _liveEnabled) element.SetEnabled(enabled());
        }

        // ---------- Tabs and selection ----------

        public void ShowTab(string tab)
        {
            if (!Tabs.Contains(tab)) return;
            Tab = tab;
            foreach (var name in Tabs)
            {
                _pages[name].Show(name == tab);
                _tabButtons[name].EnableInClassList("ff-segment--chosen", name == tab);
            }
            _signatures.Clear();
        }

        // Selects a lot on the map (null clears it); choosing another lot resets the order form.
        public void SelectLot(string lotId)
        {
            if (lotId == SelectedLotId) return;
            SelectedLotId = lotId;
            _mapView.SelectedLotId = lotId;
            GoodOfferId = null;
            Packs = 1;
            LastRejection = null;
            LastAccepted = null;
            _mapView.MarkDirtyRepaint();
        }

        // Shows a place on the map and selects its lot.
        public void ShowOnMap(string lotId)
        {
            ShowTab(MapTab);
            SelectLot(lotId);
            var lot = map != null ? map.Shown?.Lots.FirstOrDefault(x => x.Id == lotId) : null;
            if (lot != null) _mapView.Focus(new Vector2(lot.X + lot.Width * 0.5f, lot.Z + lot.Depth * 0.5f));
        }

        public void ChooseGood(string offerId)
        {
            GoodOfferId = offerId;
            Packs = 1;
            DockId ??= Docks().FirstOrDefault()?.Id;
            TruckId ??= ParkedTrucks().FirstOrDefault()?.Id;
        }

        public void SetPacks(int packs) => Packs = Mathf.Clamp(packs, 1, GoodsWorld.MaxFarmOrderPacks);
        public void SetEveryHours(int hours) => EveryHours = Mathf.Clamp(hours, 1, GoodsWorld.MaxFarmScheduleHours);

        public void SetPickup(bool pickup)
        {
            Pickup = pickup;
            if (pickup) TruckId ??= ParkedTrucks().FirstOrDefault()?.Id;
        }

        public void CycleDock(int step) => DockId = Cycle(Docks().Select(x => x.Id).ToList(), DockId, step);
        public void CycleTruck(int step) => TruckId = Cycle(ParkedTrucks().Select(x => x.Id).ToList(), TruckId, step);

        private static string Cycle(IReadOnlyList<string> options, string current, int step)
        {
            if (options.Count == 0) return null;
            var index = current == null ? -1 : options.ToList().IndexOf(current);
            return options[((index < 0 ? 0 : index + step) % options.Count + options.Count) % options.Count];
        }

        // ---------- Requests ----------

        // Buys the selected lot with the cash of this site's company (decision 0028).
        public void Buy()
        {
            var bridge = _subscription?.Bridge;
            var offer = SelectedOffer;
            if (bridge == null || offer == null || HasPendingRequests) return;
            var request = Track("Bought " + LotName(offer.LotId) + ".");
            _watchOnAccept[request] = offer.SiteId;
            bridge.RequestBuyProperty(request, _subscription.SiteId, offer.LotId);
        }

        // Orders the form's packs from the selected farm (decision 0047).
        public void Order()
        {
            var bridge = _subscription?.Bridge;
            var offer = SelectedOffer;
            if (bridge == null || offer == null || GoodOfferId == null || DockId == null || (Pickup && TruckId == null)) return;
            bridge.RequestFarmOrder(Track($"Ordered {Packs} x {GoodName(GoodOfferId)}."), offer.LotId, GoodOfferId, Packs, DockId, Pickup ? TruckId : "");
        }

        public void Schedule()
        {
            var bridge = _subscription?.Bridge;
            var offer = SelectedOffer;
            if (bridge == null || offer == null || GoodOfferId == null || DockId == null || (Pickup && TruckId == null)) return;
            bridge.RequestFarmSchedule(Track($"Repeating every {EveryHours} h."), offer.LotId, GoodOfferId, Packs, DockId, Pickup ? TruckId : "", EveryHours);
        }

        public void CancelSchedule(string scheduleId) => _subscription?.Bridge?.RequestCancelFarmSchedule(Track("Repeating order cancelled."), scheduleId);

        public void Rename(string siteId, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            _subscription?.Bridge?.RequestRenameSite(Track("Renamed."), siteId, name.Trim());
        }

        public void Hire() => interaction.Hire();

        // Opens an employee's task screen from the staff list.
        public void OpenTasks(string employeeId)
        {
            var worker = FindObjectsByType<EmployeeWorker>().FirstOrDefault(x => x != null && x.EmployeeId == employeeId);
            if (worker != null) interaction.OpenEmployeeFromPhone(worker);
        }

        private string Track(string success)
        {
            var requestId = Guid.NewGuid().ToString("N");
            _pending.Add(requestId);
            _successes[requestId] = success;
            LastRejection = null;
            LastAccepted = null;
            return requestId;
        }

        private readonly Dictionary<string, string> _successes = new();

        private void OnResult(GoodsOutcome outcome)
        {
            if (!_pending.Remove(outcome.RequestId)) return;
            _successes.Remove(outcome.RequestId, out var success);
            if (_watchOnAccept.Remove(outcome.RequestId, out var siteId) && outcome.Accepted) _subscription?.Watch(siteId);
            if (outcome.Accepted)
            {
                LastAccepted = success;
                _names.Clear();
            }
            else LastRejection = string.IsNullOrEmpty(outcome.Reason) ? "rejected" : outcome.Reason;
            Debug.Log($"[Phone] {(outcome.Accepted ? "Accepted" : "Rejected")}: {outcome.Reason} (revision {outcome.Revision}).");
        }

        private void Subscribe(ClientSiteSubscription subscription)
        {
            if (ReferenceEquals(subscription, _subscription)) return;
            if (_subscription != null) _subscription.ResultReceived -= OnResult;
            _subscription = subscription;
            if (_subscription != null) _subscription.ResultReceived += OnResult;
            _pending.Clear();
            _watchOnAccept.Clear();
            _successes.Clear();
        }

        private void OnDestroy() => Subscribe(null);

        // ---------- Content ----------

        private PropertyOffer SelectedOffer => SelectedLotId != null && LotOffers.TryGetValue(SelectedLotId, out var offer) ? offer : null;

        private void RefreshFarmKinds()
        {
            var offers = LotOffers;
            if (offers.Count == _farmOfferCount) return;
            _farmOfferCount = offers.Count;
            _farmKinds = FarmCatalog.KindsOf(offers.Values);
        }

        public FarmKind? KindOf(string lotId) => lotId != null && _farmKinds.TryGetValue(lotId, out var kind) ? kind : null;

        // The supplier offers of the goods a farm sells, in the farm type's order.
        public IReadOnlyList<OfferAsset> FarmGoods(string lotId)
        {
            if (KindOf(lotId) is not { } kind) return Array.Empty<OfferAsset>();
            var items = FarmCatalog.Items(kind);
            return Session.Offers.Where(x => x != null && x.Equipment == null && x.Truck == null && items.Contains(x.ItemId))
                .OrderBy(x => items.ToList().IndexOf(x.ItemId)).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        }

        private bool OwnsSelectedFarm => Company != null && GoodsWorld.OwnsLot(Primary, Company.Id, SelectedLotId);

        // What the form's order costs: goods (halved for an owned farm) and the delivery fee.
        public long OrderCents()
        {
            var good = Session.Offers.FirstOrDefault(x => x != null && x.Id == GoodOfferId);
            if (good == null) return 0;
            return GoodsWorld.FarmGoodsCents(good.PriceCents, Packs, OwnsSelectedFarm) + (Pickup ? 0 : GoodsWorld.FarmDeliveryFeeCents);
        }

        // Placed docks of the company on this site and on its watched sites, this site's first.
        public IReadOnlyList<GoodsEquipment> Docks()
        {
            var site = Primary;
            var company = Company;
            if (site == null || company == null) return Array.Empty<GoodsEquipment>();
            return company.SiteIds.Select(SiteView).Where(x => x != null).SelectMany(x => x.Equipment)
                .Where(x => x.Kind == GoodsWorld.DockKind && x.State == EquipmentState.Placed && company.SiteIds.Contains(x.SiteId))
                .GroupBy(x => x.Id).Select(x => x.First())
                .OrderBy(x => x.SiteId == Session.ClientSiteId ? 0 : 1).ThenBy(x => x.SiteId, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal)
                .ToList();
        }

        // The company's fleet trucks free for a pickup.
        public IReadOnlyList<GoodsTruck> ParkedTrucks() => Primary?.Trucks
            .Where(x => !x.Supplier && x.State == TruckState.Parked && string.IsNullOrEmpty(x.OrderId))
            .OrderBy(x => x.Id, StringComparer.Ordinal).ToList() ?? new List<GoodsTruck>();

        private GoodsSnapshot SiteView(string siteId) => siteId == _subscription?.SiteId ? Primary : _subscription?.Remote(siteId);

        // The phone watches every site of the company, so docks and names elsewhere are known.
        private void WatchCompanySites(GoodsSnapshot site)
        {
            var company = Company;
            if (company == null || _subscription == null) return;
            foreach (var siteId in company.SiteIds.Where(x => x != _subscription.SiteId && !_subscription.IsWatching(x))) _subscription.Watch(siteId);
        }

        private Color? LotColour(string lotId)
        {
            if (!LotOffers.TryGetValue(lotId, out var offer)) return null;
            var owner = Primary?.Properties.FirstOrDefault(x => x.LotId == lotId)?.CompanyId;
            if (owner != null) return owner == Company?.Id ? Yours : Taken;
            if (!offer.ForSale) return Taken;
            return offer.Category == GoodsWorld.FarmCategory ? Farm : ForSale;
        }

        private Vector2? PlayerOnMap()
        {
            var placement = SitePlacement.Active;
            if (placement == null) return null;
            var avatar = FindObjectsByType<PlayerAvatar>().FirstOrDefault(x => x != null && x.IsOwner);
            return avatar == null ? null : placement.ToMap(avatar.transform.position);
        }

        private string SiteName(string siteId) => Primary?.Sites.FirstOrDefault(x => x.Id == siteId)?.Name ?? siteId ?? "";

        // A lot's name: the company's own name for its place, or what it is and its building.
        public string LotName(string lotId)
        {
            if (!LotOffers.TryGetValue(lotId ?? "", out var offer)) return lotId ?? "";
            if (Company?.SiteIds.Contains(offer.SiteId) == true) return SiteName(offer.SiteId);
            return $"{CategoryName(offer)} {offer.BuildingId}";
        }

        private string CategoryName(PropertyOffer offer) => offer.Category switch
        {
            GoodsWorld.RestaurantKind => "Restaurant",
            GoodsWorld.FactoryKind => "Factory",
            GoodsWorld.FarmCategory => KindOf(offer.LotId) is { } kind ? FarmCatalog.Name(kind) : "Farm",
            GoodsWorld.StationCategory => "Station",
            _ => offer.Category
        };

        private string ItemName(string itemId) => Session.Items.FirstOrDefault(x => x != null && x.Id == itemId)?.DisplayName ?? itemId;

        private string GoodName(string offerId) => ItemName(Session.Offers.FirstOrDefault(x => x != null && x.Id == offerId)?.ItemId);

        private string DockName(string dockId)
        {
            var docks = Docks();
            var dock = docks.FirstOrDefault(x => x.Id == dockId);
            if (dock == null) return string.IsNullOrEmpty(dockId) ? "(no dock)" : "(dock gone)";
            var siblings = docks.Where(x => x.SiteId == dock.SiteId).ToList();
            return $"{SiteName(dock.SiteId)} dock{(siblings.Count > 1 ? $" {siblings.IndexOf(dock) + 1}" : "")}";
        }

        private string TruckName(string truckId) => Primary?.Trucks.FirstOrDefault(x => x.Id == truckId)?.Name ?? "(no parked truck)";

        // ---------- Pages ----------

        private void Rebuild(string key, string signature, Action build)
        {
            if (_signatures.TryGetValue(key, out var shown) && shown == signature) return;
            _signatures[key] = signature;
            _live.Clear();
            _liveEnabled.Clear();
            build();
        }

        private string DetailSignature(GoodsSnapshot site)
        {
            var text = new StringBuilder(SelectedLotId ?? "-");
            text.Append('|').Append(Primary.Properties.FirstOrDefault(x => x.LotId == SelectedLotId)?.CompanyId);
            text.Append('|').Append(GoodOfferId).Append('|').Append(Pickup).Append('|').Append(_farmKinds.Count);
            if (SelectedOffer is { } offer) text.Append('|').Append(SiteName(offer.SiteId));
            return text.ToString();
        }

        private void BuildDetail()
        {
            _detail.Clear();
            var offer = SelectedOffer;
            if (offer == null)
            {
                _detail.Add(Text("Tap a lot on the map to see its price, buy it, or order from a farm. Drag to move, scroll to zoom.", "ff-small", "ff-wrap"));
                return;
            }
            var mine = Company?.SiteIds.Contains(offer.SiteId) == true;
            _detail.Add(Live(Text("", "phone-detail__title"), () => LotName(offer.LotId)));
            _detail.Add(Text(CategoryName(offer) + (offer.Category == GoodsWorld.FarmCategory ? "" : $"  ·  lot {offer.Width} x {offer.Depth} m"), "ff-small", "phone-detail__kind"));
            var facts = new VisualElement();
            facts.AddToClassList("ff-inset");
            facts.AddToClassList("phone-detail__facts");
            facts.Add(Text($"Price: {PlayerHud.FormatCash(offer.PriceCents)}", "ff-text"));
            var state = Live(Text("", "phone-detail__state"), () =>
            {
                var owner = Primary?.Properties.FirstOrDefault(x => x.LotId == offer.LotId)?.CompanyId;
                return owner != null ? (owner == Company?.Id ? "Owned by your company" : "Owned by another company") : offer.ForSale ? "For sale" : "Not for sale";
            });
            var owned = Primary.Properties.FirstOrDefault(x => x.LotId == offer.LotId)?.CompanyId;
            state.EnableInClassList("phone-detail__state--yours", owned != null && owned == Company?.Id);
            state.EnableInClassList("phone-detail__state--taken", owned != null && owned != Company?.Id);
            facts.Add(state);
            _detail.Add(facts);
            if (!mine)
            {
                var buy = Button("Buy  ·  " + PlayerHud.FormatCash(offer.PriceCents), Buy, "ff-button--primary", "phone-detail__buy");
                buy.name = "phone-buy";
                Enabled(buy, () => !HasPendingRequests && offer.ForSale && Company != null && Company.Cash >= offer.PriceCents
                    && Primary.Properties.All(x => x.LotId != offer.LotId));
                _detail.Add(buy);
            }
            else _detail.Add(RenameRow(offer.SiteId, "phone-detail__section"));
            if (offer.Category == GoodsWorld.FarmCategory) BuildFarm(offer);
        }

        private void BuildFarm(PropertyOffer farm)
        {
            var goods = FarmGoods(farm.LotId);
            var heading = Text(OwnsSelectedFarm ? "SELLS  ·  your farm: half price" : "SELLS", "ff-eyebrow", "phone-detail__section");
            _detail.Add(heading);
            if (goods.Count == 0) _detail.Add(Text("Nothing for sale here.", "ff-small"));
            foreach (var good in goods)
            {
                var id = good.Id;
                var row = Button("", () => ChooseGood(id), "ff-card", "ff-row", "phone-good");
                row.name = "phone-good-" + id;
                row.EnableInClassList("phone-good--chosen", id == GoodOfferId);
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("ff-icon");
                icon.AddToClassList("phone-good__icon");
                icon.SetIcon(Session.Items.FirstOrDefault(x => x != null && x.Id == good.ItemId)?.Icon, ItemName(good.ItemId));
                row.Add(icon);
                row.Add(Text($"{ItemName(good.ItemId)}  ·  pack of {good.Quantity}", "ff-text", "ff-grow"));
                row.Add(Text(PlayerHud.FormatCash(GoodsWorld.FarmGoodsCents(good.PriceCents, 1, OwnsSelectedFarm)), "ff-strong", "phone-good__price"));
                _detail.Add(row);
            }
            if (GoodOfferId == null || goods.All(x => x.Id != GoodOfferId)) return;

            var form = new VisualElement();
            form.AddToClassList("ff-inset");
            form.AddToClassList("phone-form");
            form.Add(Stepper("Packs", () => $"{Packs}  ({Packs * (Session.Offers.FirstOrDefault(x => x?.Id == GoodOfferId)?.Quantity ?? 0)} {GoodName(GoodOfferId)})",
                step => SetPacks(Packs + step), "packs"));
            var mode = new VisualElement();
            mode.AddToClassList("ff-row");
            mode.AddToClassList("phone-form__row");
            mode.Add(Text("Get it by", "phone-form__label"));
            var modes = new VisualElement();
            modes.AddToClassList("ff-segmented");
            var delivery = Button($"Delivery +{PlayerHud.FormatCash(GoodsWorld.FarmDeliveryFeeCents)}", () => SetPickup(false), "ff-segment");
            delivery.name = "phone-delivery";
            delivery.EnableInClassList("ff-segment--chosen", !Pickup);
            var pickup = Button("Own truck", () => SetPickup(true), "ff-segment");
            pickup.name = "phone-pickup";
            pickup.EnableInClassList("ff-segment--chosen", Pickup);
            modes.Add(delivery);
            modes.Add(pickup);
            mode.Add(modes);
            form.Add(mode);
            if (Pickup) form.Add(Stepper("Truck", () => TruckName(TruckId), CycleTruck, "truck"));
            form.Add(Stepper("Deliver to", () => DockName(DockId), CycleDock, "dock"));
            form.Add(Live(Text("", "phone-form__total"), () => $"Total {PlayerHud.FormatCash(OrderCents())}"
                + (Pickup ? "  ·  your truck drives to the farm" : "  ·  a farm truck brings it")));
            var order = Button("Order now", Order, "ff-button--primary", "phone-form__submit");
            order.name = "phone-order";
            Enabled(order, CanOrder);
            form.Add(order);
            form.Add(Stepper("Repeat every", () => $"{EveryHours} game h", step => SetEveryHours(EveryHours + step), "hours"));
            var schedule = Button("Repeat this order", Schedule, "phone-form__submit");
            schedule.name = "phone-schedule";
            Enabled(schedule, () => !HasPendingRequests && Docks().Any(x => x.Id == DockId) && (!Pickup || TruckId != null));
            form.Add(schedule);
            _detail.Add(form);
        }

        public bool CanOrder() => !HasPendingRequests && Company != null && Docks().Any(x => x.Id == DockId)
            && (!Pickup || ParkedTrucks().Any(x => x.Id == TruckId)) && Company.Cash >= OrderCents();

        private string OrdersSignature(GoodsSnapshot site) =>
            string.Join("|", site.FarmOrders.Select(x => x.Id)) + "#" + string.Join("|", site.FarmSchedules.Select(x => x.Id));

        private void BuildOrders()
        {
            _orders.Clear();
            _schedules.Clear();
            var site = Primary;
            if (site.FarmOrders.Count == 0) _orders.Add(Text("No open orders. Tap a farm on the map to order.", "ff-small"));
            foreach (var order in site.FarmOrders.OrderBy(x => x.PlacedSeconds).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var id = order.Id;
                _orders.Add(Row($"{order.Units} {ItemName(order.ItemId)} from {LotName(order.FarmLotId)}", () => OrderStatus(id), null));
            }
            if (site.FarmSchedules.Count == 0) _schedules.Add(Text("No repeating orders.", "ff-small"));
            foreach (var schedule in site.FarmSchedules.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                var id = schedule.Id;
                var row = Row($"{schedule.Packs} x {GoodName(schedule.OfferId)} every {schedule.EveryHours} h from {LotName(schedule.FarmLotId)}",
                    () => ScheduleStatus(id), Button("Cancel", () => CancelSchedule(id), "ff-button--danger", "phone-row__button"));
                _schedules.Add(row);
            }
        }

        // What an open order's truck is doing, for the Orders app.
        public string OrderStatus(string orderId)
        {
            var site = Primary;
            var order = site?.FarmOrders.FirstOrDefault(x => x.Id == orderId);
            if (order == null) return "Done";
            var truck = site.Trucks.FirstOrDefault(x => x.Id == order.TruckId);
            var to = DockName(order.DockId);
            var eta = truck == null || SitePlacement.Active == null ? null : GoodsWorld.EstimateRoadSeconds(site, SitePlacement.Active.Roads, truck);
            var arriving = eta is { } seconds ? $" (about {Math.Max(1, seconds)} s)" : "";
            if (truck == null) return "Waiting for its truck";
            return truck.State switch
            {
                TruckState.ToPickup => $"{truck.Name} driving to the farm{arriving}",
                TruckState.Loading => $"{truck.Name} loading at the farm",
                TruckState.ToDropoff => $"{truck.Name} bringing it to {to}{arriving}",
                TruckState.Unloading => $"Unloading at {to}: {order.UnloadedUnits} of {order.Units}"
                    + (order.WaitSeconds > 0 ? $"  ·  dock full, waiting {order.WaitSeconds} s of {GoodsWorld.FarmDockTimeoutSeconds}" : ""),
                TruckState.ToPark => order.UnloadedUnits < order.Units
                    ? $"Returning to the farm with {order.Units - order.UnloadedUnits} undelivered (refunded on return)" : "Delivered; farm truck leaving",
                _ => truck.State.ToString()
            };
        }

        private string ScheduleStatus(string scheduleId)
        {
            var schedule = Primary?.FarmSchedules.FirstOrDefault(x => x.Id == scheduleId);
            if (schedule == null) return "";
            var next = GoodsWorld.GameTime(schedule.NextHour * GoodsWorld.GameHourSeconds);
            var how = string.IsNullOrEmpty(schedule.TruckId) ? "delivered" : "picked up by " + TruckName(schedule.TruckId);
            return $"To {DockName(schedule.DockId)}, {how}  ·  next: day {next.Day} {next.Hour:00}:00"
                + (string.IsNullOrEmpty(schedule.LastProblem) ? "" : $"  ·  last time: {schedule.LastProblem}");
        }

        private string StaffSignature(GoodsSnapshot site) =>
            Session.ClientSiteId + "#" + string.Join("|", site.Employees.Select(x => x.Id + "/" + x.Name)) + "#" + Company?.Id;

        private void BuildStaff()
        {
            var site = Primary;
            var siteId = GoodsWorld.ViewSiteId(site);
            _staff.Clear();
            Live(_staffSite, () => SiteName(Session.ClientSiteId));
            var cap = GoodsWorld.EmployeeCap(site, siteId);
            Live(_staffCount, () => Company == null ? "This place belongs to no company; nobody can be hired here."
                : $"Employees here: {Primary.Employees.Count} of {cap}  ·  {PlayerHud.FormatCash(GoodsWorld.WageCentsPerHour)} per game hour each"
                  + $"  ·  wages {PlayerHud.FormatCash(Primary.CompanyWageCentsPerHour)} / h for the company");
            _hire.text = $"Hire  ·  {PlayerHud.FormatCash(GoodsWorld.WageCentsPerHour)} / h";
            Enabled(_hire, () => Company != null && !interaction.HasPendingRequests && Primary.Employees.Count < cap);
            if (site.Employees.Count == 0) _staff.Add(Text("Nobody works here yet. Staff are hired for the place you stand in.", "ff-small"));
            foreach (var employee in site.Employees)
            {
                var id = employee.Id;
                var tasks = Button("Tasks", () => OpenTasks(id), "phone-row__button");
                tasks.name = "phone-tasks-" + id;
                Enabled(tasks, () => FindObjectsByType<EmployeeWorker>().Any(x => x != null && x.EmployeeId == id));
                var fire = Button("Fire", () => interaction.Fire(id), "ff-button--danger", "phone-row__button");
                fire.name = "phone-fire-" + id;
                Enabled(fire, () => !interaction.HasPendingRequests && !Holding(id));
                var row = Row(employee.Name, () => EmployeeStatus(id), tasks);
                row.Add(fire);
                _staff.Add(row);
            }
        }

        private bool Holding(string employeeId) => Primary != null && (Primary.Lots.Any(x => x.LocationId == GoodsWorld.InventoryLocationId(employeeId))
            || Primary.Equipment.Any(x => x.State == EquipmentState.Held && x.HolderId == employeeId));

        private string EmployeeStatus(string employeeId)
        {
            var employee = Primary?.Employees.FirstOrDefault(x => x.Id == employeeId);
            if (employee == null) return "";
            var worker = FindObjectsByType<EmployeeWorker>().FirstOrDefault(x => x != null && x.EmployeeId == employeeId);
            var doing = worker == null || string.IsNullOrEmpty(worker.Status) ? "" : "  ·  " + worker.Status;
            return (employee.Unpaid ? "Unpaid, waiting outside" : Holding(employeeId) ? "Holding goods (empty its hands to fire)" : "Paid") + doing;
        }

        private string PlacesSignature(GoodsSnapshot site) =>
            string.Join("|", Company?.SiteIds ?? new List<string>()) + "#" + string.Join("|", site.Sites.Select(x => x.Id + "/" + x.Name));

        private void BuildPlaces()
        {
            _places.Clear();
            var company = Company;
            if (company == null)
            {
                _places.Add(Text("Your company owns no places yet.", "ff-small"));
                return;
            }
            foreach (var siteId in company.SiteIds)
            {
                var lot = SitePlacement.Active?.LotOf(siteId);
                var card = new VisualElement();
                card.AddToClassList("ff-card");
                var offer = lot != null && LotOffers.TryGetValue(lot.Id, out var found) ? found : null;
                var kind = offer == null ? "Place" : CategoryName(offer);
                card.Add(Text(kind + (siteId == Session.ClientSiteId ? "  ·  you are here" : ""), "ff-small"));
                card.Add(RenameRow(siteId, null));
                if (lot != null)
                {
                    var lotId = lot.Id;
                    card.Add(Button("Show on map", () => ShowOnMap(lotId), "ff-button--sm", "phone-form__submit"));
                }
                _places.Add(card);
            }
        }

        // ---------- Elements ----------

        private VisualElement RenameRow(string siteId, string extraClass)
        {
            var row = new VisualElement();
            row.AddToClassList("ff-row");
            row.AddToClassList("phone-row");
            row.AddToClassList("phone-form__row");
            if (extraClass != null) row.AddToClassList(extraClass);
            var field = new TextField { value = _names.TryGetValue(siteId, out var draft) ? draft : SiteName(siteId), maxLength = GoodsWorld.MaxSiteNameLength };
            field.name = "phone-name-" + siteId;
            field.AddToClassList("ff-text-field");
            field.AddToClassList("phone-row__field");
            field.RegisterValueChangedCallback(evt => _names[siteId] = evt.newValue);
            var rename = Button("Rename", () => Rename(siteId, field.value), "phone-row__button");
            rename.name = "phone-rename-" + siteId;
            Enabled(rename, () => !HasPendingRequests && !string.IsNullOrWhiteSpace(field.value) && field.value.Trim() != SiteName(siteId));
            row.Add(field);
            row.Add(rename);
            return row;
        }

        private VisualElement Row(string title, Func<string> sub, Button button)
        {
            var row = new VisualElement();
            row.AddToClassList("ff-card");
            row.AddToClassList("ff-row");
            row.AddToClassList("phone-row");
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("phone-row__text");
            text.Add(Text(title, "phone-row__title"));
            text.Add(Live(Text("", "ff-small", "phone-row__sub"), sub));
            row.Add(text);
            if (button != null) row.Add(button);
            return row;
        }

        private VisualElement Stepper(string label, Func<string> value, Action<int> step, string name)
        {
            var row = new VisualElement();
            row.AddToClassList("ff-row");
            row.AddToClassList("phone-form__row");
            row.Add(Text(label, "phone-form__label"));
            var back = Button("<", () => step(-1), "phone-form__step");
            back.name = $"phone-{name}-back";
            row.Add(back);
            row.Add(Live(Text("", "phone-form__value", "ff-nowrap"), value));
            var next = Button(">", () => step(1), "phone-form__step");
            next.name = $"phone-{name}-next";
            row.Add(next);
            return row;
        }

        private static Label Text(string text, params string[] classes)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (var name in classes) label.AddToClassList(name);
            return label;
        }

        private static Button Button(string text, Action clicked, params string[] classes)
        {
            var button = new Button(clicked) { text = text, focusable = false };
            button.AddToClassList("ff-button");
            foreach (var name in classes) button.AddToClassList(name);
            return button;
        }

        private T Live<T>(T element, Func<string> text) where T : TextElement
        {
            element.text = text();
            _live.Add((element, text));
            return element;
        }

        private void Enabled(VisualElement element, Func<bool> enabled)
        {
            element.SetEnabled(enabled());
            _liveEnabled.Add((element, enabled));
        }
    }
}
