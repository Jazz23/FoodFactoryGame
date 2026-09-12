// Defines scene-independent recipe and buffer capabilities for persistent factory entities.
using System;

public readonly struct FactoryEntityDefinition
{
    public FactoryEntityDefinition(
        string newDefinitionId,
        string newAcceptedItemId,
        string newProducedItemId,
        int newInputQuantity,
        int newOutputQuantity,
        float newCycleDuration)
    {
        DefinitionId = newDefinitionId;
        AcceptedItemId = newAcceptedItemId;
        ProducedItemId = newProducedItemId;
        InputQuantity = newInputQuantity;
        OutputQuantity = newOutputQuantity;
        CycleDuration = newCycleDuration;
    }

    public string DefinitionId { get; }
    public string AcceptedItemId { get; }
    public string ProducedItemId { get; }
    public int InputQuantity { get; }
    public int OutputQuantity { get; }
    public float CycleDuration { get; }
    public bool IsReceiver => !string.IsNullOrWhiteSpace(AcceptedItemId);
    public bool IsProducer => !string.IsNullOrWhiteSpace(ProducedItemId);
    public bool IsProcessor => InputQuantity > 0;
    public bool IsShippingDock => DefinitionId == FactoryEntityDefinitions.ShippingDockDefinitionId;
    public bool IsReceivingDock => DefinitionId == FactoryEntityDefinitions.ReceivingDockDefinitionId;
    public bool IsDock => IsShippingDock || IsReceivingDock;
    public bool IsSendingTerminal => IsShippingDock;
    public bool IsReceivingTerminal => IsReceivingDock;
    public bool IsTerminal => IsDock;
    public bool IsSupplier => IsProducer || IsDock;
    public string SuppliedItemId => IsProducer
        ? ProducedItemId
        : IsDock
            ? AcceptedItemId
            : string.Empty;
    public bool IsStorage => IsReceiver && !IsProducer && !IsProcessor && !IsDock;
}

public static class FactoryEntityDefinitions
{
    public const string TestMachineDefinitionId = "test-machine";
    public const string ProcessorDefinitionId = "test-processor";
    public const string TestStorageDefinitionId = "test-storage";
    public const string PackedStorageDefinitionId = "packed-storage";
    public const string ShippingDockDefinitionId = "shipping-dock";
    public const string ReceivingDockDefinitionId = "receiving-dock";
    public const string SendingTerminalDefinitionId = ShippingDockDefinitionId;
    public const string ReceivingTerminalDefinitionId = ReceivingDockDefinitionId;
    public const string TestProductId = "test-product";
    public const string PackedProductId = "packed-product";

    public static FactoryEntityDefinition Get(string definitionId)
    {
        definitionId = NormalizeDefinitionId(definitionId);
        if (FactoryConveyor.IsConveyor(definitionId))
        {
            return new FactoryEntityDefinition(definitionId, TestProductId, TestProductId, 1, 1, 0.6f);
        }
        if (definitionId == ProcessorDefinitionId)
        {
            return new FactoryEntityDefinition(
                ProcessorDefinitionId,
                TestProductId,
                PackedProductId,
                2,
                1,
                1f);
        }

        if (definitionId == TestStorageDefinitionId)
        {
            return new FactoryEntityDefinition(
                TestStorageDefinitionId,
                TestProductId,
                string.Empty,
                0,
                0,
                0f);
        }

        if (definitionId == PackedStorageDefinitionId)
        {
            return new FactoryEntityDefinition(
                PackedStorageDefinitionId,
                PackedProductId,
                string.Empty,
                0,
                0,
                0f);
        }

        if (definitionId == ShippingDockDefinitionId
            || definitionId == ReceivingDockDefinitionId)
        {
            return new FactoryEntityDefinition(
                definitionId,
                TestProductId,
                string.Empty,
                0,
                0,
                0f);
        }

        var safeDefinitionId = string.IsNullOrWhiteSpace(definitionId)
            ? TestMachineDefinitionId
            : definitionId;
        return new FactoryEntityDefinition(
            safeDefinitionId,
            string.Empty,
            TestProductId,
            0,
            1,
            0f);
    }

    public static string NormalizeDefinitionId(string definitionId)
    {
        return definitionId switch
        {
            "sending-terminal" => ShippingDockDefinitionId,
            "receiving-terminal" => ReceivingDockDefinitionId,
            _ => definitionId
        };
    }
}
