// Local player's employee script screen in UI Toolkit, built in code: a text box for a Lua program, Run/Stop/Close, the
// employee's replicated status line and a short API reference. Opened by clicking an employee (EquipmentInteraction);
// Run and Stop are requests the server checks (EmployeeWorker). Presentation only: the text box is this client's draft.
// It opens unfocused, whether by E or by the left click, which is ignored by the text box until released. E closes the
// screen unless the text box has focus, where it is typed.
// "Select world pos" hides the screen while the player clicks a cell or machine in the world, then inserts its Lua text at
// the showing text box's caret (replacing any selection); the draft survives because the screen stays open while hidden.
// "Give" buttons hand the employee one of each machine kind the player holds, for its script's place().
// The Assistant tab takes an English description and has the local language model (ScriptAssistant, LocalScriptModel) write
// the program into the Script tab's text box. A reply that fails ScriptDryRun (parse, then a stand-in run) is retried with
// the error, and after ScriptAssistant.MaxAttempts replies the error is shown and the draft is left untouched.
using System;
using System.Linq;
using System.Threading.Tasks;
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

        private const string AssistantHint =
            "Describe what this employee should do in plain English. The assistant writes a Lua program into the Script tab, " +
            "replacing the draft there; check it, then press Run. Select world pos inserts a cell or machine here too.";

        private const string Starter = "-- Carry dough from the storage into the nearest oven.\nlocal n = take(\"storage\", \"dough\", 5)\nsay(\"took \" .. n .. \" dough\")\nput(\"oven\", \"dough\")\n";

        private static readonly Color Backdrop = new(0.19f, 0.19f, 0.2f, 0.97f);
        private static readonly Color Inset = new(0.11f, 0.11f, 0.12f, 1f);
        private static readonly Color Heading = new(1f, 0.9f, 0.74f, 1f);
        private static readonly Color Muted = new(0.7f, 0.7f, 0.75f, 1f);
        private static readonly Color Error = new(0.95f, 0.45f, 0.4f, 1f);
        private static readonly Color Selection = new(0.35f, 0.55f, 1f, 0.45f);
        private static readonly Color TabActive = new(0.85f, 0.85f, 0.87f, 1f);
        private static readonly Color TabIdle = new(0.26f, 0.26f, 0.28f, 1f);
        private const float PageHeight = 280f;
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
        private Button _scriptTab;
        private Button _assistantTab;
        private VisualElement _assistantPage;
        private TextField _prompt;
        private Button _generate;
        private Label _assistantStatus;
        private bool _assistantShown;
        private IScriptModel _model;
        private Task _warming;
        private bool _generating;
        // The showing text box's caret and selection end, kept while it has focus so a world pick inserts where the player
        // was typing.
        private int _caret;
        private int _anchor;
        private VisualElement _caretBar;
        private VisualElement _promptCaretBar;
        // Caret index at the last blink restart, and when that was: the bar stays lit while the caret moves.
        private int _blinkIndex = -1;
        private float _blinkStart;

        public VisualElement Window => _window;
        public TextField SourceField => _source;
        public TextField PromptField => _prompt;
        public bool AssistantShown => _assistantShown;
        public string AssistantStatus => _assistantStatus.text;
        public bool Generating => _generating;
        // The text box world picks insert into: the showing tab's.
        private TextField Active => _assistantShown ? _prompt : _source;

        // Replaces the local language model, for tests.
        public void UseModel(IScriptModel model) => _model = model;

        private void Start()
        {
            _model ??= gameObject.AddComponent<LocalScriptModel>();
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

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginTop = 6;
            _scriptTab = Button("employee-script-tab-script", "Script", () => ShowAssistant(false));
            _assistantTab = Button("employee-script-tab-assistant", "Assistant", () => ShowAssistant(true));
            tabs.Add(_scriptTab);
            tabs.Add(_assistantTab);
            _window.Add(tabs);

            _source = Editor("employee-script-source", PageHeight, out _caretBar);
            _window.Add(_source);

            _assistantPage = new VisualElement { name = "employee-script-assistant" };
            _assistantPage.style.height = PageHeight;
            _assistantPage.style.marginTop = 6;
            var hint = Caption(AssistantHint, 12, Muted);
            hint.style.whiteSpace = WhiteSpace.Normal;
            _assistantPage.Add(hint);
            _prompt = Editor("employee-script-prompt", 180, out _promptCaretBar);
            _assistantPage.Add(_prompt);
            var generateRow = new VisualElement();
            generateRow.style.flexDirection = FlexDirection.Row;
            generateRow.style.alignItems = Align.Center;
            generateRow.style.marginTop = 6;
            _generate = Button("employee-script-generate", "Write script", ClickGenerate);
            _generate.style.minWidth = 110;
            generateRow.Add(_generate);
            _assistantStatus = Caption("", 12, Muted);
            _assistantStatus.name = "employee-script-assistant-status";
            _assistantStatus.style.whiteSpace = WhiteSpace.Normal;
            _assistantStatus.style.flexShrink = 1;
            _assistantStatus.style.marginLeft = 6;
            generateRow.Add(_assistantStatus);
            _assistantPage.Add(generateRow);
            _window.Add(_assistantPage);

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
            ShowAssistant(false);
        }

        private void Update()
        {
            if (_window == null) return;
            // Picking a world position hides the screen without closing it, so the draft is kept.
            var employee = interaction.Screen is InteractionScreen.Employee or InteractionScreen.PickPosition ? interaction.OpenEmployee : null;
            var visible = employee != null && interaction.Screen == InteractionScreen.Employee;
            _window.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            // A hidden text box keeps keyboard focus, so it would swallow E/WASD typed after closing, while picking or on the
            // other tab.
            foreach (var field in new[] { _source, _prompt })
                if ((!visible || field != Active) && field.focusController?.focusedElement == field) field.Blur();
            if (employee != _shown)
            {
                _shown = employee;
                if (employee == null) return;
                _title.text = $"{employee.DisplayName} ({employee.EmployeeId}): Lua script";
                _source.value = string.IsNullOrEmpty(employee.Source) ? Starter : employee.Source;
                _prompt.value = "";
                if (!_generating) SetAssistantStatus("", false);
                // Not focused on opening: the player clicks into the text box to type. Focusing it here would also catch the
                // E press that opened the screen, whose typed "e" reaches UI Toolkit after the Input System action fired.
                ShowAssistant(false);
            }
            if (employee == null) return;
            var active = Active;
            if (active.focusController?.focusedElement == active)
            {
                _caret = active.cursorIndex;
                _anchor = active.selectIndex;
            }
            RefreshGive();
            UpdateCaret(_source, _caretBar);
            UpdateCaret(_prompt, _promptCaretBar);
            _generate.SetEnabled(!_generating);
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

        // Switches between the Script and Assistant tabs. The first time the Assistant shows, the model starts loading so the
        // first request does not also wait for it.
        public void ShowAssistant(bool assistant)
        {
            _assistantShown = assistant;
            _source.style.display = assistant ? DisplayStyle.None : DisplayStyle.Flex;
            _assistantPage.style.display = assistant ? DisplayStyle.Flex : DisplayStyle.None;
            StyleTab(_scriptTab, !assistant);
            StyleTab(_assistantTab, assistant);
            _caret = _anchor = Active.value?.Length ?? 0;
            if (assistant && _warming == null) _warming = Warm();
        }

        // The showing tab is light with dark text, like the other buttons; the hidden one is dark with light text.
        private static void StyleTab(Button tab, bool showing)
        {
            tab.style.backgroundColor = showing ? TabActive : TabIdle;
            tab.style.color = showing ? Color.black : Muted;
            tab.style.unityFontStyleAndWeight = showing ? FontStyle.Bold : FontStyle.Normal;
        }

        private async Task Warm()
        {
            if (_model is not LocalScriptModel local) return;
            if (!_generating) SetAssistantStatus("Loading the language model...", false);
            try
            {
                await local.Warm();
                if (!_generating) SetAssistantStatus("Ready.", false);
            }
            catch (Exception error)
            {
                // Forgotten so the next visit to the tab tries again (for example after the model is downloaded).
                _warming = null;
                if (!_generating) SetAssistantStatus(error.Message, true);
            }
        }

        // Has the model write a program for the prompt and, when it parses, replaces the Script tab's draft with it and shows
        // that tab. The result is dropped if the screen has moved on to another employee meanwhile.
        public async void ClickGenerate()
        {
            if (_generating || _shown == null) return;
            var employee = _shown;
            _generating = true;
            _generate.SetEnabled(false);
            SetAssistantStatus("Writing the script...", false);
            try
            {
                var result = await new ScriptAssistant(_model).Generate(_prompt.value, x => SetAssistantStatus(x, false));
                Debug.Log($"[Assistant] \"{_prompt.value}\": {(result.Success ? "accepted" : "failed")} after {result.Attempts} attempt(s)\n" +
                          string.Join("\n", result.Tries.Select((x, i) => $"--- attempt {i + 1}: {x.Error ?? "ok"}\n{x.Source}")));
                if (_shown != employee) return;
                if (!result.Success)
                {
                    SetAssistantStatus(result.Error, true);
                    return;
                }
                _source.value = result.Source;
                SetAssistantStatus(result.Attempts == 1 ? "Script written to the Script tab. Check it, then press Run."
                    : $"Script written to the Script tab after {result.Attempts} attempts. Check it, then press Run.", false);
                ShowAssistant(false);
            }
            catch (Exception error)
            {
                SetAssistantStatus($"Error: {error.Message}", true);
            }
            finally
            {
                _generating = false;
            }
        }

        private void SetAssistantStatus(string text, bool error)
        {
            _assistantStatus.text = text;
            _assistantStatus.style.color = error ? Error : Muted;
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

        // Replaces the showing text box's remembered selection (or inserts at its caret) with text and puts the caret after it.
        public void Insert(string text)
        {
            var field = Active;
            var value = field.value ?? "";
            var start = Mathf.Clamp(Mathf.Min(_caret, _anchor), 0, value.Length);
            var end = Mathf.Clamp(Mathf.Max(_caret, _anchor), 0, value.Length);
            field.value = value.Substring(0, start) + text + value.Substring(end);
            _caret = _anchor = start + text.Length;
            var caret = _caret;
            field.schedule.Execute(() =>
            {
                field.Focus();
                field.SelectRange(caret, caret);
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

        // A dark multiline text box with a wide caret bar, since UI Toolkit draws its own caret 1 px wide. No select-all on
        // focus: a world pick inserts at the caret and must never replace the whole text.
        private TextField Editor(string name, float height, out VisualElement caretBar)
        {
            var field = new TextField { name = name, multiline = true, selectAllOnFocus = false, selectAllOnMouseUp = false };
            field.style.height = height;
            field.style.marginTop = 6;
            field.style.marginLeft = field.style.marginRight = 0;
            field.style.whiteSpace = WhiteSpace.Normal;
            var input = field.Q(TextField.textInputUssName);
            if (input != null)
            {
                input.style.backgroundColor = Inset;
                input.style.color = Color.white;
                input.style.unityTextAlign = TextAnchor.UpperLeft;
                input.style.fontSize = 13;
            }
            field.textSelection.cursorColor = Color.white;
            field.textSelection.selectionColor = Selection;
            caretBar = new VisualElement { name = name + "-caret", pickingMode = PickingMode.Ignore };
            caretBar.style.position = Position.Absolute;
            caretBar.style.width = CaretWidth;
            caretBar.style.height = CaretHeight;
            caretBar.style.backgroundColor = Color.white;
            caretBar.style.display = DisplayStyle.None;
            field.Q<TextElement>()?.Add(caretBar);
            // The left click that opened the screen lands on the text box under the freed pointer; that press must not focus it.
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
