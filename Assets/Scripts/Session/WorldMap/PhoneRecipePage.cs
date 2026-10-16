// The phone's recipe detail screen (Recipes app): clicking a recipe card replaces the list with that recipe's ingredient tree
// and a quantity calculator, with a back button to the list. Only the direct ingredients show at first; tapping an ingredient
// another recipe makes expands it in place (tap again to collapse), raw goods are leaves. Amounts are whole units: each made
// ingredient is made in whole batches (ceil of the need over the recipe's output per batch), and its inputs are counted for
// those batches, so a spare from rounding up is shown, never hidden. An item already on the path from the top is shown as a
// loop and never expands, so recipe cycles cannot recurse. Content only: nothing here is sent to the server or saved, and a
// rebuild of the Recipes page (PhonePanel.BuildRecipes) simply drops this screen back to the list.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.WorldMap
{
    public sealed class PhoneRecipePage
    {
        private const int MaxQuantity = 9999;
        private const string CardPrefix = "phone-recipe-";

        private readonly VisualElement _host;
        private readonly SessionRoot _session;
        private readonly Dictionary<string, RecipeAsset> _byId = new();
        // The recipe that makes each item (the first by id when several do).
        private readonly Dictionary<string, RecipeAsset> _makers = new();
        // Expanded nodes, by their path of input indices from the top recipe, so the same item can be open in one branch only.
        private readonly HashSet<string> _expanded = new();
        private readonly List<VisualElement> _hidden = new();
        private VisualElement _detail;
        private VisualElement _tree;
        private TextField _field;
        private Label _summary;
        private RecipeAsset _recipe;
        private int _quantity = 1;

        private PhoneRecipePage(VisualElement host, SessionRoot session)
        {
            _host = host;
            _session = session;
            foreach (var recipe in session.Recipes.Where(x => x != null))
            {
                if (!string.IsNullOrEmpty(recipe.Id)) _byId.TryAdd(recipe.Id, recipe);
            }
            foreach (var recipe in session.Recipes.Where(x => x != null && !x.IsSale && !string.IsNullOrEmpty(x.OutputItemId))
                         .OrderBy(x => x.Id, StringComparer.Ordinal))
                _makers.TryAdd(recipe.OutputItemId, recipe);
        }

        // Makes every recipe card under host (built by PhonePanel.RecipeCard, named phone-recipe-<id>) open its detail screen.
        public static void Attach(VisualElement host, SessionRoot session)
        {
            var sheet = Resources.Load<StyleSheet>("UI/Phone/PhoneRecipe");
            if (sheet != null && !host.styleSheets.Contains(sheet)) host.styleSheets.Add(sheet);
            var page = new PhoneRecipePage(host, session);
            host.Query(className: "phone-recipe").ForEach(card =>
            {
                if (card.name == null || !card.name.StartsWith(CardPrefix, StringComparison.Ordinal)) return;
                if (!page._byId.TryGetValue(card.name.Substring(CardPrefix.Length), out var recipe)) return;
                card.pickingMode = PickingMode.Position;
                card.AddToClassList("phone-recipe--link");
                card.RegisterCallback<ClickEvent>(_ => page.Open(recipe));
            });
        }

        private void Open(RecipeAsset recipe)
        {
            Close();
            _recipe = recipe;
            _quantity = 1;
            _expanded.Clear();
            foreach (var child in _host.Children().ToList())
            {
                if (child.resolvedStyle.display == DisplayStyle.None) continue;
                child.Show(false);
                _hidden.Add(child);
            }
            _detail = BuildDetail();
            _host.Add(_detail);
            RebuildTree();
            var scroll = _host.GetFirstAncestorOfType<ScrollView>();
            if (scroll != null) scroll.scrollOffset = Vector2.zero;
        }

        private void Close()
        {
            _detail?.RemoveFromHierarchy();
            _detail = null;
            foreach (var child in _hidden) child.Show(true);
            _hidden.Clear();
        }

        private VisualElement BuildDetail()
        {
            var detail = new VisualElement { name = "phone-recipe-detail" };
            detail.AddToClassList("phone-recipe-detail");

            var bar = new VisualElement();
            bar.AddToClassList("ff-row");
            bar.AddToClassList("phone-recipe-detail__bar");
            var back = Button("", Close, "ff-button--sm", "ff-button--with-glyph", "phone-recipe-detail__back");
            back.name = "phone-recipe-back";
            back.Add(Glyphs.Create("chevron-left"));
            back.Add(Text("Recipes"));
            bar.Add(back);
            detail.Add(bar);

            var head = new VisualElement { pickingMode = PickingMode.Ignore };
            head.AddToClassList("ff-card");
            head.AddToClassList("ff-row");
            head.AddToClassList("phone-recipe-detail__head");
            var iconItem = _recipe.IsSale ? _recipe.Inputs.FirstOrDefault()?.itemId : _recipe.OutputItemId;
            head.Add(Icon(iconItem, "phone-recipe-detail__icon"));
            var titles = new VisualElement { pickingMode = PickingMode.Ignore };
            titles.AddToClassList("ff-grow");
            titles.Add(Text(_recipe.DisplayName, "phone-recipe-detail__name"));
            var about = _recipe.IsSale
                ? $"Menu item  ·  sells for {PlayerHud.FormatCash(_recipe.SaleCents)}  ·  served in {PlayerHud.FormatDuration(_recipe.DurationSeconds, true)}"
                : $"{MachineName(_recipe.StationKind)}  ·  makes {PerBatch(_recipe)} {ItemName(_recipe.OutputItemId)} in {PlayerHud.FormatDuration(_recipe.DurationSeconds, true)}";
            titles.Add(Text(about, "ff-small", "phone-recipe__line"));
            head.Add(titles);
            detail.Add(head);

            var calc = new VisualElement();
            calc.AddToClassList("ff-inset");
            calc.AddToClassList("phone-recipe-calc");
            var row = new VisualElement();
            row.AddToClassList("ff-row");
            row.Add(Text(_recipe.IsSale ? "Serve" : "Make", "phone-form__label"));
            var minus = Button("-", () => SetQuantity(_quantity - 1), "phone-form__step");
            minus.name = "phone-recipe-minus";
            row.Add(minus);
            _field = new TextField { name = "phone-recipe-quantity", maxLength = MaxQuantity.ToString().Length, value = _quantity.ToString() };
            _field.AddToClassList("ff-text-field");
            _field.AddToClassList("ff-text-field--compact");
            _field.AddToClassList("phone-recipe-calc__field");
            _field.RegisterValueChangedCallback(evt =>
            {
                // Typing applies at once when it is a whole number; anything else waits, and leaving the box restores the value.
                if (int.TryParse(evt.newValue, out var typed) && typed >= 1) Apply(Math.Min(typed, MaxQuantity));
            });
            _field.RegisterCallback<FocusOutEvent>(_ => SetQuantity(_quantity));
            row.Add(_field);
            var plus = Button("+", () => SetQuantity(_quantity + 1), "phone-form__step");
            plus.name = "phone-recipe-plus";
            row.Add(plus);
            row.Add(Text(_recipe.IsSale ? "orders" : ItemName(_recipe.OutputItemId), "phone-recipe-calc__unit", "ff-grow"));
            calc.Add(row);
            _summary = Text("", "ff-small", "phone-recipe-calc__summary");
            calc.Add(_summary);
            detail.Add(calc);

            detail.Add(Text("INGREDIENTS  ·  TAP ONE WITH AN ARROW TO SEE WHAT IT IS MADE FROM", "ff-eyebrow", "phone__heading"));
            _tree = new VisualElement { name = "phone-recipe-tree" };
            _tree.AddToClassList("phone-recipe-tree");
            detail.Add(_tree);
            return detail;
        }

        private void SetQuantity(int quantity)
        {
            Apply(Mathf.Clamp(quantity, 1, MaxQuantity));
            _field?.SetValueWithoutNotify(_quantity.ToString());
        }

        private void Apply(int quantity)
        {
            if (quantity == _quantity) return;
            _quantity = quantity;
            RebuildTree();
        }

        private void Toggle(string path)
        {
            if (!_expanded.Remove(path)) _expanded.Add(path);
            RebuildTree();
        }

        private void RebuildTree()
        {
            if (_tree == null || _recipe == null) return;
            var perBatch = PerBatch(_recipe);
            var batches = Batches(_quantity, perBatch);
            _summary.SetText(_recipe.IsSale
                ? $"{_quantity} {(_quantity == 1 ? "order" : "orders")}  ·  {PlayerHud.FormatCash(_recipe.SaleCents * _quantity)} in sales"
                : BatchText(batches, perBatch, _quantity) + $"  ·  {PlayerHud.FormatDuration((long)batches * _recipe.DurationSeconds, true)} on one {MachineName(_recipe.StationKind)}");
            _tree.Clear();
            var ancestors = new HashSet<string>();
            if (!_recipe.IsSale && !string.IsNullOrEmpty(_recipe.OutputItemId)) ancestors.Add(_recipe.OutputItemId);
            if (_recipe.Inputs.Count == 0) _tree.Add(Text("Needs nothing.", "ff-small"));
            AddInputs(_tree, _recipe, batches, "", ancestors);
        }

        // One node per input of recipe, needed for that many batches; expanded made inputs nest their own inputs beneath them.
        private void AddInputs(VisualElement parent, RecipeAsset recipe, long batches, string path, HashSet<string> ancestors)
        {
            for (var index = 0; index < recipe.Inputs.Count; index++)
            {
                var input = recipe.Inputs[index];
                if (input == null || string.IsNullOrEmpty(input.itemId)) continue;
                var itemId = input.itemId;
                var need = batches * Math.Max(1, input.quantity);
                var nodePath = path + "/" + index;
                var maker = _makers.TryGetValue(itemId, out var found) ? found : null;
                var loop = maker != null && ancestors.Contains(itemId);
                var expandable = maker != null && !loop;
                var open = expandable && _expanded.Contains(nodePath);

                var node = new VisualElement { name = "phone-recipe-node" + nodePath.Replace('/', '-') };
                node.AddToClassList("phone-recipe-tree__node");
                VisualElement row;
                if (expandable)
                {
                    var toggle = nodePath;
                    row = Button("", () => Toggle(toggle), "ff-card", "phone-recipe-tree__row--link");
                    row.Add(Glyphs.Create(open ? "chevron-down" : "chevron-right"));
                }
                else
                {
                    row = new VisualElement { pickingMode = PickingMode.Ignore };
                    row.AddToClassList("ff-card");
                    var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
                    spacer.AddToClassList("phone-recipe-tree__spacer");
                    row.Add(spacer);
                }
                row.AddToClassList("ff-row");
                row.AddToClassList("phone-recipe-tree__row");
                row.EnableInClassList("phone-recipe-tree__row--open", open);
                row.Add(Icon(itemId, "phone-recipe-tree__icon"));
                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("ff-grow");
                text.Add(Text(ItemName(itemId), "phone-recipe-tree__name"));
                var childBatches = maker == null ? 0 : Batches(need, PerBatch(maker));
                var sub = maker == null ? "Raw: bought from a farm or supplier"
                    : loop ? "Loop: already being made above"
                    : open ? $"{BatchText(childBatches, PerBatch(maker), need)} at the {MachineName(maker.StationKind)}"
                    : $"Made at the {MachineName(maker.StationKind)}";
                text.Add(Text(sub, "ff-small", "phone-recipe-tree__sub"));
                row.Add(text);
                row.Add(Text("x " + need, "phone-recipe-tree__amount", "ff-nowrap"));
                node.Add(row);

                if (open)
                {
                    var children = new VisualElement();
                    children.AddToClassList("phone-recipe-tree__children");
                    var below = new HashSet<string>(ancestors) { itemId };
                    if (maker.Inputs.Count == 0) children.Add(Text("Needs nothing.", "ff-small"));
                    AddInputs(children, maker, childBatches, nodePath, below);
                    node.Add(children);
                }
                parent.Add(node);
            }
        }

        private static int PerBatch(RecipeAsset recipe) => recipe.IsSale ? 1 : Math.Max(1, recipe.OutputQuantity);

        private static long Batches(long need, int perBatch) => (need + perBatch - 1) / perBatch;

        // "3 batches of 4 (2 spare)", or just the batch count when each batch makes one.
        private static string BatchText(long batches, int perBatch, long need)
        {
            var text = $"{batches} {(batches == 1 ? "batch" : "batches")}";
            if (perBatch > 1) text += $" of {perBatch}";
            var spare = batches * perBatch - need;
            return spare > 0 ? text + $" ({spare} spare)" : text;
        }

        private string ItemName(string itemId) => _session.Items.FirstOrDefault(x => x != null && x.Id == itemId)?.DisplayName ?? itemId ?? "";

        private string MachineName(string kind)
        {
            var name = _session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == kind)?.DisplayName;
            if (string.IsNullOrEmpty(name)) name = kind ?? "";
            return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        private VisualElement Icon(string itemId, string className)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("ff-icon");
            icon.AddToClassList(className);
            icon.SetIcon(_session.Items.FirstOrDefault(x => x != null && x.Id == itemId)?.Icon, ItemName(itemId));
            return icon;
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
    }
}
