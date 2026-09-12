// Provides uGUI truck route creation, status inspection, and deletion from the shared test shell.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using NotAI;

public sealed class FactoryTruckRoutePanel : MonoBehaviour
{
    private readonly List<FactoryTerminalListing> listings = new();
    private readonly List<FactoryTruckRouteRecord> routes = new();
    private readonly List<FactoryTruckRecord> trucks = new();
    private readonly Dictionary<Guid, Text> routeStatusTexts = new();
    private readonly Dictionary<Guid, Button> routeDeleteButtons = new();

    private PlayerSceneTransition owner = null!;
    private TestToolsShell shell = null!;
    private Transform routeContent = null!;
    private Text shippingText = null!;
    private Text receivingText = null!;
    private Text statusText = null!;
    private Text emptyRoutesText = null!;
    private Button shippingPreviousButton = null!;
    private Button shippingNextButton = null!;
    private Button receivingPreviousButton = null!;
    private Button receivingNextButton = null!;
    private int senderIndex;
    private int receiverIndex;
    private float nextRefresh;
    private string routeFingerprint = string.Empty;
    private string status = "Select a shipping dock and a receiving dock, then create the route.";
    private bool initialized;

    public static bool ContainsPointer(Vector2 guiPoint) => TestToolsShell.ContainsPointer(guiPoint);

    public void Initialize(PlayerSceneTransition newOwner)
    {
        if (initialized)
        {
            return;
        }

        owner = newOwner;
        shell = TestToolsShell.GetOrCreate();
        shell.BindPlayer(owner);
        CreateInterface();
        initialized = true;
        RefreshListings();
    }

    public void SetStatus(string message)
    {
        status = message;
        if (statusText is not null)
        {
            statusText.text = message;
        }
    }

    private void Update()
    {
        if (!initialized || Time.unscaledTime < nextRefresh)
        {
            return;
        }

        RefreshListings();
    }

    private void RefreshListings()
    {
        nextRefresh = Time.unscaledTime + 0.25f;
        if (GameSceneManager.Instance is null)
        {
            return;
        }

        listings.Clear();
        listings.AddRange(GameSceneManager.Instance.GetFactoryTerminalListings());
        if (NAIStateManager.Instance is { IsInitialized: true })
        {
            routes.Clear();
            routes.AddRange(NAIStateManager.Instance.GetTruckRoutes());
            trucks.Clear();
            trucks.AddRange(NAIStateManager.Instance.GetTrucks());
        }

        var sendingCount = GetSendingCount();
        var receivingCount = GetReceivingCount();
        senderIndex = sendingCount == 0 ? 0 : Mathf.Clamp(senderIndex, 0, sendingCount - 1);
        receiverIndex = receivingCount == 0 ? 0 : Mathf.Clamp(receiverIndex, 0, receivingCount - 1);
        RefreshSelectors();
        RefreshRouteRows();
    }

