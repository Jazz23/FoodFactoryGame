// Local player's employee script screen in UI Toolkit, built in code: a text box for a Lua program, Run/Stop/Close, the
// employee's replicated status line and a short API reference. Opened by clicking an employee (EquipmentInteraction);
// Run and Stop are requests the server checks (EmployeeWorker). Presentation only: the text box is this client's draft.
using FoodFactoryGame.Session.Equipment;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Employees
{
    [DisallowMultipleComponent]
    public sealed class EmployeeScriptPanel : MonoBehaviour
    {
        private const string Reference =
            "move_to(place)  ·  take(place, item, amount)  ·  put(place, item, amount)  ·  wait(seconds)\n" +
            "count(place, item)  ·  find(kind)  ·  carrying()  ·  say(text) / print(text)\n" +
            "place: \"storage\", a machine kind (\"oven\", \"fridge\", \"counter\"), a machine ID, or \"<id>:in\" / \"<id>:out\". " +
            "take() and put() walk there first; amount and item may be left out (as many as fit / any item).";

        private const string Starter = "-- Carry dough from the storage into the nearest oven.\nlocal n = take(\"storage\", \"dough\", 5)\nsay(\"took \" .. n .. \" dough\")\nput(\"oven\", \"dough\")\n";

        private static readonly Color Backdrop = new(0.19f, 0.19f, 0.2f, 0.97f);
        private static readonly Color Inset = new(0.11f, 0.11f, 0.12f, 1f);
        private static readonly Color Heading = new(1f, 0.9f, 0.74f, 1f);
        private static readonly Color Muted = new(0.7f, 0.7f, 0.75f, 1f);
        private static readonly Color Error = new(0.95f, 0.45f, 0.4f, 1f);

        [SerializeField] private UIDocument document;
        [SerializeField] private EquipmentInteraction interaction;

        private VisualElement _window;
        private Label _title;
        private TextField _source;
        private Label _status;
        private EmployeeWorker _shown;

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

            _source = new TextField { name = "employee-script-source", multiline = true };
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
            buttons.Add(Button("employee-script-close", "Close", interaction.CloseScreen));
            _window.Add(buttons);

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
            var employee = interaction.Screen == InteractionScreen.Employee ? interaction.OpenEmployee : null;
            _window.style.display = employee != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (employee != _shown)
            {
                _shown = employee;
                if (employee == null) return;
                _title.text = $"{employee.DisplayName} ({employee.EmployeeId}): Lua script";
                _source.value = string.IsNullOrEmpty(employee.Source) ? Starter : employee.Source;
                _source.schedule.Execute(() => _source.Focus());
            }
            if (employee == null) return;
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
