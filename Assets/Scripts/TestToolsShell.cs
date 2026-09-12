// Hosts the shared runtime uGUI shell for developer tools, network controls, and the build bar.
using System;
using System.Collections.Generic;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public enum TestToolsTab
{
    Network,
    Floors,
    Machines,
    Routes
}

public sealed class TestToolsShell : MonoBehaviour
{
    public static TestToolsShell Instance { get; private set; } = null!;

    public static readonly Color Charcoal = new(0.035f, 0.045f, 0.05f, 0.98f);
    public static readonly Color CharcoalRaised = new(0.075f, 0.09f, 0.1f, 1f);
    public static readonly Color CharcoalField = new(0.11f, 0.13f, 0.14f, 1f);
    public static readonly Color Teal = new(0.12f, 0.64f, 0.62f, 1f);
    public static readonly Color TealMuted = new(0.1f, 0.28f, 0.29f, 1f);
    public static readonly Color TextPrimary = new(0.91f, 0.96f, 0.95f, 1f);
    public static readonly Color TextSecondary = new(0.61f, 0.75f, 0.74f, 1f);
    public static readonly Color Destructive = new(0.48f, 0.16f, 0.17f, 1f);

    private readonly Dictionary<TestToolsTab, RectTransform> tabContents = new();
    private readonly Dictionary<TestToolsTab, GameObject> tabPages = new();
    private readonly Dictionary<TestToolsTab, Text> unavailableTexts = new();
    private readonly Dictionary<TestToolsTab, Button> tabButtons = new();
    private readonly List<Button> buildEquipmentButtons = new();
    private readonly List<Text> buildEquipmentLabels = new();

    private Canvas toolsCanvas = null!;
    private Canvas persistentCanvas = null!;
    private GameObject toolsCanvasObject = null!;
    private GameObject persistentCanvasObject = null!;
    private GameObject toolsPanel = null!;
    private GameObject recoveryPanel = null!;
    private Transform recoveryContent = null!;
    private Button launcherButton = null!;
    private Button collapseButton = null!;
    private Button buildButton = null!;
    private Button rotateButton = null!;
    private Button recoveryToggle = null!;
    private Text buildHintText = null!;
    private Text buildStatusText = null!;
    private Text recoveryTitleText = null!;
    private Text networkServerText = null!;
    private Text networkClientText = null!;
    private Text networkHintText = null!;
    private Text selectedTabText = null!;
    private InputField addressInput = null!;
    private InputAction floorToggle = null!;
    private InputAction routeToggle = null!;
    private InputAction cancel = null!;
    private EventSystem eventSystem = null!;
    private NetworkManager subscribedNetworkManager = null!;
    private NetworkManager networkManager = null!;
    private FactoryBuildController builder = null!;
    private PlayerSceneTransition player = null!;
    private bool expanded;
    private bool recoveryExpanded;
    private bool eventSubscribed;
    private TestToolsTab selectedTab = TestToolsTab.Network;
    private LocalConnectionState serverState = LocalConnectionState.Stopped;
    private LocalConnectionState clientState = LocalConnectionState.Stopped;
    private string lastAddress = string.Empty;
    private string recoveryFingerprint = string.Empty;
    private float nextNetworkRefresh;
    private float nextHudScan;

    public GameObject ToolsCanvasObject => toolsCanvasObject;
    public GameObject PersistentCanvasObject => persistentCanvasObject;
    public TestToolsTab SelectedTab => selectedTab;
    public bool IsExpanded => expanded;
    public bool IsFloorToolsVisible => expanded
        && selectedTab == TestToolsTab.Floors
        && TestUIVisibility.Visible;

    public event Action<TestToolsTab> TabChanged = delegate { };

    public static TestToolsShell GetOrCreate()
    {
        if (Instance is not null && Instance)
        {
            return Instance;
        }

        var shellObject = new GameObject("Test Tools Shell");
        DontDestroyOnLoad(shellObject);
        return shellObject.AddComponent<TestToolsShell>();
    }

