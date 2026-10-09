// Local player's employee screen in UI Toolkit, built in code (decision 0037). The Tasks tab (shown first) is a visual task
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
// Drawn in HudTheme's look (decision 0040); the on/off/toggle and whitelist choices stay checkboxes as decision 0037 requires.
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
        private const string Reference =
            "move_to(place)  ·  path(place, place, ...)  ·  take(place, items, amount)  ·  put(place, items, amount)  ·  wait(seconds)\n" +
            "turn_on(machine)  ·  turn_off(machine)  ·  toggle(machine)  ·  is_on(machine)  ·  accepts(place, except)  ·  room(place, items)\n" +
            "place(machine, cell, rotation)  ·  pick_up(machine)  ·  place_belt(cell, direction)  ·  holding(kind)  ·  position()\n" +
            "count(place, items)  ·  find(kind)  ·  carrying()  ·  say(text) / print(text)\n" +
            "place: \"storage\", a machine kind (\"oven\", \"fridge\", \"counter\"), a machine ID, \"<id>:in\" / \"<id>:out\", or a cell " +
            "{x, z} to walk onto (Select world pos inserts one). items: an item, a list {\"dough\"}, {except = {...}} or nil for any. " +
            "take() and put() walk there first; amount may be left out.";

        private const string DetachedHint =
            "The Lua was edited by hand, so the Tasks tab is disabled. Reset to go back to the tasks (the hand edits are lost).";

        // Suggestions shown under an item search box.
        private const int MaxSuggestions = 6;

        private static readonly Color Muted = HudTheme.Muted;
        private static readonly Color Error = HudTheme.DangerText;
        private static readonly Color Code = HudTheme.Hex(0x111214);
        private static readonly Color CodeText = HudTheme.Hex(0xCFE3C8);
        // Both tabs' pages are this tall, so switching tabs never resizes the screen.
        private const float PageHeight = 420f;
        // The caret bar's size in pixels: the text box's 13 px font has lines about 14.5 px tall.
        private const float CaretWidth = 3f;
        private const float CaretHeight = 16f;
        private const float BlinkSeconds = 0.5f;

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;

        private VisualElement _dim;
        private VisualElement _avatar;
        private VisualElement _window;
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
            // Darkens the world behind the screen; never takes a click.
            _dim = new VisualElement { name = "employee-script-dim", pickingMode = PickingMode.Ignore };
            _dim.style.position = Position.Absolute;
            _dim.style.left = _dim.style.top = _dim.style.right = _dim.style.bottom = 0;
            _dim.style.backgroundColor = HudTheme.Dim;
            root.Add(_dim);
            _window = new VisualElement { name = "employee-script" };
            _window.style.position = Position.Absolute;
            _window.style.left = new Length(50, LengthUnit.Percent);
            _window.style.top = new Length(50, LengthUnit.Percent);
            _window.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            _window.style.width = 760;
            HudTheme.StylePanel(_window, 0);

            // Header: avatar, name and status light, the hands, and the close button.
            var head = new VisualElement();
            head.style.paddingLeft = head.style.paddingRight = 22;
            head.style.paddingTop = 18;
            var top = HudTheme.Row();
            _avatar = HudTheme.Avatar("", 44);
            top.Add(_avatar);
            var who = new VisualElement();
            who.style.flexGrow = 1;
            who.style.flexShrink = 1;
            who.style.marginLeft = 14;
            _title = HudTheme.Label("", 17, HudTheme.Text, true);
            who.Add(_title);
            var state = HudTheme.Row();
            state.style.marginTop = 4;
            _statusDot = HudTheme.Dot(HudTheme.Faint, 7);
            state.Add(_statusDot);
            _status = HudTheme.Label("", 12, HudTheme.TextSoft);
            _status.name = "employee-script-status";
            _status.style.marginLeft = 6;
            _status.style.flexShrink = 1;
            _status.style.whiteSpace = WhiteSpace.Normal;
            state.Add(_status);
            who.Add(state);
            top.Add(who);
            var close = HudTheme.IconButton("employee-script-close", "✕", interaction.CloseScreen);
            close.style.marginLeft = 14;
            close.style.alignSelf = Align.FlexStart;
            top.Add(close);
            head.Add(top);
            // On a row of its own under the name, so however many hand slots there are, the close button never covers them.
            _hands = new VisualElement { name = "employee-hands" };
            _hands.style.flexDirection = FlexDirection.Row;
            _hands.style.flexWrap = Wrap.Wrap;
            _hands.style.alignItems = Align.Center;
            _hands.style.marginTop = 12;
            head.Add(_hands);

            var tabs = HudTheme.Row();
            tabs.style.marginTop = 14;
            tabs.style.borderBottomWidth = 1;
            tabs.style.borderBottomColor = HudTheme.Edge;
            _tasksTab = Tab("employee-script-tab-tasks", "Tasks", () => ShowLua(false));
            _luaTab = Tab("employee-script-tab-lua", "Lua source", () => ShowLua(true));
            tabs.Add(_tasksTab);
            tabs.Add(_luaTab);
            head.Add(tabs);
            _window.Add(head);

            _tasksPage = new VisualElement { name = "employee-script-tasks" };
            _tasksPage.style.height = PageHeight;
            Page(_tasksPage);
            _banner = new VisualElement { name = "employee-script-detached" };
            _banner.style.flexDirection = FlexDirection.Row;
            _banner.style.alignItems = Align.Center;
            _banner.style.backgroundColor = HudTheme.Hex(0x3A2E1C);
            HudTheme.Border(_banner, HudTheme.Accent, 1);
            HudTheme.Radius(_banner, 10);
            _banner.style.paddingLeft = _banner.style.paddingRight = 12;
            _banner.style.paddingTop = _banner.style.paddingBottom = 8;
            _banner.style.marginBottom = 10;
            var hint = Caption(DetachedHint, 12, HudTheme.Text);
            hint.style.whiteSpace = WhiteSpace.Normal;
            hint.style.flexShrink = 1;
            hint.style.flexGrow = 1;
            hint.style.marginRight = 10;
            _banner.Add(hint);
            _banner.Add(Button("employee-script-reset", "Reset to tasks", ResetToTasks));
            _tasksPage.Add(_banner);
            var loop = Caption("Repeats forever, top to bottom: one trip or one switch per task each pass.", 12, Muted);
            loop.style.marginBottom = 10;
            _tasksPage.Add(loop);
            _list = new ScrollView(ScrollViewMode.Vertical) { name = "employee-script-task-list" };
            _list.style.flexGrow = 1;
            HudTheme.StyleScroller(_list);
            _tasksPage.Add(_list);
            _add = Button("employee-script-add-task", "+   Add task", AddTask, HudButton.Secondary, 44);
            _add.style.marginTop = 10;
            _add.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _tasksPage.Add(_add);
            _window.Add(_tasksPage);

            _luaPage = new VisualElement { name = "employee-script-lua" };
            _luaPage.style.height = PageHeight;
            Page(_luaPage);
            var luaHead = HudTheme.Row();
            luaHead.style.marginBottom = 10;
            var luaHint = Caption("Generated from the task list. Editing it disables the Tasks tab.", 12, Muted);
            luaHint.style.flexShrink = 1;
            luaHint.style.whiteSpace = WhiteSpace.Normal;
            luaHead.Add(luaHint);
            luaHead.Add(HudTheme.Spacer());
            var pick = Button("employee-script-pick", "Select world pos", ClickSelectWorldPos);
            pick.style.marginLeft = 12;
            luaHead.Add(pick);
            _luaPage.Add(luaHead);
            _source = Editor("employee-script-source", 0f, out _caretBar);
            _source.style.flexGrow = 1;
            // The box keeps the page's height and scrolls its text instead of growing (Editor builds its scroll view); the text
            // box scrolls the caret into view as it moves.
            _source.style.flexShrink = 1;
            _source.style.minHeight = 0;
            _source.RegisterValueChangedCallback(_ => RefreshDetached());
            _luaPage.Add(_source);
            var reference = Caption(Reference, 11, HudTheme.Faint);
            reference.name = "employee-script-reference";
            reference.style.marginTop = 10;
            reference.style.whiteSpace = WhiteSpace.Normal;
            _luaPage.Add(reference);
            _window.Add(_luaPage);

            // Footer: Stop and Run.
            var footer = HudTheme.Row();
            footer.style.paddingLeft = footer.style.paddingRight = 22;
            footer.style.paddingTop = footer.style.paddingBottom = 14;
            footer.style.borderTopWidth = 1;
            footer.style.borderTopColor = HudTheme.Edge;
            var note = Caption("Changes apply when you press Run.", 12, Muted);
            note.style.flexGrow = 1;
            footer.Add(note);
            footer.Add(Button("employee-script-stop", "Stop", ClickStop, HudButton.Secondary, 44));
            var run = Button("employee-script-run", "Run", ClickRun, HudButton.Primary, 44);
            run.style.marginLeft = 10;
            footer.Add(run);
            _window.Add(footer);

            root.Add(_window);
            _window.style.display = _dim.style.display = DisplayStyle.None;
            ShowLua(false);
        }

        private void Update()
        {
            if (_window == null) return;
            // Picking in the world hides the screen without closing it, so the drafts are kept.
            var employee = interaction.Screen is InteractionScreen.Employee or InteractionScreen.PickPosition ? interaction.OpenEmployee : null;
            var visible = employee != null && interaction.Screen == InteractionScreen.Employee;
            _window.style.display = _dim.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            // A hidden text box keeps keyboard focus, so it would swallow E/WASD typed after closing, while picking or on the
            // other tab.
            if (!visible && _window.focusController?.focusedElement is VisualElement focused && _window.Contains(focused)) focused.Blur();
            if (_luaShown == false && _source.focusController?.focusedElement == _source) _source.Blur();
            if (employee != _shown)
            {
                _shown = employee;
                if (employee == null) return;
                _title.text = employee.DisplayName;
                _avatar.Q<Label>().text = string.IsNullOrEmpty(employee.DisplayName) ? "?" : employee.DisplayName.Substring(0, 1).ToUpperInvariant();
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
            var text = $"{(status.Length == 0 ? "Idle" : status)}{(employee.Carrying ? "  ·  carrying a box" : "")}";
            if (_status.text != text) _status.text = text;
            var failing = status.StartsWith("Error") || employee.Unpaid;
            _status.style.color = failing ? Error : HudTheme.TextSoft;
            _statusDot.style.backgroundColor = failing ? HudTheme.Danger : status.Length == 0 ? HudTheme.Faint : HudTheme.Positive;
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
            card.style.backgroundColor = HudTheme.Card;
            HudTheme.Border(card, HudTheme.CardEdge, 1);
            HudTheme.Radius(card, 12);
            card.style.marginBottom = 10;
            card.style.paddingLeft = card.style.paddingRight = 16;
            card.style.paddingTop = card.style.paddingBottom = 14;

            var header = Row();
            var number = HudTheme.Label($"{index + 1}", 12, HudTheme.Text, true);
            number.style.width = number.style.height = 24;
            number.style.flexShrink = 0;
            number.style.unityTextAlign = TextAnchor.MiddleCenter;
            number.style.backgroundColor = HudTheme.Edge;
            HudTheme.Radius(number, 6);
            header.Add(number);
            var choices = new List<string> { EmployeeTaskList.MoveLabel, EmployeeTaskList.PowerLabel };
            var type = new DropdownField(choices, task.Type == EmployeeTaskType.Power ? 1 : 0) { name = $"employee-task-{index}-type" };
            type.style.flexGrow = 1;
            type.style.marginLeft = 10;
            type.style.marginRight = 10;
            var input = type.Q(className: BasePopupField<string, string>.inputUssClassName);
            if (input != null)
            {
                input.style.height = 36;
                input.style.backgroundColor = HudTheme.Panel;
                input.style.color = HudTheme.Text;
                input.style.fontSize = 14;
                input.style.paddingLeft = 10;
                HudTheme.Border(input, HudTheme.ControlEdge, 1);
                HudTheme.Radius(input, 8);
            }
            type.RegisterValueChangedCallback(evt =>
                SetTaskType(index, evt.newValue == EmployeeTaskList.PowerLabel ? EmployeeTaskType.Power : EmployeeTaskType.Move));
            header.Add(type);
            header.Add(HudTheme.IconButton($"employee-task-{index}-remove", "✕", () => RemoveTask(index), 36));
            card.Add(header);

            if (task.Type == EmployeeTaskType.Power) PowerBody(card, index, task);
            else MoveBody(card, index, task);
            return card;
        }

        private void MoveBody(VisualElement card, int index, EmployeeTask task)
        {
            var places = Row();
            places.style.marginTop = 12;
            places.Add(PlaceRow(index, "From", "Set source", PickTarget.Source, task.Source));
            var arrow = Caption("→", 18, HudTheme.Faint);
            arrow.style.marginLeft = arrow.style.marginRight = 8;
            places.Add(arrow);
            places.Add(PlaceRow(index, "To", "Set destination", PickTarget.Destination, task.Destination));
            card.Add(places);
            var any = Check($"employee-task-{index}-any", "Move any valid item (what the destination takes)", task.AnyItem,
                value => SetAnyItem(index, value));
            any.style.marginTop = 12;
            card.Add(any);
            if (task.AnyItem) return;

            var whitelist = Check($"employee-task-{index}-whitelist", "Whitelist (unchecked: blacklist)", task.Whitelist,
                value => SetWhitelist(index, value));
            card.Add(whitelist);
            // The item chips and the search box share one field, like a tag input.
            var chips = new VisualElement { name = $"employee-task-{index}-items" };
            chips.style.flexDirection = FlexDirection.Row;
            chips.style.flexWrap = Wrap.Wrap;
            chips.style.alignItems = Align.Center;
            chips.style.marginTop = 8;
            chips.style.backgroundColor = HudTheme.Panel;
            HudTheme.Border(chips, HudTheme.ControlEdge, 1);
            HudTheme.Radius(chips, 10);
            HudTheme.Pad(chips, 4);
            if (task.Items.Count == 0)
            {
                var none = Caption(task.Whitelist ? "No items yet: nothing will be moved." : "No items yet: any valid item is moved.", 12, Muted);
                none.style.marginLeft = 6;
                none.style.marginRight = 6;
                chips.Add(none);
            }
            foreach (var itemId in task.Items)
            {
                var chip = Row();
                chip.style.height = 32;
                chip.style.backgroundColor = HudTheme.Control;
                chip.style.marginLeft = chip.style.marginRight = chip.style.marginTop = chip.style.marginBottom = 2;
                chip.style.paddingLeft = 6;
                HudTheme.Radius(chip, 8);
                var icon = ItemIcon(itemId);
                if (icon != null)
                {
                    var image = new VisualElement { pickingMode = PickingMode.Ignore };
                    image.style.width = image.style.height = 22;
                    image.style.marginRight = 6;
                    image.style.backgroundImage = new StyleBackground(icon);
                    image.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                    chip.Add(image);
                }
                chip.Add(Caption(ItemName(itemId), 13, HudTheme.Text));
                var id = itemId;
                var x = Button($"employee-task-{index}-item-{itemId}-remove", "✕", () => RemoveItem(index, id), HudButton.Secondary, 24);
                x.style.width = 24;
                x.style.marginLeft = 4;
                x.style.paddingLeft = x.style.paddingRight = 0;
                x.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                x.style.color = Muted;
                HudTheme.Border(x, HudTheme.Control, 0);
                chip.Add(x);
                chips.Add(chip);
            }

            var search = TextBox($"employee-task-{index}-search");
            search.textEdition.placeholder = task.Whitelist ? "Add an item to allow…" : "Add an item to never move…";
            search.style.flexGrow = 1;
            search.style.minWidth = 160;
            search.style.marginLeft = search.style.marginRight = search.style.marginTop = search.style.marginBottom = 0;
            var field = search.Q(TextField.textInputUssName);
            if (field != null)
            {
                field.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                HudTheme.Border(field, HudTheme.Panel, 0);
            }
            chips.Add(search);
            card.Add(chips);
            var suggestions = new VisualElement { name = $"employee-task-{index}-suggestions" };
            suggestions.style.backgroundColor = HudTheme.Panel;
            HudTheme.Border(suggestions, HudTheme.ControlEdge, 1);
            HudTheme.Radius(suggestions, 10);
            HudTheme.Pad(suggestions, 4);
            suggestions.style.marginTop = 4;
            suggestions.style.display = DisplayStyle.None;
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
                if (search.focusController?.focusedElement != search) suggestions.style.display = DisplayStyle.None;
            }).ExecuteLater(200));
            // Enter adds the first suggestion.
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode is not (KeyCode.Return or KeyCode.KeypadEnter)) return;
                var first = Matches(task, search.value).FirstOrDefault();
                if (first != null) AddItem(index, first.Id);
                evt.StopPropagation();
            }, TrickleDown.TrickleDown);
            card.Add(suggestions);
        }

        // Fills the drop-down under an item search box: the recent items with no text, else those whose name or ID contains it.
        private void Suggest(VisualElement suggestions, int index, EmployeeTask task, string text)
        {
            suggestions.Clear();
            var matches = Matches(task, text).Take(MaxSuggestions).ToList();
            suggestions.style.display = matches.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var item in matches)
            {
                var id = item.Id;
                var option = Row();
                option.name = $"employee-task-{index}-suggest-{id}";
                option.style.height = 32;
                option.style.paddingLeft = 8;
                HudTheme.Radius(option, 6);
                if (item.Icon != null)
                {
                    var image = new VisualElement { pickingMode = PickingMode.Ignore };
                    image.style.width = image.style.height = 20;
                    image.style.marginRight = 8;
                    image.style.backgroundImage = new StyleBackground(item.Icon);
                    image.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                    option.Add(image);
                }
                var label = Caption(item.DisplayName, 13, HudTheme.Text);
                label.pickingMode = PickingMode.Ignore;
                option.Add(label);
                option.RegisterCallback<PointerEnterEvent>(_ => option.style.backgroundColor = HudTheme.Control);
                option.RegisterCallback<PointerLeaveEvent>(_ => option.style.backgroundColor = new Color(0f, 0f, 0f, 0f));
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
            var place = PlaceRow(index, "Machine", "Select machine", PickTarget.Machine, task.Machine);
            place.style.marginTop = 12;
            card.Add(place);
            var row = Row();
            row.style.marginTop = 12;
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

        // A picked place in a box: its machine's icon, a caption, the place's name and the pick button.
        private VisualElement PlaceRow(int index, string caption, string text, PickTarget target, string place)
        {
            var box = Row();
            box.style.flexGrow = 1;
            box.style.flexBasis = 0;
            box.style.backgroundColor = HudTheme.Panel;
            HudTheme.Radius(box, 10);
            box.style.paddingLeft = box.style.paddingRight = 10;
            box.style.paddingTop = box.style.paddingBottom = 8;
            var name = target switch { PickTarget.Source => "source", PickTarget.Destination => "destination", _ => "machine" };
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.style.width = icon.style.height = 36;
            icon.style.flexShrink = 0;
            icon.style.marginRight = 10;
            var sprite = PlaceIcon(place);
            if (sprite != null)
            {
                icon.style.backgroundImage = new StyleBackground(sprite);
                icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            }
            else
            {
                icon.style.backgroundColor = HudTheme.SlotEmpty;
                HudTheme.Radius(icon, 8);
            }
            box.Add(icon);
            var words = new VisualElement();
            words.style.flexGrow = 1;
            words.style.flexShrink = 1;
            words.style.minWidth = 0;
            words.Add(Caption(caption, 11, HudTheme.Faint));
            var label = Caption(string.IsNullOrEmpty(place) ? "Not set" : PlaceName(place), 14,
                string.IsNullOrEmpty(place) ? Muted : HudTheme.Text);
            label.name = $"employee-task-{index}-{name}-label";
            label.style.marginTop = 1;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            words.Add(label);
            box.Add(words);
            var button = Button($"employee-task-{index}-{name}", text, () => ClickPick(index, target), HudButton.Secondary, 32);
            button.style.marginLeft = 8;
            box.Add(button);
            return box;
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

        // The showing tab has light text over an amber underline; the hidden one is muted.
        private static void StyleTab(Button tab, bool showing)
        {
            tab.style.color = showing ? HudTheme.Text : Muted;
            tab.style.borderBottomColor = showing ? HudTheme.Accent : new Color(0f, 0f, 0f, 0f);
            tab.style.unityFontStyleAndWeight = showing ? FontStyle.Bold : FontStyle.Normal;
        }

        private static Button Tab(string name, string text, Action clicked)
        {
            var tab = new Button(clicked) { name = name, text = text, focusable = false };
            tab.style.height = 40;
            tab.style.marginLeft = tab.style.marginRight = tab.style.marginTop = 0;
            // Overlaps the tab row's hairline, so the underline sits on it.
            tab.style.marginBottom = -1;
            tab.style.paddingLeft = tab.style.paddingRight = 16;
            tab.style.fontSize = 14;
            tab.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            HudTheme.Border(tab, new Color(0f, 0f, 0f, 0f), 0);
            tab.style.borderBottomWidth = 2;
            HudTheme.Radius(tab, 0);
            return tab;
        }

        // A tab's page: padded inside the window.
        private static void Page(VisualElement page)
        {
            page.style.paddingLeft = page.style.paddingRight = 22;
            page.style.paddingTop = page.style.paddingBottom = 16;
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
            var caption = HudTheme.Eyebrow("Hands");
            caption.style.marginRight = 8;
            _hands.Add(caption);
            for (var index = 0; index < Math.Max(capacity, stacks.Count); index++)
            {
                var stack = index < stacks.Count ? stacks[index] : default;
                var full = index < stacks.Count;
                var slot = new Button(full ? () => ClickHand(stack.ItemId, stack.Spoiled) : (Action)null)
                {
                    name = $"employee-hand-{index}", text = "", focusable = false
                };
                slot.style.width = slot.style.height = 40;
                slot.style.marginLeft = slot.style.marginRight = 2;
                slot.style.marginTop = slot.style.marginBottom = 0;
                slot.style.paddingLeft = slot.style.paddingRight = slot.style.paddingTop = slot.style.paddingBottom = 0;
                slot.style.backgroundColor = full ? stack.Spoiled ? HudTheme.SpoiledFill : HudTheme.Slot : HudTheme.SlotEmpty;
                HudTheme.Border(slot, HudTheme.Edge, 1);
                HudTheme.Radius(slot, 8);
                if (full)
                {
                    var item = interaction.Session.Items.FirstOrDefault(x => x != null && x.Id == stack.ItemId);
                    if (item?.Icon != null)
                    {
                        var image = new VisualElement { pickingMode = PickingMode.Ignore };
                        image.style.width = image.style.height = 30;
                        image.style.alignSelf = Align.Center;
                        image.style.marginTop = 4;
                        image.style.backgroundImage = new StyleBackground(item.Icon);
                        image.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
                        slot.Add(image);
                    }
                    else slot.Add(Caption(item?.DisplayName ?? stack.ItemId, 9, HudTheme.Text));
                    var count = Caption(stack.Count.ToString(), 11, stack.Spoiled ? Error : HudTheme.Text);
                    count.style.position = Position.Absolute;
                    count.style.right = 3;
                    count.style.bottom = 1;
                    count.style.unityFontStyleAndWeight = FontStyle.Bold;
                    slot.Add(count);
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

        // A dark multiline text box with a wide caret bar, since UI Toolkit draws its own caret 1 px wide. No select-all on
        // focus: a world pick inserts at the caret and must never replace the whole text.
        private TextField Editor(string name, float height, out VisualElement caretBar)
        {
            var field = TextBox(name);
            field.multiline = true;
            // Builds the text box's own scroll view now, so the caret bar and the scroller style below find it.
            field.verticalScrollerVisibility = ScrollerVisibility.Auto;
            if (height > 0f) field.style.height = height;
            field.style.marginTop = 0;
            field.style.marginLeft = field.style.marginRight = 0;
            field.style.whiteSpace = WhiteSpace.Normal;
            var input = field.Q(TextField.textInputUssName);
            if (input != null) input.style.unityTextAlign = TextAnchor.UpperLeft;
            if (input != null)
            {
                input.style.backgroundColor = Code;
                input.style.color = CodeText;
                HudTheme.Border(input, HudTheme.CardEdge, 1);
                HudTheme.Radius(input, 10);
                HudTheme.Pad(input, 14);
            }
            caretBar = new VisualElement { name = name + "-caret", pickingMode = PickingMode.Ignore };
            caretBar.style.position = Position.Absolute;
            caretBar.style.width = CaretWidth;
            caretBar.style.height = CaretHeight;
            caretBar.style.backgroundColor = HudTheme.Accent;
            caretBar.style.display = DisplayStyle.None;
            // Beside the text element in the scroll view's content, never inside it: a text element with a child is no longer
            // measured, so it would stay the viewport's height and nothing would scroll. Both share the content's origin, so the
            // text element's cursorPosition places the bar.
            var scroll = field.Q<ScrollView>();
            if (scroll != null)
            {
                scroll.contentContainer.Add(caretBar);
                HudTheme.StyleScroller(scroll);
            }
            return field;
        }

        // A dark text box that never takes the click that opened the screen, and that marks typing so E is text, not a close.
        private TextField TextBox(string name)
        {
            var field = new TextField { name = name, selectAllOnFocus = false, selectAllOnMouseUp = false };
            // A light caret and selection (Assets/UI/Hud.uss): inline styles cannot set those properties.
            field.AddToClassList(HudTheme.TextFieldClass);
            var input = field.Q(TextField.textInputUssName);
            if (input != null)
            {
                input.style.backgroundColor = HudTheme.Panel;
                input.style.color = HudTheme.Text;
                input.style.fontSize = 13;
                HudTheme.Border(input, HudTheme.ControlEdge, 1);
                HudTheme.Radius(input, 8);
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
            toggle.style.marginTop = 4;
            toggle.style.marginLeft = 0;
            toggle.style.color = HudTheme.Text;
            toggle.style.fontSize = 14;
            toggle.style.flexGrow = 0;
            // Only as wide as its box and text: in a column a stretched toggle took clicks across the whole card.
            toggle.style.alignSelf = Align.FlexStart;
            // A field label is 150 px wide by default; these sit in rows, so they take their text's width.
            toggle.labelElement.style.minWidth = StyleKeyword.Auto;
            toggle.labelElement.style.marginRight = 8;
            HudTheme.StyleToggle(toggle);
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

        // Not focusable (HudTheme): a focused button would click again on keyboard Submit while typing.
        private static Button Button(string name, string text, Action clicked, HudButton kind = HudButton.Secondary, int height = 36) =>
            HudTheme.Button(name, text, clicked, kind, height);

        private static Label Caption(string text, int size, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
            label.style.paddingLeft = label.style.paddingRight = 0;
            return label;
        }
    }
}
