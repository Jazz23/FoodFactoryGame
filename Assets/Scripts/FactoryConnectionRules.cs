// Centralizes item, endpoint-role, and topology rules shared by authoritative connections and UI discovery.
public static class FactoryConnectionRules
{
    public static bool TryValidate(
        FactoryEntityDefinition source,
        FactoryEntityDefinition destination,
        bool sameBuilding,
        bool sameFloor,
        out string error)
    {
        error = string.Empty;
        if (!source.IsSupplier)
        {
            error = "The connection source must supply an item.";
            return false;
        }

        if (!destination.IsReceiver)
        {
            error = "The connection destination must accept an item.";
            return false;
        }

        if (source.SuppliedItemId != destination.AcceptedItemId)
        {
            error = $"The connection item types do not match: {source.SuppliedItemId} -> {destination.AcceptedItemId}.";
            return false;
        }

        if (destination.IsShippingDock
            && (!source.IsProducer || !sameBuilding))
        {
            error = "A shipping dock must receive from a producer in its own building.";
            return false;
        }

        if (source.IsShippingDock
            && (!destination.IsReceivingDock || sameBuilding))
        {
            error = "A shipping dock must connect to a receiving dock in another building.";
            return false;
        }

        if (destination.IsReceivingDock
            && (!source.IsShippingDock || sameBuilding))
        {
            error = "A receiving dock must receive from a shipping dock in another building.";
            return false;
        }

        if (source.IsReceivingDock
            && (!sameBuilding || (!destination.IsProcessor && !destination.IsStorage)))
        {
            error = "A receiving dock must supply a processor or storage in its own building.";
            return false;
        }

        if (sameBuilding
            && sameFloor
            && !source.IsDock
            && !destination.IsDock)
        {
            error = "Connection endpoints must be on different floors unless a dock is involved.";
            return false;
        }

        return true;
    }
}