    private void Awake()
    {
        if (Instance is not null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        CreateInterface();
        CreateActions();
        TestUIVisibility.VisibilityChanged += VisibilityChanged;
        eventSubscribed = true;
        ApplyVisibility();
    }

    private void Update()
    {
        EnsureInterface();
        EnsureSingleEventSystem();
        RefreshNetworkManager();
        if (Time.unscaledTime >= nextNetworkRefresh)
        {
            nextNetworkRefresh = Time.unscaledTime + 0.1f;
            RefreshNetworkUi();
            RefreshBuildUi();
        }

        if (Time.unscaledTime >= nextHudScan)
        {
            nextHudScan = Time.unscaledTime + 0.5f;
            SuppressLegacyHud();
        }

        RefreshRecoveryUi();
    }

    public void AttachTo(Transform parent)
    {
        EnsureInterface();
        var target = parent is not null ? parent : transform;
        toolsCanvasObject.transform.SetParent(target, false);
        persistentCanvasObject.transform.SetParent(target, false);
        toolsCanvasObject.transform.SetAsFirstSibling();
        persistentCanvasObject.transform.SetAsLastSibling();
    }

    public void BindPlayer(PlayerSceneTransition newPlayer)
    {
        player = newPlayer;
        AttachTo(newPlayer.transform);
        SetTabAvailability(TestToolsTab.Floors, true, string.Empty);
        SetTabAvailability(TestToolsTab.Machines, true, string.Empty);
        SetTabAvailability(TestToolsTab.Routes, true, string.Empty);
        RefreshNetworkManager();
        ApplyTabState();
    }

    public void UnbindPlayer(PlayerSceneTransition oldPlayer)
    {
        if (player != oldPlayer)
        {
            return;
        }

        player = null!;
        AttachTo(transform);
        SetTabAvailability(TestToolsTab.Floors, false, "Connect as the local player to inspect floors.");
        SetTabAvailability(TestToolsTab.Machines, false, "Connect as the local player to edit equipment.");
        SetTabAvailability(TestToolsTab.Routes, false, "Connect as the local host to manage truck routes.");
        ApplyTabState();
    }

    public void BindBuilder(FactoryBuildController newBuilder)
    {
        builder = newBuilder;
        recoveryFingerprint = string.Empty;
        RefreshBuildUi();
    }

    public void UnbindBuilder(FactoryBuildController oldBuilder)
    {
        if (builder == oldBuilder)
        {
            builder = null!;
            recoveryFingerprint = string.Empty;
            RefreshBuildUi();
        }
    }

    public Transform GetTabContent(TestToolsTab tab)
    {
        return tabContents[tab];
    }

    public void SetTabAvailability(TestToolsTab tab, bool available, string explanation)
    {
        if (!unavailableTexts.TryGetValue(tab, out var unavailableText))
        {
            return;
        }

        unavailableText.text = explanation;
        unavailableText.gameObject.SetActive(!available && selectedTab == tab && expanded);
        if (tabContents.TryGetValue(tab, out var content))
        {
            content.gameObject.SetActive(available && selectedTab == tab && expanded);
        }
    }

    public void OpenTab(TestToolsTab tab)
    {
        if (selectedTab != tab)
        {
            selectedTab = tab;
            TabChanged(tab);
        }

        expanded = true;
        ApplyTabState();
    }

    public void ToggleTab(TestToolsTab tab)
    {
        if (expanded && selectedTab == tab)
        {
            Collapse();
            return;
        }

        OpenTab(tab);
    }

    public void Collapse()
    {
        expanded = false;
        recoveryExpanded = false;
        EventSystem.current?.SetSelectedGameObject(null);
        ApplyTabState();
    }

    public void Expand()
    {
        expanded = true;
        ApplyTabState();
    }

    public static bool ContainsPointer(Vector2 guiPoint)
    {
        var panel = new Rect(Screen.width - 476f, 16f, 460f, Mathf.Max(260f, Screen.height - 160f));
        var launcher = new Rect(Screen.width - 164f, 8f, 148f, 44f);
        var buildBar = new Rect(12f, Screen.height - 124f, Screen.width - 24f, 112f);
        var recovery = new Rect(16f, Screen.height - 304f, 460f, 168f);
        return launcher.Contains(guiPoint) || buildBar.Contains(guiPoint) || recovery.Contains(guiPoint)
            || (Instance is not null && Instance.expanded && TestUIVisibility.Visible && panel.Contains(guiPoint));
    }

    public static bool IsTextInputFocused
    {
        get
        {
            var selected = EventSystem.current?.currentSelectedGameObject;
            return selected is not null
                && selected.TryGetComponent<InputField>(out var field)
                && field.isFocused;
        }
    }

    private void CreateInterface()
    {
        tabContents.Clear();
        tabPages.Clear();
        unavailableTexts.Clear();
        tabButtons.Clear();
        buildEquipmentButtons.Clear();
        buildEquipmentLabels.Clear();
        toolsCanvasObject = CreateCanvas("Test Tools Canvas", 221, out toolsCanvas);
        persistentCanvasObject = CreateCanvas("Test Tools Persistent Canvas", 220, out persistentCanvas);
        CreateToolsPanel();
        CreateNetworkContent();
        CreatePersistentControls();
        SetTabAvailability(TestToolsTab.Floors, false, "Connect as the local player to inspect floors.");
        SetTabAvailability(TestToolsTab.Machines, false, "Connect as the local player to edit equipment.");
        SetTabAvailability(TestToolsTab.Routes, false, "Connect as the local host to manage truck routes.");
        ApplyTabState();
    }

    private void EnsureInterface()
    {
        if (toolsCanvasObject is not null && toolsCanvasObject
            && persistentCanvasObject is not null && persistentCanvasObject)
        {
            return;
        }

        CreateInterface();
        ApplyVisibility();
    }

    private void CreateToolsPanel()
    {
        toolsPanel = CreateImage("Test Tools Panel", toolsCanvas.transform, Charcoal);
        SetTopRect(toolsPanel.GetComponent<RectTransform>(), 16f, 16f, 460f, 520f);

        var title = CreateText(
            "Test Tools Title",
            toolsPanel.transform,
            "TEST TOOLS",
            18,
            TextAnchor.MiddleLeft,
            TextPrimary);
        SetTopRect(title.rectTransform, 16f, 10f, 250f, 32f);

        selectedTabText = CreateText(
            "Selected Tab",
            toolsPanel.transform,
            "NETWORK",
            11,
            TextAnchor.MiddleLeft,
            TextSecondary);
        SetTopRect(selectedTabText.rectTransform, 16f, 39f, 240f, 18f);

        collapseButton = CreateButton(
            "Collapse Test Tools",
            "COLLAPSE",
            toolsPanel.transform,
            330f,
            12f,
            114f,
            32f,
            Collapse);
        collapseButton.GetComponent<Image>().color = TealMuted;

        var tabBar = CreateImage("Test Tools Tabs", toolsPanel.transform, CharcoalRaised);
        SetTopRect(tabBar.GetComponent<RectTransform>(), 16f, 64f, 428f, 38f);
        var tabNames = new[] { "NETWORK", "FLOORS", "MACHINES", "ROUTES" };
        for (var index = 0; index < tabNames.Length; index++)
        {
            var capturedTab = (TestToolsTab)index;
            var tabButton = CreateButton(
                $"{tabNames[index]} Tab",
                tabNames[index],
                tabBar.transform,
                index * 107f,
                0f,
                107f,
                38f,
                () => OpenTab(capturedTab));
            tabButtons[capturedTab] = tabButton;
        }

        var body = CreateImage("Test Tools Body", toolsPanel.transform, CharcoalRaised);
        SetTopRect(body.GetComponent<RectTransform>(), 16f, 110f, 428f, 394f);
        CreateTabPage(TestToolsTab.Network, body.transform);
        CreateTabPage(TestToolsTab.Floors, body.transform);
        CreateTabPage(TestToolsTab.Machines, body.transform);
        CreateTabPage(TestToolsTab.Routes, body.transform);
    }

    private void CreateTabPage(TestToolsTab tab, Transform parent)
    {
        var page = CreateImage($"{tab} Tab Page", parent, new Color(0f, 0f, 0f, 0f));
        Stretch(page.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
        page.GetComponent<Image>().raycastTarget = false;
        var scroll = page.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;

        var viewport = new GameObject(
            $"{tab} Viewport",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(RectMask2D));
        viewport.transform.SetParent(page.transform, false);
        var viewportRect = viewport.GetComponent<RectTransform>();
        Stretch(viewportRect, Vector2.zero, Vector2.zero);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
        viewport.GetComponent<Image>().raycastTarget = true;

        var content = new GameObject($"{tab} Content", typeof(RectTransform)).transform;
        content.SetParent(viewport.transform, false);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0f, 1f);
        contentRect.sizeDelta = new Vector2(0f, 760f);
        contentRect.anchoredPosition = Vector2.zero;
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        tabContents[tab] = contentRect;
        tabPages[tab] = page;

        var unavailable = CreateText(
            $"{tab} Unavailable",
            page.transform,
            string.Empty,
            14,
            TextAnchor.UpperLeft,
            TextSecondary);
        SetTopRect(unavailable.rectTransform, 16f, 18f, 396f, 72f);
        unavailable.gameObject.SetActive(false);
        unavailableTexts[tab] = unavailable;
    }

    private void CreateNetworkContent()
    {
        var content = tabContents[TestToolsTab.Network];
        var root = CreateImage("Network Controls", content, new Color(0f, 0f, 0f, 0f));
        root.GetComponent<Image>().raycastTarget = false;
        SetTopRect(root.GetComponent<RectTransform>(), 0f, 0f, 428f, 300f);

        var heading = CreateText("Network Heading", root.transform, "CONNECTION", 16,
            TextAnchor.MiddleLeft, Teal);
        SetTopRect(heading.rectTransform, 16f, 12f, 396f, 28f);
        networkServerText = CreateText("Server Status", root.transform, string.Empty, 14,
            TextAnchor.MiddleLeft, TextPrimary);
        SetTopRect(networkServerText.rectTransform, 16f, 50f, 396f, 28f);
        networkClientText = CreateText("Client Status", root.transform, string.Empty, 14,
            TextAnchor.MiddleLeft, TextPrimary);
        SetTopRect(networkClientText.rectTransform, 16f, 78f, 396f, 28f);

        var startServer = CreateButton("Start Server", "START SERVER", root.transform, 16f, 116f, 190f, 34f,
            ToggleServer);
        startServer.GetComponent<Image>().color = TealMuted;
        var startClient = CreateButton("Start Client", "START CLIENT", root.transform, 222f, 116f, 190f, 34f,
            ToggleClient);
        startClient.GetComponent<Image>().color = TealMuted;

        var addressLabel = CreateText("Server Address Label", root.transform, "SERVER ADDRESS", 12,
            TextAnchor.MiddleLeft, TextSecondary);
        SetTopRect(addressLabel.rectTransform, 16f, 164f, 150f, 24f);
        addressInput = CreateInputField(root.transform, "Server Address", "localhost", 16f, 190f, 396f, 36f,
            InputField.ContentType.Standard);
        networkHintText = CreateText("Network Hint", root.transform, string.Empty, 12,
            TextAnchor.UpperLeft, TextSecondary);
        SetTopRect(networkHintText.rectTransform, 16f, 238f, 396f, 52f);
    }

    private void CreatePersistentControls()
    {
        launcherButton = CreateButton(
            "Test Tools Launcher",
            "TEST TOOLS",
            persistentCanvas.transform,
            1116f,
            16f,
            148f,
            38f,
            LauncherClicked);
        launcherButton.GetComponent<Image>().color = TealMuted;

        var buildBar = CreateImage("Factory Build Bar", persistentCanvas.transform, Charcoal);
        SetBottomStretch(buildBar.GetComponent<RectTransform>(), 16f, 16f, 104f);
        var buildHeading = CreateText("Build Bar Heading", buildBar.transform, "BUILD", 13,
            TextAnchor.MiddleLeft, Teal);
        SetTopRect(buildHeading.rectTransform, 12f, 7f, 58f, 22f);
        buildButton = CreateButton("Build Toggle", "B: BUILD", buildBar.transform, 72f, 6f, 94f, 32f,
            ToggleBuild);
        buildButton.GetComponent<Image>().color = TealMuted;
        var labels = new[] { "SOURCE", "BELT", "STORAGE", "SHIP DOCK", "RECEIVE DOCK" };
        for (var index = 0; index < labels.Length; index++)
        {
            var capturedIndex = index;
            var button = CreateButton(
                $"Build {labels[index]}",
                labels[index],
                buildBar.transform,
                172f + index * 93f,
                6f,
                88f,
                32f,
                () => SelectBuildEquipment(capturedIndex));
            buildEquipmentButtons.Add(button);
            buildEquipmentLabels.Add(button.GetComponentInChildren<Text>());
        }

        rotateButton = CreateButton("Build Rotation", "R: EAST", buildBar.transform, 642f, 6f, 96f, 32f,
            RotateBuild);
        rotateButton.GetComponent<Image>().color = TealMuted;
        buildHintText = CreateText("Build Instructions", buildBar.transform, string.Empty, 11,
            TextAnchor.MiddleLeft, TextPrimary);
        SetTopRect(buildHintText.rectTransform, 12f, 48f, 950f, 22f);
        buildStatusText = CreateText("Build Status", buildBar.transform, string.Empty, 11,
            TextAnchor.MiddleLeft, TextSecondary);
        SetTopRect(buildStatusText.rectTransform, 12f, 73f, 1110f, 22f);
        recoveryToggle = CreateButton("Overflow Recovery", "RECOVER OVERFLOW", buildBar.transform, 1128f, 48f,
            128f, 42f, ToggleRecovery);
        recoveryToggle.GetComponent<Image>().color = Destructive;

        recoveryPanel = CreateImage("Overflow Recovery Panel", persistentCanvas.transform, CharcoalRaised);
        SetBottomRect(recoveryPanel.GetComponent<RectTransform>(), 16f, 128f, 460f, 176f);
        var recoveryHeader = CreateText("Overflow Recovery Heading", recoveryPanel.transform, "OVERFLOW RECOVERY",
            13, TextAnchor.MiddleLeft, Teal);
        SetTopRect(recoveryHeader.rectTransform, 12f, 8f, 300f, 24f);
        recoveryTitleText = CreateText("Overflow Recovery Count", recoveryPanel.transform, string.Empty, 11,
            TextAnchor.MiddleRight, TextSecondary);
        SetTopRect(recoveryTitleText.rectTransform, 306f, 8f, 140f, 24f);
        var viewport = new GameObject(
            "Overflow Recovery Viewport",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(RectMask2D));
        viewport.transform.SetParent(recoveryPanel.transform, false);
        SetTopRect(viewport.GetComponent<RectTransform>(), 8f, 38f, 444f, 128f);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
        var scroll = recoveryPanel.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.viewport = viewport.GetComponent<RectTransform>();
        recoveryContent = new GameObject("Overflow Recovery Content", typeof(RectTransform)).transform;
        recoveryContent.SetParent(viewport.transform, false);
        var recoveryRect = recoveryContent.GetComponent<RectTransform>();
        recoveryRect.anchorMin = new Vector2(0f, 1f);
        recoveryRect.anchorMax = new Vector2(1f, 1f);
        recoveryRect.pivot = new Vector2(0f, 1f);
        recoveryRect.sizeDelta = new Vector2(0f, 128f);
        scroll.content = recoveryRect;
        recoveryPanel.SetActive(false);
    }

    private void CreateActions()
    {
        floorToggle = InputSystem.actions.FindAction("OutsideTest/ToggleDebug", true).Clone();
        routeToggle = InputSystem.actions.FindAction("FactoryBuild/ToggleTruckRoutes", true).Clone();
        cancel = InputSystem.actions.FindAction("UI/Cancel", true).Clone();
        floorToggle.performed += FloorTogglePerformed;
        routeToggle.performed += RouteTogglePerformed;
        cancel.performed += CancelPerformed;
        floorToggle.Enable();
        routeToggle.Enable();
        cancel.Enable();
    }

    private void FloorTogglePerformed(InputAction.CallbackContext _)
    {
        if (!IsTextInputFocused)
        {
            ToggleTab(TestToolsTab.Floors);
        }
    }

    private void RouteTogglePerformed(InputAction.CallbackContext _)
    {
        if (!IsTextInputFocused)
        {
            ToggleTab(TestToolsTab.Routes);
        }
    }

    private void CancelPerformed(InputAction.CallbackContext _)
    {
        if (!IsTextInputFocused && expanded)
        {
            Collapse();
        }
    }

    private void LauncherClicked()
    {
        if (!TestUIVisibility.Visible)
        {
            TestUIVisibility.SetVisible(true);
        }

        if (expanded)
        {
            Collapse();
            return;
        }

        Expand();
    }

    private void ToggleBuild()
    {
        if (builder is not null)
        {
            builder.ToggleBuilding();
        }
    }

    private void SelectBuildEquipment(int index)
    {
        if (builder is not null)
        {
            builder.SelectEquipment(index);
        }
    }

    private void RotateBuild()
    {
        if (builder is not null)
        {
            builder.RotateBuilding();
        }
    }

    private void ToggleRecovery()
    {
        recoveryExpanded = !recoveryExpanded;
        recoveryPanel.SetActive(recoveryExpanded && builder is not null);
        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void ToggleServer()
    {
        RefreshNetworkManager();
        if (networkManager is null)
        {
            return;
        }

        if (serverState != LocalConnectionState.Stopped)
        {
            networkManager.ServerManager.StopConnection(true);
        }
        else
        {
            networkManager.ServerManager.StartConnection();
        }

        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void ToggleClient()
    {
        RefreshNetworkManager();
        if (networkManager is null || networkManager.TransportManager.Transport is null)
        {
            return;
        }

        if (clientState != LocalConnectionState.Stopped)
        {
            networkManager.ClientManager.StopConnection();
        }
        else
        {
            var address = addressInput.text.Trim();
            if (address.Length == 0)
            {
                networkHintText.text = "Enter a server address before connecting.";
                return;
            }

            networkManager.TransportManager.Transport.SetClientAddress(address);
            networkManager.ClientManager.StartConnection();
        }

        EventSystem.current?.SetSelectedGameObject(null);
    }

    private void RefreshNetworkManager()
    {
        var candidate = FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (candidate is null || !candidate)
        {
            candidate = null!;
        }

        if (candidate == subscribedNetworkManager && (candidate is null || candidate))
        {
            networkManager = candidate;
            return;
        }

        if (subscribedNetworkManager is not null && subscribedNetworkManager)
        {
            if (subscribedNetworkManager.ServerManager is not null)
            {
                subscribedNetworkManager.ServerManager.OnServerConnectionState -= ServerConnectionStateChanged;
            }

            if (subscribedNetworkManager.ClientManager is not null)
            {
                subscribedNetworkManager.ClientManager.OnClientConnectionState -= ClientConnectionStateChanged;
            }
        }

        subscribedNetworkManager = candidate;
        networkManager = candidate;
        if (candidate is null)
        {
            serverState = LocalConnectionState.Stopped;
            clientState = LocalConnectionState.Stopped;
            return;
        }

        if (candidate.ServerManager is not null)
        {
            candidate.ServerManager.OnServerConnectionState += ServerConnectionStateChanged;
        }

        if (candidate.ClientManager is not null)
        {
            candidate.ClientManager.OnClientConnectionState += ClientConnectionStateChanged;
        }

        serverState = candidate.ServerManager is not null && candidate.ServerManager.Started
            ? LocalConnectionState.Started
            : LocalConnectionState.Stopped;
        clientState = candidate.ClientManager is not null && candidate.ClientManager.Started
            ? LocalConnectionState.Started
            : LocalConnectionState.Stopped;
        if (candidate.TransportManager is not null && candidate.TransportManager.Transport is not null)
        {
            var configuredAddress = candidate.TransportManager.Transport.GetClientAddress();
            if (!string.IsNullOrWhiteSpace(configuredAddress) && !addressInput.isFocused)
            {
                addressInput.text = configuredAddress;
                lastAddress = configuredAddress;
            }
        }
    }

    private void ServerConnectionStateChanged(ServerConnectionStateArgs args)
    {
        serverState = args.ConnectionState;
    }

    private void ClientConnectionStateChanged(ClientConnectionStateArgs args)
    {
        clientState = args.ConnectionState;
    }

    private void RefreshNetworkUi()
    {
        var hasNetworkManager = networkManager is not null && networkManager;
        var transportReady = hasNetworkManager
            && networkManager.TransportManager is not null
            && networkManager.TransportManager.Transport is not null;
        networkServerText.text = $"SERVER   {GetConnectionStateText(serverState)}";
        networkClientText.text = $"CLIENT   {GetConnectionStateText(clientState)}";
        networkHintText.text = !hasNetworkManager
            ? "Waiting for the FishNet NetworkManager."
            : transportReady
                ? "Server controls are available before a local player connects."
                : "The configured transport is unavailable.";
        if (addressInput is not null && !addressInput.isFocused && transportReady)
        {
            var address = networkManager.TransportManager.Transport.GetClientAddress();
            if (!string.IsNullOrWhiteSpace(address) && address != lastAddress)
            {
                addressInput.text = address;
                lastAddress = address;
            }
        }
    }

    private void RefreshBuildUi()
    {
        var hasBuilder = builder is not null && builder;
        var active = hasBuilder && builder.IsBuildContextActive;
        buildButton.interactable = active;
        rotateButton.interactable = active;
        foreach (var button in buildEquipmentButtons)
        {
            button.interactable = active;
        }

        if (!hasBuilder)
        {
            buildHintText.text = "Waiting for the local player.";
            buildStatusText.text = "Build controls will appear when gameplay is ready.";
            buildButton.GetComponentInChildren<Text>().text = "B: BUILD";
            rotateButton.GetComponentInChildren<Text>().text = "R: EAST";
            recoveryToggle.interactable = false;
            recoveryPanel.SetActive(false);
            return;
        }

        buildButton.GetComponentInChildren<Text>().text = builder.IsBuilding ? "B: STOP" : "B: BUILD";
        for (var index = 0; index < buildEquipmentLabels.Count; index++)
        {
            buildEquipmentLabels[index].text = builder.GetEquipmentLabel(index);
        }

        rotateButton.GetComponentInChildren<Text>().text = $"R: {builder.DirectionLabel}";
        buildHintText.text = builder.GetInstructions();
        buildStatusText.text = builder.Status;
        recoveryToggle.interactable = active && builder.RecoveryCount > 0;
        if (!active)
        {
            recoveryPanel.SetActive(false);
        }
    }

    private void RefreshRecoveryUi()
    {
        if (builder is null || !builder || !builder.IsBuildContextActive)
        {
            return;
        }

        var fingerprint = builder.RecoveryCount.ToString();
        for (var index = 0; index < builder.RecoveryCount; index++)
        {
            if (builder.TryGetRecoveryCandidate(index, out var id, out var label))
            {
                fingerprint += $"|{id}:{label}";
            }
        }

        if (fingerprint == recoveryFingerprint)
        {
            return;
        }

        recoveryFingerprint = fingerprint;
        for (var index = recoveryContent.childCount - 1; index >= 0; index--)
        {
            Destroy(recoveryContent.GetChild(index).gameObject);
        }

        var rowIndex = 0;
        for (var index = 0; index < builder.RecoveryCount; index++)
        {
            if (!builder.TryGetRecoveryCandidate(index, out var id, out var label))
            {
                continue;
            }

            var capturedId = id;
            var button = CreateButton(
                $"Recover Entity {id}",
                label,
                recoveryContent,
                0f,
                rowIndex * 34f,
                428f,
                30f,
                () => builder.SelectRecovery(capturedId));
            button.GetComponent<Image>().color = Destructive;
            rowIndex++;
        }

        recoveryTitleText.text = $"{rowIndex} item(s) outside the usable interior";
        recoveryPanel.SetActive(recoveryExpanded && rowIndex > 0);
    }

    private void VisibilityChanged(bool _)
    {
        ApplyVisibility();
    }

    private void ApplyVisibility()
    {
        var visible = TestUIVisibility.Visible;
        toolsCanvas.enabled = visible;
        toolsPanel.SetActive(visible && expanded);
        if (!visible)
        {
            EventSystem.current?.SetSelectedGameObject(null);
        }

        ApplyTabState();
    }

    private void ApplyTabState()
    {
        toolsPanel.SetActive(TestUIVisibility.Visible && expanded);
        selectedTabText.text = selectedTab.ToString().ToUpperInvariant();
        foreach (var pair in tabPages)
        {
            var selected = pair.Key == selectedTab;
            pair.Value.SetActive(TestUIVisibility.Visible && expanded && selected);
            if (tabContents.TryGetValue(pair.Key, out var content))
            {
                content.gameObject.SetActive(selected && expanded && TestUIVisibility.Visible
                    && IsTabAvailable(pair.Key));
            }

            if (unavailableTexts.TryGetValue(pair.Key, out var unavailable))
            {
                unavailable.gameObject.SetActive(selected && expanded && TestUIVisibility.Visible
                    && !IsTabAvailable(pair.Key));
            }

            if (tabButtons.TryGetValue(pair.Key, out var button))
            {
                button.GetComponent<Image>().color = selected ? Teal : TealMuted;
            }
        }
    }

    private bool IsTabAvailable(TestToolsTab tab)
    {
        return tab == TestToolsTab.Network || player is not null;
    }

    private void EnsureSingleEventSystem()
    {
        var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (systems.Length == 0)
        {
            var eventObject = new GameObject(
                "Test Tools EventSystem",
                typeof(EventSystem),
                typeof(InputSystemUIInputModule));
            eventObject.transform.SetParent(transform, false);
            eventSystem = eventObject.GetComponent<EventSystem>();
            var inputModule = eventObject.GetComponent<InputSystemUIInputModule>();
            inputModule.actionsAsset = InputSystem.actions;
            return;
        }

        if (eventSystem is null || !eventSystem)
        {
            eventSystem = systems[0];
        }

        foreach (var system in systems)
        {
            if (system == eventSystem)
            {
                continue;
            }

            Destroy(system.gameObject);
        }
    }

    private void SuppressLegacyHud()
    {
        foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (canvas.name != "NetworkHudCanvas")
            {
                continue;
            }

            canvas.enabled = false;
            foreach (var behaviour in canvas.GetComponents<MonoBehaviour>())
            {
                if (behaviour.GetType().Name == "NetworkHudCanvases")
                {
                    behaviour.enabled = false;
                }
            }
        }
    }

    private static string GetConnectionStateText(LocalConnectionState state)
    {
        return state switch
        {
            LocalConnectionState.Started => "CONNECTED",
            LocalConnectionState.Starting => "STARTING",
            LocalConnectionState.Stopping => "STOPPING",
            _ => "STOPPED"
        };
    }

    private static GameObject CreateCanvas(string name, int sortingOrder, out Canvas canvas)
    {
        var result = new GameObject(
            name,
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        var rect = result.GetComponent<RectTransform>();
        Stretch(rect, Vector2.zero, Vector2.zero);
        canvas = result.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = result.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f);
        scaler.matchWidthOrHeight = 0.5f;
        return result;
    }

    public static InputField CreateInputField(
        Transform parent,
        string name,
        string value,
        float x,
        float y,
        float width,
        float height,
        InputField.ContentType contentType)
    {
        var inputObject = CreateImage(name, parent, CharcoalField);
        SetTopRect(inputObject.GetComponent<RectTransform>(), x, y, width, height);
        var input = inputObject.AddComponent<InputField>();
        input.contentType = contentType;
        input.text = value;
        var text = CreateText("Text", inputObject.transform, value, 14, TextAnchor.MiddleLeft, TextPrimary);
        Stretch(text.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));
        input.textComponent = text;
        var placeholder = CreateText("Placeholder", inputObject.transform, "type here", 14,
            TextAnchor.MiddleLeft, TextSecondary);
        Stretch(placeholder.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));
        input.placeholder = placeholder;
        return input;
    }

