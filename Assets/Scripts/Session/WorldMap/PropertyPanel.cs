// Local player's buy panel for a map building (decision 0028) in UI Toolkit, built in code: opened by aiming at a purchasable
// building and pressing Interact (EquipmentInteraction.OpenProperty). It shows the building's category, its lot's price,
// whether it is for sale, owned by this company, owned by another or not sold, and the company's cash, with a Buy button.
// The offer comes from the replicated layout through the same WorldLayoutShells call the server uses, so nothing extra is sent;
// ownership comes from the primary baseline's public property records. Buy is a request the server checks; like
// LogisticsPanel it shows "waiting" while the request is pending and the server's reason when it is rejected, and nothing
// changes here until the next baseline. After an accepted purchase this client also watches the new site's baseline.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.WorldMap
{
    [DisallowMultipleComponent]
    public sealed class PropertyPanel : MonoBehaviour
    {
        private static readonly Color Backdrop = new(0.19f, 0.19f, 0.2f, 0.97f);
        private static readonly Color Heading = new(1f, 0.9f, 0.74f, 1f);
        private static readonly Color Muted = new(0.7f, 0.7f, 0.75f, 1f);
        private static readonly Color Error = new(0.95f, 0.45f, 0.4f, 1f);

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;
        [SerializeField] private WorldLayoutPresenter map;

        // Pending buy requests and the site each would create.
        private readonly Dictionary<string, string> _pending = new();
        private ClientSiteSubscription _subscription;
        private VisualElement _window;
        private Label _title;
        private Label _price;
        private Label _state;
        private Label _cash;
        private Label _status;
        private Button _buy;

        public VisualElement Window => _window;
        public string LastRejection { get; private set; }
        public bool HasPendingRequests => _pending.Count > 0;
        // What the panel shows now, for tests and captures.
        public string StateText => _state?.text;
        public bool CanBuy => _buy != null && _buy.enabledSelf;

        private SessionRoot Session => interaction.Session;

        private void Start()
        {
            var root = document.rootVisualElement;
            root.Clear();
            _window = new VisualElement { name = "property" };
            _window.style.width = 340;
            _window.style.backgroundColor = Backdrop;
            _window.style.paddingLeft = _window.style.paddingRight = _window.style.paddingTop = _window.style.paddingBottom = 10;
            _window.style.borderTopLeftRadius = _window.style.borderTopRightRadius = 4;
            _window.style.borderBottomLeftRadius = _window.style.borderBottomRightRadius = 4;
            _title = Caption("", 16, Heading);
            _title.name = "property-title";
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _window.Add(_title);
            _price = Caption("", 13, Color.white, 6);
            _price.name = "property-price";
            _window.Add(_price);
            _state = Caption("", 13, Color.white, 2);
            _state.name = "property-state";
            _window.Add(_state);
            _cash = Caption("", 12, Muted, 2);
            _cash.name = "property-cash";
            _window.Add(_cash);
            _status = Caption(" ", 12, Error, 6);
            _status.name = "property-status";
            _window.Add(_status);
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 6;
            // Not focusable: a focused button would click again on every keyboard Submit (Enter/Space).
            _buy = new Button(Buy) { name = "property-buy", text = "Buy", focusable = false };
            _buy.style.minWidth = 70;
            buttons.Add(_buy);
            var close = new Button(interaction.CloseScreen) { name = "property-close", text = "Close", focusable = false };
            close.style.minWidth = 70;
            buttons.Add(close);
            _window.Add(buttons);
            root.Add(CentredWindow.Overlay("property-overlay", _window));
            _window.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (_window == null) return;
            Subscribe(Session.ClientSubscription);
            var site = Session.ClientSite;
            var offer = Offer();
            var open = site != null && offer != null && Session.IsRunning && interaction.Screen == InteractionScreen.Property;
            _window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;
            var company = site.Companies.FirstOrDefault(x => x.SiteIds.Contains(Session.ClientSiteId));
            var owner = site.Properties.FirstOrDefault(x => x.LotId == offer.LotId)?.CompanyId;
            _title.text = $"{Category(offer.Category)}  {offer.BuildingId}";
            _price.text = $"Price: {PlayerHud.FormatCash(offer.PriceCents)}  (lot {offer.Width} x {offer.Depth} m)";
            _state.text = owner != null ? (owner == company?.Id ? "Owned by your company" : "Owned by another company")
                : offer.ForSale ? "For sale" : "Not for sale";
            _cash.text = company == null ? "Your company: none on this site" : $"Your company's cash: {PlayerHud.FormatCash(company.Cash)}";
            _buy.SetEnabled(owner == null && offer.ForSale && company != null && !HasPendingRequests);
            _status.text = HasPendingRequests ? "Waiting for the server..." : string.IsNullOrEmpty(LastRejection) ? " " : $"Rejected: {LastRejection}";
        }

        private PropertyOffer Offer() =>
            interaction.OpenLotId != null && map != null && map.Offers.TryGetValue(interaction.OpenLotId, out var offer) ? offer : null;

        // Requests the open lot, paid by the company of this client's site. Public so tests drive the same path as the button.
        public void Buy()
        {
            var bridge = _subscription?.Bridge;
            var offer = Offer();
            if (bridge == null || offer == null || HasPendingRequests) return;
            LastRejection = null;
            var requestId = Guid.NewGuid().ToString("N");
            _pending[requestId] = offer.SiteId;
            Debug.Log($"[Property] Requesting {offer.LotId} for {PlayerHud.FormatCash(offer.PriceCents)}.");
            bridge.RequestBuyProperty(requestId, _subscription.SiteId, offer.LotId);
        }

        private void OnResult(GoodsOutcome outcome)
        {
            if (!_pending.Remove(outcome.RequestId, out var siteId)) return;
            if (outcome.Accepted) _subscription?.Watch(siteId);
            else LastRejection = string.IsNullOrEmpty(outcome.Reason) ? "rejected" : outcome.Reason;
            Debug.Log($"[Property] {(outcome.Accepted ? "Accepted" : "Rejected")}: {outcome.Reason} (revision {outcome.Revision}).");
        }

        private void Subscribe(ClientSiteSubscription subscription)
        {
            if (ReferenceEquals(subscription, _subscription)) return;
            if (_subscription != null) _subscription.ResultReceived -= OnResult;
            _subscription = subscription;
            if (_subscription != null) _subscription.ResultReceived += OnResult;
            _pending.Clear();
        }

        private void OnDestroy() => Subscribe(null);

        private static string Category(string category) => category switch
        {
            GoodsWorld.RestaurantKind => "Restaurant",
            GoodsWorld.FactoryKind => "Factory",
            GoodsWorld.FarmCategory => "Farm",
            GoodsWorld.StationCategory => "Station",
            _ => category
        };

        private static Label Caption(string text, int size, Color color, int marginTop = 0)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginTop = marginTop;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }
    }
}
