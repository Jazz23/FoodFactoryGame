// Defines scene-independent truck route and cargo records used by the authoritative factory simulation.
using System;
using UnityEngine;

public enum FactoryTruckState
{
    Loading,
    Outbound,
    Unloading,
    Returning,
    Blocked
}

public readonly struct FactoryTerminalListing
{
    public FactoryTerminalListing(
        FactoryEntityEndpoint newEndpoint,
        uint newBuildingInstanceId,
        int newFloorIndex,
        uint newEntityId,
        string newDefinitionId,
        string newFloorLabel)
    {
        Endpoint = newEndpoint;
        BuildingInstanceId = newBuildingInstanceId;
        FloorIndex = newFloorIndex;
        EntityId = newEntityId;
        DefinitionId = newDefinitionId;
        FloorLabel = newFloorLabel;
    }

    public FactoryEntityEndpoint Endpoint { get; }
    public uint BuildingInstanceId { get; }
    public int FloorIndex { get; }
    public uint EntityId { get; }
    public string DefinitionId { get; }
    public string FloorLabel { get; }
    public bool IsSending => DefinitionId == FactoryEntityRecord.SendingTerminalDefinitionId;
    public bool IsReceiving => DefinitionId == FactoryEntityRecord.ReceivingTerminalDefinitionId;
}

[Serializable]
public sealed class FactoryTruckRouteRecord
{
    public const int DefaultCargoCapacity = 20;
    public const float DefaultTransferRateItemsPerSecond = 10f;
    public const float DefaultOutboundTravelSeconds = 10f;
    public const float DefaultReturnTravelSeconds = 10f;
    public const float DefaultPartialLoadDepartureWindowSeconds = 2f;

    [SerializeField] private string guidString = string.Empty;
    [SerializeField] private string truckGuidString = string.Empty;
    [SerializeField] private FactoryEntityEndpoint source;
    [SerializeField] private FactoryEntityEndpoint destination;
    [SerializeField] private string itemId = FactoryEntityDefinitions.TestProductId;
    [SerializeField] private int cargoCapacity = DefaultCargoCapacity;
    [SerializeField] private float transferRateItemsPerSecond = DefaultTransferRateItemsPerSecond;
    [SerializeField] private float outboundTravelSeconds = DefaultOutboundTravelSeconds;
    [SerializeField] private float returnTravelSeconds = DefaultReturnTravelSeconds;
    [SerializeField] private float partialLoadDepartureWindowSeconds = DefaultPartialLoadDepartureWindowSeconds;

    public FactoryTruckRouteRecord()
    {
    }

    public FactoryTruckRouteRecord(
        Guid newGuid,
        Guid newTruckGuid,
        FactoryEntityEndpoint newSource,
        FactoryEntityEndpoint newDestination,
        string newItemId = FactoryEntityDefinitions.TestProductId,
        int newCargoCapacity = DefaultCargoCapacity,
        float newTransferRateItemsPerSecond = DefaultTransferRateItemsPerSecond,
        float newOutboundTravelSeconds = DefaultOutboundTravelSeconds,
        float newReturnTravelSeconds = DefaultReturnTravelSeconds,
        float newPartialLoadDepartureWindowSeconds = DefaultPartialLoadDepartureWindowSeconds)
    {
        Guid = newGuid;
        TruckGuid = newTruckGuid;
        source = newSource;
        destination = newDestination;
        itemId = newItemId;
        cargoCapacity = newCargoCapacity;
        transferRateItemsPerSecond = newTransferRateItemsPerSecond;
        outboundTravelSeconds = newOutboundTravelSeconds;
        returnTravelSeconds = newReturnTravelSeconds;
        partialLoadDepartureWindowSeconds = newPartialLoadDepartureWindowSeconds;
    }

