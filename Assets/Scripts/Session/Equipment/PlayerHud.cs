// Local player's Factorio-style HUD in UI Toolkit, built in code: crosshair, hotbar, and screens made of slot grids. The
// inventory screen shows the player's inventory grid (goods stacks and held machines) beside the dev storage; the machine
// screen shows the inventory beside the machine's input slots, progress arrow and output slots. A grid has one slot per
// unit of its location's capacity (decision 0009); each (item, spoiled) stack fills as many slots as its max stack needs.
// Clicking a slot picks its stack up onto the cursor (the icon follows the pointer); clicking another container drops it
// there, and shift+click sends it straight to the other open container, both with ordinary server-checked transfers.
// On a screen a hotbar key over a stack, or clicking a hotbar slot with a stack on the cursor, assigns that stack's machine
// or item to the hotbar slot (the cursor stack goes back where it was); an empty cursor takes up the slot's machine or item.
// The site company's cash is shown top right, read from the latest baseline.
// Presentation only: slot positions are this client's arrangement of the replicated stacks, never saved or sent, and
// progress is interpolated for at most one clock step past the latest baseline.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FoodFactoryGame.Goods;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Equipment
{
    [DisallowMultipleComponent]
    public sealed class PlayerHud : MonoBehaviour
    {
        public const string InventoryGrid = "inventory";
        public const string StorageGrid = "storage";
        public const string InputGrid = "input";
        public const string OutputGrid = "output";
        public const int GridColumns = 10;

        private const int SlotSize = 48;
        private const int IconSize = 42;
        private static readonly Color Backdrop = new(0.19f, 0.19f, 0.2f, 0.97f);
        private static readonly Color Inset = new(0.11f, 0.11f, 0.12f, 1f);
        private static readonly Color SlotFill = new(0.33f, 0.33f, 0.35f, 1f);
        private static readonly Color SlotEdgeLight = new(0.47f, 0.47f, 0.5f, 1f);
        private static readonly Color SlotEdgeDark = new(0.08f, 0.08f, 0.09f, 1f);
        private static readonly Color Highlight = new(0.98f, 0.66f, 0.2f, 1f);
        private static readonly Color Heading = new(1f, 0.9f, 0.74f, 1f);
        private static readonly Color Spoiled = new(0.95f, 0.35f, 0.3f, 1f);
        private static readonly Color Muted = new(0.7f, 0.7f, 0.75f, 1f);

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;

        // One stack in a slot: up to a max stack of goods (item and spoiled state; Part numbers the slots one stack
        // fills) or held machines of one kind. Key names the slot's stack uniquely; StackKey is shared by its parts.
        private sealed class SlotContent
        {
            public string Key;
            public string StackKey;
            public int Part;
            public string ItemId;
            public bool Spoiled;
            public string MachineKind;
            public int Count;
            public bool Carried;
        }

        // Per-grid slot arrangement: slot index -> stack key. New stacks take the first free slot; a stack keeps its slot.
        private readonly Dictionary<string, List<string>> _arrangement = new();
        // Slots claimed by a drop that the server has not answered yet, so arriving goods land where they were dropped.
        private readonly HashSet<(string Grid, string Key)> _claimed = new();
        private readonly Dictionary<string, List<SlotContent>> _grids = new();
        private VisualElement _crosshair;
        private VisualElement _hotbar;
        private Label _cash;
        // Balance the cash label shows, so the text is rebuilt only when it changes (never a real balance before the first).
        private long _shownCash = long.MinValue;
        private VisualElement _screen;
        private VisualElement _cursor;
        private VisualElement _progressFill;
        private Label _progressLabel;
        private Label _hoverLabel;
        private string _signature;
        private GoodsSnapshot _site;
        private float _siteSeenAt;
        // Slot (element name) where the current left press began, recorded only once screen clicks were armed; releasing on
        // that slot clicks it. So the press that opened the screen never also picks up the stack under the pointer.
        private string _pressedSlot;

        public VisualElement ScreenRoot => _screen;
        public VisualElement CursorIcon => _cursor;
        public IReadOnlyList<ItemDefinition> Items => interaction.Session.Items;

        private void Start()
        {
            var root = document.rootVisualElement;
            root.Clear();
            var layer = new VisualElement { name = "hud", pickingMode = PickingMode.Ignore };
            Fill(layer);
            _crosshair = new VisualElement { name = "hud-crosshair", pickingMode = PickingMode.Ignore };
            _crosshair.style.position = Position.Absolute;
            _crosshair.style.left = new Length(50, LengthUnit.Percent);
            _crosshair.style.top = new Length(50, LengthUnit.Percent);
            _crosshair.style.width = _crosshair.style.height = 6;
            _crosshair.style.marginLeft = _crosshair.style.marginTop = -3;
            _crosshair.style.backgroundColor = Color.white;
            SetRadius(_crosshair, 3);
            _hotbar = new VisualElement { name = "hud-hotbar", pickingMode = PickingMode.Ignore };
            _hotbar.style.position = Position.Absolute;
            _hotbar.style.bottom = 12;
            _hotbar.style.left = 0;
            _hotbar.style.right = 0;
            _hotbar.style.flexDirection = FlexDirection.Row;
            _hotbar.style.justifyContent = Justify.Center;
            _screen = new VisualElement { name = "hud-screen" };
            _screen.style.position = Position.Absolute;
            _screen.style.left = new Length(50, LengthUnit.Percent);
            _screen.style.top = new Length(50, LengthUnit.Percent);
            _screen.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            _screen.style.flexDirection = FlexDirection.Row;
            _screen.style.alignItems = Align.FlexStart;
            // The cursor stack is drawn above everything and never takes a click, so the slot under it receives the click.
            _cursor = new VisualElement { name = "hud-cursor", pickingMode = PickingMode.Ignore };
            _cursor.style.position = Position.Absolute;
            _cursor.style.width = _cursor.style.height = IconSize;
            // Company cash (decision 0012): display only, from the latest site baseline.
            _cash = Caption("", 18, Heading);
            _cash.name = "hud-cash";
            _cash.style.position = Position.Absolute;
            _cash.style.top = 12;
            _cash.style.right = 16;
            _cash.style.unityFontStyleAndWeight = FontStyle.Bold;
            _cash.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), color = Color.black };
            layer.Add(_crosshair);
            layer.Add(_cash);
            layer.Add(_hotbar);
            layer.Add(_screen);
            layer.Add(_cursor);
            root.Add(layer);
            interaction.HoveredEntry = HoveredEntry;
        }

        private void Update()
        {
            if (_screen == null) return;
            var site = interaction.Session.ClientSite;
            if (!ReferenceEquals(site, _site))
            {
                _site = site;
                _siteSeenAt = Time.unscaledTime;
            }
            var active = site != null && interaction.Session.IsRunning;
            var screenOpen = active && interaction.Screen != InteractionScreen.None;
            _crosshair.style.display = active && interaction.PointerLocked ? DisplayStyle.Flex : DisplayStyle.None;
            _hotbar.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            var hasCompany = active && site.Companies is { Count: > 0 };
            _cash.style.display = hasCompany ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasCompany && site.Companies[0].Cash != _shownCash)
            {
                _shownCash = site.Companies[0].Cash;
                _cash.text = FormatCash(_shownCash);
            }
            _screen.style.display = screenOpen ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active)
            {
                _cursor.style.display = DisplayStyle.None;
                return;
            }
            if (!interaction.HasPendingRequests) _claimed.Clear();
            BuildGrids(site);
            var signature = Signature();
            if (signature != _signature)
            {
                _signature = signature;
                BuildHotbar();
                BuildScreen(site);
                BuildCursor();
            }
            UpdateCursor(screenOpen);
            UpdateProgress(site);
        }

        // Handles a click on a grid slot: pick up, put down, rearrange or drop onto another container. Public so tests can
        // drive the same path as the slot buttons.
        public void ClickSlot(string grid, int index)
        {
            var site = interaction.Session.ClientSite;
            if (site == null || !_grids.TryGetValue(grid, out var slots) || index < 0 || index >= slots.Count) return;
            var location = LocationOf(grid);
            var content = slots[index];
            var goods = interaction.CursorGoods;
            var machine = interaction.CursorKind;
            if (goods == null && machine == null)
            {
                if (content == null || content.Carried) return;
                if (content.MachineKind != null) interaction.PickUpMachine(content.MachineKind);
                else interaction.PickUpGoods(location, content.ItemId, content.Spoiled, content.Count, content.Key);
                return;
            }
            if (machine != null)
            {
                // Machines live only in the inventory grid; elsewhere the click is ignored and the machine stays on the cursor.
                if (grid != InventoryGrid) return;
                Arrange(grid, MachineKey(machine), index);
                interaction.ClearCursor();
                return;
            }
            var key = GoodsKey(goods.ItemId, goods.Spoiled);
            if (goods.LocationId == location)
            {
                if (_arrangement.ContainsKey(grid) && goods.Slot != null) Arrange(grid, goods.Slot, index);
                interaction.ClearCursor();
                return;
            }
            // Outputs only give: a stack cannot be put into a machine's output.
            if (grid == OutputGrid) return;
            // A stack new to this grid lands in the clicked empty slot; an existing one tops up its own slots first.
            if (_arrangement.TryGetValue(grid, out var arrangement) && index < arrangement.Count && arrangement[index] == null
                && !arrangement.Any(x => x != null && StackOf(x) == key))
            {
                arrangement[index] = SlotKey(key, 0);
                _claimed.Add((grid, SlotKey(key, 0)));
            }
            interaction.DropGoods(location);
        }

        // Shift+click: sends a slot's goods stack to the other open container (inventory <-> storage; inventory -> machine
        // input; machine input or output -> inventory), as much as fits there. The cursor is left as it is.
        public void QuickTransferSlot(string grid, int index)
        {
            if (!_grids.TryGetValue(grid, out var slots) || index < 0 || index >= slots.Count) return;
            var content = slots[index];
            var target = interaction.Screen switch
            {
                InteractionScreen.Inventory => grid == InventoryGrid ? StorageGrid : grid == StorageGrid ? InventoryGrid : null,
                InteractionScreen.Machine => grid == InventoryGrid ? InputGrid : grid is InputGrid or OutputGrid ? InventoryGrid : null,
                _ => null
            };
            if (content == null || content.MachineKind != null || content.Carried || target == null) return;
            interaction.TransferStack(LocationOf(grid), content.ItemId, content.Spoiled, content.Count, LocationOf(target));
        }

        // Click on a hotbar slot while a screen is open: the cursor's machine or item is assigned to it and the cursor emptied
        // (nothing moves; a goods stack stays in its container), or an empty cursor takes up the slot's machine or item.
        public void ClickHotbarSlot(int index)
        {
            if (interaction.Screen == InteractionScreen.None) return;
            var entry = interaction.CursorKind != null ? HotbarEntry.Machine(interaction.CursorKind)
                : interaction.CursorGoods != null ? HotbarEntry.Goods(interaction.CursorGoods.ItemId)
                : null;
            if (entry == null)
            {
                interaction.SelectSlot(index);
                return;
            }
            interaction.AssignHotbar(index, entry);
            interaction.ClearCursor();
        }

        // Machine or item of the grid stack under the pointer on an open screen; null over an empty slot or anything else.
        public HotbarEntry HoveredEntry()
        {
            if (interaction.Screen == InteractionScreen.None || _screen?.panel == null) return null;
            var pointer = interaction.PointerPosition;
            var position = RuntimePanelUtils.ScreenToPanel(_screen.panel, new Vector2(pointer.x, UnityEngine.Screen.height - pointer.y));
            for (var element = _screen.panel.Pick(position); element != null; element = element.parent)
                if (element.userData is SlotContent content)
                    return content.MachineKind != null ? HotbarEntry.Machine(content.MachineKind) : HotbarEntry.Goods(content.ItemId);
            return null;
        }

        // First slot index holding a stack in a grid: goods by stack ("item" or "item:spoiled") or slot ("item#part"),
        // or "machine:kind"; -1 when absent.
        public int SlotOf(string grid, string key) =>
            _grids.TryGetValue(grid, out var slots) ? slots.FindIndex(x => x != null && (x.Key == key || x.StackKey == key)) : -1;

        // Units in a grid slot; 0 when it is empty or absent.
        public int CountAt(string grid, int index) =>
            _grids.TryGetValue(grid, out var slots) && index >= 0 && index < slots.Count ? slots[index]?.Count ?? 0 : 0;

        public static string GoodsKey(string itemId, bool spoiled) => spoiled ? itemId + ":spoiled" : itemId;
        public static string MachineKey(string kind) => "machine:" + kind;
        private static string SlotKey(string stackKey, int part) => stackKey + "#" + part.ToString(CultureInfo.InvariantCulture);

        private static string StackOf(string slotKey)
        {
            var mark = slotKey.LastIndexOf('#');
            return mark < 0 ? slotKey : slotKey.Substring(0, mark);
        }

        private string LocationOf(string grid)
        {
            var machine = interaction.Session.ClientSite?.Equipment.FirstOrDefault(x => x.Id == interaction.OpenMachineId);
            return grid switch
            {
                InventoryGrid => interaction.InventoryId,
                StorageGrid => DevWorld.StorageId,
                InputGrid => machine?.InputLocationId,
                OutputGrid => machine?.OutputLocationId,
                _ => null
            };
        }

        private void BuildGrids(GoodsSnapshot site)
        {
            _grids.Clear();
            if (interaction.InventoryId == null) return;
            _grids[InventoryGrid] = Contents(site, InventoryGrid, true);
            if (interaction.Screen == InteractionScreen.Inventory) _grids[StorageGrid] = Contents(site, StorageGrid, false);
            if (interaction.Screen == InteractionScreen.Machine && LocationOf(InputGrid) != null)
            {
                _grids[InputGrid] = Contents(site, InputGrid, false);
                _grids[OutputGrid] = Contents(site, OutputGrid, false);
            }
        }

        // One slot per unit of capacity; an over-full location (spoilage, or held machines, which take no server slot)
        // shows its extra stacks in extra slots.
        private List<SlotContent> Contents(GoodsSnapshot site, string grid, bool withMachines)
        {
            var location = LocationOf(grid);
            var minimumSlots = site.Locations.FirstOrDefault(x => x.Id == location)?.Capacity ?? 0;
            var stacks = new List<SlotContent>();
            foreach (var group in site.Lots.Where(x => x.LocationId == location).GroupBy(x => (x.ItemId, x.Spoiled)))
            {
                var stackKey = GoodsKey(group.Key.ItemId, group.Key.Spoiled);
                var maxStack = interaction.Session.MaxStack(group.Key.ItemId);
                var remaining = group.Sum(x => (long)x.Quantity);
                for (var part = 0; remaining > 0; part++)
                {
                    var count = (int)Math.Min(maxStack, remaining);
                    remaining -= count;
                    stacks.Add(new SlotContent
                    {
                        Key = SlotKey(stackKey, part), StackKey = stackKey, Part = part, ItemId = group.Key.ItemId,
                        Spoiled = group.Key.Spoiled, Count = count
                    });
                }
            }
            if (withMachines)
                stacks.AddRange(site.Equipment.Where(x => x.State == EquipmentState.Held && x.HolderId == interaction.LocalPlayerId)
                    .GroupBy(x => x.Kind).Select(x => new SlotContent { Key = MachineKey(x.Key), StackKey = MachineKey(x.Key), MachineKind = x.Key, Count = x.Count() }));
            var cursor = interaction.CursorGoods;
            foreach (var stack in stacks)
                stack.Carried = stack.MachineKind != null
                    ? interaction.Screen != InteractionScreen.None && stack.MachineKind == interaction.CursorKind
                    : cursor != null && cursor.LocationId == location && cursor.Slot == stack.Key;
            var byKey = stacks.ToDictionary(x => x.Key);
            var ordered = stacks.OrderBy(x => x.StackKey, StringComparer.Ordinal).ThenBy(x => x.Part).Select(x => x.Key).ToList();
            List<string> keys;
            if (grid == InventoryGrid || grid == StorageGrid)
            {
                if (!_arrangement.TryGetValue(grid, out keys)) _arrangement[grid] = keys = new List<string>();
                while (keys.Count < minimumSlots) keys.Add(null);
                for (var index = 0; index < keys.Count; index++)
                    if (keys[index] != null && !byKey.ContainsKey(keys[index]) && !_claimed.Contains((grid, keys[index]))) keys[index] = null;
                foreach (var key in ordered.Where(x => !keys.Contains(x)))
                {
                    var free = keys.IndexOf(null);
                    if (free < 0) keys.Add(key);
                    else keys[free] = key;
                }
            }
            else
            {
                keys = ordered;
                while (keys.Count < minimumSlots) keys.Add(null);
            }
            return keys.Select(x => x != null && byKey.TryGetValue(x, out var content) ? content : null).ToList();
        }

        // Moves a stack to a slot in an arranged grid, swapping with whatever was there.
        private void Arrange(string grid, string key, int index)
        {
            var keys = _arrangement[grid];
            var from = keys.IndexOf(key);
            var other = keys[index];
            keys[index] = key;
            if (from >= 0 && from != index) keys[from] = other;
        }

        // Rebuild only when something shown changes, so a click is not lost to a rebuild every clock tick.
        private string Signature()
        {
            var text = new StringBuilder();
            text.Append(interaction.Screen).Append('|').Append(interaction.OpenMachineId).Append('|').Append(interaction.SelectedSlot)
                .Append('|').Append(interaction.CursorKind).Append('|').Append(interaction.CursorGoods?.ItemId);
            foreach (var entry in interaction.Hotbar)
                text.Append('|').Append(entry?.MachineKind).Append('/').Append(entry?.ItemId).Append(interaction.HotbarCount(entry));
            // The cursor stack stays on the pointer outside screens, so its count is shown there too.
            if (interaction.CursorGoods != null) text.Append('|').Append(interaction.CursorLots(_site).Sum(x => x.Quantity));
            if (interaction.Screen == InteractionScreen.None) return text.ToString();
            foreach (var grid in _grids.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                text.Append('#').Append(grid.Key);
                foreach (var slot in grid.Value) text.Append('|').Append(slot == null ? "" : $"{slot.Key}:{slot.Count}:{slot.Carried}");
            }
            foreach (var location in _site.Locations) text.Append('|').Append(location.Id).Append(location.Capacity);
            return text.ToString();
        }

        private void BuildHotbar()
        {
            _hotbar.Clear();
            var entries = interaction.Hotbar;
            for (var index = 0; index < EquipmentInteraction.HotbarSize; index++)
            {
                var slotIndex = index;
                var selected = index == interaction.SelectedSlot;
                var slot = SlotFrame($"hud-slot-{index + 1}", selected);
                // Hotbar slots take clicks only on a screen (assigning the cursor stack); in the world they are display only.
                slot.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0)
                        _pressedSlot = interaction.Screen != InteractionScreen.None && interaction.ScreenClicksArmed ? slot.name : null;
                }, TrickleDown.TrickleDown);
                slot.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (evt.button != 0 || _pressedSlot != slot.name) return;
                    _pressedSlot = null;
                    ClickHotbarSlot(slotIndex);
                }, TrickleDown.TrickleDown);
                slot.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (interaction.Screen != InteractionScreen.None) SetBorder(slot, Highlight, selected ? 2 : 1);
                });
                slot.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (selected) SetBorder(slot, Highlight, 2);
                    else SlotEdges(slot);
                });
                var number = Caption($"{index + 1}", 10, Muted);
                number.style.position = Position.Absolute;
                number.style.left = 3;
                number.style.top = 1;
                var entry = entries[index];
                if (entry != null)
                {
                    var count = interaction.HotbarCount(entry);
                    var sprite = entry.MachineKind != null ? MachineIcon(entry.MachineKind) : ItemIcon(entry.ItemId);
                    var name = entry.MachineKind != null ? Title(entry.MachineKind) : ItemName(entry.ItemId);
                    slot.Add(Icon(sprite, name, count > 0 ? 1f : 0.35f));
                    if (count > 0) slot.Add(Count(count));
                }
                slot.Add(number);
                _hotbar.Add(slot);
            }
        }

        private void BuildScreen(GoodsSnapshot site)
        {
            _screen.Clear();
            _progressFill = null;
            _progressLabel = null;
            _hoverLabel = null;
            var inventoryId = interaction.InventoryId;
            if (interaction.Screen == InteractionScreen.None || inventoryId == null) return;
            var inventory = Window("hud-inventory", $"Inventory  {Units(site, inventoryId)}");
            inventory.Add(GridView(InventoryGrid));
            _hoverLabel = Caption(" ", 12, Muted, 6);
            inventory.Add(_hoverLabel);
            _screen.Add(inventory);
            if (interaction.Screen == InteractionScreen.Inventory)
            {
                var storage = Window("hud-storage", $"Storage  {Units(site, DevWorld.StorageId)}");
                storage.Add(GridView(StorageGrid));
                _screen.Add(storage);
                if (interaction.Session.Offers.Count > 0) _screen.Add(SupplierWindow());
                return;
            }
            var equipment = site.Equipment.FirstOrDefault(x => x.Id == interaction.OpenMachineId);
            if (equipment != null) _screen.Add(MachineWindow(site, equipment));
        }

        // Supplier offers (decision 0014): one row per pack with its price and a Buy button. The company pays; the goods arrive
        // in this player's inventory once the server accepts. Affordability is not previewed; the server's reason is shown.
        private VisualElement SupplierWindow()
        {
            var window = Window("hud-supplier", "Supplier");
            foreach (var offer in interaction.Session.Offers.Where(x => x != null))
            {
                var row = new VisualElement { name = $"hud-offer-row-{offer.Id}" };
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;
                row.style.marginTop = 4;
                row.Add(Icon(ItemIcon(offer.ItemId), ItemName(offer.ItemId), 1f));
                var label = Caption($"{offer.Quantity} {ItemName(offer.ItemId)}  {FormatCash(offer.PriceCents)}", 12, Color.white);
                label.style.minWidth = 120;
                label.style.marginLeft = 6;
                row.Add(label);
                var id = offer.Id;
                var buy = new Button(() => ClickOffer(id)) { name = $"hud-offer-{id}", text = "Buy" };
                buy.style.minWidth = 48;
                row.Add(buy);
                window.Add(row);
            }
            return window;
        }

        // Buys one pack of the offer, like its Buy button. Public so tests can drive the same path as the button.
        public void ClickOffer(string offerId) => interaction.Buy(offerId);

        // Input slot -> progress arrow -> output slot, like a Factorio furnace; the machine runs by itself (decision 0008).
        private VisualElement MachineWindow(GoodsSnapshot site, GoodsEquipment equipment)
        {
            var window = Window("hud-machine", Title(equipment.Kind));
            window.style.minWidth = 300;
            var body = new VisualElement();
            body.style.flexDirection = FlexDirection.Row;
            body.style.alignItems = Align.Center;
            body.style.justifyContent = Justify.Center;
            body.style.backgroundColor = Inset;
            Pad(body, 12);
            body.Add(Labelled(GridView(InputGrid), $"Input {Units(site, equipment.InputLocationId)}"));
            var arrow = new VisualElement { name = "hud-progress" };
            arrow.style.width = 90;
            arrow.style.height = 12;
            arrow.style.marginLeft = arrow.style.marginRight = 12;
            arrow.style.marginBottom = 16;
            arrow.style.backgroundColor = SlotEdgeDark;
            SetBorder(arrow, SlotEdgeLight, 1);
            _progressFill = new VisualElement { name = "hud-progress-fill", pickingMode = PickingMode.Ignore };
            _progressFill.style.height = new Length(100, LengthUnit.Percent);
            _progressFill.style.backgroundColor = Highlight;
            arrow.Add(_progressFill);
            body.Add(arrow);
            // A sale station (decision 0013) turns its input into company cash, so it shows prices instead of an output grid.
            var sales = SaleRecipes(equipment.Kind);
            if (sales.Count == 0) body.Add(Labelled(GridView(OutputGrid), $"Output {Units(site, equipment.OutputLocationId)}"));
            else
            {
                var prices = new VisualElement { name = "hud-sale-prices" };
                prices.style.minWidth = 96;
                prices.Add(Caption("Sells", 12, Heading));
                foreach (var sale in sales)
                    prices.Add(Caption($"{string.Join(" + ", sale.Inputs.Select(x => $"{x.quantity} {ItemName(x.itemId)}"))}  {FormatCash(sale.SaleCents)}", 12, Color.white, 4));
                body.Add(prices);
            }
            window.Add(body);
            _progressLabel = Caption("", 12, Muted, 6);
            _progressLabel.name = "hud-progress-label";
            window.Add(_progressLabel);
            return window;
        }

        private VisualElement GridView(string grid)
        {
            var view = new VisualElement { name = $"hud-{grid}-grid" };
            view.style.flexDirection = FlexDirection.Row;
            view.style.flexWrap = Wrap.Wrap;
            var slots = _grids.TryGetValue(grid, out var list) ? list : new List<SlotContent>();
            view.style.width = Math.Min(slots.Count, GridColumns) * (SlotSize + 2);
            for (var index = 0; index < slots.Count; index++)
            {
                var slotIndex = index;
                var content = slots[index];
                var slot = SlotFrame($"hud-{grid}-slot-{index}", false);
                // Read by HoveredEntry to find the stack under the pointer when a hotbar key is pressed.
                slot.userData = content;
                // Presses are read here rather than through Button.clicked, whose activator ignores any press with a
                // modifier held and so would never report a shift+click. Trickle-down runs before the button's own handler.
                slot.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button == 0) _pressedSlot = interaction.ScreenClicksArmed ? slot.name : null;
                }, TrickleDown.TrickleDown);
                slot.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (evt.button != 0 || _pressedSlot != slot.name) return;
                    _pressedSlot = null;
                    OnSlotClicked(grid, slotIndex);
                }, TrickleDown.TrickleDown);
                if (content != null)
                {
                    var name = content.MachineKind != null ? Title(content.MachineKind) : ItemName(content.ItemId);
                    var sprite = content.MachineKind != null ? MachineIcon(content.MachineKind) : ItemIcon(content.ItemId);
                    var icon = Icon(sprite, name, content.Carried ? 0.3f : 1f);
                    if (content.Spoiled) icon.style.unityBackgroundImageTintColor = new Color(0.6f, 0.75f, 0.35f, content.Carried ? 0.3f : 1f);
                    slot.Add(icon);
                    slot.Add(Count(content.Count));
                    var hover = $"{name} ×{content.Count}{(content.Spoiled ? " (spoiled)" : "")}";
                    slot.RegisterCallback<PointerEnterEvent>(_ => { if (_hoverLabel != null) _hoverLabel.text = hover; });
                }
                slot.RegisterCallback<PointerEnterEvent>(_ => SetBorder(slot, Highlight, 1));
                slot.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    SlotEdges(slot);
                    if (_hoverLabel != null) _hoverLabel.text = " ";
                });
                view.Add(slot);
            }
            return view;
        }

        private void OnSlotClicked(string grid, int index)
        {
            if (interaction.QuickTransferHeld) QuickTransferSlot(grid, index);
            else ClickSlot(grid, index);
        }

        private void BuildCursor()
        {
            _cursor.Clear();
            var goods = interaction.CursorGoods;
            if (goods != null)
            {
                // On a screen a stack carries one slot's worth; in the world it stands for everything of that item in the inventory.
                var total = interaction.CursorLots(_site).Sum(x => x.Quantity);
                var count = interaction.Screen == InteractionScreen.None ? total : Math.Min(goods.Quantity, total);
                _cursor.Add(Icon(ItemIcon(goods.ItemId), ItemName(goods.ItemId), 1f));
                _cursor.Add(Count(count));
            }
            else if (interaction.CursorKind != null)
            {
                _cursor.Add(Icon(MachineIcon(interaction.CursorKind), Title(interaction.CursorKind), 1f));
                _cursor.Add(Count(interaction.HeldCount(interaction.CursorKind)));
            }
        }

        // The cursor stack follows the pointer while a screen is open. In the world a goods stack stays on the cursor beside the
        // crosshair (belts to build, goods to put on belts); a machine shows its ghost instead.
        private void UpdateCursor(bool screenOpen)
        {
            var carrying = interaction.CursorGoods != null || (screenOpen && interaction.CursorKind != null);
            _cursor.style.display = carrying ? DisplayStyle.Flex : DisplayStyle.None;
            if (!carrying || _cursor.panel == null) return;
            var pointer = interaction.PointerLocked
                ? new Vector2(UnityEngine.Screen.width * 0.5f + IconSize * 0.6f, UnityEngine.Screen.height * 0.5f - IconSize * 0.6f)
                : interaction.PointerPosition;
            var position = RuntimePanelUtils.ScreenToPanel(_cursor.panel, new Vector2(pointer.x, UnityEngine.Screen.height - pointer.y));
            _cursor.style.left = position.x - IconSize * 0.3f;
            _cursor.style.top = position.y - IconSize * 0.3f;
        }

        private void UpdateProgress(GoodsSnapshot site)
        {
            if (_progressFill == null) return;
            var equipment = site.Equipment.FirstOrDefault(x => x.Id == interaction.OpenMachineId);
            var job = site.Jobs.FirstOrDefault(x => x.StationId == interaction.OpenMachineId);
            var fraction = 0f;
            if (job == null)
            {
                var recipes = interaction.Session.Recipes.Where(x => x != null && x.StationKind == equipment?.Kind).ToList();
                var ingredients = recipes.SelectMany(x => x.Inputs).Select(x => ItemName(x.itemId)).Distinct().ToList();
                var output = site.Locations.FirstOrDefault(x => x.Id == equipment?.OutputLocationId);
                var makes = recipes.Where(x => !x.IsSale).ToList();
                // The server starts nothing while the output cannot take a batch (decision 0008), so say why it waits.
                if (output != null && makes.Count > 0 && makes.Count == recipes.Count
                    && makes.All(x => GoodsSlots.FreeUnits(site, output.Id, x.OutputItemId, false, interaction.Session.MaxStack) < x.OutputQuantity))
                    _progressLabel.text = "Stopped: output full, take the results out";
                // A sale needs a company to pay (decision 0013); the baseline carries the site's company, if any.
                else if (recipes.Count > 0 && recipes.All(x => x.IsSale) && site.Companies is not { Count: > 0 })
                    _progressLabel.text = "Idle: this site has no company to sell for";
                else _progressLabel.text = ingredients.Count == 0 ? "Idle: no recipes for this machine" : $"Idle: put {string.Join(" or ", ingredients)} in the input";
            }
            else if (job.State == StationJobState.Blocked)
            {
                fraction = 1f;
                _progressLabel.text = $"Done, waiting: output full ({ItemName(job.OutputItemId)})";
            }
            else
            {
                var elapsed = job.DurationSeconds - job.RemainingSeconds + Mathf.Min(Time.unscaledTime - _siteSeenAt, 1f);
                fraction = Mathf.Clamp01((float)(elapsed / job.DurationSeconds));
                var what = job.IsSale
                    ? $"Serving a customer: {string.Join(" + ", job.Inputs.Select(x => ItemName(x.ItemId)).Distinct())} for {FormatCash(job.SaleCents)}"
                    : $"Making {ItemName(job.OutputItemId)}";
                _progressLabel.text = $"{what}: {job.DurationSeconds - job.RemainingSeconds}/{job.DurationSeconds} s";
            }
            _progressFill.style.width = new Length(fraction * 100f, LengthUnit.Percent);
        }

        // Slots used out of the location's capacity.
        private string Units(GoodsSnapshot site, string locationId)
        {
            var location = site.Locations.FirstOrDefault(x => x.Id == locationId);
            return location == null ? ""
                : $"{GoodsSlots.SlotsUsed(site.Lots.Where(x => x.LocationId == locationId), interaction.Session.MaxStack)}/{location.Capacity}";
        }

        private List<RecipeAsset> SaleRecipes(string kind) =>
            interaction.Session.Recipes.Where(x => x != null && x.IsSale && x.StationKind == kind).ToList();

        private ItemDefinition Item(string itemId) => Items.FirstOrDefault(x => x != null && x.Id == itemId);
        private string ItemName(string itemId) => Item(itemId)?.DisplayName ?? Title(itemId);
        private Sprite ItemIcon(string itemId) => Item(itemId)?.Icon;
        private Sprite MachineIcon(string kind) =>
            interaction.Session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == kind)?.Icon;

        // Item and kind IDs are shown directly when no content names them: "dough" -> "Dough".
        private static string Title(string id) =>
            string.IsNullOrEmpty(id) ? "" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));

        private static VisualElement Window(string name, string title)
        {
            var window = new VisualElement { name = name };
            window.style.backgroundColor = Backdrop;
            window.style.marginLeft = window.style.marginRight = 6;
            SetBorder(window, SlotEdgeDark, 2);
            Pad(window, 8);
            var heading = Caption(title, 15, Heading);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginBottom = 6;
            window.Add(heading);
            return window;
        }

        private static VisualElement Labelled(VisualElement content, string label)
        {
            var column = new VisualElement();
            column.style.alignItems = Align.Center;
            column.Add(content);
            column.Add(Caption(label, 11, Muted, 2));
            return column;
        }

        private static VisualElement SlotFrame(string name, bool selected)
        {
            var slot = new Button { name = name, text = "" };
            slot.style.width = slot.style.height = SlotSize;
            slot.style.marginLeft = slot.style.marginRight = slot.style.marginTop = slot.style.marginBottom = 1;
            slot.style.paddingLeft = slot.style.paddingRight = slot.style.paddingTop = slot.style.paddingBottom = 0;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            slot.style.backgroundColor = SlotFill;
            SetRadius(slot, 2);
            if (selected) SetBorder(slot, Highlight, 2);
            else SlotEdges(slot);
            return slot;
        }

        // Bevelled slot edge: light top/left, dark bottom/right.
        private static void SlotEdges(VisualElement slot)
        {
            slot.style.borderTopWidth = slot.style.borderLeftWidth = slot.style.borderBottomWidth = slot.style.borderRightWidth = 1;
            slot.style.borderTopColor = slot.style.borderLeftColor = SlotEdgeLight;
            slot.style.borderBottomColor = slot.style.borderRightColor = SlotEdgeDark;
        }

        private static VisualElement Icon(Sprite sprite, string name, float opacity)
        {
            var icon = new VisualElement { name = "hud-icon", pickingMode = PickingMode.Ignore };
            icon.style.width = icon.style.height = IconSize;
            icon.style.flexShrink = 0;
            icon.style.opacity = opacity;
            if (sprite != null)
            {
                icon.style.backgroundImage = new StyleBackground(sprite);
                icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            else
            {
                // No icon content: the item's name stands in.
                icon.style.justifyContent = Justify.Center;
                var label = Caption(name, 10, Color.white);
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.whiteSpace = WhiteSpace.Normal;
                icon.Add(label);
            }
            return icon;
        }

        // Whole cents as dollars, e.g. 50000 -> "$500.00".
        public static string FormatCash(long cents) =>
            (cents < 0 ? "-$" : "$") + (Math.Abs((decimal)cents) / 100m).ToString("N2", CultureInfo.InvariantCulture);

        private static Label Count(int count)
        {
            var label = Caption(count.ToString(CultureInfo.InvariantCulture), 12, Color.white);
            label.name = "hud-count";
            label.style.position = Position.Absolute;
            label.style.right = 3;
            label.style.bottom = 0;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), color = Color.black };
            return label;
        }

        private static Label Caption(string text, int size, Color color, int marginTop = 0)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginTop = marginTop;
            label.style.paddingLeft = label.style.paddingRight = 0;
            return label;
        }

        private static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.top = element.style.right = element.style.bottom = 0;
        }

        private static void Pad(VisualElement element, int padding) =>
            element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = padding;

        private static void SetBorder(VisualElement element, Color color, int width)
        {
            element.style.borderTopWidth = element.style.borderLeftWidth = element.style.borderBottomWidth = element.style.borderRightWidth = width;
            element.style.borderTopColor = element.style.borderLeftColor = element.style.borderBottomColor = element.style.borderRightColor = color;
        }

        private static void SetRadius(VisualElement element, int radius) =>
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
    }
}
