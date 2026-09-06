// Provides temporary local controls for inspecting and editing the OutsideTest floor proof of concept.
using System.Globalization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public sealed class OutsideTestFloorDebugPanel : MonoBehaviour
{
    private PlayerSceneTransition owner = null!;
    private GameObject canvasObject = null!;
    private GameObject root = null!;
    private Text titleText = null!;
    private Text stateText = null!;
    private Text summaryText = null!;
    private Text statusText = null!;
    private InputField labelInput = null!;
    private InputField rateInput = null!;
    private InputAction toggle = null!;
    private InputAction cancel = null!;
    private InputAction moveMarker = null!;
    private GameObject createdEventSystem = null!;
    private string statusMessage = "Waiting for server state";
    private int selectedFloor;
    private bool initialized;
    private bool isOpen = true;

    public bool IsOpen => isOpen;

    public void Initialize(PlayerSceneTransition newOwner)
    {
        if (initialized)
        {
            return;
        }

        owner = newOwner;
        CreateEventSystem();
        CreateInterface();
        toggle = InputSystem.actions.FindAction("OutsideTest/ToggleDebug", true);
        cancel = InputSystem.actions.FindAction("UI/Cancel", true);
        moveMarker = InputSystem.actions.FindAction("OutsideTest/MoveMarker", true);
        toggle.performed += TogglePerformed;
        cancel.performed += CancelPerformed;
        moveMarker.performed += MoveMarkerPerformed;
        toggle.Enable();
        cancel.Enable();
        moveMarker.Enable();
        initialized = true;
        owner.RequestOutsideTestFloorSnapshot();
        Refresh();
    }

    public void SelectFloor(int floorIndex)
    {
        selectedFloor = Mathf.Clamp(
            floorIndex,
            0,
            GameSceneManager.OutsideTestFloorCount - 1);
        statusMessage = $"Selected floor {selectedFloor}";
        Refresh();
    }

    private void Update()
    {
        if (initialized)
        {
            Refresh();
        }
    }

    private void OnDestroy()
    {
        if (!initialized)
        {
            return;
        }

        toggle.performed -= TogglePerformed;
        cancel.performed -= CancelPerformed;
        moveMarker.performed -= MoveMarkerPerformed;
        toggle.Disable();
        cancel.Disable();
        moveMarker.Disable();
        if (canvasObject is not null && canvasObject)
        {
            Destroy(canvasObject);
        }

        if (createdEventSystem is not null && createdEventSystem)
        {
            Destroy(createdEventSystem);
        }
    }

    private void TogglePerformed(InputAction.CallbackContext _)
    {
        isOpen = !isOpen;
        root.SetActive(isOpen);
        if (isOpen)
        {
            Refresh();
        }
    }

    private void CancelPerformed(InputAction.CallbackContext _)
    {
        if (!isOpen)
        {
            return;
        }

        isOpen = false;
        root.SetActive(false);
    }

    private void MoveMarkerPerformed(InputAction.CallbackContext context)
    {
        if (!isOpen || labelInput.isFocused || rateInput.isFocused)
        {
            return;
        }

        var input = context.ReadValue<Vector2>();
        if (input.sqrMagnitude < 0.25f)
        {
            return;
        }

        var delta = new Vector2(
            Mathf.Abs(input.x) > 0.5f ? Mathf.Sign(input.x) * 0.25f : 0f,
            Mathf.Abs(input.y) > 0.5f ? Mathf.Sign(input.y) * 0.25f : 0f);
        MoveMarker(delta);
    }

    private void MoveMarker(Vector2 delta)
    {
        if (!GameSceneManager.Instance.TryGetOutsideTestFloorState(
                selectedFloor,
                out var floor))
        {
            return;
        }

        if (!TryReadEditorState(out var label, out var productionRate))
        {
            return;
        }

        var markerPosition = floor.MarkerPosition + delta;
        owner.RequestSetOutsideTestFloorState(
            selectedFloor,
            label,
            productionRate,
            markerPosition);
        statusMessage = "Marker edit sent";
    }

    private void SelectFloorButton(int floorIndex)
    {
        SelectFloor(floorIndex);
    }

    private void ApplyEditsClicked()
    {
        if (!TrySubmitEditorState())
        {
            return;
        }

        statusMessage = "Floor edit sent";
    }

    private void SaveClicked()
    {
        if (!TrySubmitEditorState())
        {
            return;
        }

        owner.RequestSaveOutsideTestFloorState();
        statusMessage = "Save requested";
    }

    private void LoadClicked()
    {
        owner.RequestLoadOutsideTestFloorState();
        statusMessage = "Load requested";
    }

    private bool TrySubmitEditorState()
    {
        if (!GameSceneManager.Instance.TryGetOutsideTestFloorState(
                selectedFloor,
                out var floor)
            || !TryReadEditorState(out var label, out var productionRate))
        {
            return false;
        }

        owner.RequestSetOutsideTestFloorState(
            selectedFloor,
            label,
            productionRate,
            floor.MarkerPosition);
        return true;
    }

    private bool TryReadEditorState(out string label, out float productionRate)
    {
        label = labelInput.text.Trim();
        if (!float.TryParse(
                rateInput.text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out productionRate)
            || float.IsNaN(productionRate)
            || float.IsInfinity(productionRate))
        {
            statusMessage = "Rate must be a finite number";
            return false;
        }

        return true;
    }

    private void Refresh()
    {
        if (!isOpen)
        {
            return;
        }

        var hasState = GameSceneManager.Instance.TryGetOutsideTestFloorState(
            selectedFloor,
            out var selectedState);
        var isInside = owner.TryGetCurrentOutsideTestFloor(out var currentFloor);
        titleText.text = isInside
            ? $"OUTSIDETEST DEBUG  |  INSIDE FLOOR {currentFloor}"
            : "OUTSIDETEST DEBUG  |  OUTSIDE";
        stateText.text = hasState
            ? $"SELECTED: BUILDING {selectedState.BuildingInstanceId} / FLOOR {selectedState.FloorIndex}\n"
                + $"LABEL: {selectedState.Label}\n"
                + $"RATE: {selectedState.ProductionRate.ToString("0.###", CultureInfo.InvariantCulture)} / sec\n"
                + $"TOTAL: {selectedState.AccumulatedProduction.ToString("0.0", CultureInfo.InvariantCulture)}\n"
                + $"MARKER: ({selectedState.MarkerPosition.x:0.00}, {selectedState.MarkerPosition.y:0.00})"
            : "SELECTED FLOOR: waiting for authoritative state";

        if (hasState)
        {
            if (!labelInput.isFocused)
            {
                labelInput.text = selectedState.Label;
            }

            if (!rateInput.isFocused)
            {
                rateInput.text = selectedState.ProductionRate.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);
            }
        }

        var summary = "FLOOR TOTALS\n";
        for (var floorIndex = 0;
            floorIndex < GameSceneManager.OutsideTestFloorCount;
            floorIndex++)
        {
            if (!GameSceneManager.Instance.TryGetOutsideTestFloorState(
                    floorIndex,
                    out var floor))
            {
                summary += $"F{floorIndex}: waiting\n";
                continue;
            }

            summary += $"F{floorIndex} {floor.Label}: "
                + $"{floor.AccumulatedProduction.ToString("0.0", CultureInfo.InvariantCulture)} "
                + $"@ {floor.ProductionRate.ToString("0.###", CultureInfo.InvariantCulture)} / sec\n";
        }

        summaryText.text = summary.TrimEnd();
        statusText.text = $"{statusMessage}\nLoaded interiors: {GameSceneManager.Instance.OutsideTestLoadedInteriorCount}";
    }

    private void CreateEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() is not null)
        {
            return;
        }

        createdEventSystem = new GameObject(
            "OutsideTest EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        createdEventSystem.transform.SetParent(transform, false);
    }

    private void CreateInterface()
    {
        canvasObject = new GameObject(
            "OutsideTest Debug Canvas",
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);

        root = CreateImage(
            "OutsideTest Debug Panel",
            canvasObject.transform,
            new Color(0.025f, 0.055f, 0.075f, 0.94f));
        SetTopRect(root.GetComponent<RectTransform>(), 16f, 16f, 420f, 510f);

        titleText = CreateText(
            "Title",
            root.transform,
            string.Empty,
            17,
            TextAnchor.MiddleLeft,
            new Color(0.78f, 1f, 0.9f));
        SetTopRect(titleText.rectTransform, 14f, 10f, 392f, 26f);

        stateText = CreateText(
            "Selected State",
            root.transform,
            string.Empty,
            13,
            TextAnchor.UpperLeft,
            new Color(0.9f, 0.94f, 0.96f));
        SetTopRect(stateText.rectTransform, 14f, 40f, 392f, 105f);

        summaryText = CreateText(
            "Floor Summary",
            root.transform,
            string.Empty,
            12,
            TextAnchor.UpperLeft,
            new Color(0.66f, 0.82f, 0.8f));
        SetTopRect(summaryText.rectTransform, 14f, 148f, 392f, 100f);

        for (var floorIndex = 0;
            floorIndex < GameSceneManager.OutsideTestFloorCount;
            floorIndex++)
        {
            var capturedFloorIndex = floorIndex;
            var button = CreateButton(
                $"Floor {floorIndex} Button",
                $"FLOOR {floorIndex}",
                root.transform,
                14f + floorIndex * 132f,
                252f,
                124f,
                30f,
                () => SelectFloorButton(capturedFloorIndex));
            button.GetComponent<Image>().color = new Color(0.12f, 0.25f, 0.28f, 1f);
        }

        CreateTextLabel(root.transform, "Label", "LABEL", 14f, 292f);
        labelInput = CreateInputField(
            root.transform,
            "Label Input",
            string.Empty,
            90f,
            286f,
            316f,
            34f,
            InputField.ContentType.Standard);

        CreateTextLabel(root.transform, "Rate", "RATE", 14f, 332f);
        rateInput = CreateInputField(
            root.transform,
            "Rate Input",
            string.Empty,
            90f,
            326f,
            316f,
            34f,
            InputField.ContentType.DecimalNumber);

        CreateTextLabel(root.transform, "Marker", "MARKER", 14f, 374f);
        CreateButton(
            "Marker Left",
            "<",
            root.transform,
            90f,
            368f,
            52f,
            32f,
            () => MoveMarker(new Vector2(-0.25f, 0f)));
        CreateButton(
            "Marker Down",
            "v",
            root.transform,
            148f,
            368f,
            52f,
            32f,
            () => MoveMarker(new Vector2(0f, -0.25f)));
        CreateButton(
            "Marker Up",
            "^",
            root.transform,
            206f,
            368f,
            52f,
            32f,
            () => MoveMarker(new Vector2(0f, 0.25f)));
        CreateButton(
            "Marker Right",
            ">",
            root.transform,
            264f,
            368f,
            52f,
            32f,
            () => MoveMarker(new Vector2(0.25f, 0f)));

        CreateButton(
            "Apply Edits",
            "APPLY",
            root.transform,
            322f,
            368f,
            84f,
            32f,
            ApplyEditsClicked);
        CreateButton(
            "Save State",
            "SAVE",
            root.transform,
            14f,
            412f,
            188f,
            34f,
            SaveClicked);
        CreateButton(
            "Load State",
            "LOAD",
            root.transform,
            218f,
            412f,
            188f,
            34f,
            LoadClicked);

        statusText = CreateText(
            "Status",
            root.transform,
            string.Empty,
            11,
            TextAnchor.UpperLeft,
            new Color(0.6f, 0.78f, 0.76f));
        SetTopRect(statusText.rectTransform, 14f, 454f, 392f, 44f);
        root.SetActive(true);
    }

    private static InputField CreateInputField(
        Transform parent,
        string name,
        string value,
        float x,
        float y,
        float width,
        float height,
        InputField.ContentType contentType)
    {
        var inputObject = CreateImage(
            name,
            parent,
            new Color(0.1f, 0.15f, 0.17f, 1f));
        SetTopRect(inputObject.GetComponent<RectTransform>(), x, y, width, height);
        var input = inputObject.AddComponent<InputField>();
        input.contentType = contentType;
        input.text = value;
        var text = CreateText(
            "Text",
            inputObject.transform,
            value,
            14,
            TextAnchor.MiddleLeft,
            Color.white);
        Stretch(text.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));
        input.textComponent = text;
        var placeholder = CreateText(
            "Placeholder",
            inputObject.transform,
            "type here",
            14,
            TextAnchor.MiddleLeft,
            new Color(0.46f, 0.54f, 0.56f));
        Stretch(placeholder.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));
        input.placeholder = placeholder;
        return input;
    }

    private static Button CreateButton(
        string name,
        string value,
        Transform parent,
        float x,
        float y,
        float width,
        float height,
        UnityAction action)
    {
        var buttonObject = CreateImage(
            name,
            parent,
            new Color(0.16f, 0.28f, 0.3f, 1f));
        SetTopRect(buttonObject.GetComponent<RectTransform>(), x, y, width, height);
        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        button.onClick.AddListener(action);
        var text = CreateText(
            "Text",
            buttonObject.transform,
            value,
            13,
            TextAnchor.MiddleCenter,
            new Color(0.9f, 0.98f, 0.96f));
        Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
        return button;
    }

    private static void CreateTextLabel(
        Transform parent,
        string name,
        string value,
        float x,
        float y)
    {
        var text = CreateText(
            name,
            parent,
            value,
            12,
            TextAnchor.MiddleLeft,
            new Color(0.58f, 0.73f, 0.72f));
        SetTopRect(text.rectTransform, x, y, 70f, 24f);
    }

    private static GameObject CreateImage(
        string name,
        Transform parent,
        Color color)
    {
        var result = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image));
        result.transform.SetParent(parent, false);
        result.GetComponent<Image>().color = color;
        return result;
    }

    private static Text CreateText(
        string name,
        Transform parent,
        string value,
        int fontSize,
        TextAnchor alignment,
        Color color)
    {
        var result = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Text));
        result.transform.SetParent(parent, false);
        var text = result.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private static void SetTopRect(
        RectTransform rect,
        float x,
        float y,
        float width,
        float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    private static void Stretch(
        RectTransform rect,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }
}
