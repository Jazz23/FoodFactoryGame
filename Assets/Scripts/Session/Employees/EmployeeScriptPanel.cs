// Local player's employee script screen in UI Toolkit, built in code: a text box for a Lua program, Run/Stop/Close, the
// employee's replicated status line and a short API reference. Opened by clicking an employee (EquipmentInteraction);
// Run and Stop are requests the server checks (EmployeeWorker). Presentation only: the text box is this client's draft.
// "Select world pos" hides the screen while the player clicks a cell or machine in the world, then inserts its Lua text at
// the text box's caret (replacing any selection); the draft survives because the screen stays open while hidden. "Give"
// buttons hand the employee one of each machine kind the player holds, for its script's place().
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
            "move_to(place)  ·  path(place, place, ...)  ·  take(place, item, amount)  ·  put(place, item, amount)  ·  wait(seconds)\n" +
            "place(machine, cell, rotation)  ·  pick_up(machine)  ·  place_belt(cell, direction)  ·  holding(kind)  ·  position()\n" +
            "count(place, item)  ·  find(kind)  ·  carrying()  ·  say(text) / print(text)\n" +
            "place: \"storage\", a machine kind (\"oven\", \"fridge\", \"counter\"), a machine ID, \"<id>:in\" / \"<id>:out\", or a cell " +
            "{x, z} to walk onto (Select world pos inserts one). take() and put() walk there first; amount and item may be left out. " +
            "place() puts a held machine with its lowest corner on the cell, turned 0-3 quarter turns.";

        private const string Starter = "-- Carry dough from the storage into the nearest oven.\nlocal n = take(\"storage\", \"dough\", 5)\nsay(\"took \" .. n .. \" dough\")\nput(\"oven\", \"dough\")\n";

        private static readonly Color Backdrop = new(0.19f, 0.19f, 0.2f, 0.97f);
        private static readonly Color Inset = new(0.11f, 0.11f, 0.12f, 1f);
        private static readonly Color Heading = new(1f, 0.9f, 0.74f, 1f);
        private static readonly Color Muted = new(0.7f, 0.7f, 0.75f, 1f);
        private static readonly Color Error = new(0.95f, 0.45f, 0.4f, 1f);
        private static readonly Color Selection = new(0.35f, 0.55f, 1f, 0.45f);
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
        // The text box's caret and selection end, kept while it has focus so a world pick inserts where the player was typing.
        private int _caret;
        private int _anchor;
        private VisualElement _caretBar;
        // Caret index at the last blink restart, and when that was: the bar stays lit while the caret moves.
        private int _blinkIndex = -1;
        private float _blinkStart;

        public VisualElement Window => _window;
        public TextField SourceField => _source;

        private void Start()
        {
            var root = document.rootVisualElement;
            root.Clear();
            _window = new VisualElement { name = "employee-script" };
            _window.style.position = Position.Absolute;
            _window.style.left = new Length(50, LengthUnit.Percent);
            _window.style.top = new Length(50, LengthUnit.Percent);
            _window.style.translate = new Translate(new Length(-50, LengthUnit.Percent), new Length(-50, LengthUnit.Percent));
            _window.style.width = 640;
            _window.style.backgroundColor = Backdrop;
            _window.style.paddingLeft = _window.style.paddingRight = 12;
            _window.style.paddingTop = _window.style.paddingBottom = 10;
            _window.style.borderTopLeftRadius = _window.style.borderTopRightRadius = 4;
            _window.style.borderBottomLeftRadius = _window.style.borderBottomRightRadius = 4;

            _title = Caption("", 16, Heading);
            _title.style.unityFontStyleAndWeight = FontStyle.Bold;
            _window.Add(_title);

            // No select-all on focus: a world pick inserts at the caret and must never replace the whole script.
            _source = new TextField { name = "employee-script-source", multiline = true, selectAllOnFocus = false, selectAllOnMouseUp = false };
            _source.style.height = 280;
            _source.style.marginTop = 6;
            _source.style.marginLeft = _source.style.marginRight = 0;
            _source.style.whiteSpace = WhiteSpace.Normal;
            var input = _source.Q(TextField.textInputUssName);
            if (input != null)
            {
                input.style.backgroundColor = Inset;
                input.style.color = Color.white;
                input.style.unityTextAlign = TextAnchor.UpperLeft;
                input.style.fontSize = 13;
            }
            _source.textSelection.cursorColor = Color.white;
            _source.textSelection.selectionColor = Selection;
            // UI Toolkit draws its caret 1 px wide, so a thicker blinking bar is drawn over it at the caret position.
            _caretBar = new VisualElement { name = "employee-script-caret", pickingMode = PickingMode.Ignore };
            _caretBar.style.position = Position.Absolute;
            _caretBar.style.width = CaretWidth;
            _caretBar.style.height = CaretHeight;
            _caretBar.style.backgroundColor = Color.white;
            _caretBar.style.display = DisplayStyle.None;
            _source.Q<TextElement>()?.Add(_caretBar);
            _window.Add(_source);

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
            var pick = Button("employee-script-pick", "Select world pos", ClickSelectWorldPos);
            pick.style.minWidth = 130;
            buttons.Add(pick);
            buttons.Add(Button("employee-script-close", "Close", interaction.CloseScreen));
            _window.Add(buttons);

            _give = new VisualElement { name = "employee-script-give" };
            _give.style.flexDirection = FlexDirection.Row;
            _give.style.flexWrap = Wrap.Wrap;
            _give.style.alignItems = Align.Center;
            _give.style.marginTop = 6;
            _window.Add(_give);

            var reference = Caption(Reference, 11, Muted);
            reference.style.marginTop = 8;
            reference.style.whiteSpace = WhiteSpace.Normal;
            _window.Add(reference);

            root.Add(_window);
            _window.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (_window == null) return;
            // Picking a world position hides the screen without closing it, so the draft is kept.
            var employee = interaction.Screen is InteractionScreen.Employee or InteractionScreen.PickPosition ? interaction.OpenEmployee : null;
            var visible = employee != null && interaction.Screen == InteractionScreen.Employee;
            _window.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            // A hidden text box keeps keyboard focus, so it would swallow E/WASD typed after closing or while picking.
            if (!visible && _source.focusController?.focusedElement == _source) _source.Blur();
            if (employee != _shown)
            {
                _shown = employee;
                if (employee == null) return;
                _title.text = $"{employee.DisplayName} ({employee.EmployeeId}): Lua script";
                _source.value = string.IsNullOrEmpty(employee.Source) ? Starter : employee.Source;
                // Not focused on opening: the player clicks into the text box to type. Focusing it here would also catch the
                // E press that opened the screen, whose typed "e" reaches UI Toolkit after the Input System action fired.
                _caret = _anchor = _source.value.Length;
            }
            if (employee == null) return;
            if (_source.focusController?.focusedElement == _source)
            {
                _caret = _source.cursorIndex;
                _anchor = _source.selectIndex;
            }
            RefreshGive();
            UpdateCaret();
            var status = employee.Status ?? "";
            _status.text = $"Status: {status}{(employee.Carrying ? "  (carrying a box)" : "")}";
            _status.style.color = status.StartsWith("Error") ? Error : Muted;
        }

        // Sends the text box's program to the server. Public so tests can drive the same path as the button.
        public void ClickRun()
        {
            if (_shown != null) _shown.RequestRun(_source.value);
        }

        public void ClickStop()
        {
            if (_shown != null) _shown.RequestStop();
        }

        // Shows the wide caret bar while the text box has focus and no selection, blinking but lit whenever the caret moves.
        // cursorPosition is in the text element's space with y at the bottom of the caret's line.
        private void UpdateCaret()
        {
            if (_caretBar == null) return;
            var focused = _source.focusController?.focusedElement == _source && _source.cursorIndex == _source.selectIndex;
            if (_source.cursorIndex != _blinkIndex)
            {
                _blinkIndex = _source.cursorIndex;
                _blinkStart = Time.unscaledTime;
            }
            var lit = (int)((Time.unscaledTime - _blinkStart) / BlinkSeconds) % 2 == 0;
            _caretBar.style.display = focused && lit ? DisplayStyle.Flex : DisplayStyle.None;
            if (!focused) return;
            var position = _source.cursorPosition;
            _caretBar.style.left = position.x - CaretWidth * 0.5f;
            _caretBar.style.top = position.y - CaretHeight + 1f;
        }

        public void ClickSelectWorldPos() => interaction.BeginWorldPick(Insert);

        // Replaces the remembered selection (or inserts at the caret) with text and puts the caret after it.
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
                .GroupBy(x => x.Kind).OrderBy(x => x.Key, System.StringComparer.Ordinal).Select(x => (Kind: x.Key, Count: x.Count())).ToList();
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

        private static Button Button(string name, string text, System.Action clicked)
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
