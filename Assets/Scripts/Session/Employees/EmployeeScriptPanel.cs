// Local player's employee screen (decision 0037). Its look is the Employee UXML and USS under Assets/UI/Resources/UI
// (decision 0042); this class fills and wires it. The Tasks tab (shown first) is a visual task
// list: + adds a task, each a "Move stuff from A to B" (a source, a destination and which items) or a "Turn machine on" (a
// machine and on, off or toggle). "Set source", "Set destination" and "Select machine" hide the screen while the player
// picks one of the pulsing valid targets in the world (EquipmentInteraction.BeginTargetPick). The list generates the Lua the
// employee actually runs (EmployeeTaskList.ToLua), shown in the Lua source tab. Editing that Lua by hand disables the Tasks
// tab: it then locks behind a banner until "Reset to tasks" regenerates the Lua. Run sends the Lua and the list
// (saved together), Stop halts it; both are requests the server checks (EmployeeWorker). Presentation only: the list and
// the Lua are this client's draft until Run. The script assistant tab is disabled (its code is kept, unused).
// It opens unfocused, whether by E or by the left click, which is ignored by every text box until released. E closes the
// screen unless a text box has focus, where it is typed.
// On the Lua tab, "Select world pos" inserts a cell's or machine's Lua text at the text box's caret (replacing any selection);
// the draft survives because the screen stays open while hidden. The Hands row shows what the employee holds, one small square per hand slot;
// clicking one takes that stack into the player's inventory with an ordinary server-checked transfer (decision 0039).
// The on/off/toggle and whitelist choices stay checkboxes as decision 0037 requires.
// An item search box opens its drop-down as soon as it has focus: with no text it lists the items this player touched most
// recently (EquipmentInteraction.RecentItemRank), then the rest; typing narrows it by name.
using System;
using System.Collections.Generic;
using System.Linq;
using FoodFactoryGame.Goods;
using FoodFactoryGame.Session.Equipment;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Employees
{
    [DisallowMultipleComponent]
    public sealed class EmployeeScriptPanel : MonoBehaviour
    {
        private const string Templates = "Employee/";

        // Suggestions shown under an item search box.
        private const int MaxSuggestions = 6;

        // The caret bar's size in pixels (Employee.uss emp__caret): the text box's 13 px font has lines about 14.5 px tall.
        private const float CaretWidth = 3f;
        private const float CaretHeight = 16f;
        private const float BlinkSeconds = 0.5f;

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;

        private VisualElement _dim;
        private VisualElement _window;
        private Label _initial;
        private Label _title;
        private TextField _source;
        private Label _status;
        private VisualElement _statusDot;
        private VisualElement _hands;
        private string _handsKey;
        private EmployeeWorker _shown;
        private Button _tasksTab;
        private Button _luaTab;
        private VisualElement _tasksPage;
        private VisualElement _luaPage;
        private VisualElement _banner;
        private ScrollView _list;
        private Button _add;
        private bool _luaShown;
        private EmployeeTaskList _draft = new();
        // The task whose item search box gets focus once the list is rebuilt (after adding an item from it); -1 for none.
        private int _focusSearch = -1;
        // The Lua text box's caret and selection end, kept while it has focus so a world pick inserts where the player was typing.
        private int _caret;
        private int _anchor;
        private VisualElement _caretBar;
        // Caret index at the last blink restart, and when that was: the bar stays lit while the caret moves.
        private int _blinkIndex = -1;
        private float _blinkStart;

        public VisualElement Window => _window;
        public TextField SourceField => _source;
        public bool LuaShown => _luaShown;
        // This client's draft task list; change it through the panel's methods so the Lua follows.
        public EmployeeTaskList Draft => _draft;
        // True while the Lua draft is not what the task list generates (edited by hand).
        public bool Detached => _source != null && (_source.value ?? "") != _draft.ToLua();
        public ScrollView TaskList => _list;
        public VisualElement Hands => _hands;

        private void Start()
        {
            var root = document.rootVisualElement;
            root.Clear();
            UiTemplates.Fill(root, Templates + "EmployeeScript");
            _dim = root.Part("employee-script-dim");
            _window = root.Part("employee-script");
            _initial = root.Part<Label>("employee-script-initial");
            _title = root.Part<Label>("employee-script-title");
            _statusDot = root.Part("employee-script-dot");
            _status = root.Part<Label>("employee-script-status");
            root.Part<Button>("employee-script-close").clicked += interaction.CloseScreen;
            _hands = root.Part("employee-hands");
            _tasksTab = root.Part<Button>("employee-script-tab-tasks");
            _luaTab = root.Part<Button>("employee-script-tab-lua");
            _tasksTab.clicked += () => ShowLua(false);
            _luaTab.clicked += () => ShowLua(true);
            _tasksPage = root.Part("employee-script-tasks");
            _banner = root.Part("employee-script-detached");
            root.Part<Button>("employee-script-reset").clicked += ResetToTasks;
            _list = root.Part<ScrollView>("employee-script-task-list");
            _add = root.Part<Button>("employee-script-add-task");
            _add.clicked += AddTask;
            _luaPage = root.Part("employee-script-lua");
            root.Part<Button>("employee-script-pick").clicked += ClickSelectWorldPos;
            _source = root.Part<TextField>("employee-script-source");
            PrepareTextBox(_source);
            _caretBar = CaretBar(_source);
            _source.RegisterValueChangedCallback(_ => RefreshDetached());
            root.Part<Button>("employee-script-stop").clicked += ClickStop;
            root.Part<Button>("employee-script-run").clicked += ClickRun;
            _window.Show(false);
            _dim.Show(false);
            ShowLua(false);
        }

        private void Update()
        {
            if (_window == null) return;
            // Picking in the world hides the screen without closing it, so the drafts are kept.
            var employee = interaction.Screen is InteractionScreen.Employee or InteractionScreen.PickPosition ? interaction.OpenEmployee : null;
            var visible = employee != null && interaction.Screen == InteractionScreen.Employee;
            _window.Show(visible);
            _dim.Show(visible);
            // A hidden text box keeps keyboard focus, so it would swallow E/WASD typed after closing, while picking or on the
            // other tab.
            if (!visible && _window.focusController?.focusedElement is VisualElement focused && _window.Contains(focused)) focused.Blur();
            if (_luaShown == false && _source.focusController?.focusedElement == _source) _source.Blur();
            if (employee != _shown)
            {
                _shown = employee;
                if (employee == null) return;
                _title.text = employee.DisplayName;
                _initial.text = string.IsNullOrEmpty(employee.DisplayName) ? "?" : employee.DisplayName.Substring(0, 1).ToUpperInvariant();
                _handsKey = null;
                _draft = EmployeeTaskList.FromJson(employee.Tasks);
                // A script that does not match its saved list (or one saved before task lists existed) opens detached.
                _source.value = string.IsNullOrEmpty(employee.Source) ? _draft.ToLua() : employee.Source;
                _focusSearch = -1;
                RebuildTasks();
                // Not focused on opening: the player clicks into a text box to type. Focusing one here would also catch the
                // E press that opened the screen, whose typed "e" reaches UI Toolkit after the Input System action fired.
                ShowLua(false);
            }
            if (employee == null) return;
            if (_source.focusController?.focusedElement == _source)
            {
                _caret = _source.cursorIndex;
                _anchor = _source.selectIndex;
            }
            RefreshHands();
            UpdateCaret(_source, _caretBar);
            var status = employee.Status ?? "";
            _status.SetText($"{(status.Length == 0 ? "Idle" : status)}{(employee.Carrying ? "  ·  carrying a box" : "")}");
            var failing = status.StartsWith("Error") || employee.Unpaid;
            _window.EnableInClassList("emp--failing", failing);
            _statusDot.EnableInClassList("ff-dot--danger", failing);
            _statusDot.EnableInClassList("ff-dot--positive", !failing && status.Length > 0);
        }

        // Sends the Lua draft to the server with the task list it came from. Public so tests drive the same path as the button.
        public void ClickRun()
        {
            if (_shown != null) _shown.RequestRun(_source.value, _draft.ToJson());
        }

        public void ClickStop()
        {
            if (_shown != null) _shown.RequestStop();
        }

        // Switches between the Tasks tab (false) and the Lua source tab (true).
        public void ShowLua(bool lua)
        {
            _luaShown = lua;
            _tasksPage.Show(!lua);
            _luaPage.Show(lua);
            _tasksTab.EnableInClassList("emp__tab--showing", !lua);
            _luaTab.EnableInClassList("emp__tab--showing", lua);
            _caret = _anchor = _source.value?.Length ?? 0;
            RefreshDetached();
        }

        // ---- Task list ----

        // Adds a "Move stuff from A to B" task at the end. Public so tests drive the same path as the + button.
        public void AddTask()
        {
            if (Detached) return;
            _draft.Tasks.Add(new EmployeeTask());
            Changed();
            _list.schedule.Execute(() => _list.scrollOffset = new Vector2(0f, float.MaxValue));
        }

        public void RemoveTask(int index)
        {
            if (Detached || index < 0 || index >= _draft.Tasks.Count) return;
            _draft.Tasks.RemoveAt(index);
            Changed();
        }

        public void SetTaskType(int index, EmployeeTaskType type)
        {
            if (Detached || index < 0 || index >= _draft.Tasks.Count || _draft.Tasks[index].Type == type) return;
            var task = _draft.Tasks[index];
            task.Type = type;
            // A machine picked for one kind of task may not suit another (decision 0043), so it is picked again.
            task.Machine = "";
            task.Recipe = "";
            Changed();
        }

        // The recipe a Work or Recipe task uses (decision 0043): a recipe ID, or empty for the station's setting / automatic.
        public void SetTaskRecipe(int index, string recipeId)
        {
            if (Detached || index < 0 || index >= _draft.Tasks.Count) return;
            _draft.Tasks[index].Recipe = recipeId ?? "";
            Changed();
        }

        // On, off and toggle are exclusive: one of them is always chosen.
        public void SetTaskPower(int index, EmployeeTaskPower power)
        {
            if (Detached || index < 0 || index >= _draft.Tasks.Count) return;
            _draft.Tasks[index].Power = power;
            Changed();
        }

        public void SetAnyItem(int index, bool any)
        {
            if (Detached || index < 0 || index >= _draft.Tasks.Count) return;
            _draft.Tasks[index].AnyItem = any;
            Changed();
        }

        public void SetWhitelist(int index, bool whitelist)
        {
            if (Detached || index < 0 || index >= _draft.Tasks.Count) return;
            _draft.Tasks[index].Whitelist = whitelist;
            Changed();
        }

        public void AddItem(int index, string itemId)
        {
            if (Detached || index < 0 || index >= _draft.Tasks.Count || string.IsNullOrEmpty(itemId)) return;
            var items = _draft.Tasks[index].Items;
            if (!items.Contains(itemId)) items.Add(itemId);
            _focusSearch = index;
            Changed();
        }

        public void RemoveItem(int index, string itemId)
        {
            if (Detached || index < 0 || index >= _draft.Tasks.Count) return;
            _draft.Tasks[index].Items.Remove(itemId);
            Changed();
        }

        // Starts a world pick for a task's source, destination or machine; the picked place is set when the player confirms.
        public void ClickPick(int index, PickTarget target)
        {
            if (Detached || index < 0 || index >= _draft.Tasks.Count) return;
            var task = _draft.Tasks[index];
            var draft = _draft;
            interaction.BeginTargetPick(target, place => SetPlace(draft, task, target, place));
        }

        // Sets a picked place on a task, if the screen still shows the list it came from.
        public void SetPlace(EmployeeTaskList draft, EmployeeTask task, PickTarget target, string place)
        {
            if (draft != _draft || !_draft.Tasks.Contains(task) || Detached) return;
            switch (target)
            {
                case PickTarget.Source:
                    task.Source = place ?? "";
                    break;
                case PickTarget.Destination:
                    task.Destination = place ?? "";
                    break;
                case PickTarget.Machine:
                    task.Machine = place ?? "";
                    break;
                case PickTarget.Station:
                case PickTarget.RecipeMachine:
                    // Another machine kind has other recipes.
                    if (MachineKind(place) != MachineKind(task.Machine)) task.Recipe = "";
                    task.Machine = place ?? "";
                    break;
            }
            Changed();
        }

        // Regenerates the Lua from the list (which is never changed while detached) and redraws it.
        private void Changed()
        {
            _source.value = _draft.ToLua();
            RebuildTasks();
        }

        // Throws the hand-edited Lua away for what the task list generates.
        public void ResetToTasks()
        {
            _source.value = _draft.ToLua();
            RefreshDetached();
        }

        private void RefreshDetached()
        {
            if (_banner == null) return;
            var detached = Detached;
            _banner.Show(detached);
            _list.SetEnabled(!detached);
            _add.SetEnabled(!detached);
        }

        private void RebuildTasks()
        {
            var scroll = _list.scrollOffset;
            _list.Clear();
            if (_draft.Tasks.Count == 0)
            {
                var empty = new Label("No tasks yet. Press + to add one; the employee repeats its tasks in order, forever.");
                empty.AddToClassList("emp__empty");
                _list.Add(empty);
            }
            for (var index = 0; index < _draft.Tasks.Count; index++) _list.Add(TaskCard(index, _draft.Tasks[index]));
            _list.schedule.Execute(() => _list.scrollOffset = scroll);
            RefreshDetached();
            var focus = _focusSearch;
            _focusSearch = -1;
            if (focus >= 0)
                _list.schedule.Execute(() => _list.Q<TextField>($"employee-task-{focus}-search")?.Focus());
        }

        private VisualElement TaskCard(int index, EmployeeTask task)
        {
            var card = UiTemplates.Clone(Templates + "EmployeeTaskCard");
            card.name = $"employee-task-{index}";
            card.Part<Label>("number").text = $"{index + 1}";
            var type = card.Part<DropdownField>("type");
            type.name = $"employee-task-{index}-type";
            var labels = new Dictionary<EmployeeTaskType, string>
            {
                [EmployeeTaskType.Move] = EmployeeTaskList.MoveLabel, [EmployeeTaskType.Power] = EmployeeTaskList.PowerLabel,
                [EmployeeTaskType.Work] = EmployeeTaskList.WorkLabel, [EmployeeTaskType.Recipe] = EmployeeTaskList.RecipeLabel
            };
            type.choices = labels.Values.ToList();
            type.SetValueWithoutNotify(labels[task.Type]);
            type.RegisterValueChangedCallback(evt => SetTaskType(index, labels.First(x => x.Value == evt.newValue).Key));
            var remove = card.Part<Button>("remove");
            remove.name = $"employee-task-{index}-remove";
            remove.clicked += () => RemoveTask(index);
            if (task.Type != EmployeeTaskType.Move) card.Part("move").RemoveFromHierarchy();
            if (task.Type != EmployeeTaskType.Power) card.Part("power").RemoveFromHierarchy();
            if (task.Type is not (EmployeeTaskType.Work or EmployeeTaskType.Recipe)) card.Part("recipe").RemoveFromHierarchy();
            switch (task.Type)
            {
                case EmployeeTaskType.Power:
                    PowerBody(card, index, task);
                    break;
                case EmployeeTaskType.Work:
                case EmployeeTaskType.Recipe:
                    RecipeBody(card, index, task);
                    break;
                default:
                    MoveBody(card, index, task);
                    break;
            }
            return card;
        }

        private void MoveBody(VisualElement card, int index, EmployeeTask task)
        {
            FillPlace(card.Part("source"), index, "From", "Set source", PickTarget.Source, task.Source);
            FillPlace(card.Part("destination"), index, "To", "Set destination", PickTarget.Destination, task.Destination);
            Check(card.Part<Toggle>("any"), $"employee-task-{index}-any", task.AnyItem, value => SetAnyItem(index, value));
            var filter = card.Part("filter");
            if (task.AnyItem)
            {
                filter.RemoveFromHierarchy();
                return;
            }
            Check(card.Part<Toggle>("whitelist"), $"employee-task-{index}-whitelist", task.Whitelist, value => SetWhitelist(index, value));
            var chips = card.Part("items");
            chips.name = $"employee-task-{index}-items";
            var none = chips.Part<Label>("none");
            none.text = task.Whitelist ? "No items yet: nothing will be moved." : "No items yet: any valid item is moved.";
            none.Show(task.Items.Count == 0);
            var search = chips.Part<TextField>("search");
            foreach (var itemId in task.Items)
            {
                var chip = UiTemplates.Clone(Templates + "EmployeeChip");
                chip.Part("icon").SetIcon(ItemIcon(itemId), "");
                chip.Part("icon").Show(ItemIcon(itemId) != null);
                chip.Part<Label>("title").text = ItemName(itemId);
                var id = itemId;
                var x = chip.Part<Button>("remove");
                x.name = $"employee-task-{index}-item-{itemId}-remove";
                x.clicked += () => RemoveItem(index, id);
                chips.Insert(chips.IndexOf(search), chip);
            }

            search.name = $"employee-task-{index}-search";
            PrepareTextBox(search);
            search.textEdition.placeholder = task.Whitelist ? "Add an item to allow…" : "Add an item to never move…";
            var suggestions = card.Part("suggestions");
            suggestions.name = $"employee-task-{index}-suggestions";
            search.RegisterValueChangedCallback(evt => Suggest(suggestions, index, task, evt.newValue));
            // The drop-down opens with focus and closes a moment after it leaves, so a click on a suggestion (which takes focus
            // away from the box) still lands on it.
            search.RegisterCallback<FocusInEvent>(_ =>
            {
                Suggest(suggestions, index, task, search.value);
                // Once laid out, the task list scrolls so the whole drop-down shows.
                _list.schedule.Execute(() =>
                {
                    if (suggestions.resolvedStyle.display == DisplayStyle.Flex) _list.ScrollTo(suggestions);
                }).ExecuteLater(30);
            });
            search.RegisterCallback<FocusOutEvent>(_ => suggestions.schedule.Execute(() =>
            {
                if (search.focusController?.focusedElement != search) suggestions.RemoveFromClassList("emp-task__suggestions--open");
            }).ExecuteLater(200));
            // Enter adds the first suggestion.
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter)) return;
                var first = Matches(task, search.value).FirstOrDefault();
                if (first != null) AddItem(index, first.Id);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
        }

        // Fills the drop-down under an item search box: the recent items with no text, else those whose name or ID contains it.
        private void Suggest(VisualElement suggestions, int index, EmployeeTask task, string text)
        {
            suggestions.Clear();
            var matches = Matches(task, text).Take(MaxSuggestions).ToList();
            suggestions.EnableInClassList("emp-task__suggestions--open", matches.Count > 0);
            foreach (var item in matches)
            {
                var id = item.Id;
                var option = UiTemplates.Clone(Templates + "EmployeeSuggestion");
                option.name = $"employee-task-{index}-suggest-{id}";
                option.Part("icon").SetIcon(item.Icon, "");
                option.Part("icon").Show(item.Icon != null);
                option.Part<Label>("title").text = item.DisplayName;
                option.RegisterCallback<ClickEvent>(_ => AddItem(index, id));
                suggestions.Add(option);
            }
        }

        // Catalog items not already listed. With no text: the items this player touched most recently first, then the rest by
        // name. With text: those whose display name or ID contains it, those starting with it first.
        public IEnumerable<ItemDefinition> Matches(EmployeeTask task, string text)
        {
            text = (text ?? "").Trim();
            var items = interaction.Session.Items.Where(x => x != null && !task.Items.Contains(x.Id));
            if (text.Length == 0)
            {
                return items.OrderBy(x => interaction.RecentItemRank(x.Id)).ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase);
            }
            return items.Where(x => x.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || x.Id.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(x => x.DisplayName.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        private void PowerBody(VisualElement card, int index, EmployeeTask task)
        {
            FillPlace(card.Part("machine"), index, "Machine", "Select machine", PickTarget.Machine, task.Machine);
            var toggling = task.Power == EmployeeTaskPower.Toggle;
            var on = Check(card.Part<Toggle>("on"), $"employee-task-{index}-on", task.Power == EmployeeTaskPower.On,
                value => SetTaskPower(index, value ? EmployeeTaskPower.On : task.Power));
            var off = Check(card.Part<Toggle>("off"), $"employee-task-{index}-off", task.Power == EmployeeTaskPower.Off,
                value => SetTaskPower(index, value ? EmployeeTaskPower.Off : task.Power));
            Check(card.Part<Toggle>("toggle"), $"employee-task-{index}-toggle", toggling,
                value => SetTaskPower(index, value ? EmployeeTaskPower.Toggle : EmployeeTaskPower.On));
            // Toggle greys the other two out; unchecking the chosen one of those leaves it chosen.
            on.SetEnabled(!toggling);
            off.SetEnabled(!toggling);
        }

        // A Work task (a worker-bound station and the recipe to make there) or a Recipe task (a machine and what to set it to),
        // decision 0043. The chooser lists the picked machine's recipes; empty means the station's own setting or automatic.
        private void RecipeBody(VisualElement card, int index, EmployeeTask task)
        {
            var work = task.Type == EmployeeTaskType.Work;
            FillPlace(card.Part("station"), index, work ? "Counter" : "Machine", work ? "Select counter" : "Select machine",
                work ? PickTarget.Station : PickTarget.RecipeMachine, task.Machine);
            var kind = MachineKind(task.Machine);
            var recipes = kind == null ? new List<RecipeAsset>()
                : interaction.Session.Recipes.Where(x => x != null && !x.IsSale && x.StationKind == kind).ToList();
            var none = work ? "Whatever it is set to" : "Automatic (first ready recipe)";
            var names = recipes.ToDictionary(x => x.Id, x => ItemName(x.OutputItemId) + (x.OutputQuantity > 1 ? $" ×{x.OutputQuantity}" : ""));
            card.Part<Label>("recipe-caption").text = work ? "Make" : "Set to";
            var choice = card.Part<DropdownField>("recipe-choice");
            choice.name = $"employee-task-{index}-recipe";
            choice.choices = new List<string> { none }.Concat(recipes.Select(x => names[x.Id])).ToList();
            choice.SetValueWithoutNotify(names.TryGetValue(task.Recipe ?? "", out var current) ? current : none);
            choice.RegisterValueChangedCallback(evt => SetTaskRecipe(index, names.FirstOrDefault(x => x.Value == evt.newValue).Key ?? ""));
            choice.SetEnabled(kind != null);
        }

        // The kind of the placed machine a task names, or null.
        private string MachineKind(string machineId) => string.IsNullOrEmpty(machineId) ? null
            : interaction.Session.ClientSite?.Equipment.FirstOrDefault(x => x.Id == machineId)?.Kind;

        // A picked place in a box (EmployeePlace.uxml): its machine's icon, a caption, the place's name and the pick button.
        private void FillPlace(VisualElement box, int index, string caption, string text, PickTarget target, string place)
        {
            var name = target switch { PickTarget.Source => "source", PickTarget.Destination => "destination", _ => "machine" };
            var sprite = PlaceIcon(place);
            var icon = box.Part("icon");
            icon.SetIcon(sprite, "");
            icon.EnableInClassList("ff-icon--empty", sprite == null);
            box.Part<Label>("caption").text = caption;
            var value = box.Part<Label>("value");
            value.name = $"employee-task-{index}-{name}-label";
            value.text = string.IsNullOrEmpty(place) ? "Not set" : PlaceName(place);
            box.PartByClass<VisualElement>("emp-place").EnableInClassList("emp-place--unset", string.IsNullOrEmpty(place));
            var button = box.Part<Button>("pick");
            button.name = $"employee-task-{index}-{name}";
            button.text = text;
            button.clicked += () => ClickPick(index, target);
        }

        // The icon of the machine a place names; null for the storage or an unknown place.
        private Sprite PlaceIcon(string place)
        {
            if (string.IsNullOrEmpty(place)) return null;
            var colon = place.LastIndexOf(':');
            var id = colon > 0 && (place.EndsWith(":in", StringComparison.Ordinal) || place.EndsWith(":out", StringComparison.Ordinal))
                ? place.Substring(0, colon) : place;
            var equipment = interaction.Session.ClientSite?.Equipment.FirstOrDefault(x => x.Id == id);
            return equipment == null ? null
                : interaction.Session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == equipment.Kind)?.Icon;
        }

        private Sprite ItemIcon(string itemId) => interaction.Session.Items.FirstOrDefault(x => x != null && x.Id == itemId)?.Icon;

        // "Storage", or a machine's display name with its ID and which of its buffers.
        private string PlaceName(string place)
        {
            var buffer = place.EndsWith(":in", StringComparison.Ordinal) ? " input" : place.EndsWith(":out", StringComparison.Ordinal) ? " output" : "";
            var id = buffer.Length > 0 ? place.Substring(0, place.LastIndexOf(':')) : place;
            var equipment = interaction.Session.ClientSite?.Equipment.FirstOrDefault(x => x.Id == id);
            if (equipment == null) return place == "storage" ? "Storage" : place;
            var definition = interaction.Session.EquipmentDefinitions.FirstOrDefault(x => x != null && x.Kind == equipment.Kind);
            var gone = equipment.State == EquipmentState.Placed ? "" : " (not placed)";
            return $"{definition?.DisplayName ?? equipment.Kind} {id}{buffer}{gone}";
        }

        private string ItemName(string itemId) =>
            interaction.Session.Items.FirstOrDefault(x => x != null && x.Id == itemId)?.DisplayName ?? itemId;

        // ---- Lua tab ----

        // Shows a text box's wide caret bar while it has focus and no selection, blinking but lit whenever the caret moves.
        // cursorPosition is in the text element's space with y at the bottom of the caret's line.
        private void UpdateCaret(TextField field, VisualElement bar)
        {
            if (bar == null) return;
            var focused = field.focusController?.focusedElement == field && field.cursorIndex == field.selectIndex;
            if (focused && field.cursorIndex != _blinkIndex)
            {
                _blinkIndex = field.cursorIndex;
                _blinkStart = Time.unscaledTime;
            }
            var lit = (int)((Time.unscaledTime - _blinkStart) / BlinkSeconds) % 2 == 0;
            bar.Show(focused && lit);
            if (!focused) return;
            var position = field.cursorPosition;
            bar.style.left = position.x - CaretWidth * 0.5f;
            bar.style.top = position.y - CaretHeight + 1f;
        }

        public void ClickSelectWorldPos() => interaction.BeginWorldPick(Insert);

        // Replaces the Lua text box's remembered selection (or inserts at its caret) with text and puts the caret after it.
        public void Insert(string text)
        {
            var value = _source.value ?? "";
            var start = Mathf.Clamp(Mathf.Min(_caret, _anchor), 0, value.Length);
            var end = Mathf.Clamp(Mathf.Max(_caret, _anchor), 0, value.Length);
            _source.value = value.Substring(0, start) + text + value.Substring(end);
            _caret = _anchor = start + text.Length;
            var caret = _caret;
            _source.schedule.Execute(() =>
            {
                _source.Focus();
                _source.SelectRange(caret, caret);
            });
        }

        // One small square per hand slot showing what the employee holds (item and count); rebuilt only when that changes.
        private void RefreshHands()
        {
            var site = interaction.Session.ClientSite;
            var handsId = GoodsWorld.InventoryLocationId(_shown.EmployeeId);
            var capacity = site?.Locations.FirstOrDefault(x => x.Id == handsId)?.Capacity ?? 0;
            var stacks = site?.Lots.Where(x => x.LocationId == handsId).GroupBy(x => (x.ItemId, x.Spoiled))
                .OrderBy(x => x.Key.ItemId, StringComparer.Ordinal).ThenBy(x => x.Key.Spoiled)
                .Select(x => (x.Key.ItemId, x.Key.Spoiled, Count: x.Sum(y => y.Quantity))).ToList() ?? new();
            var key = capacity + "|" + string.Join(",", stacks.Select(x => $"{x.ItemId}:{x.Spoiled}:{x.Count}"));
            if (key == _handsKey) return;
            _handsKey = key;
            _hands.Clear();
            var caption = new Label("HANDS") { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("ff-eyebrow");
            caption.AddToClassList("emp__hands-caption");
            _hands.Add(caption);
            for (var index = 0; index < Math.Max(capacity, stacks.Count); index++)
            {
                var stack = index < stacks.Count ? stacks[index] : default;
                var full = index < stacks.Count;
                var slot = (Button)UiTemplates.Clone(Templates + "EmployeeHand");
                slot.name = $"employee-hand-{index}";
                slot.EnableInClassList("ff-slot--filled", full);
                slot.EnableInClassList("ff-slot--spoiled", full && stack.Spoiled);
                var icon = slot.Part("icon");
                var count = slot.Part<Label>("count");
                icon.Show(full);
                count.Show(full);
                if (full)
                {
                    slot.clicked += () => ClickHand(stack.ItemId, stack.Spoiled);
                    var item = interaction.Session.Items.FirstOrDefault(x => x != null && x.Id == stack.ItemId);
                    icon.SetIcon(item?.Icon, item?.DisplayName ?? stack.ItemId);
                    count.text = stack.Count.ToString();
                    slot.tooltip = $"{stack.Count} {item?.DisplayName ?? stack.ItemId}{(stack.Spoiled ? " (spoiled)" : "")}";
                }
                _hands.Add(slot);
            }
        }

        // Takes one of the employee's hand stacks into the player's inventory, as much as fits, like clicking its square. Public
        // so tests drive the same path.
        public void ClickHand(string itemId, bool spoiled)
        {
            var site = interaction.Session.ClientSite;
            if (_shown == null || site == null || interaction.InventoryId == null) return;
            var handsId = GoodsWorld.InventoryLocationId(_shown.EmployeeId);
            var count = site.Lots.Where(x => x.LocationId == handsId && x.ItemId == itemId && x.Spoiled == spoiled).Sum(x => x.Quantity);
            if (count > 0) interaction.TransferStack(handsId, itemId, spoiled, count, interaction.InventoryId);
        }

        // ---- Building blocks ----

        // The Lua text box's wide caret bar, since UI Toolkit draws its own caret 1 px wide. It sits beside the text element in the
        // text box's scroll view content, never inside it: a text element with a child is no longer measured, so it would stay
        // the viewport's height and nothing would scroll. Both share the content's origin, so cursorPosition places the bar.
        private static VisualElement CaretBar(TextField field)
        {
            var bar = new VisualElement { name = field.name + "-caret", pickingMode = PickingMode.Ignore };
            bar.AddToClassList("emp__caret");
            bar.Show(false);
            field.Q<ScrollView>()?.contentContainer.Add(bar);
            return bar;
        }

        // A text box from the UXML made safe for this screen: no select-all on focus (a world pick inserts at the caret and must
        // never replace the whole text), never taking the click that opened the screen, and marking typing so E is text, not
        // a close.
        private void PrepareTextBox(TextField field)
        {
            field.selectAllOnFocus = false;
            field.selectAllOnMouseUp = false;
            // The left click that opened the screen lands on a text box under the freed pointer; that press must not focus it.
            field.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (interaction.ScreenClicksArmed) return;
                field.focusController?.IgnoreEvent(evt);
                evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
            // Tracked on the focus events themselves, so an E typed right after a click is already seen as text.
            field.RegisterCallback<FocusInEvent>(_ => interaction.ScriptTextFocused = true);
            field.RegisterCallback<FocusOutEvent>(_ => interaction.ScriptTextFocused = false);
        }

        // A checkbox from the UXML, named and set, that reports the player's clicks. It is never focused (a focused one would flip
        // again on keyboard Submit).
        private static Toggle Check(Toggle toggle, string name, bool value, Action<bool> changed)
        {
            toggle.name = name;
            toggle.focusable = false;
            toggle.SetValueWithoutNotify(value);
            toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return toggle;
        }
    }
}
