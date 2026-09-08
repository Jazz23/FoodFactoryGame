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
    private InputField anchorXInput = null!;
    private InputField anchorYInput = null!;
    private InputField widthInput = null!;
    private InputField heightInput = null!;
    private InputField storyCountInput = null!;
    private Toggle includeEntranceToggle = null!;
    private InputField machineXInput = null!;
    private InputField machineYInput = null!;
    private Text machineSelectionText = null!;
    private Text machineStatusText = null!;
    private Button addMachineButton = null!;
    private Button removeMachineButton = null!;
    private Button nextMachineButton = null!;
    private uint machineBuildingId;
    private int machineFloorIndex = -1;
    private uint selectedMachineId;
    private Transform selectorRoot = null!;
    private InputAction toggle = null!;
    private InputAction cancel = null!;
    private InputAction moveMarker = null!;
    private GameObject createdEventSystem = null!;
    private string statusMessage = "Waiting for server state";
    private string selectorFingerprint = string.Empty;
    private uint selectedBuildingInstanceId;
    private int selectedFloor;
    private bool initialized;
    private bool isOpen = true;

    public bool IsOpen => isOpen;

    public void SetMachineEditResult(string message)
    {
        machineStatusText.text = message;
        RefreshMachines();
    }

    private void AddMachineClicked()
    {
        if (!float.TryParse(machineXInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !float.TryParse(machineYInput.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            machineStatusText.text = "X and Y must be finite numbers.";
            return;
        }

        machineStatusText.text = "Adding machine...";
        owner.RequestAddCurrentFloorMachine(new Vector2(x, y));
    }

    private void RemoveMachineClicked()
    {
        machineStatusText.text = "Removing machine...";
        owner.RequestRemoveCurrentFloorMachine(selectedMachineId);
    }

    private void NextMachineClicked()
    {
        RefreshMachines();
        if (!GameSceneManager.Instance.CanEditCurrentFloorMachines(owner)
            || !GameSceneManager.Instance.TryGetOutsideTestFloorState(machineBuildingId, machineFloorIndex, out var floor)
            || floor.Entities.Count == 0)
        {
            return;
        }

        var nextIndex = 0;
        for (var index = 0; index < floor.Entities.Count; index++)
        {
            if (floor.Entities[index].EntityId == selectedMachineId)
            {
                nextIndex = (index + 1) % floor.Entities.Count;
                break;
            }
        }

        selectedMachineId = floor.Entities[nextIndex].EntityId;
        RefreshMachines();
    }

    private void RefreshMachines()
    {
        var canEdit = GameSceneManager.Instance.CanEditCurrentFloorMachines(owner);
        owner.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        if (buildingId != machineBuildingId || floorIndex != machineFloorIndex || !canEdit)
        {
            selectedMachineId = 0;
            machineBuildingId = buildingId;
            machineFloorIndex = floorIndex;
        }

        var selected = false;
        var count = 0;
        machineSelectionText.text = canEdit ? "No machine selected" : "Enter a floor as host to edit machines";
        if (canEdit && GameSceneManager.Instance.TryGetOutsideTestFloorState(buildingId, floorIndex, out var floor))
        {
            count = floor.Entities.Count;
            foreach (var entity in floor.Entities)
            {
                if (entity.EntityId == selectedMachineId)
                {
                    selected = true;
                    machineSelectionText.text = $"B{buildingId}/F{floorIndex} — {entity.EntityId}: {entity.DefinitionId}";
                    break;
                }
            }
        }

        if (!selected)
        {
            selectedMachineId = 0;
        }

        machineXInput.interactable = canEdit;
        machineYInput.interactable = canEdit;
        addMachineButton.interactable = canEdit;
        nextMachineButton.interactable = canEdit && count > 0;
        removeMachineButton.interactable = canEdit && selected;
    }

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

    public void SelectFloor(uint buildingInstanceId, int floorIndex)
    {
        if (!GameSceneManager.Instance.IsValidOutsideTestFloor(
                buildingInstanceId,
                floorIndex))
        {
            return;
        }

        selectedBuildingInstanceId = buildingInstanceId;
        selectedFloor = floorIndex;
        statusMessage = $"Selected building {buildingInstanceId}, floor {selectedFloor}";
        Refresh();
    }

    public void SetBuildingCreationResult(
        bool created,
        uint buildingInstanceId,
        string error)
    {
        statusMessage = created
            ? $"Created building {buildingInstanceId}."
            : string.IsNullOrWhiteSpace(error)
                ? "Building creation failed."
                : error;
        if (created)
        {
            SelectBuilding(buildingInstanceId);
            return;
        }

        Refresh();
    }

    private void SelectBuilding(uint buildingInstanceId)
    {
        if (!GameSceneManager.Instance.TryGetOutsideTestBuildingInfo(
                buildingInstanceId,
                out var info))
        {
            return;
        }

        selectedBuildingInstanceId = buildingInstanceId;
        selectedFloor = Mathf.Clamp(selectedFloor, 0, info.StoryCount - 1);
        statusMessage = $"Selected building {buildingInstanceId}";
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
        if (!isOpen || labelInput.isFocused || rateInput.isFocused
            || machineXInput.isFocused || machineYInput.isFocused)
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
                selectedBuildingInstanceId,
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
            selectedBuildingInstanceId,
            selectedFloor,
            label,
            productionRate,
            markerPosition);
        statusMessage = "Marker edit sent";
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
                selectedBuildingInstanceId,
                selectedFloor,
                out var floor)
            || !TryReadEditorState(out var label, out var productionRate))
        {
            return false;
        }

        owner.RequestSetOutsideTestFloorState(
            selectedBuildingInstanceId,
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

        RefreshSelectionButtons();
        RefreshMachines();
        var hasState = GameSceneManager.Instance.TryGetOutsideTestFloorState(
            selectedBuildingInstanceId,
            selectedFloor,
            out var selectedState);
        var isInside = owner.TryGetCurrentOutsideTestFloor(
            out var currentBuilding,
            out var currentFloor);
        titleText.text = isInside
            ? $"OUTSIDETEST DEBUG  |  INSIDE B{currentBuilding} / F{currentFloor}"
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
        foreach (var buildingInstanceId in GameSceneManager.Instance.GetRegisteredOutsideTestBuildingIds())
        {
            if (!GameSceneManager.Instance.TryGetOutsideTestBuildingInfo(
                    buildingInstanceId,
                    out var info))
            {
                continue;
            }

            for (var floorIndex = 0; floorIndex < info.StoryCount; floorIndex++)
            {
                if (!GameSceneManager.Instance.TryGetOutsideTestFloorState(
                        buildingInstanceId,
                        floorIndex,
                        out var floor))
                {
                    summary += $"B{buildingInstanceId}/F{floorIndex}: waiting\n";
                    continue;
                }

                summary += $"B{buildingInstanceId}/F{floorIndex} {floor.Label}: "
                    + $"{floor.AccumulatedProduction.ToString("0.0", CultureInfo.InvariantCulture)} "
                    + $"@ {floor.ProductionRate.ToString("0.###", CultureInfo.InvariantCulture)} / sec\n";
            }
        }

        summaryText.text = summary.TrimEnd();
        var authoritativeError = GameSceneManager.Instance.LastOutsideTestError;
        statusText.text = string.IsNullOrEmpty(authoritativeError)
            ? $"{statusMessage}\nLoaded interiors: {GameSceneManager.Instance.OutsideTestLoadedInteriorCount}"
            : $"{statusMessage}\n{authoritativeError}";
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
        SetTopRect(root.GetComponent<RectTransform>(), 16f, 16f, 420f, 700f);

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

        selectorRoot = new GameObject(
            "Building and Floor Selectors",
            typeof(RectTransform)).transform;
        selectorRoot.SetParent(root.transform, false);
        SetTopRect(selectorRoot.GetComponent<RectTransform>(), 14f, 252f, 392f, 70f);

        CreateTextLabel(root.transform, "Label", "LABEL", 14f, 332f);
        labelInput = CreateInputField(
            root.transform,
            "Label Input",
            string.Empty,
            90f,
            326f,
            316f,
            34f,
            InputField.ContentType.Standard);

        CreateTextLabel(root.transform, "Rate", "RATE", 14f, 372f);
        rateInput = CreateInputField(
            root.transform,
            "Rate Input",
            string.Empty,
            90f,
            366f,
            316f,
            34f,
            InputField.ContentType.DecimalNumber);

        CreateTextLabel(root.transform, "Marker", "MARKER", 14f, 414f);
        CreateButton(
            "Marker Left",
            "<",
            root.transform,
            90f,
            408f,
            52f,
            32f,
            () => MoveMarker(new Vector2(-0.25f, 0f)));
        CreateButton(
            "Marker Down",
            "v",
            root.transform,
            148f,
            408f,
            52f,
            32f,
            () => MoveMarker(new Vector2(0f, -0.25f)));
        CreateButton(
            "Marker Up",
            "^",
            root.transform,
            206f,
            408f,
            52f,
            32f,
            () => MoveMarker(new Vector2(0f, 0.25f)));
        CreateButton(
            "Marker Right",
            ">",
            root.transform,
            264f,
            408f,
            52f,
            32f,
            () => MoveMarker(new Vector2(0.25f, 0f)));

        CreateButton(
            "Apply Edits",
            "APPLY",
            root.transform,
            322f,
            408f,
            84f,
            32f,
            ApplyEditsClicked);

        CreateTextLabel(root.transform, "Create Anchor X", "ANCHOR X", 14f, 494f);
        anchorXInput = CreateInputField(
            root.transform,
            "Anchor X Input",
            "0",
            90f,
            488f,
            62f,
            32f,
            InputField.ContentType.IntegerNumber);
        CreateTextLabel(root.transform, "Create Anchor Y", "ANCHOR Y", 164f, 494f);
        anchorYInput = CreateInputField(
            root.transform,
            "Anchor Y Input",
            "0",
            240f,
            488f,
            62f,
            32f,
            InputField.ContentType.IntegerNumber);
        CreateTextLabel(root.transform, "Create Width", "WIDTH", 14f, 532f);
        widthInput = CreateInputField(
            root.transform,
            "Width Input",
            "4",
            90f,
            526f,
            62f,
            32f,
            InputField.ContentType.IntegerNumber);
        CreateTextLabel(root.transform, "Create Height", "HEIGHT", 164f, 532f);
        heightInput = CreateInputField(
            root.transform,
            "Height Input",
            "4",
            240f,
            526f,
            62f,
            32f,
            InputField.ContentType.IntegerNumber);
        CreateTextLabel(root.transform, "Create Stories", "STORIES", 314f, 532f);
        storyCountInput = CreateInputField(
            root.transform,
            "Story Count Input",
            "2",
            350f,
            526f,
            56f,
            32f,
            InputField.ContentType.IntegerNumber);
        includeEntranceToggle = CreateToggle(
            root.transform,
            "Include Entrance",
            "INCLUDE ENTRANCE",
            14f,
            566f,
            180f,
            30f);
        CreateButton(
            "Create Building",
            "CREATE BUILDING",
            root.transform,
            14f,
            600f,
            392f,
            34f,
            CreateBuildingClicked);
        CreateButton(
            "Save State",
            "SAVE",
            root.transform,
            14f,
            642f,
            188f,
            34f,
            SaveClicked);
        CreateButton(
            "Load State",
            "LOAD",
            root.transform,
            218f,
            642f,
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
        SetTopRect(statusText.rectTransform, 14f, 446f, 392f, 40f);
        var machines = CreateImage("Current Floor Machines", root.transform,
            new Color(0.025f, 0.055f, 0.075f, 0.94f));
        SetTopRect(machines.GetComponent<RectTransform>(), 434f, 0f, 310f, 272f);
        var machineTitle = CreateText("Machine Title", machines.transform, "CURRENT FLOOR MACHINES", 16,
            TextAnchor.MiddleLeft, new Color(0.78f, 1f, 0.9f));
        SetTopRect(machineTitle.rectTransform, 12f, 10f, 286f, 28f);
        CreateTextLabel(machines.transform, "Machine X Label", "X", 12f, 46f);
        machineXInput = CreateInputField(machines.transform, "Machine X Input", "1", 38f, 42f, 95f, 32f,
            InputField.ContentType.DecimalNumber);
        CreateTextLabel(machines.transform, "Machine Y Label", "Y", 157f, 46f);
        machineYInput = CreateInputField(machines.transform, "Machine Y Input", "1", 183f, 42f, 115f, 32f,
            InputField.ContentType.DecimalNumber);
        addMachineButton = CreateButton("Add Test Machine", "ADD TEST MACHINE", machines.transform,
            12f, 84f, 286f, 32f, AddMachineClicked);
        machineSelectionText = CreateText("Machine Selection", machines.transform, string.Empty, 13,
            TextAnchor.MiddleLeft, Color.white);
        SetTopRect(machineSelectionText.rectTransform, 12f, 122f, 286f, 44f);
        nextMachineButton = CreateButton("Next Machine", "SELECT NEXT", machines.transform,
            12f, 172f, 134f, 32f, NextMachineClicked);
        removeMachineButton = CreateButton("Remove Machine", "REMOVE SELECTED", machines.transform,
            152f, 172f, 146f, 32f, RemoveMachineClicked);
        machineStatusText = CreateText("Machine Status", machines.transform, "Select a machine to remove it.", 12,
            TextAnchor.UpperLeft, new Color(0.6f, 0.78f, 0.76f));
        SetTopRect(machineStatusText.rectTransform, 12f, 214f, 286f, 48f);
        root.SetActive(true);
    }

    private void CreateBuildingClicked()
    {
        if (!int.TryParse(anchorXInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var anchorX)
            || !int.TryParse(anchorYInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var anchorY)
            || !int.TryParse(widthInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(heightInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height)
            || !int.TryParse(storyCountInput.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var storyCount))
        {
            statusMessage = "Anchor, footprint, and story values must be integers.";
            return;
        }

        owner.RequestCreateOutsideTestBuilding(
            new Vector3Int(anchorX, anchorY, 0),
            new Vector2Int(width, height),
            storyCount,
            includeEntranceToggle.isOn);
        statusMessage = "Building creation requested";
    }

    private void RefreshSelectionButtons()
    {
        var buildingIds = GameSceneManager.Instance.GetRegisteredOutsideTestBuildingIds();
        if (buildingIds.Length == 0)
        {
            return;
        }

        if (!GameSceneManager.Instance.IsValidOutsideTestFloor(
                selectedBuildingInstanceId,
                selectedFloor))
        {
            selectedBuildingInstanceId = buildingIds[0];
            selectedFloor = 0;
        }

        var fingerprint = $"selected:{selectedBuildingInstanceId}|"
            + buildingIds.Length.ToString(CultureInfo.InvariantCulture);
        foreach (var buildingId in buildingIds)
        {
            GameSceneManager.Instance.TryGetOutsideTestBuildingInfo(
                buildingId,
                out var info);
            fingerprint += $"|{buildingId}:{info.StoryCount}";
        }

        if (fingerprint == selectorFingerprint)
        {
            return;
        }

        selectorFingerprint = fingerprint;
        for (var index = selectorRoot.childCount - 1; index >= 0; index--)
        {
            Destroy(selectorRoot.GetChild(index).gameObject);
        }

        for (var index = 0; index < buildingIds.Length; index++)
        {
            var buildingId = buildingIds[index];
            var buildingButton = CreateButton(
                $"Building {buildingId} Button",
                $"BUILDING {buildingId}",
                selectorRoot,
                index * 126f,
                0f,
                120f,
                30f,
                () => SelectBuilding(buildingId));
            buildingButton.GetComponent<Image>().color = new Color(0.12f, 0.25f, 0.28f, 1f);
        }

        GameSceneManager.Instance.TryGetOutsideTestBuildingInfo(
            selectedBuildingInstanceId,
            out var selectedInfo);
        for (var floorIndex = 0; floorIndex < selectedInfo.StoryCount; floorIndex++)
        {
            var capturedFloorIndex = floorIndex;
            var floorButton = CreateButton(
                $"Floor {floorIndex} Button",
                $"FLOOR {floorIndex}",
                selectorRoot,
                floorIndex * 126f,
                34f,
                120f,
                30f,
                () => SelectFloor(selectedBuildingInstanceId, capturedFloorIndex));
            floorButton.GetComponent<Image>().color = new Color(0.16f, 0.3f, 0.32f, 1f);
        }
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

    private static Toggle CreateToggle(
        Transform parent,
        string name,
        string value,
        float x,
        float y,
        float width,
        float height)
    {
        var toggleObject = CreateImage(
            name,
            parent,
            new Color(0.1f, 0.15f, 0.17f, 1f));
        SetTopRect(toggleObject.GetComponent<RectTransform>(), x, y, width, height);
        var toggle = toggleObject.AddComponent<Toggle>();
        toggle.targetGraphic = toggleObject.GetComponent<Image>();
        var checkmarkObject = CreateImage(
            "Checkmark",
            toggleObject.transform,
            new Color(0.25f, 0.85f, 0.68f, 1f));
        SetTopRect(checkmarkObject.GetComponent<RectTransform>(), 7f, 5f, 20f, 20f);
        toggle.graphic = checkmarkObject.GetComponent<Image>();
        toggle.isOn = true;
        var text = CreateText(
            "Text",
            toggleObject.transform,
            value,
            12,
            TextAnchor.MiddleLeft,
            new Color(0.9f, 0.98f, 0.96f));
        SetTopRect(text.rectTransform, 34f, 0f, width - 34f, height);
        return toggle;
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