    public Guid Guid
    {
        get => FactoryGuidMigration.TryParseCanonical(guidString, out var value)
            ? value
            : Guid.Empty;
        private set => guidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public Guid TruckGuid
    {
        get => FactoryGuidMigration.TryParseCanonical(truckGuidString, out var value)
            ? value
            : Guid.Empty;
        private set => truckGuidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public FactoryEntityEndpoint Source => source;
    public FactoryEntityEndpoint Destination => destination;
    public FactoryEntityEndpoint SourceEndpoint => source;
    public FactoryEntityEndpoint DestinationEndpoint => destination;
    public string ItemId => itemId;
    public int CargoCapacity => cargoCapacity;
    public float TransferRateItemsPerSecond => transferRateItemsPerSecond;
    public float OutboundTravelSeconds => outboundTravelSeconds;
    public float ReturnTravelSeconds => returnTravelSeconds;
    public float PartialLoadDepartureWindowSeconds => partialLoadDepartureWindowSeconds;

    public FactoryTruckRouteRecord Clone()
    {
        return new FactoryTruckRouteRecord(
            Guid,
            TruckGuid,
            source,
            destination,
            itemId,
            cargoCapacity,
            transferRateItemsPerSecond,
            outboundTravelSeconds,
            returnTravelSeconds,
            partialLoadDepartureWindowSeconds);
    }
}

[Serializable]
public sealed class FactoryTruckRecord : IFactoryItemTransferInventory
{
    [SerializeField] private string guidString = string.Empty;
    [SerializeField] private string routeGuidString = string.Empty;
    [SerializeField] private FactoryTruckState state;
    [SerializeField] private string cargoItemId = string.Empty;
    [SerializeField] private int cargoCount;
    [SerializeField] private float remainingTravelSeconds;
    [SerializeField] private float loadingWindowProgress;
    [SerializeField] private string blockingReason = string.Empty;

    [NonSerialized] private string routeItemId = string.Empty;
    [NonSerialized] private int routeCargoCapacity;

    public FactoryTruckRecord()
    {
    }

    public FactoryTruckRecord(
        Guid newGuid,
        Guid newRouteGuid,
        FactoryTruckState newState,
        string newCargoItemId,
        int newCargoCount,
        float newRemainingTravelSeconds,
        float newLoadingWindowProgress,
        string newBlockingReason)
    {
        Guid = newGuid;
        RouteGuid = newRouteGuid;
        state = newState;
        cargoItemId = newCargoItemId;
        cargoCount = newCargoCount;
        remainingTravelSeconds = newRemainingTravelSeconds;
        loadingWindowProgress = newLoadingWindowProgress;
        blockingReason = newBlockingReason;
    }

    public FactoryTruckRecord(
        Guid newGuid,
        FactoryTruckRouteRecord route,
        FactoryTruckState newState,
        string newCargoItemId,
        int newCargoCount,
        float newRemainingTravelSeconds,
        float newLoadingWindowProgress,
        string newBlockingReason)
        : this(
            newGuid,
            route.Guid,
            newState,
            newCargoItemId,
            newCargoCount,
            newRemainingTravelSeconds,
            newLoadingWindowProgress,
            newBlockingReason)
    {
        ConfigureCargoCapacity(route.ItemId, route.CargoCapacity);
    }

    public Guid Guid
    {
        get => FactoryGuidMigration.TryParseCanonical(guidString, out var value)
            ? value
            : Guid.Empty;
        private set => guidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public Guid RouteGuid
    {
        get => FactoryGuidMigration.TryParseCanonical(routeGuidString, out var value)
            ? value
            : Guid.Empty;
        private set => routeGuidString = value == Guid.Empty ? string.Empty : value.ToString("D");
    }

    public FactoryTruckState State => state;
    public string CargoItemId => cargoItemId;
    public int CargoCount => cargoCount;
    public float RemainingTravelSeconds => remainingTravelSeconds;
    public float LoadingWindowProgress => loadingWindowProgress;
    public string BlockingReason => blockingReason;
    public string SuppliedItemId => cargoItemId;
    public string RouteItemId => routeItemId;
    public int RouteCargoCapacity => routeCargoCapacity;

    public void ConfigureCargoCapacity(string newRouteItemId, int newRouteCargoCapacity)
    {
        routeItemId = newRouteItemId;
        routeCargoCapacity = newRouteCargoCapacity;
    }

    public void ConfigureCargoCapacity(FactoryTruckRouteRecord route)
    {
        ConfigureCargoCapacity(route.ItemId, route.CargoCapacity);
    }

    public void SetState(
        FactoryTruckState newState,
        float newRemainingTravelSeconds,
        float newLoadingWindowProgress,
        string newBlockingReason)
    {
        state = newState;
        remainingTravelSeconds = newRemainingTravelSeconds;
        loadingWindowProgress = newLoadingWindowProgress;
        blockingReason = newBlockingReason;
    }

    public bool CanAcceptItem(string itemId)
    {
        return cargoCount < routeCargoCapacity
            && !string.IsNullOrWhiteSpace(itemId)
            && itemId == routeItemId
            && (cargoCount == 0 || cargoItemId == itemId);
    }

    public bool CanSupplyItem(string itemId)
    {
        return cargoCount > 0
            && !string.IsNullOrWhiteSpace(itemId)
            && cargoItemId == itemId
            && itemId == routeItemId;
    }

    public int GetAcceptCapacity(string itemId)
    {
        return CanAcceptItem(itemId) ? routeCargoCapacity - cargoCount : 0;
    }

    public int GetSupplyCapacity(string itemId)
    {
        return CanSupplyItem(itemId) ? cargoCount : 0;
    }

    public int TryAcceptItem(string itemId, int quantity)
    {
        if (!CanAcceptItem(itemId) || quantity <= 0)
        {
            return 0;
        }

        var accepted = Math.Min(quantity, routeCargoCapacity - cargoCount);
        if (accepted <= 0)
        {
            return 0;
        }

        if (cargoCount == 0)
        {
            cargoItemId = itemId;
        }

        cargoCount += accepted;
        return accepted;
    }

    public bool TryExtractItem(string itemId, int quantity, out int removed)
    {
        removed = 0;
        if (!CanSupplyItem(itemId) || quantity <= 0 || quantity > cargoCount)
        {
            return false;
        }

        cargoCount -= quantity;
        removed = quantity;
        if (cargoCount == 0)
        {
            cargoItemId = string.Empty;
        }

        return true;
    }

    public bool TryRemoveAcceptedItem(string itemId, int quantity, out int removed)
    {
        return TryExtractItem(itemId, quantity, out removed);
    }

    public FactoryTruckRecord Clone()
    {
        var result = new FactoryTruckRecord(
            Guid,
            RouteGuid,
            state,
            cargoItemId,
            cargoCount,
            remainingTravelSeconds,
            loadingWindowProgress,
            blockingReason);
        result.ConfigureCargoCapacity(routeItemId, routeCargoCapacity);
        return result;
    }
}