    public static Button CreateButton(
        string name,
        string value,
        Transform parent,
        float x,
        float y,
        float width,
        float height,
        UnityEngine.Events.UnityAction action)
    {
        var buttonObject = CreateImage(name, parent, TealMuted);
        SetTopRect(buttonObject.GetComponent<RectTransform>(), x, y, width, height);
        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        button.onClick.AddListener(action);
        var text = CreateText("Text", buttonObject.transform, value, 12, TextAnchor.MiddleCenter, TextPrimary);
        Stretch(text.rectTransform, Vector2.zero, Vector2.zero);
        return button;
    }

    public static GameObject CreateImage(string name, Transform parent, Color color)
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

    public static Text CreateText(
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

    public static void SetTopRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, -y);
    }

    public static void SetBottomRect(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = new Vector2(x, y);
    }

    public static void SetBottomStretch(RectTransform rect, float horizontalPadding, float bottomPadding,
        float height)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.sizeDelta = new Vector2(-horizontalPadding * 2f, height);
        rect.anchoredPosition = new Vector2(0f, bottomPadding);
    }

    public static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private void OnDestroy()
    {
        if (eventSubscribed)
        {
            TestUIVisibility.VisibilityChanged -= VisibilityChanged;
        }

        if (subscribedNetworkManager is not null && subscribedNetworkManager)
        {
            if (subscribedNetworkManager.ServerManager is not null)
            {
                subscribedNetworkManager.ServerManager.OnServerConnectionState -= ServerConnectionStateChanged;
            }

            if (subscribedNetworkManager.ClientManager is not null)
            {
                subscribedNetworkManager.ClientManager.OnClientConnectionState -= ClientConnectionStateChanged;
            }
        }

        if (floorToggle is not null)
        {
            floorToggle.performed -= FloorTogglePerformed;
            floorToggle.Disable();
            floorToggle.Dispose();
        }

        if (routeToggle is not null)
        {
            routeToggle.performed -= RouteTogglePerformed;
            routeToggle.Disable();
            routeToggle.Dispose();
        }

        if (cancel is not null)
        {
            cancel.performed -= CancelPerformed;
            cancel.Disable();
            cancel.Dispose();
        }

        if (Instance == this)
        {
            Instance = null!;
        }
    }
}
