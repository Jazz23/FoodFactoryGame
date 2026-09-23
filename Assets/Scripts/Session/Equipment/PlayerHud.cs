// Local player's HUD in UI Toolkit, built in code: crosshair, hotbar, the inventory screen (inventory beside the dev
// storage) and the machine screen (recipe picker, Start, progress, input and output). Presentation only: it renders the
// latest replicated baseline and sends every change through EquipmentInteraction's server requests. Progress is
// interpolated for at most one clock step past the baseline, so it never runs ahead of the server by more than that.
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
        private static readonly Color Panel = new(0.08f, 0.08f, 0.1f, 0.92f);
        private static readonly Color Slot = new(0.2f, 0.2f, 0.24f, 1f);
        private static readonly Color SlotSelected = new(0.85f, 0.6f, 0.15f, 1f);
        private static readonly Color Spoiled = new(0.95f, 0.35f, 0.3f, 1f);
        private static readonly Color Muted = new(0.7f, 0.7f, 0.75f, 1f);

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;

        private readonly Dictionary<string, string> _selectedRecipe = new();
        private VisualElement _crosshair;
        private VisualElement _hotbar;
        private VisualElement _screen;
        private VisualElement _progressFill;
        private Label _progressLabel;
        private string _signature;
        private GoodsSnapshot _site;
        private float _siteSeenAt;

        public VisualElement ScreenRoot => _screen;

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
            _screen.style.backgroundColor = Panel;
            Pad(_screen, 12);
            layer.Add(_crosshair);
            layer.Add(_hotbar);
            layer.Add(_screen);
            root.Add(layer);
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
            _crosshair.style.display = active && interaction.PointerLocked ? DisplayStyle.Flex : DisplayStyle.None;
            _hotbar.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            _screen.style.display = active && interaction.Screen != InteractionScreen.None ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active) return;
            var signature = Signature(site);
            if (signature != _signature)
            {
                _signature = signature;
                BuildHotbar();
                BuildScreen(site);
            }
            UpdateProgress(site);
        }

        // Rebuild only when something shown as a button changes, so a click is not lost to a rebuild every clock tick.
        private string Signature(GoodsSnapshot site)
        {
            var text = new StringBuilder();
            text.Append(interaction.Screen).Append('|').Append(interaction.OpenMachineId).Append('|').Append(interaction.SelectedSlot);
            foreach (var kind in interaction.HotbarKinds) text.Append('|').Append(kind).Append(interaction.HeldCount(kind));
            if (interaction.Screen == InteractionScreen.None) return text.ToString();
            foreach (var lot in site.Lots.OrderBy(x => x.Id, StringComparer.Ordinal))
                text.Append('|').Append(lot.Id).Append(lot.LocationId).Append(lot.Quantity).Append(lot.Spoiled);
            foreach (var job in site.Jobs) text.Append('|').Append(job.Id).Append(job.State);
            if (interaction.OpenMachineId != null && _selectedRecipe.TryGetValue(interaction.OpenMachineId, out var recipe))
                text.Append('|').Append(recipe);
            return text.ToString();
        }

        private void BuildHotbar()
        {
            _hotbar.Clear();
            var kinds = interaction.HotbarKinds;
            for (var index = 0; index < EquipmentInteraction.HotbarSize; index++)
            {
                var slot = new VisualElement { name = $"hud-slot-{index + 1}", pickingMode = PickingMode.Ignore };
                slot.style.width = 64;
                slot.style.height = 76;
                slot.style.marginLeft = slot.style.marginRight = 2;
                slot.style.backgroundColor = index == interaction.SelectedSlot ? SlotSelected : Slot;
                Pad(slot, 4);
                slot.Add(Caption($"{index + 1}", 11, Muted));
                if (index < kinds.Count)
                {
                    var count = interaction.HeldCount(kinds[index]);
                    slot.Add(Caption(Title(kinds[index]), 12, count > 0 ? Color.white : Muted));
                    slot.Add(Caption($"×{count}", 12, count > 0 ? Color.white : Muted));
                }
                _hotbar.Add(slot);
            }
        }

        private void BuildScreen(GoodsSnapshot site)
        {
            _screen.Clear();
            _progressFill = null;
            _progressLabel = null;
            var inventoryId = interaction.InventoryId;
            if (interaction.Screen == InteractionScreen.None || inventoryId == null) return;
            if (interaction.Screen == InteractionScreen.Inventory)
            {
                _screen.Add(InventoryColumn(site, inventoryId, DevWorld.StorageId));
                _screen.Add(StacksColumn(site, "Storage", DevWorld.StorageId, inventoryId, "hud-storage"));
                return;
            }
            var equipment = site.Equipment.FirstOrDefault(x => x.Id == interaction.OpenMachineId);
            if (equipment == null) return;
            _screen.Add(InventoryColumn(site, inventoryId, equipment.InputLocationId));
            _screen.Add(MachineColumn(site, equipment, inventoryId));
        }

        private VisualElement InventoryColumn(GoodsSnapshot site, string inventoryId, string clickDestination)
        {
            var column = StacksColumn(site, "Inventory", inventoryId, clickDestination, "hud-inventory");
            var machines = site.Equipment.Where(x => x.State == EquipmentState.Held && x.HolderId == interaction.LocalPlayerId)
                .GroupBy(x => x.Kind).OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
            column.Add(Caption("Machines", 13, Muted, 8));
            if (machines.Count == 0) column.Add(Caption("none", 12, Muted));
            foreach (var group in machines) column.Add(Caption($"{Title(group.Key)} ×{group.Count()}", 13, Color.white));
            return column;
        }

        // One button per (item, spoiled) stack; clicking moves that stack's lots to the destination.
        private VisualElement StacksColumn(GoodsSnapshot site, string title, string locationId, string destinationId, string name)
        {
            var column = Column(name);
            var location = site.Locations.FirstOrDefault(x => x.Id == locationId);
            var lots = site.Lots.Where(x => x.LocationId == locationId).ToList();
            column.Add(Caption(location == null ? title : $"{title} {lots.Sum(x => x.Quantity)}/{location.Capacity}", 14, Color.white));
            if (lots.Count == 0) column.Add(Caption("empty", 12, Muted));
            foreach (var stack in lots.GroupBy(x => (x.ItemId, x.Spoiled)).OrderBy(x => x.Key.ItemId, StringComparer.Ordinal).ThenBy(x => x.Key.Spoiled))
            {
                var stackLots = stack.ToList();
                var button = new Button(() => interaction.MoveGoods(stackLots, destinationId))
                {
                    name = $"{name}-{stack.Key.ItemId}{(stack.Key.Spoiled ? "-spoiled" : "")}",
                    text = $"{Title(stack.Key.ItemId)} ×{stackLots.Sum(x => x.Quantity)}{(stack.Key.Spoiled ? "  (spoiled)" : "")}"
                };
                if (stack.Key.Spoiled) button.style.color = Spoiled;
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                column.Add(button);
            }
            return column;
        }

        private VisualElement MachineColumn(GoodsSnapshot site, GoodsEquipment equipment, string inventoryId)
        {
            var column = Column("hud-machine");
            column.style.width = 300;
            column.Add(Caption($"{Title(equipment.Kind)}  ({equipment.Id})", 14, Color.white));
            var recipes = interaction.Session.Recipes.Where(x => x != null && x.StationKind == equipment.Kind).ToList();
            var job = site.Jobs.FirstOrDefault(x => x.StationId == equipment.Id);
            if (!_selectedRecipe.TryGetValue(equipment.Id, out var selected) || recipes.All(x => x.Id != selected))
                selected = job?.RecipeId ?? recipes.FirstOrDefault()?.Id;
            column.Add(Caption("Recipe", 13, Muted, 8));
            if (recipes.Count == 0) column.Add(Caption("no recipes for this machine", 12, Muted));
            foreach (var recipe in recipes)
            {
                var id = recipe.Id;
                var button = new Button(() => _selectedRecipe[equipment.Id] = id) { name = $"hud-recipe-{id}", text = Describe(recipe) };
                button.style.unityTextAlign = TextAnchor.MiddleLeft;
                if (id == selected) button.style.backgroundColor = SlotSelected;
                column.Add(button);
            }
            var start = new Button(() => interaction.StartJob(equipment.Id, selected)) { name = "hud-start", text = "Start batch" };
            start.SetEnabled(job == null && selected != null);
            start.style.marginTop = 6;
            column.Add(start);

            var bar = new VisualElement { name = "hud-progress" };
            bar.style.height = 14;
            bar.style.marginTop = 8;
            bar.style.backgroundColor = Slot;
            _progressFill = new VisualElement { name = "hud-progress-fill" };
            _progressFill.style.height = new Length(100, LengthUnit.Percent);
            _progressFill.style.backgroundColor = SlotSelected;
            bar.Add(_progressFill);
            column.Add(bar);
            _progressLabel = Caption("", 12, Muted);
            column.Add(_progressLabel);

            var input = StacksColumn(site, "Input", equipment.InputLocationId, inventoryId, "hud-machine-input");
            var output = StacksColumn(site, "Output", equipment.OutputLocationId, inventoryId, "hud-machine-output");
            input.style.marginLeft = output.style.marginLeft = 0;
            column.Add(input);
            column.Add(output);
            return column;
        }

        private void UpdateProgress(GoodsSnapshot site)
        {
            if (_progressFill == null) return;
            var job = site.Jobs.FirstOrDefault(x => x.StationId == interaction.OpenMachineId);
            var fraction = 0f;
            if (job == null) _progressLabel.text = "Idle: add inputs, pick a recipe and press Start";
            else if (job.State == StationJobState.Blocked)
            {
                fraction = 1f;
                _progressLabel.text = $"Done, waiting: output full ({Title(job.OutputItemId)})";
            }
            else
            {
                var elapsed = job.DurationSeconds - job.RemainingSeconds + Mathf.Min(Time.unscaledTime - _siteSeenAt, 1f);
                fraction = Mathf.Clamp01((float)(elapsed / job.DurationSeconds));
                _progressLabel.text = $"Making {Title(job.OutputItemId)}: {job.DurationSeconds - job.RemainingSeconds}/{job.DurationSeconds} s";
            }
            _progressFill.style.width = new Length(fraction * 100f, LengthUnit.Percent);
        }

        private static string Describe(RecipeAsset recipe) =>
            $"{recipe.DisplayName}: {string.Join(" + ", recipe.Inputs.Select(x => $"{x.quantity} {x.itemId}"))} → "
            + $"{recipe.OutputQuantity} {recipe.OutputItemId}, {recipe.DurationSeconds} s";

        // Item and kind IDs are shown directly until item content exists: "dough" -> "Dough".
        private static string Title(string id) =>
            string.IsNullOrEmpty(id) ? "" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Replace('-', ' '));

        private static VisualElement Column(string name)
        {
            var column = new VisualElement { name = name };
            column.style.width = 220;
            column.style.marginLeft = 8;
            column.style.marginRight = 8;
            return column;
        }

        private static Label Caption(string text, int size, Color color, int marginTop = 0)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginTop = marginTop;
            return label;
        }

        private static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = element.style.top = element.style.right = element.style.bottom = 0;
        }

        private static void Pad(VisualElement element, int padding) =>
            element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = padding;

        private static void SetRadius(VisualElement element, int radius) =>
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius =
                element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
    }
}
