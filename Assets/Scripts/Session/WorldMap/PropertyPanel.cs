// Local player's buy panel for a map building (decision 0028), drawn from the Property UXML and USS under Assets/UI/Resources/UI
// (decision 0042): opened by aiming at a purchasable building and pressing Interact (EquipmentInteraction.OpenProperty). It
// shows the building's category, its lot's price,
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
        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;
        [SerializeField] private WorldLayoutPresenter map;

        // Pending buy requests and the site each would create.
        private readonly Dictionary<string, string> _pending = new();
        private ClientSiteSubscription _subscription;
        private VisualElement _window;
        private VisualElement _dim;
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
            UiTemplates.Fill(root, "Property/Property");
            _window = root.Part("property");
            _dim = root.Part("property-dim");
            _title = root.Part<Label>("property-title");
            _price = root.Part<Label>("property-price");
            _state = root.Part<Label>("property-state");
            _cash = root.Part<Label>("property-cash");
            _status = root.Part<Label>("property-status");
            _buy = root.Part<Button>("property-buy");
            _buy.clicked += Buy;
            root.Part<Button>("property-close").clicked += interaction.CloseScreen;
            _window.Show(false);
            _dim.Show(false);
        }

        private void Update()
        {
            if (_window == null) return;
            Subscribe(Session.ClientSubscription);
            var site = Session.ClientSite;
            var offer = Offer();
            var open = site != null && offer != null && Session.IsRunning && interaction.Screen == InteractionScreen.Property;
            _window.Show(open);
            _dim.Show(open);
            if (!open) return;
            var company = site.Companies.FirstOrDefault(x => x.SiteIds.Contains(Session.ClientSiteId));
            var owner = site.Properties.FirstOrDefault(x => x.LotId == offer.LotId)?.CompanyId;
            _title.text = $"{Category(offer.Category)}  {offer.BuildingId}";
            _price.text = $"Price: {PlayerHud.FormatCash(offer.PriceCents)}  (lot {offer.Width} x {offer.Depth} m)";
            _state.text = owner != null ? (owner == company?.Id ? "Owned by your company" : "Owned by another company")
                : offer.ForSale ? "For sale" : "Not for sale";
            _state.EnableInClassList("prop__state--yours", owner != null && owner == company?.Id);
            _state.EnableInClassList("prop__state--taken", owner != null && owner != company?.Id);
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
    }
}
