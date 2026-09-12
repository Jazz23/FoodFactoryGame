// Provides atomic, scene-independent item acceptance and extraction for factory logistics.
public interface IFactoryItemTransferInventory
{
    string SuppliedItemId { get; }
    bool CanAcceptItem(string itemId);
    bool CanSupplyItem(string itemId);
    int GetAcceptCapacity(string itemId);
    int GetSupplyCapacity(string itemId);
    int TryAcceptItem(string itemId, int quantity);
    bool TryExtractItem(string itemId, int quantity, out int removed);
    bool TryRemoveAcceptedItem(string itemId, int quantity, out int removed);
}

public static class FactoryItemTransfer
{
    public static int TryTransfer(
        IFactoryItemTransferInventory source,
        IFactoryItemTransferInventory destination,
        int requestedQuantity)
    {
        if (requestedQuantity <= 0)
        {
            return 0;
        }

        var itemId = source.SuppliedItemId;
        var transferable = UnityEngine.Mathf.Min(
            requestedQuantity,
            source.GetSupplyCapacity(itemId),
            destination.GetAcceptCapacity(itemId));
        if (transferable <= 0)
        {
            return 0;
        }

        var accepted = destination.TryAcceptItem(itemId, transferable);
        if (accepted <= 0)
        {
            return 0;
        }

        if (source.TryExtractItem(itemId, accepted, out var removed)
            && removed == accepted)
        {
            return accepted;
        }

        destination.TryRemoveAcceptedItem(itemId, accepted, out _);
        return 0;
    }
}