    private void CreateInterface()
    {
        var root = TestToolsShell.CreateImage(
            "Routes Tool Content",
            shell.GetTabContent(TestToolsTab.Routes),
            TestToolsShell.CharcoalRaised);
        SetTopRect(root.GetComponent<RectTransform>(), 0f, 0f, 428f, 720f);

        var heading = TestToolsShell.CreateText("Routes Heading", root.transform, "TRUCK ROUTES", 16,
            TextAnchor.MiddleLeft, TestToolsShell.Teal);
        SetTopRect(heading.rectTransform, 16f, 12f, 396f, 28f);
        var helper = TestToolsShell.CreateText(
            "Routes Helper",
            root.transform,
            "Choose endpoints on the authoritative factory world.",
            11,
            TextAnchor.MiddleLeft,
            TestToolsShell.TextSecondary);
        SetTopRect(helper.rectTransform, 16f, 40f, 396f, 20f);

        var shippingLabel = TestToolsShell.CreateText("Shipping Label", root.transform, "SHIPPING", 12,
            TextAnchor.MiddleLeft, TestToolsShell.TextSecondary);
        SetTopRect(shippingLabel.rectTransform, 16f, 70f, 74f, 30f);
        shippingPreviousButton = TestToolsShell.CreateButton(
            "Shipping Previous",
            "<",
            root.transform,
            92f,
            68f,
            34f,
            34f,
            () => MoveSender(-1));
        shippingText = TestToolsShell.CreateText("Shipping Selection", root.transform, "no shipping docks", 12,
            TextAnchor.MiddleLeft, TestToolsShell.TextPrimary);
        SetTopRect(shippingText.rectTransform, 132f, 68f, 250f, 34f);
        shippingNextButton = TestToolsShell.CreateButton(
            "Shipping Next",
            ">",
            root.transform,
            384f,
            68f,
            28f,
            34f,
            () => MoveSender(1));

        var receivingLabel = TestToolsShell.CreateText("Receiving Label", root.transform, "RECEIVING", 12,
            TextAnchor.MiddleLeft, TestToolsShell.TextSecondary);
        SetTopRect(receivingLabel.rectTransform, 16f, 112f, 74f, 30f);
        receivingPreviousButton = TestToolsShell.CreateButton(
            "Receiving Previous",
            "<",
            root.transform,
            92f,
            110f,
            34f,
            34f,
            () => MoveReceiver(-1));
        receivingText = TestToolsShell.CreateText("Receiving Selection", root.transform, "no receiving docks", 12,
            TextAnchor.MiddleLeft, TestToolsShell.TextPrimary);
        SetTopRect(receivingText.rectTransform, 132f, 110f, 250f, 34f);
        receivingNextButton = TestToolsShell.CreateButton(
            "Receiving Next",
            ">",
            root.transform,
            384f,
            110f,
            28f,
            34f,
            () => MoveReceiver(1));

        var createButton = TestToolsShell.CreateButton(
            "Create Route",
            "CREATE ROUTE",
            root.transform,
            16f,
            154f,
            396f,
            36f,
            CreateRouteClicked);
        createButton.GetComponent<Image>().color = TestToolsShell.TealMuted;
        statusText = TestToolsShell.CreateText("Route Status", root.transform, status, 12,
            TextAnchor.UpperLeft, TestToolsShell.TextSecondary);
        SetTopRect(statusText.rectTransform, 16f, 198f, 396f, 42f);

        var routeHeading = TestToolsShell.CreateText("Route List Heading", root.transform, "ACTIVE ROUTES", 13,
            TextAnchor.MiddleLeft, TestToolsShell.Teal);
        SetTopRect(routeHeading.rectTransform, 16f, 248f, 396f, 24f);
        var scrollObject = TestToolsShell.CreateImage("Route Status Scroll", root.transform,
            new Color(0f, 0f, 0f, 0.001f));
        SetTopRect(scrollObject.GetComponent<RectTransform>(), 8f, 278f, 412f, 420f);
        var scroll = scrollObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28f;
        var viewport = new GameObject(
            "Route Status Viewport",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(RectMask2D));
        viewport.transform.SetParent(scrollObject.transform, false);
        SetStretch(viewport.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.001f);
        routeContent = new GameObject("Route Status Content", typeof(RectTransform)).transform;
        routeContent.SetParent(viewport.transform, false);
        var contentRect = routeContent.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0f, 1f);
        contentRect.sizeDelta = new Vector2(0f, 420f);
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRect;
        emptyRoutesText = TestToolsShell.CreateText("No Routes", routeContent,
            "No routes created.", 12, TextAnchor.UpperLeft, TestToolsShell.TextSecondary);
        SetTopRect(emptyRoutesText.rectTransform, 8f, 8f, 396f, 32f);
    }

    private void CreateRouteRows()
    {
        routeStatusTexts.Clear();
        routeDeleteButtons.Clear();
        emptyRoutesText = null!;
        for (var index = routeContent.childCount - 1; index >= 0; index--)
        {
            Destroy(routeContent.GetChild(index).gameObject);
        }

        var rowIndex = 0;
        foreach (var route in routes)
        {
            var truck = trucks.Find(candidate => candidate.Guid == route.TruckGuid);
            if (truck is null)
            {
                continue;
            }

            var row = TestToolsShell.CreateImage(
                $"Route {route.Guid:D}",
                routeContent,
                TestToolsShell.CharcoalField);
            SetTopRect(row.GetComponent<RectTransform>(), 4f, rowIndex * 58f, 396f, 52f);
            var rowText = TestToolsShell.CreateText(
                "Status",
                row.transform,
                DescribeTruck(truck),
                11,
                TextAnchor.MiddleLeft,
                TestToolsShell.TextPrimary);
            SetTopRect(rowText.rectTransform, 8f, 4f, 286f, 44f);
            var capturedRouteGuid = route.Guid;
            var deleteButton = TestToolsShell.CreateButton(
                $"Delete Route {route.Guid:D}",
                "DELETE",
                row.transform,
                302f,
                10f,
                82f,
                32f,
                () => owner.RequestDeleteTruckRoute(capturedRouteGuid));
            deleteButton.GetComponent<Image>().color = TestToolsShell.Destructive;
            routeStatusTexts[route.Guid] = rowText;
            routeDeleteButtons[route.Guid] = deleteButton;
            rowIndex++;
        }

        if (rowIndex == 0)
        {
            emptyRoutesText = TestToolsShell.CreateText("No Routes", routeContent,
                "No routes created.", 12, TextAnchor.UpperLeft, TestToolsShell.TextSecondary);
            SetTopRect(emptyRoutesText.rectTransform, 8f, 8f, 396f, 32f);
        }

        var contentRect = routeContent.GetComponent<RectTransform>();
        contentRect.sizeDelta = new Vector2(0f, Mathf.Max(420f, rowIndex * 58f));
    }

    private void RefreshSelectors()
    {
        var sendings = GetSendings();
        var receivings = GetReceivings();
        shippingText.text = sendings.Count == 0
            ? "No shipping docks"
            : DescribeListing(sendings[senderIndex]);
        receivingText.text = receivings.Count == 0
            ? "No receiving docks"
            : DescribeListing(receivings[receiverIndex]);
        shippingPreviousButton.interactable = sendings.Count > 1;
        shippingNextButton.interactable = sendings.Count > 1;
        receivingPreviousButton.interactable = receivings.Count > 1;
        receivingNextButton.interactable = receivings.Count > 1;
        statusText.text = status;
    }

    private void RefreshRouteRows()
    {
        var fingerprint = routes.Count.ToString();
        foreach (var route in routes)
        {
            fingerprint += $"|{route.Guid:D}:{route.TruckGuid:D}";
        }

        if (fingerprint != routeFingerprint)
        {
            routeFingerprint = fingerprint;
            CreateRouteRows();
        }

        var visibleRows = 0;
        foreach (var route in routes)
        {
            var truck = trucks.Find(candidate => candidate.Guid == route.TruckGuid);
            if (truck is null || !routeStatusTexts.TryGetValue(route.Guid, out var routeText))
            {
                continue;
            }

            routeText.text = DescribeTruck(truck);
            routeDeleteButtons[route.Guid].interactable = truck.CargoCount == 0;
            visibleRows++;
        }

        if (emptyRoutesText is not null)
        {
            emptyRoutesText.gameObject.SetActive(visibleRows == 0);
        }
    }

    private void CreateRouteClicked()
    {
        var sendings = GetSendings();
        var receivings = GetReceivings();
        if (sendings.Count == 0 || receivings.Count == 0)
        {
            SetStatus("Place a shipping dock and a receiving dock first.");
            return;
        }

        SetStatus("Creating truck route...");
        owner.RequestCreateTruckRoute(
            sendings[senderIndex].Endpoint,
            receivings[receiverIndex].Endpoint);
    }

    private void MoveSender(int delta)
    {
        var count = GetSendingCount();
        if (count > 0)
        {
            senderIndex = (senderIndex + delta + count) % count;
            RefreshSelectors();
        }
    }

    private void MoveReceiver(int delta)
    {
        var count = GetReceivingCount();
        if (count > 0)
        {
            receiverIndex = (receiverIndex + delta + count) % count;
            RefreshSelectors();
        }
    }

    private List<FactoryTerminalListing> GetSendings()
    {
        var result = new List<FactoryTerminalListing>();
        foreach (var listing in listings)
        {
            if (listing.IsShipping)
            {
                result.Add(listing);
            }
        }

        return result;
    }

    private List<FactoryTerminalListing> GetReceivings()
    {
        var result = new List<FactoryTerminalListing>();
        foreach (var listing in listings)
        {
            if (listing.IsReceiving)
            {
                result.Add(listing);
            }
        }

        return result;
    }

    private int GetSendingCount()
    {
        var count = 0;
        foreach (var listing in listings)
        {
            if (listing.IsShipping)
            {
                count++;
            }
        }

        return count;
    }

    private int GetReceivingCount()
    {
        var count = 0;
        foreach (var listing in listings)
        {
            if (listing.IsReceiving)
            {
                count++;
            }
        }

        return count;
    }

    private static string DescribeListing(FactoryTerminalListing listing)
    {
        return $"B{listing.BuildingInstanceId}/F{listing.FloorIndex} {listing.FloorLabel} ({listing.DefinitionId})";
    }

    private static string DescribeTruck(FactoryTruckRecord truck)
    {
        return truck.State switch
        {
            FactoryTruckState.Loading => $"Loading {truck.CargoCount} items (window {truck.LoadingWindowProgress:0.0}s)",
            FactoryTruckState.Outbound => $"Outbound, {truck.CargoCount} items, {truck.RemainingTravelSeconds:0.0}s left",
            FactoryTruckState.Unloading => $"Unloading {truck.CargoCount} items",
            FactoryTruckState.Returning => $"Returning, {truck.RemainingTravelSeconds:0.0}s left",
            _ => $"Blocked: {truck.BlockingReason} (cargo {truck.CargoCount})"
        };
    }

    private static void SetTopRect(RectTransform rect, float x, float y, float width, float height)
    {
        TestToolsShell.SetTopRect(rect, x, y, width, height);
    }

    private static void SetStretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        TestToolsShell.Stretch(rect, offsetMin, offsetMax);
    }

    private void OnDestroy()
    {
        initialized = false;
    }
}
