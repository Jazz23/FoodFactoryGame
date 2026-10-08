// Local player's employee screen in UI Toolkit, built in code (decision 0037). The Tasks tab (shown first) is a visual task
// list: + adds a task, each a "Move stuff from A to B" (a source, a destination and which items) or a "Turn machine on" (a
// machine and on, off or toggle). "Set source", "Set destination" and "Select machine" hide the screen while the player
// picks one of the pulsing valid targets in the world (EquipmentInteraction.BeginTargetPick). The list generates the Lua the
// employee actually runs (EmployeeTaskList.ToLua), shown in the Lua source tab. Editing that Lua by hand detaches it from the
// list: the Tasks tab then locks behind a banner until "Reset to tasks" regenerates the Lua. Run sends the Lua and the list
// (saved together), Stop halts it; both are requests the server checks (EmployeeWorker). Presentation only: the list and
// the Lua are this client's draft until Run. The script assistant tab is disabled (its code is kept, unused).
// It opens unfocused, whether by E or by the left click, which is ignored by every text box until released. E closes the
// screen unless a text box has focus, where it is typed.
// On the Lua tab, "Select world pos" inserts a cell's or machine's Lua text at the text box's caret (replacing any selection);
// the draft survives because the screen stays open while hidden. "Give" buttons hand the employee one of each machine kind
// the player holds, for its script's place().
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
        private const string Reference =
            "move_to(place)  ·  path(place, place, ...)  ·  take(place, items, amount)  ·  put(place, items, amount)  ·  wait(seconds)\n" +
            "turn_on(machine)  ·  turn_off(machine)  ·  toggle(machine)  ·  is_on(machine)  ·  accepts(place, except)  ·  room(place, items)\n" +
            "place(machine, cell, rotation)  ·  pick_up(machine)  ·  place_belt(cell, direction)  ·  holding(kind)  ·  position()\n" +
            "count(place, items)  ·  find(kind)  ·  carrying()  ·  say(text) / print(text)\n" +
            "place: \"storage\", a machine kind (\"oven\", \"fridge\", \"counter\"), a machine ID, \"<id>:in\" / \"<id>:out\", or a cell " +
            "{x, z} to walk onto (Select world pos inserts one). items: an item, a list {\"dough\"}, {except = {...}} or nil for any. " +
            "take() and put() walk there first; amount may be left out.";

        private const string DetachedHint =
            "The Lua was edited by hand, so these tasks are not what runs. Reset to go back to the tasks (the hand edits are lost).";

        // Suggestions shown under an item search box.
        private const int MaxSuggestions = 6;

        private static readonly Color Backdrop = new(0.19f, 0.19f, 0.2f, 0.97f);
        private static readonly Color Inset = new(0.11f, 0.11f, 0.12f, 1f);
        private static readonly Color Card = new(0.24f, 0.24f, 0.26f, 1f);
        private static readonly Color Heading = new(1f, 0.9f, 0.74f, 1f);
        private static readonly Color Muted = new(0.7f, 0.7f, 0.75f, 1f);
        private static readonly Color Error = new(0.95f, 0.45f, 0.4f, 1f);
        private static readonly Color Warning = new(0.45f, 0.3f, 0.12f, 1f);
        private static readonly Color Selection = new(0.35f, 0.55f, 1f, 0.45f);
        private static readonly Color TabActive = new(0.85f, 0.85f, 0.87f, 1f);
        private static readonly Color TabIdle = new(0.26f, 0.26f, 0.28f, 1f);
        // Both tabs' pages are this tall, so switching tabs never resizes the screen.
        private const float PageHeight = 360f;
        // The caret bar's size in pixels: the text box's 13 px font has lines about 14.5 px tall.
        private const float CaretWidth = 3f;
        private const float CaretHeight = 16f;
        private const float BlinkSeconds = 0.5f;

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;

        private VisualElement _window;
        private Label _title;
        private TextField _source;
        private Label _status;
        private VisualElement _give;
        private string _giveKinds;
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

        private void Start()
        {
            var root = document.rootVisualElement;
            root.Clear();
            _window = new VisualElement { name = "employee-script" };
            _window.style.position = Position.Absolute;
            _window.style.left = new Length(50, LengthUnit.Percent);
            _window.style.top = new Length(50, LengthUnit.Percent);
            _window.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            _window.style.width = 660;
            _window.style.backgroundColor = Backdrop;
            _window.style.paddingLeft = _window.style.paddingRight = 12;
            _window.style.paddingTop = _window.style.paddingBottom = 10;
            Round(_window, 4);

            _title = Caption("", 16, Heading);
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _window.Add(_title);

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginTop = 6;
            _tasksTab = Button("employee-script-tab-tasks", "Tasks", () => ShowLua(false));
            _luaTab = Button("employee-script-tab-lua", "Lua source", () => ShowLua(true));
            tabs.Add(_tasksTab);
            tabs.Add(_luaTab);
            _window.Add(tabs);

            _tasksPage = new VisualElement { name = "employee-script-tasks" };
            _tasksPage.style.height = PageHeight;
            _tasksPage.style.marginTop = 6;
            _banner = new VisualElement { name = "employee-script-detached" };
            _banner.style.flexDirection = FlexDirection.Row;
            _banner.style.alignItems = Align.Center;
            _banner.style.backgroundColor = Warning;
            _banner.style.paddingLeft = _banner.style.paddingRight = 8;
            _banner.style.paddingTop = _banner.style.paddingBottom = 4;
            _banner.style.marginBottom = 6;
            Round(_banner, 3);
            var hint = Caption(DetachedHint, 12, Color.white);
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.flexShrink = 1;
            hint.style.flexGrow = 1;
            _banner.Add(hint);
            _banner.Add(Button("employee-script-reset", "Reset to tasks", ResetToTasks));
            _tasksPage.Add(_banner);
            _list = new ScrollView(ScrollViewMode.Vertical) { name = "employee-script-task-list" };
            _list.style.flexGrow = 1;
            _list.style.backgroundColor = Inset;
            _list.style.paddingLeft = _list.style.paddingRight = 6;
            _list.style.paddingTop = _list.style.paddingBottom = 6;
            _tasksPage.Add(_list);
            _add = Button("employee-script-add-task", "+  Add task", AddTask);
            _add.style.marginTop = 6;
            _add.style.alignSelf = Align.FlexStart;
            _tasksPage.Add(_add);
            _window.Add(_tasksPage);

            _luaPage = new VisualElement { name = "employee-script-lua" };
            _luaPage.style.marginTop = 6;
            _luaPage.style.height = PageHeight;
            _source = Editor("employee-script-source", 0f, out _caretBar);
            _source.style.flexGrow = 1;
            // The box keeps the page's height and scrolls its text instead of growing; the text box scrolls the caret into view
            // as it moves.
            _source.style.flexShrink = 1;
            _source.style.minHeight = 0;
            _source.verticalScrollerVisibility = ScrollerVisibility.Auto;
            _source.RegisterValueChangedCallback(_ => RefreshDetached());
            _luaPage.Add(_source);
            var pick = Button("employee-script-pick", "Select world pos", ClickSelectWorldPos);
            pick.style.minWidth = 130;
            pick.style.marginTop = 6;
            pick.style.alignSelf = Align.FlexStart;
            _luaPage.Add(pick);
            _window.Add(_luaPage);

            _status = Caption("", 12, Muted);
            _status.name = "employee-script-status";
            _status.style.marginTop = 6;
            _status.style.whiteSpace = WhiteSpace.Normal;
            _window.Add(_status);

            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.marginTop = 6;
            buttons.Add(Button("employee-script-run", "Run", ClickRun));
            buttons.Add(Button("employee-script-stop", "Stop", ClickStop));
            buttons.Add(Button("employee-script-close", "Close", interaction.CloseScreen));
            _window.Add(buttons);

            _give = new VisualElement { name = "employee-script-give" };
            _give.style.flexDirection = FlexDirection.Row;
            _give.style.flexWrap = Wrap.Wrap;
            _give.style.alignItems = Align.Center;
            _give.style.marginTop = 6;
            _window.Add(_give);

            var reference = Caption(Reference, 11, Muted);
            reference.name = "employee-script-reference";
            reference.style.marginTop = 8;
            reference.style.whiteSpace = WhiteSpace.Normal;
            _luaPage.Add(reference);

            root.Add(_window);
            _window.style.display = DisplayStyle.None;
            ShowLua(false);
        }

        private void Update()
        {
            if (_window == null) return;
            // Picking in the world hides the screen without closing it, so the drafts are kept.
            var employee = interaction.Screen is InteractionScreen.Employee or InteractionScreen.PickPosition ? interaction.OpenEmployee : null;
            var visible = employee != null && interaction.Screen == InteractionScreen.Employee;
            _window.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            // A hidden text box keeps keyboard focus, so it would swallow E/WASD typed after closing, while picking or on the
            // other tab.
            if (!visible && _window.focusController?.focusedElement is VisualElement focused && _window.Contains(focused)) focused.Blur();
            if (_luaShown == false && _source.focusController?.focusedElement == _source) _source.Blur();
            if (employee != _shown)
            {
                _shown = employee;
                if (employee == null) return;
                _title.text = $"{employee.DisplayName} ({employee.EmployeeId})";
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
            RefreshGive();
            UpdateCaret(_source, _caretBar);
            var status = employee.Status ?? "";
            _status.text = $"Status: {status}{(employee.Carrying ? "  (carrying a box)" : "")}";
            _status.style.color = status.StartsWith("Error") ? Error : Muted;
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
            _tasksPage.style.display = lua ? DisplayStyle.None : DisplayStyle.Flex;
            _luaPage.style.display = lua ? DisplayStyle.Flex : DisplayStyle.None;
            StyleTab(_tasksTab, !lua);
            StyleTab(_luaTab, lua);
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
            _draft.Tasks[index].Type = type;
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
            _banner.style.display = detached ? DisplayStyle.Flex : DisplayStyle.None;
            _list.SetEnabled(!detached);
            _add.SetEnabled(!detached);
        }

        private void RebuildTasks()
        {
            var scroll = _list.scrollOffset;
            _list.Clear();
            if (_draft.Tasks.Count == 0)
            {
                var empty = Caption("No tasks yet. Press + to add one; the employee repeats its tasks in order, forever.", 12, Muted);
                empty.style.whiteSpace = WhiteSpace.Normal;
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
            var card = new VisualElement { name = $"employee-task-{index}" };
            card.style.backgroundColor = Card;
            card.style.marginBottom = 6;
            card.style.paddingLeft = card.style.paddingRight = 8;
            card.style.paddingTop = card.style.paddingBottom = 6;
            Round(card, 3);

            var header = Row();
            var number = Caption($"{index + 1}.", 13, Heading);
            number.style.unityFontStyleAndWeight = FontStyle.Bold;
            number.style.minWidth = 22;
            header.Add(number);
            var choices = new List<string> { EmployeeTaskList.MoveLabel, EmployeeTaskList.PowerLabel };
            var type = new DropdownField(choices, task.Type == EmployeeTaskType.Power ? 1 : 0) { name = $"employee-task-{index}-type" };
            type.style.minWidth = 220;
            type.RegisterValueChangedCallback(evt =>
                SetTaskType(index, evt.newValue == EmployeeTaskList.PowerLabel ? EmployeeTaskType.Power : EmployeeTaskType.Move));
            header.Add(type);
            var spacer = new VisualElement();
            spacer.style.flexGrow = 1;
            header.Add(spacer);
            var remove = Button($"employee-task-{index}-remove", "✕", () => RemoveTask(index));
            remove.style.minWidth = 28;
            header.Add(remove);
            card.Add(header);

            if (task.Type == EmployeeTaskType.Power) PowerBody(card, index, task);
            else MoveBody(card, index, task);
            return card;
        }

        private void MoveBody(VisualElement card, int index, EmployeeTask task)
        {
            card.Add(PlaceRow(index, "Set source", PickTarget.Source, task.Source));
            card.Add(PlaceRow(index, "Set destination", PickTarget.Destination, task.Destination));
            var any = Check($"employee-task-{index}-any", "Move any valid item (what the destination takes)", task.AnyItem,
                value => SetAnyItem(index, value));
            card.Add(any);
            if (task.AnyItem) return;

            var whitelist = Check($"employee-task-{index}-whitelist", "Whitelist (unchecked: blacklist)", task.Whitelist,
                value => SetWhitelist(index, value));
            card.Add(whitelist);
            var chips = new VisualElement { name = $"employee-task-{index}-items" };
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.marginTop = 2;
            if (task.Items.Count == 0)
                chips.Add(Caption(task.Whitelist ? "No items yet: nothing will be moved." : "No items yet: any valid item is moved.", 12, Muted));
            foreach (var itemId in task.Items)
            {
                var chip = Row();
                chip.style.backgroundColor = Inset;
                chip.style.marginRight = 4;
                chip.style.marginTop = 2;
                chip.style.paddingLeft = 6;
                Round(chip, 3);
                chip.Add(Caption(ItemName(itemId), 12, Color.white));
                var id = itemId;
                var x = Button($"employee-task-{index}-item-{itemId}-remove", "✕", () => RemoveItem(index, id));
                x.style.minWidth = 22;
                chip.Add(x);
                chips.Add(chip);
            }
            card.Add(chips);

            var search = TextBox($"employee-task-{index}-search");
            search.textEdition.placeholder = task.Whitelist ? "Search an item to allow..." : "Search an item to never move...";
            search.style.marginTop = 4;
            search.style.marginLeft = search.style.marginRight = 0;
            var suggestions = new VisualElement { name = $"employee-task-{index}-suggestions" };
            suggestions.style.backgroundColor = Inset;
            suggestions.style.display = DisplayStyle.None;
            search.RegisterValueChangedCallback(evt => Suggest(suggestions, index, task, evt.newValue));
            // Enter adds the first suggestion.
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter)) return;
                var first = Matches(task, search.value).FirstOrDefault();
                if (first != null) AddItem(index, first.Id);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            card.Add(search);
            card.Add(suggestions);
        }

        // Fills the drop-down under an item search box with the items whose name or ID contains the text.
        private void Suggest(VisualElement suggestions, int index, EmployeeTask task, string text)
        {
            suggestions.Clear();
            var matches = string.IsNullOrWhiteSpace(text) ? new List<ItemDefinition>() : Matches(task, text).Take(MaxSuggestions).ToList();
            suggestions.style.display = matches.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var item in matches)
            {
                var id = item.Id;
                var option = Button($"employee-task-{index}-suggest-{id}", item.DisplayName == id ? id : $"{item.DisplayName}  ({id})",
                    () => AddItem(index, id));
                option.style.unityTextAlign = TextAnchor.MiddleLeft;
                option.style.marginLeft = option.style.marginRight = 0;
                suggestions.Add(option);
            }
        }

        // Catalog items not already listed whose display name or ID contains the text, those starting with it first.
        public IEnumerable<ItemDefinition> Matches(EmployeeTask task, string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return Enumerable.Empty<ItemDefinition>();
            return interaction.Session.Items.Where(x => x != null && !task.Items.Contains(x.Id)
                    && (x.DisplayName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 || x.Id.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0))
                .OrderBy(x => x.DisplayName.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase);
        }

        private void PowerBody(VisualElement card, int index, EmployeeTask task)
        {
            card.Add(PlaceRow(index, "Select machine", PickTarget.Machine, task.Machine));
            var row = Row();
            row.style.marginTop = 2;
            var toggling = task.Power == EmployeeTaskPower.Toggle;
            var on = Check($"employee-task-{index}-on", "Turn machine on", task.Power == EmployeeTaskPower.On,
                value => SetTaskPower(index, value ? EmployeeTaskPower.On : task.Power));
            var off = Check($"employee-task-{index}-off", "Turn machine off", task.Power == EmployeeTaskPower.Off,
                value => SetTaskPower(index, value ? EmployeeTaskPower.Off : task.Power));
            var toggle = Check($"employee-task-{index}-toggle", "Toggle", toggling,
                value => SetTaskPower(index, value ? EmployeeTaskPower.Toggle : EmployeeTaskPower.On));
            // Toggle greys the other two out; unchecking the chosen one of those leaves it chosen.
            on.SetEnabled(!toggling);
            off.SetEnabled(!toggling);
            on.style.marginRight = off.style.marginRight = 16;
            row.Add(on);
            row.Add(off);
            row.Add(toggle);
            card.Add(row);
        }

        // A pick button and the place it picked.
        private VisualElement PlaceRow(int index, string text, PickTarget target, string place)
        {
            var row = Row();
            row.style.marginTop = 4;
            var name = target switch { PickTarget.Source => "source", PickTarget.Destination => "destination", _ => "machine" };
            var button = Button($"employee-task-{index}-{name}", text, () => ClickPick(index, target));
            button.style.minWidth = 120;
            row.Add(button);
            var label = Caption(string.IsNullOrEmpty(place) ? "(not set)" : PlaceName(place), 12,
                string.IsNullOrEmpty(place) ? Muted : Color.white);
            label.name = $"employee-task-{index}-{name}-label";
            label.style.marginLeft = 6;
            row.Add(label);
            return row;
        }

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

        // The showing tab is light with dark text, like the other buttons; the hidden one is dark with light text.
        private static void StyleTab(Button tab, bool showing)
        {
            tab.style.backgroundColor = showing ? TabActive : TabIdle;
            tab.style.color = showing ? Color.black : Muted;
            tab.style.unityFontStyleAndWeight = showing ? FontStyle.Bold : FontStyle.Normal;
        }

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
            bar.style.display = focused && lit ? DisplayStyle.Flex : DisplayStyle.None;
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

        // One "Give <kind> (n)" button per machine kind the local player holds; rebuilt only when that list changes.
        private void RefreshGive()
        {
            var me = interaction.LocalPlayerId;
            var held = interaction.Session.ClientSite?.Equipment.Where(x => x.State == EquipmentState.Held && x.HolderId == me)
                .GroupBy(x => x.Kind).OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => (Kind: x.Key, Count: x.Count())).ToList();
            var employeeId = _shown.EmployeeId;
            var employeeHolds = interaction.Session.ClientSite?.Equipment.Count(x => x.State == EquipmentState.Held && x.HolderId == employeeId) ?? 0;
            var key = string.Join(",", held?.Select(x => $"{x.Kind}:{x.Count}") ?? Enumerable.Empty<string>()) + "|" + employeeHolds;
            if (key == _giveKinds) return;
            _giveKinds = key;
            _give.Clear();
            var caption = Caption(employeeHolds > 0 ? $"Employee holds {employeeHolds} machine(s). Give:" : "Give a machine to place:", 12, Muted);
            caption.style.marginRight = 6;
            _give.Add(caption);
            if (held == null || held.Count == 0)
            {
                _give.Add(Caption("(you hold no machines)", 12, Muted));
                return;
            }
            foreach (var (kind, count) in held)
                _give.Add(Button("employee-script-give-" + kind, $"{kind} ({count})", () => interaction.GiveMachine(kind)));
        }

        // ---- Building blocks ----

        // A dark multiline text box with a wide caret bar, since UI Toolkit draws its own caret 1 px wide. No select-all on
        // focus: a world pick inserts at the caret and must never replace the whole text.
        private TextField Editor(string name, float height, out VisualElement caretBar)
        {
            var field = TextBox(name);
            field.multiline = true;
            if (height > 0f) field.style.height = height;
            field.style.marginTop = 0;
            field.style.marginLeft = field.style.marginRight = 0;
            field.style.whiteSpace = WhiteSpace.Normal;
            var input = field.Q(TextField.textInputUssName);
            if (input != null) input.style.unityTextAlign = TextAnchor.UpperLeft;
            field.textSelection.cursorColor = Color.white;
            field.textSelection.selectionColor = Selection;
            caretBar = new VisualElement { name = name + "-caret", pickingMode = PickingMode.Ignore };
            caretBar.style.position = Position.Absolute;
            caretBar.style.width = CaretWidth;
            caretBar.style.height = CaretHeight;
            caretBar.style.backgroundColor = Color.white;
            caretBar.style.display = DisplayStyle.None;
            field.Q<TextElement>()?.Add(caretBar);
            return field;
        }

        // A dark text box that never takes the click that opened the screen, and that marks typing so E is text, not a close.
        private TextField TextBox(string name)
        {
            var field = new TextField { name = name, selectAllOnFocus = false, selectAllOnMouseUp = false };
            var input = field.Q(TextField.textInputUssName);
            if (input != null)
            {
                input.style.backgroundColor = Inset;
                input.style.color = Color.white;
                input.style.fontSize = 13;
            }
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
            return field;
        }

        // A checkbox that is never focused (a focused one would flip again on keyboard Submit) and reports the player's clicks.
        private static Toggle Check(string name, string text, bool value, Action<bool> changed)
        {
            var toggle = new Toggle(text) { name = name, value = value, focusable = false };
            toggle.style.marginTop = 2;
            toggle.style.color = Color.white;
            toggle.style.flexGrow = 0;
            // A field label is 150 px wide by default; these sit in rows, so they take their text's width.
            toggle.labelElement.style.minWidth = StyleKeyword.Auto;
            toggle.labelElement.style.marginRight = 6;
            toggle.RegisterValueChangedCallback(evt => changed(evt.newValue));
            return toggle;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        private static void Round(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
        }

        private static Button Button(string name, string text, Action clicked)
        {
            // Not focusable: a focused button would click again on keyboard Submit while typing.
            var button = new Button(clicked) { name = name, text = text, focusable = false };
            button.style.minWidth = 70;
            return button;
        }

        private static Label Caption(string text, int size, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            return label;
        }
    }
}
