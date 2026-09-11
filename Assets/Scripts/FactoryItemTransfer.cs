// Provides atomic, scene-independent item acceptance and extraction for factory logistics.
public static class FactoryItemTransfer
{
    public static int TryTransfer(
        FactoryEntityRecord source,
        FactoryEntityRecord destination,
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
