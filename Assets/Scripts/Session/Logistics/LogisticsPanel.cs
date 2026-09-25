// Local player's logistics screen (decision 0022) in UI Toolkit, built in code: the company's trucks with their live status,
// cargo and a route editor (pickup dock, dropoff dock, cargo, Apply), and the company's remote sites with their storage and
// dock stock and buttons that move one stack at a time between them. Opened with L (EquipmentInteraction). Every button is a
// request the server checks; nothing changes here until the next baseline. Rows are rebuilt only when the set of trucks, docks,
// stacks or route drafts changes, and their text is refreshed every frame, so a click is never lost to a rebuild.
// Presentation only: route drafts are this client's edits until Apply.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
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

        private static readonly Color Backdrop = new(0.19f, 0.19f, 0.2f, 0.97f);
        private static readonly Color Inset = new(0.11f, 0.11f, 0.12f, 1f);
        private static readonly Color Heading = new(1f, 0.9f, 0.74f, 1f);
        private static readonly Color Muted = new(0.7f, 0.7f, 0.75f, 1f);
        private static readonly Color Error = new(0.95f, 0.45f, 0.4f, 1f);

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;

        // This client's unapplied route for one truck; ItemId null means any cargo.
        private sealed class RouteDraft
        {
            public string PickupDockId;
            public string DropoffDockId;
            public string ItemId;
        }

        private readonly Dictionary<string, RouteDraft> _drafts = new();
        private readonly List<(Label Label, Func<string> Text)> _live = new();
        private readonly HashSet<string> _pending = new();
        private ClientSiteSubscription _subscription;
        private VisualElement _window;
        private VisualElement _trucks;
        private VisualElement _sites;
        private Label _status;
        private string _signature;

        public VisualElement Window => _window;
        public string LastRejection { get; private set; }
        public bool HasPendingRequests => _pending.Count > 0;

        private SessionRoot Session => interaction.Session;
        private GoodsSnapshot Primary => Session.ClientSite;

        private void Start()
        {
            var root = document.rootVisualElement;
            root.Clear();
            _window = new VisualElement { name = "logistics" };
            _window.style.position = Position.Absolute;
            _window.style.left = new Length(50, LengthUnit.Percent);
            _window.style.top = new Length(50, LengthUnit.Percent);
            _window.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            _window.style.backgroundColor = Backdrop;
            Pad(_window, 10);
            _window.style.borderTopLeftRadius = _window.style.borderTopRightRadius = 4;
            _window.style.borderBottomLeftRadius = _window.style.borderBottomRightRadius = 4;
            var title = Caption("Logistics", 16, Heading);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _window.Add(title);
            var columns = new VisualElement();
            columns.style.flexDirection = FlexDirection.Row;
            columns.style.alignItems = Align.FlexStart;
            columns.style.marginTop = 6;
            _trucks = Column("logistics-trucks", 430);
            _sites = Column("logistics-sites", 330);
            _sites.style.marginLeft = 10;
            columns.Add(_trucks);
            columns.Add(_sites);
            _window.Add(columns);
            _status = Caption(" ", 12, Error);
            _status.name = "logistics-status";
            _status.style.marginTop = 6;
            _window.Add(_status);
            var close = new Button(interaction.CloseScreen) { name = "logistics-close", text = "Close", focusable = false };
            close.style.alignSelf = Align.FlexEnd;
            close.style.minWidth = 70;
            _window.Add(close);
            root.Add(_window);
            _window.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (_window == null) return;
            Subscribe(Session.ClientSubscription);
            var site = Primary;
            // Remote sites are watched as soon as the baseline names them, so their stock is there when the screen opens.
            if (site != null)
                foreach (var remote in site.Sites.Where(x => x.Id != _subscription?.SiteId)) _subscription?.Watch(remote.Id);
            var open = site != null && Session.IsRunning && interaction.Screen == InteractionScreen.Logistics;
            _window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;
            foreach (var truck in site.Trucks.Where(x => !_drafts.ContainsKey(x.Id)))
                _drafts[truck.Id] = new RouteDraft
                {
                    PickupDockId = truck.PickupDockId, DropoffDockId = truck.DropoffDockId, ItemId = truck.AllowedItemIds.FirstOrDefault()
                };
            var signature = Signature(site);
            if (signature != _signature)
            {
                _signature = signature;
                Build(site);
            }
            foreach (var (label, text) in _live) label.text = text();
            _status.text = HasPendingRequests ? "Waiting for the server..." : string.IsNullOrEmpty(LastRejection) ? " " : $"Rejected: {LastRejection}";
        }

        // Moves a truck's draft pickup, dropoff or cargo one option forward (step 1) or back (-1). Public so tests can drive
        // the same path as the arrow buttons.
        public void CycleDraft(string truckId, string field, int step)
        {
            if (!_drafts.TryGetValue(truckId, out var draft)) return;
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

        // Sends a truck's draft route, like its Apply button.
        public void ApplyRoute(string truckId)
        {
            var bridge = _subscription?.Bridge;
            if (bridge == null || !_drafts.TryGetValue(truckId, out var draft)) return;
            LastRejection = null;
            bridge.RequestSetTruckRoute(Track(), truckId, draft.PickupDockId ?? "", draft.DropoffDockId ?? "",
                draft.ItemId == null ? Array.Empty<string>() : new[] { draft.ItemId });
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
                .OrderBy(x => x.SiteId == site.Locations[0].SiteId ? 0 : 1).ThenBy(x => x.SiteId, StringComparer.Ordinal)
                .ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
        }

        private GoodsSnapshot SiteView(string siteId) =>
            siteId == _subscription?.SiteId ? Primary : _subscription?.Remote(siteId);

        private List<string> CargoOptions() =>
            new[] { (string)null }.Concat(Session.Items.Where(x => x != null).Select(x => x.Id)).ToList();

        private string Signature(GoodsSnapshot site)
        {
            var text = new StringBuilder();
            foreach (var truck in site.Trucks)
            {
                var draft = _drafts[truck.Id];
                text.Append(truck.Id).Append('/').Append(draft.PickupDockId).Append('/').Append(draft.DropoffDockId).Append('/').Append(draft.ItemId)
                    .Append('/').Append(truck.PickupDockId).Append('/').Append(truck.DropoffDockId).Append('/').Append(string.Join(",", truck.AllowedItemIds)).Append('|');
            }
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
            site.Sites.Where(x => x.Id != site.Locations[0].SiteId).OrderBy(x => x.Id, StringComparer.Ordinal);

        private void Build(GoodsSnapshot site)
        {
            _live.Clear();
            _trucks.Clear();
            _sites.Clear();
            _trucks.Add(Heading2("Trucks"));
            if (site.Trucks.Count == 0) _trucks.Add(Caption("The company has no trucks.", 12, Muted));
            foreach (var truck in site.Trucks.OrderBy(x => x.Id, StringComparer.Ordinal)) _trucks.Add(TruckCard(truck));
            _sites.Add(Heading2("Other sites"));
            var remotes = RemoteSites(site).ToList();
            if (remotes.Count == 0) _sites.Add(Caption("The company has no other sites.", 12, Muted));
            foreach (var remote in remotes) _sites.Add(SiteCard(site, remote));
        }

        private VisualElement TruckCard(GoodsTruck truck)
        {
            var id = truck.Id;
            var card = Card($"logistics-truck-{id}");
            var name = Caption(truck.Name, 13, Color.white);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(name);
            Live(card, $"logistics-truck-status-{id}", () => TruckStatus(Current(id)), Muted);
            Live(card, $"logistics-truck-cargo-{id}", () => CargoText(Current(id)), Muted);
            var draft = _drafts[id];
            card.Add(Chooser(id, PickupField, "Load at", DockName(draft.PickupDockId)));
            card.Add(Chooser(id, DropoffField, "Deliver to", DockName(draft.DropoffDockId)));
            card.Add(Chooser(id, CargoField, "Cargo", draft.ItemId == null ? "Any" : ItemName(draft.ItemId)));
            var applied = truck.PickupDockId == draft.PickupDockId && truck.DropoffDockId == draft.DropoffDockId
                && truck.AllowedItemIds.FirstOrDefault() == draft.ItemId && truck.AllowedItemIds.Count <= 1;
            var apply = new Button(() => ApplyRoute(id)) { name = $"logistics-apply-{id}", text = applied ? "Route set" : "Apply route", focusable = false };
            apply.SetEnabled(!applied && draft.PickupDockId != null && draft.DropoffDockId != null);
            apply.style.alignSelf = Align.FlexStart;
            apply.style.marginTop = 4;
            card.Add(apply);
            return card;
        }

        private GoodsTruck Current(string truckId) => Primary?.Trucks.FirstOrDefault(x => x.Id == truckId);

        private VisualElement Chooser(string truckId, string field, string label, string value)
        {
            var row = new VisualElement { name = $"logistics-{field}-{truckId}" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginTop = 3;
            var caption = Caption(label, 12, Muted);
            caption.style.width = 70;
            row.Add(caption);
            row.Add(SmallButton("<", () => CycleDraft(truckId, field, -1)));
            var shown = Caption(value, 12, Color.white);
            shown.name = $"logistics-{field}-value-{truckId}";
            shown.style.width = 200;
            shown.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(shown);
            row.Add(SmallButton(">", () => CycleDraft(truckId, field, 1)));
            return row;
        }

        private VisualElement SiteCard(GoodsSnapshot primary, GoodsSite remote)
        {
            var card = Card($"logistics-site-{remote.Id}");
            var name = Caption(remote.Name, 13, Color.white);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(name);
            var metres = GoodsWorld.RoadMetres(primary, primary.Locations[0].SiteId, remote.Id);
            if (metres is { } road) card.Add(Caption($"{road.ToString("N0", CultureInfo.InvariantCulture)} m by road", 12, Muted));
            var view = SiteView(remote.Id);
            if (view == null)
            {
                card.Add(Caption("Loading...", 12, Muted));
                return card;
            }
            var storage = view.Locations.FirstOrDefault(x => x.SiteId == remote.Id && x.Kind == "storage");
            var dock = view.Equipment.Where(x => x.Kind == GoodsWorld.DockKind && x.State == EquipmentState.Placed)
                .OrderBy(x => x.Id, StringComparer.Ordinal).FirstOrDefault();
            if (storage != null) Stock(card, view, storage.Id, "Storage", dock == null ? null : ("Ship", dock.InputLocationId));
            if (dock != null)
            {
                Stock(card, view, dock.InputLocationId, "Dock outgoing", storage == null ? null : ("Unstage", storage.Id));
                Stock(card, view, dock.OutputLocationId, "Dock incoming", storage == null ? null : ("Store", storage.Id));
            }
            else card.Add(Caption("No dock here: trucks cannot serve this site.", 12, Muted, 4));
            return card;
        }

        // One location's stacks, each with its live count and a button that moves one stack to target.
        private void Stock(VisualElement card, GoodsSnapshot view, string locationId, string title, (string Label, string LocationId)? target)
        {
            var siteId = view.Locations[0].SiteId;
            var location = view.Locations.First(x => x.Id == locationId);
            Live(card, null, () => $"{title}  {Slots(SiteView(siteId), locationId)}/{location.Capacity}", Heading, 4);
            var groups = view.Lots.Where(x => x.LocationId == locationId).Select(x => (x.ItemId, x.Spoiled)).Distinct()
                .OrderBy(x => x.ItemId, StringComparer.Ordinal).ThenBy(x => x.Spoiled).ToList();
            if (groups.Count == 0) card.Add(Caption("empty", 12, Muted));
            foreach (var (itemId, spoiled) in groups)
            {
                var row = new VisualElement { name = $"logistics-stock-{locationId}-{itemId}{(spoiled ? "-spoiled" : "")}" };
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                var label = Caption("", 12, Color.white);
                label.style.width = 170;
                row.Add(label);
                _live.Add((label, () => $"{ItemName(itemId)}{(spoiled ? " (spoiled)" : "")} ×{Units(SiteView(siteId), locationId, itemId, spoiled)}"));
                if (target is { } move)
                {
                    var button = SmallButton(move.Label, () => MoveStack(siteId, locationId, itemId, spoiled, move.LocationId));
                    button.name = $"logistics-move-{locationId}-{itemId}{(spoiled ? "-spoiled" : "")}";
                    button.style.minWidth = 64;
                    row.Add(button);
                }
                card.Add(row);
            }
        }

        private string TruckStatus(GoodsTruck truck)
        {
            if (truck == null) return "";
            var here = SiteName(truck.SiteId);
            var there = SiteName(truck.DestinationSiteId);
            return truck.State switch
            {
                TruckState.Parked => $"Parked at {here}: no route",
                TruckState.ToPickup => $"Driving to {there} to load: {PlayerHud.FormatDuration(truck.RemainingSeconds, false)}",
                TruckState.ToDropoff => $"Driving to {there} with cargo: {PlayerHud.FormatDuration(truck.RemainingSeconds, false)}",
                TruckState.Loading => $"Loading at {here}" + (Dock(truck.PickupDockId)?.State == EquipmentState.Placed ? "" : " (dock not placed)"),
                TruckState.Unloading => $"Unloading at {here}" + (Dock(truck.DropoffDockId)?.State == EquipmentState.Placed ? "" : " (dock not placed)"),
                _ => ""
            };
        }

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

        private void OnDestroy() => Subscribe(null);

        private void Live(VisualElement parent, string name, Func<string> text, Color color, int marginTop = 0)
        {
            var label = Caption("", 12, color, marginTop);
            if (name != null) label.name = name;
            parent.Add(label);
            _live.Add((label, text));
        }

        private static int Wrap(int index, int count) => count == 0 ? 0 : ((index % count) + count) % count;

        private static VisualElement Column(string name, int width)
        {
            var column = new VisualElement { name = name };
            column.style.width = width;
            return column;
        }

        private static VisualElement Card(string name)
        {
            var card = new VisualElement { name = name };
            card.style.backgroundColor = Inset;
            card.style.marginTop = 6;
            Pad(card, 8);
            return card;
        }

        private static Label Heading2(string text)
        {
            var label = Caption(text, 14, Heading);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Button SmallButton(string text, Action clicked)
        {
            // Not focusable: a focused button would click again on every keyboard Submit (Enter/Space).
            var button = new Button(clicked) { text = text, focusable = false };
            button.style.minWidth = 24;
            return button;
        }

        private static Label Caption(string text, int size, Color color, int marginTop = 0)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginTop = marginTop;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static void Pad(VisualElement element, int padding) =>
            element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = padding;
    }
}
