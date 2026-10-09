// Local player's Factorio-style HUD in UI Toolkit, built in code: crosshair, hotbar, and screens made of slot grids. The
// inventory screen shows the player's inventory grid (goods stacks and held machines), beside the dev storage when E opened
// it at the storage (EquipmentInteraction.StorageOpen); the machine
// screen shows the inventory beside the machine's input slots, progress arrow and output slots. A grid has one slot per
// unit of its location's capacity (decision 0009); each (item, spoiled) stack fills as many slots as its max stack needs.
// Clicking a slot picks its stack up onto the cursor (the icon follows the pointer); clicking another container drops it
// there, and shift+click sends it straight to the other open container, both with ordinary server-checked transfers.
// On a screen a hotbar key over a stack, or clicking a hotbar slot with a stack on the cursor, assigns that stack's machine
// or item to the hotbar slot (the cursor stack goes back where it was); an empty cursor takes up the slot's machine or item.
// The site company's cash is shown top right, read from the latest baseline. Edible goods slots show the ambient time left
// before their first lot spoils, frozen (blue) while refrigerated because refrigeration pauses spoilage (decision 0018), and
// the hover line gives the full time; a machine with no recipes (the fridge) opens as plain storage, and a loading dock as its
// outgoing and incoming grids (decision 0022). Inside a factory the inventory screen also offers its next floor (decision 0020).
// A machine with a power switch (decision 0037, the oven) shows it under its grids; it bakes only while switched on.
// Under the cash are the game clock and, when the company's cash is running short of its wages or employees are unpaid, a
// warning whose threshold each player sets; the inventory screen has a Staff window to hire and fire (decision 0039).
// Below those an alert names the edible stack closest to spoiling outside the cold, and while the crosshair is on something
// with a screen a prompt under it names the interact key. While Remove is held on a belt a wheel at the crosshair (or the free
// pointer) fills until the belt is taken up (EquipmentInteraction.BeltRemoveProgress). Everything is drawn in HudTheme's look.
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

        // Two 10-column grids and the side column must fit a 1440-wide panel, so grid slots are a little smaller than the
        // hotbar's.
        private const int SlotSize = 44;
        private const int HotbarSlotSize = 56;
        private const int IconSize = 36;
        private const int SideColumnWidth = 320;
        // Space between slots (each slot has half of it as margin on every side).
        private const int SlotGap = 4;
        private const float WheelSize = 34f;
        private static readonly Color Highlight = HudTheme.Accent;
        private static readonly Color Heading = HudTheme.Text;
        private static readonly Color Spoiled = HudTheme.DangerText;
        private static readonly Color Muted = HudTheme.Muted;
        private static readonly Color Chilled = HudTheme.Chilled;
        // A running (not refrigerated) spoil timer turns this colour in the last tenth of the item's shelf life.
        private static readonly Color SpoilingSoon = HudTheme.Warning;

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
        private VisualElement _cashBlock;
        // Cash, clock and warnings, top right; hidden in build mode, whose panel stands there and shows the cash.
        private VisualElement _statusColumn;
        private Label _clock;
        private VisualElement _wageWarning;
        private Label _wageWarningText;
        private VisualElement _alert;
        private Label _alertText;
        private Label _alertTime;
        private VisualElement _prompt;
        private Label _promptKey;
        private Label _promptText;
        // Darkens the world behind an open screen; never takes a click.
        private VisualElement _dim;
        // The space above the hotbar that an open screen is centred in.
        private VisualElement _screenArea;
        private VisualElement _screen;
        private VisualElement _cursor;
        private VisualElement _removeWheel;
        private VisualElement _progressFill;
        private Label _progressLabel;
        private Label _hoverLabel;
        // Spoil timers on goods slots (location, item), and the goods slot under the pointer, refreshed every frame from the
        // latest baseline without rebuilding the screen.
        private readonly List<(Label Label, string LocationId, string ItemId)> _timers = new();
        private (string LocationId, SlotContent Content)? _hovered;
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
            _dim = new VisualElement { name = "hud-dim", pickingMode = PickingMode.Ignore };
            Fill(_dim);
            _dim.style.backgroundColor = HudTheme.Dim;
            _crosshair = new VisualElement { name = "hud-crosshair", pickingMode = PickingMode.Ignore };
            _crosshair.style.position = Position.Absolute;
            _crosshair.style.left = new Length(50, LengthUnit.Percent);
            _crosshair.style.top = new Length(50, LengthUnit.Percent);
            _crosshair.style.width = _crosshair.style.height = 6;
            _crosshair.style.marginLeft = _crosshair.style.marginTop = -3;
            _crosshair.style.backgroundColor = HudTheme.Text;
            HudTheme.Border(_crosshair, new Color(0f, 0f, 0f, 0.35f), 1);
            HudTheme.Radius(_crosshair, 3);
            _prompt = Prompt();
            // The hotbar card is centred along the bottom edge; only its slots take clicks.
            var hotbarRow = new VisualElement { pickingMode = PickingMode.Ignore };
            hotbarRow.style.position = Position.Absolute;
            hotbarRow.style.bottom = 24;
            hotbarRow.style.left = 0;
            hotbarRow.style.right = 0;
            hotbarRow.style.flexDirection = FlexDirection.Row;
            hotbarRow.style.justifyContent = Justify.Center;
            _hotbar = new VisualElement { name = "hud-hotbar", pickingMode = PickingMode.Ignore };
            _hotbar.style.flexDirection = FlexDirection.Row;
            HudTheme.StyleOverlay(_hotbar);
            HudTheme.Radius(_hotbar, 14);
            HudTheme.Pad(_hotbar, 8 - SlotGap / 2);
            hotbarRow.Add(_hotbar);
            // An open screen is centred in the space above the hotbar, which stays clickable as a drop target.
            var screenArea = _screenArea = new VisualElement { name = "hud-screen-area", pickingMode = PickingMode.Ignore };
            screenArea.style.position = Position.Absolute;
            screenArea.style.left = screenArea.style.right = 0;
            screenArea.style.top = 24;
            screenArea.style.bottom = 24 + HotbarSlotSize + 16 + 16;
            screenArea.style.justifyContent = Justify.Center;
            screenArea.style.alignItems = Align.Center;
            _screen = new VisualElement { name = "hud-screen" };
            _screen.style.flexDirection = FlexDirection.Row;
            _screen.style.alignItems = Align.FlexStart;
            screenArea.Add(_screen);
            // The cursor stack is drawn above everything and never takes a click, so the slot under it receives the click.
            _cursor = new VisualElement { name = "hud-cursor", pickingMode = PickingMode.Ignore };
            _cursor.style.position = Position.Absolute;
            _cursor.style.width = _cursor.style.height = IconSize;
            // Top right: company cash (decision 0012) over the game clock, then the wage warning (decision 0039) and the
            // spoilage alert, each its own card. Display only, from the latest site baseline.
            var status = _statusColumn = new VisualElement { name = "hud-status", pickingMode = PickingMode.Ignore };
            status.style.position = Position.Absolute;
            status.style.top = 24;
            status.style.right = 24;
            status.style.width = 260;
            status.style.alignItems = Align.FlexEnd;
            // A compact card sized to its text, right-aligned over the wider warning cards.
            var money = new VisualElement { name = "hud-money", pickingMode = PickingMode.Ignore };
            HudTheme.StyleOverlay(money);
            money.style.minWidth = 150;
            money.style.paddingTop = money.style.paddingBottom = 8;
            money.style.paddingLeft = money.style.paddingRight = 12;
            _cashBlock = new VisualElement { pickingMode = PickingMode.Ignore };
            _cashBlock.Add(HudTheme.Eyebrow("Cash"));
            _cash = HudTheme.Label("", 20, HudTheme.Text, true);
            _cash.name = "hud-cash";
            _cash.style.marginTop = 1;
            _cashBlock.Add(_cash);
            var divider = HudTheme.Divider();
            divider.style.marginTop = 5;
            divider.style.marginBottom = 5;
            _cashBlock.Add(divider);
            money.Add(_cashBlock);
            _clock = HudTheme.Label("", 12, HudTheme.TextSoft);
            _clock.name = "hud-clock";
            money.Add(_clock);
            status.Add(money);
            _wageWarning = new VisualElement { name = "hud-wage-warning", pickingMode = PickingMode.Ignore };
            HudTheme.StyleOverlay(_wageWarning);
            HudTheme.Border(_wageWarning, HudTheme.Accent, 1);
            _wageWarning.style.marginTop = 8;
            _wageWarning.style.alignSelf = Align.Stretch;
            _wageWarning.style.flexDirection = FlexDirection.Row;
            _wageWarning.style.alignItems = Align.FlexStart;
            var mark = HudTheme.Label("!", 14, HudTheme.Ink, true);
            mark.style.width = mark.style.height = 18;
            mark.style.flexShrink = 0;
            mark.style.marginRight = 10;
            mark.style.marginTop = 1;
            mark.style.unityTextAlign = TextAnchor.MiddleCenter;
            mark.style.backgroundColor = HudTheme.Accent;
            HudTheme.Radius(mark, 9);
            _wageWarning.Add(mark);
            _wageWarningText = HudTheme.Label("", 13, HudTheme.Text);
            _wageWarningText.name = "hud-wage-warning-text";
            _wageWarningText.style.whiteSpace = WhiteSpace.Normal;
            _wageWarningText.style.flexShrink = 1;
            _wageWarning.Add(_wageWarningText);
            status.Add(_wageWarning);
            _alert = new VisualElement { name = "hud-alert", pickingMode = PickingMode.Ignore };
            HudTheme.StyleOverlay(_alert);
            _alert.style.marginTop = 8;
            _alert.style.alignSelf = Align.Stretch;
            _alert.style.flexDirection = FlexDirection.Row;
            _alert.style.alignItems = Align.Center;
            _alert.Add(HudTheme.Dot(HudTheme.Danger));
            _alertText = HudTheme.Label("", 13, HudTheme.Text);
            _alertText.name = "hud-alert-text";
            _alertText.style.marginLeft = 10;
            _alertText.style.flexShrink = 1;
            _alertText.style.whiteSpace = WhiteSpace.Normal;
            _alert.Add(_alertText);
            _alert.Add(HudTheme.Spacer());
            _alertTime = HudTheme.Label("", 12, HudTheme.Warning, true);
            _alertTime.style.marginLeft = 8;
            _alert.Add(_alertTime);
            status.Add(_alert);
            layer.Add(_dim);
            layer.Add(_crosshair);
            layer.Add(_prompt);
            layer.Add(status);
            layer.Add(screenArea);
            // Above the screen, so a window taller than its area never covers the hotbar's slots.
            layer.Add(hotbarRow);
            layer.Add(_cursor);
            _removeWheel = new VisualElement { name = "hud-remove-wheel", pickingMode = PickingMode.Ignore };
            _removeWheel.style.position = Position.Absolute;
            _removeWheel.style.width = _removeWheel.style.height = WheelSize;
            _removeWheel.style.display = DisplayStyle.None;
            _removeWheel.generateVisualContent += DrawRemoveWheel;
            layer.Add(_removeWheel);
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
            // The employee script screen is EmployeeScriptPanel's, not a slot screen.
            var screenOpen = active && interaction.Screen is InteractionScreen.Inventory or InteractionScreen.Machine;
            _crosshair.style.display = active && interaction.PointerLocked ? DisplayStyle.Flex : DisplayStyle.None;
            _hotbar.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            var hasCompany = active && site.Companies is { Count: > 0 };
            _cashBlock.style.display = hasCompany ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasCompany && site.Companies[0].Cash != _shownCash)
            {
                _shownCash = site.Companies[0].Cash;
                _cash.text = FormatCash(_shownCash);
            }
            UpdateClock(active ? site : null);
            UpdateAlert(active ? site : null);
            UpdatePrompt(active);
            UpdateRemoveWheel(active);
            _statusColumn.style.display = interaction.Screen == InteractionScreen.Build ? DisplayStyle.None : DisplayStyle.Flex;
            _screen.style.display = screenOpen ? DisplayStyle.Flex : DisplayStyle.None;
            _dim.style.display = screenOpen ? DisplayStyle.Flex : DisplayStyle.None;
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
            UpdateSpoilage(site);
        }

        // The game clock, and the wage warning (decision 0039): shown while employees are unpaid, or while company cash covers
        // fewer game hours of wages than this player's setting (0 turns the early warning off).
        private void UpdateClock(GoodsSnapshot site)
        {
            var money = _clock.parent;
            if (site == null)
            {
                money.style.display = _wageWarning.style.display = DisplayStyle.None;
                return;
            }
            var (day, hour, minute) = GoodsWorld.GameTime(site.ClockSeconds);
            money.style.display = DisplayStyle.Flex;
            var clock = $"Day {day}  ·  {hour:00}:{minute:00}";
            if (_clock.text != clock) _clock.text = clock;
            var warning = WageWarning(site, interaction.WageWarningHours);
            _wageWarning.style.display = warning == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (warning != null && _wageWarningText.text != warning) _wageWarningText.text = warning;
        }

        // Centres the belt removal wheel on the crosshair (or the free pointer in the top-down view) while it fills.
        private void UpdateRemoveWheel(bool active)
        {
            var progress = active ? interaction.BeltRemoveProgress : 0f;
            _removeWheel.style.display = progress > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            if (progress <= 0f || _removeWheel.panel == null) return;
            var pointer = interaction.PointerLocked
                ? new Vector2(UnityEngine.Screen.width * 0.5f, UnityEngine.Screen.height * 0.5f)
                : interaction.PointerPosition;
            var position = RuntimePanelUtils.ScreenToPanel(_removeWheel.panel, new Vector2(pointer.x, UnityEngine.Screen.height - pointer.y));
            _removeWheel.style.left = position.x - WheelSize * 0.5f;
            _removeWheel.style.top = position.y - WheelSize * 0.5f;
            _removeWheel.MarkDirtyRepaint();
        }

        // A dark ring filling clockwise from the top in the accent colour, like build mode's hold wheel.
        private void DrawRemoveWheel(MeshGenerationContext context)
        {
            var painter = context.painter2D;
            var center = new Vector2(WheelSize * 0.5f, WheelSize * 0.5f);
            var radius = WheelSize * 0.5f - 4f;
            painter.lineWidth = 6f;
            painter.strokeColor = new Color(0f, 0f, 0f, 0.55f);
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();
            var progress = interaction.BeltRemoveProgress;
            if (progress <= 0f) return;
            painter.lineWidth = 4f;
            painter.lineCap = LineCap.Round;
            painter.strokeColor = HudTheme.Accent;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * progress));
            painter.Stroke();
        }

        // The edible stack nearest to spoiling outside the cold, once it is in the last tenth of its shelf life, in a place this
        // player can name (their inventory, the storage, a machine, an employee's hands). Display only.
        private void UpdateAlert(GoodsSnapshot site)
        {
            GoodsLot worst = null;
            var left = long.MaxValue;
            string place = null;
            if (site != null)
                foreach (var lot in site.Lots)
                {
                    if (lot.Spoiled || lot.SpoilAfterSeconds >= GoodsWorld.NonPerishableSeconds) continue;
                    var remaining = Math.Max(0, lot.SpoilAfterSeconds - lot.ExposureSeconds);
                    if (remaining * 10 > lot.SpoilAfterSeconds || remaining >= left) continue;
                    if (site.Locations.FirstOrDefault(x => x.Id == lot.LocationId)?.Refrigerated ?? false) continue;
                    var name = PlaceName(site, lot.LocationId);
                    if (name == null) continue;
                    worst = lot;
                    left = remaining;
                    place = name;
                }
            _alert.style.display = worst == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (worst == null) return;
            var text = $"{ItemName(worst.ItemId)} spoils soon in {place}";
            if (_alertText.text != text) _alertText.text = text;
            var time = FormatDuration(left, false);
            if (_alertTime.text != time) _alertTime.text = time;
        }

        // A goods location as a player reads it; null for places this HUD does not name (another player's inventory, trucks).
        private string PlaceName(GoodsSnapshot site, string locationId)
        {
            if (locationId == interaction.InventoryId) return "your inventory";
            if (locationId == DevWorld.StorageId) return "storage";
            var machine = site.Equipment.FirstOrDefault(x => x.State == EquipmentState.Placed
                && (x.InputLocationId == locationId || x.OutputLocationId == locationId));
            if (machine != null) return MachineName(machine.Kind);
            var employee = site.Employees.FirstOrDefault(x => GoodsWorld.InventoryLocationId(x.Id) == locationId);
            return employee == null ? null : $"{employee.Name}'s hands";
        }

        // "[E]  Open Oven" under the crosshair while it is on something with a screen and nothing is open.
        private VisualElement Prompt()
        {
            var prompt = new VisualElement { name = "hud-prompt", pickingMode = PickingMode.Ignore };
            prompt.style.position = Position.Absolute;
            prompt.style.left = new Length(50, LengthUnit.Percent);
            prompt.style.top = new Length(50, LengthUnit.Percent);
            prompt.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 28);
            prompt.style.flexDirection = FlexDirection.Row;
            prompt.style.alignItems = Align.Center;
            HudTheme.StyleOverlay(prompt);
            HudTheme.Border(prompt, new Color(0f, 0f, 0f, 0f), 0);
            HudTheme.Radius(prompt, 10);
            prompt.style.paddingLeft = prompt.style.paddingTop = prompt.style.paddingBottom = 8;
            _promptKey = HudTheme.Label("", 14, HudTheme.Ink, true);
            _promptKey.name = "hud-prompt-key";
            _promptKey.style.minWidth = _promptKey.style.height = 28;
            _promptKey.style.paddingLeft = _promptKey.style.paddingRight = 6;
            _promptKey.style.unityTextAlign = TextAnchor.MiddleCenter;
            _promptKey.style.backgroundColor = HudTheme.Text;
            HudTheme.Radius(_promptKey, 6);
            prompt.Add(_promptKey);
            _promptText = HudTheme.Label("", 14, HudTheme.Text);
            _promptText.name = "hud-prompt-text";
            _promptText.style.marginLeft = 10;
            prompt.Add(_promptText);
            return prompt;
        }

        private void UpdatePrompt(bool active)
        {
            var target = active && interaction.Screen == InteractionScreen.None && interaction.PointerLocked
                ? HoverName(interaction.Hovered) : null;
            _prompt.style.display = target == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (target == null) return;
            var key = interaction.InteractKey;
            if (_promptKey.text != key) _promptKey.text = key;
            if (_promptText.text != target) _promptText.text = target;
        }

        private string HoverName(Component hovered)
        {
            if (hovered == null) return null;
            return hovered switch
            {
                Employees.EmployeeWorker worker => $"Open {worker.DisplayName}",
                EquipmentVisual visual => $"Open {MachineName(visual.Kind)}",
                Employees.SiteLocationMarker => "Open storage",
                WorldMap.PropertyMarker => "View property",
                _ => null
            };
        }

        // The warning text for a site baseline, or null when nothing needs saying. Public so tests check the rule directly.
        public static string WageWarning(GoodsSnapshot site, int warningHours)
        {
            if (site?.Companies is not { Count: > 0 }) return null;
            var unpaid = site.Employees.Count(x => x.Unpaid);
            var wages = site.CompanyWageCentsPerHour;
            if (unpaid > 0)
                return $"{unpaid} employee{(unpaid == 1 ? " is" : "s are")} unpaid and waiting outside until cash covers "
                    + $"{FormatCash(GoodsWorld.WageCentsPerHour)} each";
            var cash = site.Companies[0].Cash;
            if (wages <= 0 || warningHours <= 0 || cash >= warningHours * wages) return null;
            var minutes = Math.Max(0, GoodsWorld.NextWageSeconds(site) - site.ClockSeconds) * 60 / GoodsWorld.GameHourSeconds;
            return cash < wages
                ? $"Employees will stop at the next wages in {minutes} min: {FormatCash(cash)} does not cover {FormatCash(wages)}"
                : $"Employees will stop soon: cash covers {cash / wages} of {warningHours} h of wages ({FormatCash(wages)}/h)";
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
                InteractionScreen.Inventory when interaction.StorageOpen => grid == InventoryGrid ? StorageGrid : grid == StorageGrid ? InventoryGrid : null,
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
            if (interaction.StorageOpen) _grids[StorageGrid] = Contents(site, StorageGrid, false);
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
            text.Append(interaction.Screen).Append('|').Append(interaction.StorageOpen).Append('|').Append(interaction.OpenMachineId).Append('|').Append(interaction.SelectedSlot)
                .Append('|').Append(interaction.CursorKind).Append('|').Append(interaction.CursorGoods?.ItemId);
            foreach (var entry in interaction.Hotbar)
                text.Append('|').Append(entry?.MachineKind).Append('/').Append(entry?.ItemId).Append(interaction.HotbarCount(entry));
            // The cursor stack stays on the pointer outside screens, so its count is shown there too.
            if (interaction.CursorGoods != null) text.Append('|').Append(interaction.CursorLots(_site).Sum(x => x.Quantity));
            if (interaction.Screen == InteractionScreen.None) return text.ToString();
            var building = interaction.Buildings.LocalBuilding;
            text.Append('|').Append(building?.Id).Append(building?.Floors);
            foreach (var grid in _grids.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                text.Append('#').Append(grid.Key);
                foreach (var slot in grid.Value) text.Append('|').Append(slot == null ? "" : $"{slot.Key}:{slot.Count}:{slot.Carried}");
            }
            foreach (var location in _site.Locations) text.Append('|').Append(location.Id).Append(location.Capacity);
            // The Staff window (decision 0039): who works here, who is unpaid or holding goods, the cap and the warning setting.
            if (interaction.Screen == InteractionScreen.Inventory)
            {
                text.Append("|staff:").Append(interaction.HasPendingRequests).Append('/').Append(interaction.WageWarningHours)
                    .Append('/').Append(GoodsWorld.EmployeeCap(_site, GoodsWorld.ViewSiteId(_site)));
                foreach (var employee in _site.Employees)
                    text.Append('|').Append(employee.Id).Append(employee.Unpaid).Append(Holding(_site, employee.Id));
            }
            // An open register shows who works it and who could (decision 0034).
            var open = _site.Equipment.FirstOrDefault(x => x.Id == interaction.OpenMachineId);
            // The power switch (decision 0037) shows the server's state, and waits while a request is out.
            if (open != null && HasPowerSwitch(open.Kind)) text.Append("|power:").Append(open.PoweredOn).Append(interaction.HasPendingRequests);
            if (open?.Kind == GoodsWorld.CounterKind)
            {
                text.Append("|staff:").Append(open.StaffId).Append('|').Append(interaction.HasPendingRequests);
                foreach (var employee in _site.Employees) text.Append('|').Append(employee.Id);
            }
            // An open table shows its seats and the restaurant's standing, which change with customers.
            if (_site.Equipment.Any(x => x.Id == interaction.OpenMachineId && GoodsWorld.IsTable(x)))
            {
                text.Append('|').Append(_site.Customers.Count(x => x.TableId == interaction.OpenMachineId));
                var diner = _site.Diners.FirstOrDefault();
                text.Append('|').Append(diner?.Served).Append('/').Append(diner?.WalkedOut).Append('/').Append(diner?.Reputation);
            }
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
                var entry = entries[index];
                var slot = SlotFrame($"hud-slot-{index + 1}", selected, entry != null, HotbarSlotSize);
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
                    if (interaction.Screen != InteractionScreen.None) HudTheme.Border(slot, Highlight, selected ? 2 : 1);
                });
                slot.RegisterCallback<PointerLeaveEvent>(_ => SlotRest(slot, selected));
                var number = Caption($"{(index + 1) % 10}", 10, selected ? Highlight : HudTheme.Faint);
                number.style.position = Position.Absolute;
                number.style.left = 5;
                number.style.top = 3;
                if (entry != null)
                {
                    var count = interaction.HotbarCount(entry);
                    var sprite = entry.MachineKind != null ? MachineIcon(entry.MachineKind) : ItemIcon(entry.ItemId);
                    var name = entry.MachineKind != null ? Title(entry.MachineKind) : ItemName(entry.ItemId);
                    slot.Add(Icon(sprite, name, count > 0 ? 1f : 0.35f, 38));
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
            _timers.Clear();
            _hovered = null;
            var inventoryId = interaction.InventoryId;
            if (interaction.Screen is InteractionScreen.None or InteractionScreen.Employee or InteractionScreen.PickPosition or InteractionScreen.Logistics
                    or InteractionScreen.Build
                || inventoryId == null) return;
            var inventory = Window("hud-inventory", "Inventory", SlotsUsed(site, inventoryId));
            inventory.Add(GridView(InventoryGrid));
            // One line at the grid's width: a longer hover line must never resize the centred screen under the pointer.
            _hoverLabel = Caption(" ", 12, Muted, 10);
            _hoverLabel.style.width = Math.Min(_grids.TryGetValue(InventoryGrid, out var slots) ? slots.Count : 0, GridColumns) * (SlotSize + SlotGap);
            _hoverLabel.style.whiteSpace = WhiteSpace.NoWrap;
            _hoverLabel.style.overflow = Overflow.Hidden;
            _hoverLabel.style.textOverflow = TextOverflow.Ellipsis;
            inventory.Add(_hoverLabel);
            _screen.Add(inventory);
            if (interaction.Screen == InteractionScreen.Inventory)
            {
                if (interaction.StorageOpen)
                {
                    var storage = Window("hud-storage", "Storage", SlotsUsed(site, DevWorld.StorageId));
                    storage.Add(GridView(StorageGrid));
                    _screen.Add(storage);
                }
                // Supplier and Staff share one column, which scrolls when the two are taller than the space above the hotbar.
                var side = new ScrollView(ScrollViewMode.Vertical) { name = "hud-side-column" };
                side.style.width = SideColumnWidth;
                var room = _screenArea.resolvedStyle.height;
                if (room > 0f) side.style.maxHeight = room;
                HudTheme.StyleScroller(side);
                if (interaction.Session.Offers.Count > 0) side.Add(SupplierWindow());
                if (site.Companies is { Count: > 0 })
                {
                    var staff = StaffWindow(site);
                    if (side.childCount > 0) staff.style.marginTop = 12;
                    side.Add(staff);
                }
                if (side.childCount > 0) _screen.Add(side);
                var building = interaction.Buildings.LocalBuilding;
                if (building?.Kind == GoodsWorld.FactoryKind) _screen.Add(ConstructionWindow(building));
                return;
            }
            var equipment = site.Equipment.FirstOrDefault(x => x.Id == interaction.OpenMachineId);
            if (equipment != null) _screen.Add(MachineWindow(site, equipment));
        }

        // Supplier offers (decisions 0014, 0017): one row per pack or machine with its price and a Buy button. The company pays; the goods arrive
        // in this player's inventory once the server accepts. Affordability is not previewed; the server's reason is shown.
        private VisualElement SupplierWindow()
        {
            var window = Window("hud-supplier", "Supplier");
            // Truck offers are bought on the logistics screen (decision 0023); restaurant furnishings in build mode (decision 0034).
            foreach (var offer in interaction.Session.Offers.Where(x => x != null && x.Truck == null && (x.Equipment == null || string.IsNullOrEmpty(x.Equipment.Category))))
            {
                var row = CardRow($"hud-offer-row-{offer.Id}");
                // A machine offer (decision 0017) shows the machine; it arrives held, like a picked-up machine.
                var machine = offer.Equipment != null ? offer.Equipment.Kind : null;
                var offerName = machine != null ? Title(machine) : ItemName(offer.ItemId);
                row.Add(Icon(machine != null ? MachineIcon(machine) : ItemIcon(offer.ItemId), offerName, 1f, 32));
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.style.flexGrow = 1;
                text.style.marginLeft = 10;
                text.style.minWidth = 120;
                text.Add(HudTheme.Label(offerName, 14, HudTheme.Text, true));
                var detail = HudTheme.Label($"{offer.Quantity}  ·  {FormatCash(offer.PriceCents)}", 12, Muted);
                detail.style.marginTop = 2;
                text.Add(detail);
                row.Add(text);
                var id = offer.Id;
                row.Add(HudTheme.Button($"hud-offer-{id}", "Buy", () => ClickOffer(id)));
                window.Add(row);
            }
            return window;
        }

        // Staff (decision 0039): this site's employees against its cap, the wage, a Hire button, each employee with a Fire button
        // (refused by the server while its hands hold anything, so it is disabled then), and this player's warning setting.
        private VisualElement StaffWindow(GoodsSnapshot site)
        {
            var window = Window("hud-staff-window", "Staff");
            var siteId = GoodsWorld.ViewSiteId(site);
            var cap = GoodsWorld.EmployeeCap(site, siteId);
            var count = site.Employees.Count;
            var pending = interaction.HasPendingRequests;
            var summary = Caption($"Employees: {count} of {cap}  ·  {FormatCash(GoodsWorld.WageCentsPerHour)} per game hour each", 12, Muted);
            summary.name = "hud-staff-count";
            summary.style.whiteSpace = WhiteSpace.Normal;
            summary.style.marginTop = -6;
            summary.style.marginBottom = 12;
            window.Add(summary);
            // Hired, the hourly wage bill and how many game hours of it the cash covers.
            var wages = site.CompanyWageCentsPerHour;
            var cash = site.Companies[0].Cash;
            var tiles = HudTheme.Row();
            tiles.style.alignItems = Align.Stretch;
            tiles.Add(Tile("Hired", $"{count} / {cap}", HudTheme.Text));
            tiles.Add(Tile("Wages", $"{FormatCash(wages)} / h", HudTheme.Text));
            var covers = wages > 0 ? cash / wages : -1;
            tiles.Add(Tile("Cash covers", covers < 0 ? "—" : $"{covers} h",
                covers >= 0 && covers < Math.Max(1, interaction.WageWarningHours) ? SpoilingSoon : HudTheme.Text));
            HudTheme.Gap(tiles, 8);
            window.Add(tiles);
            foreach (var employee in site.Employees)
            {
                var row = CardRow($"hud-employee-{employee.Id}");
                var holding = Holding(site, employee.Id);
                row.Add(HudTheme.Avatar(employee.Name));
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.style.flexGrow = 1;
                text.style.marginLeft = 12;
                text.Add(HudTheme.Label(employee.Name, 14, HudTheme.Text, true));
                var state = HudTheme.Row();
                state.style.marginTop = 3;
                state.Add(HudTheme.Dot(employee.Unpaid ? HudTheme.Danger : holding ? Highlight : HudTheme.Positive, 7));
                var stateText = HudTheme.Label(employee.Unpaid ? "Unpaid, waiting outside" : holding ? "Holding goods" : "Paid", 12,
                    employee.Unpaid ? Spoiled : HudTheme.TextSoft);
                stateText.style.marginLeft = 6;
                state.Add(stateText);
                text.Add(state);
                row.Add(text);
                var id = employee.Id;
                var fireColumn = new VisualElement { pickingMode = PickingMode.Ignore };
                fireColumn.style.alignItems = Align.FlexEnd;
                var fire = HudTheme.Button($"hud-fire-{id}", "Fire", () => ClickFire(id), HudButton.Danger);
                fire.SetEnabled(!pending && !holding);
                fireColumn.Add(fire);
                if (holding)
                {
                    var hint = HudTheme.Label("Empty their hands first", 10, HudTheme.Faint);
                    hint.style.marginTop = 3;
                    fireColumn.Add(hint);
                }
                row.Add(fireColumn);
                window.Add(row);
            }
            var hireRow = HudTheme.Row();
            hireRow.style.marginTop = 12;
            var room = HudTheme.Label(count >= cap ? "No room left. More floor space allows more staff." : $"{cap - count} more can be hired here.", 12, Muted);
            room.style.flexShrink = 1;
            room.style.whiteSpace = WhiteSpace.Normal;
            hireRow.Add(room);
            hireRow.Add(HudTheme.Spacer());
            var hire = HudTheme.Button("hud-hire", count >= cap ? "Site is full" : $"Hire  ·  {FormatCash(GoodsWorld.WageCentsPerHour)} / h", ClickHire,
                HudButton.Primary, 44);
            hire.style.marginLeft = 12;
            hire.SetEnabled(!pending && count < cap);
            hireRow.Add(hire);
            window.Add(hireRow);
            var divider = HudTheme.Divider();
            divider.style.marginTop = 14;
            divider.style.marginBottom = 14;
            window.Add(divider);
            var setting = HudTheme.Row();
            setting.name = "hud-wage-warning-setting";
            var settingText = new VisualElement { pickingMode = PickingMode.Ignore };
            settingText.style.flexShrink = 1;
            settingText.Add(HudTheme.Label("Wage warning", 14, HudTheme.Text, true));
            var settingHint = HudTheme.Label("Warn when cash covers less than", 12, Muted);
            settingHint.style.marginTop = 2;
            settingText.Add(settingHint);
            setting.Add(settingText);
            setting.Add(HudTheme.Spacer());
            var hours = interaction.WageWarningHours;
            var stepper = HudTheme.Segmented();
            stepper.style.alignItems = Align.Center;
            var less = HudTheme.Segment("hud-wage-warning-less", "−", false, () => ClickWageWarning(-1));
            var more = HudTheme.Segment("hud-wage-warning-more", "+", false, () => ClickWageWarning(1));
            less.SetEnabled(hours > 0);
            more.SetEnabled(hours < Goods.Network.GoodsNetworkBridge.MaxWageWarningHours);
            stepper.Add(less);
            var value = HudTheme.Label(hours == 0 ? "off" : $"{hours} h", 13, HudTheme.Text, true);
            value.name = "hud-wage-warning-hours";
            value.style.minWidth = 44;
            value.style.unityTextAlign = TextAnchor.MiddleCenter;
            stepper.Add(value);
            stepper.Add(more);
            setting.Add(stepper);
            window.Add(setting);
            return window;
        }

        // Whether an employee's hands hold goods or a machine (it cannot be fired then).
        private static bool Holding(GoodsSnapshot site, string employeeId)
        {
            var hands = GoodsWorld.InventoryLocationId(employeeId);
            return site.Lots.Any(x => x.LocationId == hands)
                || site.Equipment.Any(x => x.State == EquipmentState.Held && x.HolderId == employeeId);
        }

        // Hire, Fire and the warning buttons. Public so tests drive the same paths as the buttons.
        public void ClickHire() => interaction.Hire();

        public void ClickFire(string employeeId) => interaction.Fire(employeeId);

        public void ClickWageWarning(int change) =>
            interaction.SetWageWarningHours(Mathf.Clamp(interaction.WageWarningHours + change, 0, Goods.Network.GoodsNetworkBridge.MaxWageWarningHours));

        // Buys one pack of the offer, like its Buy button. Public so tests can drive the same path as the button.
        public void ClickOffer(string offerId) => interaction.Buy(offerId);

        // Outsourced construction for the factory the avatar stands in (decision 0020): its floors and one Build button for the
        // next floor at the content price. The first added floor puts the elevator on the avatar's cell. Like the supplier,
        // affordability is not previewed; the server's reason is shown.
        private VisualElement ConstructionWindow(GoodsBuilding building)
        {
            var window = Window("hud-construction", "Factory");
            var floors = Caption($"Floors: {building.Floors} of {DevWorld.MaxFloors}", 12, HudTheme.Text, 4);
            floors.name = "hud-floors";
            window.Add(floors);
            if (building.Floors >= DevWorld.MaxFloors)
            {
                window.Add(Caption("Top floor reached.", 12, Muted, 4));
                return window;
            }
            var row = CardRow("hud-floor-row");
            var label = Caption($"Add a floor  ·  {FormatCash(GoodsWorld.FloorPriceCents(building, DevWorld.FloorOffer))}", 13, HudTheme.Text);
            label.style.flexGrow = 1;
            label.style.minWidth = 140;
            row.Add(label);
            row.Add(HudTheme.Button("hud-add-floor", "Build", ClickAddFloor));
            window.Add(row);
            if (!building.HasElevator) window.Add(Caption("The elevator goes where you stand.", 12, Muted, 4));
            return window;
        }

        // Orders the next floor, like the Build button. Public so tests can drive the same path as the button.
        public void ClickAddFloor() => interaction.AddFloor();

        // Input slot -> progress arrow -> output slot, like a Factorio furnace; the machine runs by itself (decision 0008).
        // A machine with no recipes is storage (the fridge, decision 0018): just its input grid.
        private VisualElement MachineWindow(GoodsSnapshot site, GoodsEquipment equipment)
        {
            if (equipment.Kind == GoodsWorld.DockKind) return DockWindow(site, equipment);
            if (GoodsWorld.IsTable(equipment)) return TableWindow(site, equipment);
            if (!interaction.Session.Recipes.Any(x => x != null && x.StationKind == equipment.Kind)) return StorageMachineWindow(site, equipment);
            var makes = interaction.Session.Recipes.Where(x => x != null && !x.IsSale && x.StationKind == equipment.Kind).ToList();
            var recipeLine = string.Join(",  ", makes.Select(x =>
                $"{string.Join(" + ", x.Inputs.Select(y => ItemName(y.itemId)).Distinct())} → {ItemName(x.OutputItemId)}  ·  {x.DurationSeconds} s"));
            var window = Window("hud-machine", MachineName(equipment.Kind), recipeLine);
            window.style.minWidth = 380;
            var body = Inset(FlexDirection.Row);
            body.style.alignItems = Align.Center;
            body.style.justifyContent = Justify.Center;
            body.Add(Labelled(GridView(InputGrid), $"In  {Units(site, equipment.InputLocationId)}"));
            var middle = new VisualElement { pickingMode = PickingMode.Ignore };
            middle.style.width = 120;
            middle.style.marginLeft = middle.style.marginRight = 16;
            middle.style.marginTop = 16;
            var arrow = HudTheme.Label("→", 20, HudTheme.Faint);
            arrow.style.unityTextAlign = TextAnchor.MiddleCenter;
            arrow.style.marginBottom = 6;
            middle.Add(arrow);
            middle.Add(HudTheme.Progress("hud-progress", out _progressFill));
            body.Add(middle);
            // A sale station (decision 0013) turns its input into company cash, so it shows prices instead of an output grid.
            var sales = SaleRecipes(equipment.Kind);
            if (sales.Count == 0) body.Add(Labelled(GridView(OutputGrid), $"Out  {Units(site, equipment.OutputLocationId)}"));
            else
            {
                var prices = new VisualElement { name = "hud-sale-prices" };
                prices.style.minWidth = 120;
                prices.Add(HudTheme.Eyebrow("Sells"));
                foreach (var sale in sales)
                    prices.Add(Caption($"{string.Join(" + ", sale.Inputs.Select(x => $"{x.quantity} {ItemName(x.itemId)}"))}  {FormatCash(sale.SaleCents)}", 13, HudTheme.Text, 6));
                body.Add(prices);
            }
            window.Add(body);
            _progressLabel = Caption("", 12, HudTheme.TextSoft, 10);
            _progressLabel.name = "hud-progress-label";
            _progressLabel.style.whiteSpace = WhiteSpace.Normal;
            window.Add(_progressLabel);
            if (equipment.Kind == GoodsWorld.CounterKind) window.Add(StaffRow(site, equipment));
            if (HasPowerSwitch(equipment.Kind)) window.Add(PowerRow(equipment));
            return window;
        }

        private bool HasPowerSwitch(string kind) =>
            interaction.Session.EquipmentDefinitions.Any(x => x != null && x.Kind == kind && x.ManualPower);

        // A machine with a power switch (decision 0037, the oven) bakes only while it is on: a sliding switch that asks the
        // server to flip it. Rebuilt with the server's answer (Signature).
        private VisualElement PowerRow(GoodsEquipment equipment)
        {
            var row = HudTheme.Row();
            row.name = "hud-power";
            row.style.marginTop = 16;
            var on = equipment.PoweredOn;
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.style.flexGrow = 1;
            text.Add(HudTheme.Label("Power", 14, HudTheme.Text, true));
            if (interaction.HasPendingRequests)
            {
                var label = Caption("Switching...", 12, Muted, 2);
                label.name = "hud-power-label";
                text.Add(label);
            }
            row.Add(text);
            var track = new VisualElement { name = "hud-power-switch" };
            track.style.width = 88;
            track.style.height = 44;
            track.style.flexDirection = FlexDirection.Row;
            track.style.alignItems = Align.Center;
            track.style.justifyContent = on ? Justify.FlexEnd : Justify.FlexStart;
            HudTheme.Pad(track, 4);
            track.style.backgroundColor = on ? HudTheme.PowerOn : HudTheme.Edge;
            HudTheme.Radius(track, 22);
            var knob = new VisualElement { pickingMode = PickingMode.Ignore };
            knob.style.width = knob.style.height = 36;
            knob.style.backgroundColor = HudTheme.Hex(0xF4F1EA);
            knob.style.alignItems = Align.Center;
            knob.style.justifyContent = Justify.Center;
            HudTheme.Radius(knob, 18);
            knob.Add(HudTheme.Label(on ? "ON" : "OFF", 10, HudTheme.Ink, true));
            track.Add(knob);
            var equipmentId = equipment.Id;
            track.RegisterCallback<ClickEvent>(_ =>
            {
                if (!interaction.HasPendingRequests) ClickPower(equipmentId, !on);
            });
            track.SetEnabled(!interaction.HasPendingRequests);
            row.Add(track);
            return row;
        }

        // Flips a machine's power switch, like the switch. Public so tests drive the same path.
        public void ClickPower(string equipmentId, bool on) => interaction.SetPower(equipmentId, on);

        // A register (decision 0034) sells only while someone works it: who does, and a button to work it or leave it.
        private VisualElement StaffRow(GoodsSnapshot site, GoodsEquipment register)
        {
            var row = HudTheme.Row();
            row.name = "hud-staff";
            row.style.marginTop = 14;
            row.style.flexWrap = Wrap.Wrap;
            var me = interaction.LocalPlayerId;
            var who = string.IsNullOrEmpty(register.StaffId) ? "nobody: customers are not served"
                : register.StaffId == me ? "you" : site.Employees.FirstOrDefault(x => x.Id == register.StaffId)?.Name ?? "a teammate";
            // While a request is out the row says so, and it is rebuilt when the server's answer arrives (Signature).
            var label = Caption(interaction.HasPendingRequests ? "Updating..." : $"Staffed by {who}", 12,
                string.IsNullOrEmpty(register.StaffId) ? Spoiled : HudTheme.Text);
            label.name = "hud-staff-label";
            label.style.flexGrow = 1;
            row.Add(label);
            var mine = register.StaffId == me;
            var registerId = register.Id;
            var button = HudTheme.Button("hud-staff-button", mine ? "Leave" : "Work this register", () => ClickStaff(registerId, !mine),
                mine ? HudButton.Secondary : HudButton.Primary);
            button.style.marginLeft = 8;
            button.SetEnabled(!interaction.HasPendingRequests);
            row.Add(button);
            foreach (var employee in site.Employees.Where(x => x.Id != register.StaffId))
            {
                var id = employee.Id;
                var assign = HudTheme.Button($"hud-staff-{id}", $"Assign {employee.Name}", () => interaction.Staff(registerId, id));
                assign.style.marginLeft = 8;
                row.Add(assign);
            }
            return row;
        }

        // Works the register (or leaves it), like the button. Public so tests drive the same path.
        public void ClickStaff(string registerId, bool work) => interaction.Staff(registerId, work ? interaction.LocalPlayerId : "");

        // A loading dock (decision 0022): trucks load from Outgoing (its input) and unload into Incoming (its output), which
        // only gives. The trucks at this dock are listed under it.
        private VisualElement DockWindow(GoodsSnapshot site, GoodsEquipment equipment)
        {
            var window = Window("hud-machine", "Loading dock");
            var body = Inset(FlexDirection.Row);
            body.style.alignItems = Align.FlexStart;
            var outgoing = Labelled(GridView(InputGrid), $"Outgoing: trucks load {Units(site, equipment.InputLocationId)}");
            outgoing.style.marginRight = 16;
            body.Add(outgoing);
            body.Add(Labelled(GridView(OutputGrid), $"Incoming: trucks unload {Units(site, equipment.OutputLocationId)}"));
            window.Add(body);
            var trucks = site.Trucks.Select(x => (Truck: x, Route: GoodsWorld.RouteOf(site, x)))
                .Where(x => x.Route?.PickupDockId == equipment.Id || x.Route?.DropoffDockId == equipment.Id).ToList();
            var note = Caption(trucks.Count == 0 ? "No truck serves this dock; press L to set up a route."
                : string.Join("\n", trucks.Select(x => $"{x.Truck.Name}: {(x.Route.PickupDockId == equipment.Id ? "picks up here" : "delivers here")}")), 12, Muted, 6);
            note.name = "hud-dock-trucks";
            window.Add(note);
            // Docks on a generated lot that is not a restaurant are never refused for this (decision 0032, owner: warn only);
            // restaurant docks cannot be placed out of the street's reach at all (decision 0034).
            var lot = SitePlacement.Active?.LotOf(equipment.SiteId);
            var offer = lot == null ? null : SitePlacement.Active.OfferOf(lot.Id);
            if (offer != null && !RestaurantRules.IsRestaurantSite(site, equipment.SiteId) && !RestaurantRules.ReachesStreet(site, equipment, offer))
            {
                var warning = Caption("Warning: no clear path from the street to this dock. Trucks still use it.", 12, new Color(1f, 0.75f, 0.3f), 6);
                warning.name = "hud-dock-street-warning";
                window.Add(warning);
            }
            return window;
        }

        // A dining table (decision 0024) holds no goods: it shows its seats and the restaurant's standing with customers.
        private VisualElement TableWindow(GoodsSnapshot site, GoodsEquipment equipment)
        {
            var window = Window("hud-machine", "Table");
            var body = Inset(FlexDirection.Column);
            var seated = site.Customers.Count(x => x.TableId == equipment.Id);
            var seats = Caption($"Seats taken: {seated}/{equipment.Seats}", 14, HudTheme.Text);
            seats.name = "hud-table-seats";
            body.Add(seats);
            var diner = site.Diners.FirstOrDefault();
            var standing = Caption(diner == null ? "No customers yet."
                : $"Served {diner.Served}, walked out {diner.WalkedOut}, reputation {diner.Reputation:+0;-0;0}", 12, Muted, 6);
            standing.name = "hud-table-standing";
            body.Add(standing);
            window.Add(body);
            return window;
        }

        private VisualElement StorageMachineWindow(GoodsSnapshot site, GoodsEquipment equipment)
        {
            var refrigerated = site.Locations.FirstOrDefault(x => x.Id == equipment.InputLocationId)?.Refrigerated ?? false;
            var window = Window("hud-machine", MachineName(equipment.Kind), SlotsUsed(site, equipment.InputLocationId));
            var body = Inset(FlexDirection.Column);
            body.style.alignItems = Align.Center;
            body.Add(Labelled(GridView(InputGrid), refrigerated ? "Refrigerated" : "Storage"));
            window.Add(body);
            if (refrigerated)
            {
                var note = Caption("Goods in here do not spoil.", 12, Chilled, 6);
                note.name = "hud-refrigerated-note";
                window.Add(note);
            }
            return window;
        }

        private VisualElement GridView(string grid)
        {
            // Explicit rows of GridColumns slots: a wrapping row of fixed width can lose its last column to device-pixel
            // rounding on a scaled panel.
            var view = new VisualElement { name = $"hud-{grid}-grid" };
            var slots = _grids.TryGetValue(grid, out var list) ? list : new List<SlotContent>();
            VisualElement row = null;
            for (var index = 0; index < slots.Count; index++)
            {
                if (index % GridColumns == 0)
                {
                    row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    view.Add(row);
                }
                var slotIndex = index;
                var content = slots[index];
                var slot = SlotFrame($"hud-{grid}-slot-{index}", false, content != null, SlotSize);
                if (content is { Spoiled: true }) slot.style.backgroundColor = HudTheme.SpoiledFill;
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
                    var location = LocationOf(grid);
                    // Edible goods show when they spoil; UpdateSpoilage fills the text in (empty for goods that do not spoil).
                    if (content.MachineKind == null && !content.Spoiled)
                    {
                        var timer = Caption("", 9, Muted);
                        timer.name = "hud-spoil-timer";
                        timer.style.position = Position.Absolute;
                        timer.style.left = 4;
                        timer.style.top = 2;
                        timer.style.unityFontStyleAndWeight = FontStyle.Bold;
                        timer.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), color = Color.black };
                        slot.Add(timer);
                        _timers.Add((timer, location, content.ItemId));
                    }
                    slot.RegisterCallback<PointerEnterEvent>(_ => _hovered = (location, content));
                }
                slot.RegisterCallback<PointerEnterEvent>(_ => HudTheme.Border(slot, Highlight, 1));
                slot.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    SlotRest(slot, false);
                    _hovered = null;
                    if (_hoverLabel != null) _hoverLabel.text = " ";
                });
                row.Add(slot);
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
            // Customers (decision 0024) buy at a counter: one being served there, or how many wait in the site's queue.
            var serving = site.Customers.FirstOrDefault(x => x.CounterId == interaction.OpenMachineId);
            var waiting = site.Customers.Count(x => x.State == CustomerState.Queued);
            var fraction = 0f;
            if (job == null && serving != null)
            {
                var item = interaction.Session.Recipes.FirstOrDefault(x => x != null && x.Id == serving.RecipeId);
                var duration = Math.Max(1, item?.DurationSeconds ?? 1);
                var elapsed = duration - serving.RemainingSeconds + Mathf.Min(Time.unscaledTime - _siteSeenAt, 1f);
                fraction = Mathf.Clamp01((float)(elapsed / duration));
                var food = item == null ? "" : string.Join(" + ", item.Inputs.Select(x => ItemName(x.itemId)).Distinct()) + " ";
                _progressLabel.text = $"Serving a customer: {food}for {FormatCash(serving.PaidCents)}"
                    + (waiting > 0 ? $"; {waiting} waiting" : "");
            }
            else if (job == null && equipment != null && HasPowerSwitch(equipment.Kind) && !equipment.PoweredOn)
                _progressLabel.text = "Off: switch it on to bake";
            else if (job == null)
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
                else if (recipes.Count > 0 && recipes.All(x => x.IsSale))
                    _progressLabel.text = WaitingText(site, equipment, recipes, ingredients, waiting);
                else _progressLabel.text = ingredients.Count == 0 ? "Idle: no recipes for this machine" : $"Idle: put {string.Join(" or ", ingredients)} in the input";
            }
            else if (job.State == StationJobState.Blocked)
            {
                fraction = 1f;
                _progressLabel.text = $"Done, waiting: output full ({ItemName(job.OutputItemId)})";
            }
            else if (equipment != null && HasPowerSwitch(equipment.Kind) && !equipment.PoweredOn)
            {
                // Switched off mid-batch (decision 0037): the batch waits where it is.
                fraction = Mathf.Clamp01((float)(job.DurationSeconds - job.RemainingSeconds) / job.DurationSeconds);
                _progressLabel.text = $"Paused (off): {ItemName(job.OutputItemId)} {job.DurationSeconds - job.RemainingSeconds}/{job.DurationSeconds} s";
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

        // Why customers at an idle counter wait (decision 0024), naming the first blocker: no edible menu item in this counter,
        // or every table seat taken while a dine-in customer queues. Otherwise they are on their way to being served.
        private static string WaitingText(GoodsSnapshot site, GoodsEquipment counter, List<RecipeAsset> menu, List<string> ingredients, int waiting)
        {
            var food = string.Join(" or ", ingredients);
            if (waiting == 0) return $"No customers waiting; keep {food} in the input";
            var who = $"{waiting} customer{(waiting == 1 ? "" : "s")} waiting";
            var items = new HashSet<string>(menu.SelectMany(x => x.Inputs).Select(x => x.itemId));
            if (!site.Lots.Any(x => x.LocationId == counter.InputLocationId && !x.Spoiled && items.Contains(x.ItemId)))
                return $"{who}: put edible {food} in the input";
            var tables = site.Equipment.Where(x => GoodsWorld.IsTable(x) && x.State == EquipmentState.Placed).ToList();
            var freeSeats = tables.Sum(x => x.Seats) - site.Customers.Count(x => tables.Any(y => y.Id == x.TableId));
            if (freeSeats <= 0 && site.Customers.Any(x => x.State == CustomerState.Queued && x.DineIn))
                return $"{who}: every table seat is taken; place more tables";
            return who;
        }

        // Spoil timers and the hover line follow each baseline (the server ages goods every clock step); display only.
        private void UpdateSpoilage(GoodsSnapshot site)
        {
            foreach (var (label, locationId, itemId) in _timers)
            {
                var time = SpoilTime(site, locationId, itemId);
                label.text = time is { } known ? FormatDuration(known.Seconds, true) : "";
                if (time is { } shown) label.style.color = shown.Soon ? SpoilingSoon : shown.Refrigerated ? Chilled : HudTheme.Text;
            }
            if (_hoverLabel == null || _hovered is not { } hovered) return;
            var content = hovered.Content;
            var name = content.MachineKind != null ? Title(content.MachineKind) : ItemName(content.ItemId);
            var text = $"{name} ×{content.Count}";
            if (content.Spoiled) text += " (spoiled)";
            else if (content.MachineKind == null && SpoilTime(site, hovered.LocationId, content.ItemId) is { } spoil)
                text += spoil.Refrigerated
                    ? $"  ·  refrigerated: not spoiling ({FormatDuration(spoil.Seconds, false)} left out of the cold)"
                    : $"  ·  spoils in {FormatDuration(spoil.Seconds, false)}";
            _hoverLabel.text = text;
        }

        // Ambient time left for the first edible lot of an item in a location to spoil; null if none of them spoils. A refrigerated
        // location pauses spoilage (decision 0018), so there the time is frozen until the goods leave the cold.
        private static (long Seconds, bool Refrigerated, bool Soon)? SpoilTime(GoodsSnapshot site, string locationId, string itemId)
        {
            var refrigerated = site.Locations.FirstOrDefault(x => x.Id == locationId)?.Refrigerated ?? false;
            GoodsLot first = null;
            var seconds = long.MaxValue;
            foreach (var lot in site.Lots)
            {
                if (lot.LocationId != locationId || lot.ItemId != itemId || lot.Spoiled || lot.SpoilAfterSeconds >= GoodsWorld.NonPerishableSeconds)
                    continue;
                var left = Math.Max(0, lot.SpoilAfterSeconds - lot.ExposureSeconds);
                if (left >= seconds) continue;
                seconds = left;
                first = lot;
            }
            if (first == null) return null;
            return (seconds, refrigerated, !refrigerated && seconds * 10 <= first.SpoilAfterSeconds);
        }

        // Whole seconds as a duration: compact for a slot corner ("45s", "12m", "3h", "2d"), otherwise "1h 05m", "12m 05s", "45s".
        public static string FormatDuration(long seconds, bool compact)
        {
            seconds = Math.Max(0, seconds);
            if (compact)
                return seconds < 60 ? $"{seconds}s" : seconds < 3600 ? $"{seconds / 60}m" : seconds < 86400 ? $"{seconds / 3600}h" : $"{seconds / 86400}d";
            if (seconds < 60) return $"{seconds}s";
            if (seconds < 3600) return $"{seconds / 60}m {seconds % 60:00}s";
            if (seconds < 86400) return $"{seconds / 3600}h {seconds / 60 % 60:00}m";
            return $"{seconds / 86400}d {seconds / 3600 % 24}h";
        }

        // Slots used out of the location's capacity.
        private string Units(GoodsSnapshot site, string locationId)
        {
            var location = site.Locations.FirstOrDefault(x => x.Id == locationId);
            return location == null ? ""
                : $"{GoodsSlots.SlotsUsed(site.Lots.Where(x => x.LocationId == locationId), interaction.Session.MaxStack)}/{location.Capacity}";
        }

        // "3/40 slots" for a window's subtitle; null when the location is unknown.
        private string SlotsUsed(GoodsSnapshot site, string locationId)
        {
            var units = Units(site, locationId);
            return units.Length == 0 ? null : $"{units} slots";
        }

        private List<RecipeAsset> SaleRecipes(string kind) =>
            interaction.Session.Recipes.Where(x => x != null && x.IsSale && x.StationKind == kind).ToList();

        private ItemDefinition Item(string itemId) => Items.FirstOrDefault(x => x != null && x.Id == itemId);
        private string ItemName(string itemId) => Item(itemId)?.DisplayName ?? Title(itemId);
        private Sprite ItemIcon(string itemId) => Item(itemId)?.Icon;
        private Sprite MachineIcon(string kind) =>
            interaction.Session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == kind)?.Icon;

        // Item and kind IDs are shown directly when no content names them: "dough" -> "Dough". Content names start upper case
        // on screen ("oven" -> "Oven").
        private string MachineName(string kind)
        {
            var name = interaction.Session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == kind)?.DisplayName;
            return string.IsNullOrEmpty(name) ? Title(kind) : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private static string Title(string id) =>
            string.IsNullOrEmpty(id) ? "" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));

        // A screen window: bold title with an optional muted subtitle beside it (slots used, the machine's recipe).
        private static VisualElement Window(string name, string title, string subtitle = null)
        {
            var window = new VisualElement { name = name };
            HudTheme.StylePanel(window, 16);
            window.style.marginLeft = window.style.marginRight = 6;
            var header = HudTheme.Row();
            header.style.alignItems = Align.FlexEnd;
            header.style.marginBottom = 12;
            header.Add(HudTheme.Label(title, 17, Heading, true));
            if (!string.IsNullOrEmpty(subtitle))
            {
                var detail = HudTheme.Label(subtitle, 12, Muted);
                detail.style.marginLeft = 10;
                detail.style.marginBottom = 2;
                detail.style.flexShrink = 1;
                header.Add(detail);
            }
            window.Add(header);
            return window;
        }

        // The darker well inside a window that holds a machine's grids.
        private static VisualElement Inset(FlexDirection direction)
        {
            var body = new VisualElement();
            body.style.flexDirection = direction;
            body.style.backgroundColor = HudTheme.Inset;
            HudTheme.Radius(body, 12);
            HudTheme.Pad(body, 16);
            return body;
        }

        // A list row on a card (supplier offers, employees, floors).
        private static VisualElement CardRow(string name)
        {
            var row = HudTheme.Row();
            row.name = name;
            row.style.marginTop = 6;
            row.style.backgroundColor = HudTheme.Card;
            HudTheme.Border(row, HudTheme.CardEdge, 1);
            HudTheme.Radius(row, 10);
            row.style.paddingLeft = row.style.paddingRight = 12;
            row.style.paddingTop = row.style.paddingBottom = 10;
            return row;
        }

        // A small stat tile: caption over a value.
        private static VisualElement Tile(string caption, string value, Color color)
        {
            var tile = new VisualElement { pickingMode = PickingMode.Ignore };
            tile.style.flexGrow = 1;
            tile.style.flexBasis = 0;
            tile.style.backgroundColor = HudTheme.Inset;
            HudTheme.Radius(tile, 10);
            tile.style.paddingLeft = tile.style.paddingRight = 10;
            tile.style.paddingTop = tile.style.paddingBottom = 10;
            tile.Add(HudTheme.Eyebrow(caption));
            var text = HudTheme.Label(value, 15, color, true);
            text.style.marginTop = 4;
            tile.Add(text);
            return tile;
        }

        private static VisualElement Labelled(VisualElement content, string label)
        {
            var column = new VisualElement();
            column.style.alignItems = Align.FlexStart;
            var caption = HudTheme.Eyebrow(label);
            caption.style.marginBottom = 6;
            caption.style.marginLeft = SlotGap / 2;
            column.Add(caption);
            column.Add(content);
            return column;
        }

        private static VisualElement SlotFrame(string name, bool selected, bool filled, int size)
        {
            var slot = new Button { name = name, text = "" };
            slot.style.width = slot.style.height = size;
            slot.style.marginLeft = slot.style.marginRight = slot.style.marginTop = slot.style.marginBottom = SlotGap / 2;
            slot.style.paddingLeft = slot.style.paddingRight = slot.style.paddingTop = slot.style.paddingBottom = 0;
            slot.style.alignItems = Align.Center;
            slot.style.justifyContent = Justify.Center;
            slot.style.backgroundColor = filled ? HudTheme.Slot : HudTheme.SlotEmpty;
            HudTheme.Radius(slot, size > SlotSize ? 9 : 8);
            SlotRest(slot, selected);
            return slot;
        }

        // A slot's edge at rest: amber when it is the selected hotbar slot, otherwise invisible.
        private static void SlotRest(VisualElement slot, bool selected) =>
            HudTheme.Border(slot, selected ? Highlight : new Color(0f, 0f, 0f, 0f), selected ? 2 : 1);

        private static VisualElement Icon(Sprite sprite, string name, float opacity, int size = IconSize)
        {
            var icon = new VisualElement { name = "hud-icon", pickingMode = PickingMode.Ignore };
            icon.style.width = icon.style.height = size;
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
                var label = Caption(name, 10, HudTheme.Text);
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
            var label = Caption(count.ToString(CultureInfo.InvariantCulture), 12, HudTheme.Text);
            label.name = "hud-count";
            label.style.position = Position.Absolute;
            label.style.right = 5;
            label.style.bottom = 2;
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

    }
}
