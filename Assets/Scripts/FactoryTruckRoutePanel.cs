// Provides host-side truck route creation, status inspection, and deletion from the gameplay toolbar.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using NotAI;

public sealed class FactoryTruckRoutePanel : MonoBehaviour
{
    private static readonly Rect PanelRect = new(Screen.width - 436f, 40f, 424f, 344f);

    private PlayerSceneTransition owner = null!;
    private InputActionMap actions = null!;
    private InputAction toggle = null!;
    private bool visible;
    private string status = "Select a sending and a receiving terminal, then create the route.";
    private List<FactoryTerminalListing> listings = new();
    private List<FactoryTruckRouteRecord> routes = new();
    private List<FactoryTruckRecord> trucks = new();
    private int senderIndex;
    private int receiverIndex;
    private float nextRefresh;

    public static bool ContainsPointer(Vector2 guiPoint) => PanelRect.Contains(guiPoint);

    public void Initialize(PlayerSceneTransition newOwner)
    {
        owner = newOwner;
        actions = InputSystem.actions.FindActionMap("FactoryBuild", true).Clone();
        actions.Enable();
        toggle = actions["ToggleTruckRoutes"];
        RefreshListings();
    }

    public void SetStatus(string message) => status = message;

    private void Update()
    {
        if (toggle.WasPressedThisFrame())
        {
            visible = !visible;
            RefreshListings();
        }

        if (visible && Time.unscaledTime >= nextRefresh)
        {
            RefreshListings();
        }
    }

    private void RefreshListings()
    {
        nextRefresh = Time.unscaledTime + 0.25f;
        if (GameSceneManager.Instance is null)
        {
            return;
        }

        listings = GameSceneManager.Instance.GetFactoryTerminalListings();
        routes = NAIStateManager.Instance is { IsInitialized: true } ? NAIStateManager.Instance.GetTruckRoutes() : routes;
        trucks = NAIStateManager.Instance is { IsInitialized: true } ? NAIStateManager.Instance.GetTrucks() : trucks;
        senderIndex = listings.Count == 0 ? 0 : Mathf.Clamp(senderIndex, 0, listings.Count - 1);
        receiverIndex = listings.Count == 0 ? 0 : Mathf.Clamp(receiverIndex, 0, listings.Count - 1);
    }

    private void OnGUI()
    {
        if (!visible)
        {
            return;
        }

        GUILayout.BeginArea(PanelRect, GUI.skin.box);
        GUILayout.Label("Truck Routes (T to close)");
        var sendings = new List<FactoryTerminalListing>();
        var receivings = new List<FactoryTerminalListing>();
        foreach (var listing in listings)
        {
            if (listing.IsSending) sendings.Add(listing);
            if (listing.IsReceiving) receivings.Add(listing);
        }

        DrawTerminalSelector("Sender", sendings, ref senderIndex);
        DrawTerminalSelector("Receiver", receivings, ref receiverIndex);
        if (GUILayout.Button("Create route"))
        {
            if (sendings.Count == 0 || receivings.Count == 0)
            {
                status = "Place a sending terminal and a receiving terminal first.";
            }
            else
            {
                owner.RequestCreateTruckRoute(
                    sendings[senderIndex % sendings.Count].Endpoint,
                    receivings[receiverIndex % receivings.Count].Endpoint);
            }
        }

        GUILayout.Label(status);
        GUILayout.Space(4f);
        foreach (var route in routes)
        {
            var truck = trucks.Find(candidate => candidate.Guid == route.TruckGuid);
            if (truck is null)
            {
                continue;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(DescribeTruck(truck), GUILayout.Width(330f));
            GUI.enabled = truck.CargoCount == 0;
            if (GUILayout.Button("Delete", GUILayout.Width(64f)))
            {
                owner.RequestDeleteTruckRoute(route.Guid);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        GUILayout.EndArea();
    }

    private void DrawTerminalSelector(string title, List<FactoryTerminalListing> options, ref int index)
    {
        GUILayout.BeginHorizontal();
        GUI.enabled = options.Count > 0;
        if (GUILayout.Button("<", GUILayout.Width(26f)) && options.Count > 0)
        {
            index = (index + options.Count - 1) % options.Count;
        }

        GUI.enabled = true;
        var label = options.Count == 0 ? $"no {title.ToLowerInvariant()} terminals" : DescribeListing(options[index % options.Count]);
        GUILayout.Label($"{title}: {label}");
        GUI.enabled = options.Count > 0;
        if (GUILayout.Button(">", GUILayout.Width(26f)) && options.Count > 0)
        {
            index = (index + 1) % options.Count;
        }

        GUI.enabled = true;
        GUILayout.EndHorizontal();
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

    private void OnDestroy()
    {
        actions.Disable();
        if (toggle is not null) toggle.Dispose();
    }
}
