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

        if (destination.IsSendingTerminal
            && (!source.IsProducer || !sameBuilding))
        {
            error = "A sending terminal must receive from a producer in its own building.";
            return false;
        }

        if (source.IsSendingTerminal
            && (!destination.IsReceivingTerminal || sameBuilding))
        {
            error = "A sending terminal must connect to a receiving terminal in another building.";
            return false;
        }

        if (destination.IsReceivingTerminal
            && (!source.IsSendingTerminal || sameBuilding))
        {
            error = "A receiving terminal must receive from a sending terminal in another building.";
            return false;
        }

        if (source.IsReceivingTerminal
            && (!sameBuilding || (!destination.IsProcessor && !destination.IsStorage)))
        {
            error = "A receiving terminal must supply a processor or storage in its own building.";
            return false;
        }

        if (sameBuilding
            && sameFloor
            && !source.IsTerminal
            && !destination.IsTerminal)
        {
            error = "Connection endpoints must be on different floors unless a terminal is involved.";
            return false;
        }

        return true;
    }
}
