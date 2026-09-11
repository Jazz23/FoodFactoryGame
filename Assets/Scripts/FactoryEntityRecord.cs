// Stores one persistent factory entity and the compact snapshot sent to floor clients.
using System;
using FishNet.CodeGenerating;
using UnityEngine;

[Serializable]
[IncludeSerialization]
public struct FactoryEntitySnapshot
{
    public FactoryEntitySnapshot(
        uint newEntityId,
        string newDefinitionId,
        Vector2 newLogicalPosition,
        float newCycleRate,
        float newCycleProgress,
        int newProducedCount,
        int newOutputCount = 0,
        int newInputCount = 0)
    {
        EntityId = newEntityId;
        DefinitionId = newDefinitionId;
        LogicalPosition = newLogicalPosition;
        CycleRate = newCycleRate;
        CycleProgress = newCycleProgress;
        ProducedCount = newProducedCount;
        OutputCount = newOutputCount;
        InputCount = newInputCount;
    }

    public uint EntityId;
    public string DefinitionId;
    public Vector2 LogicalPosition;
    public float CycleRate;
    public float CycleProgress;
    public int ProducedCount;
    public int OutputCount;
    public int InputCount;
}

[Serializable]
public sealed class FactoryEntityRecord
{
    public const int InputCapacity = 100;
    public const int OutputCapacity = 100;
    public const string OutputProductId = FactoryEntityDefinitions.TestProductId;
    public const string StorageDefinitionId = FactoryEntityDefinitions.TestStorageDefinitionId;
    public const string ProcessorDefinitionId = FactoryEntityDefinitions.ProcessorDefinitionId;
    public const string PackedStorageDefinitionId = FactoryEntityDefinitions.PackedStorageDefinitionId;
    public const string SendingTerminalDefinitionId = FactoryEntityDefinitions.SendingTerminalDefinitionId;
    public const string ReceivingTerminalDefinitionId = FactoryEntityDefinitions.ReceivingTerminalDefinitionId;
    public const string PackedProductId = FactoryEntityDefinitions.PackedProductId;

    private const string DefaultDefinitionId = "test-machine";

    [SerializeField] private uint entityId;
    [SerializeField] private string definitionId = DefaultDefinitionId;
    [SerializeField] private Vector2 logicalPosition;
    [SerializeField] private float cycleRate;
    [SerializeField] private float cycleProgress;
    [SerializeField] private int producedCount;
    [SerializeField] private int outputCount;
    [SerializeField] private int inputCount;

    public FactoryEntityRecord()
    {
    }

    public FactoryEntityRecord(
        uint newEntityId,
        string newDefinitionId,
        Vector2 newLogicalPosition,
        float newCycleRate,
        float newCycleProgress,
        int newProducedCount,
        int newOutputCount = 0,
        int newInputCount = 0)
    {
        SetState(
            newEntityId,
            newDefinitionId,
            newLogicalPosition,
            newCycleRate,
            newCycleProgress,
            newProducedCount,
            newOutputCount,
            newInputCount);
    }

    public uint EntityId => entityId;
    public string DefinitionId => definitionId;
    public Vector2 LogicalPosition => logicalPosition;
    public float CycleRate => cycleRate;
    public float CycleProgress => cycleProgress;
    public int ProducedCount => producedCount;
    public int OutputCount => outputCount;
    public int InputCount => inputCount;
    public string AcceptedItemId => FactoryEntityDefinitions.Get(definitionId).AcceptedItemId;
    public string ProducedItemId => FactoryEntityDefinitions.Get(definitionId).ProducedItemId;
    public int InputQuantity => FactoryEntityDefinitions.Get(definitionId).InputQuantity;
    public int OutputQuantity => FactoryEntityDefinitions.Get(definitionId).OutputQuantity;
    public bool IsReceiver => FactoryEntityDefinitions.Get(definitionId).IsReceiver;
    public bool IsProducer => FactoryEntityDefinitions.Get(definitionId).IsProducer;
    public bool IsSupplier => FactoryEntityDefinitions.Get(definitionId).IsSupplier;
    public bool IsProcessor => FactoryEntityDefinitions.Get(definitionId).IsProcessor;
    public bool IsStorage => FactoryEntityDefinitions.Get(definitionId).IsStorage;
    public bool IsSendingTerminal => FactoryEntityDefinitions.Get(definitionId).IsSendingTerminal;
    public bool IsReceivingTerminal => FactoryEntityDefinitions.Get(definitionId).IsReceivingTerminal;
    public bool IsTerminal => FactoryEntityDefinitions.Get(definitionId).IsTerminal;
    public string SuppliedItemId => FactoryEntityDefinitions.Get(definitionId).SuppliedItemId;
    public int InventoryCount => IsTerminal || IsStorage ? outputCount : 0;
    public int InventoryCapacity => IsTerminal || IsStorage ? OutputCapacity : 0;
    public bool IsProducingMachine => IsProducer;

    public static FactoryEntityRecord CreateDefault(
        uint newEntityId,
        int floorIndex)
    {
        var definition = floorIndex switch
        {
            0 => "test-machine-ground",
            1 => "test-machine-upper",
            _ => DefaultDefinitionId
        };
        var position = floorIndex switch
        {
            0 => new Vector2(2f, 1f),
            1 => new Vector2(3f, 1.5f),
            _ => new Vector2(2f, 2f)
        };
        var cycleRate = floorIndex switch
        {
            0 => 0.5f,
            1 => 1f,
            _ => 0.25f
        };
        var cycleProgress = floorIndex switch
        {
            0 => 0.25f,
            1 => 0.5f,
            _ => 0f
        };

        return new FactoryEntityRecord(
            newEntityId,
            definition,
            position,
            cycleRate,
            cycleProgress,
            0);
    }

    public static FactoryEntityRecord FromSnapshot(
        FactoryEntitySnapshot snapshot)
    {
        return new FactoryEntityRecord(
            snapshot.EntityId,
            snapshot.DefinitionId,
            snapshot.LogicalPosition,
            snapshot.CycleRate,
            snapshot.CycleProgress,
            snapshot.ProducedCount,
            snapshot.OutputCount,
            snapshot.InputCount);
    }

    public void SetState(
        uint newEntityId,
        string newDefinitionId,
        Vector2 newLogicalPosition,
        float newCycleRate,
        float newCycleProgress,
        int newProducedCount,
        int newOutputCount = 0,
        int newInputCount = 0)
    {
        entityId = newEntityId;
        definitionId = string.IsNullOrWhiteSpace(newDefinitionId)
            ? DefaultDefinitionId
            : newDefinitionId.Trim();
        logicalPosition = SanitizeLogicalPosition(newLogicalPosition);
        cycleRate = float.IsNaN(newCycleRate)
            || float.IsInfinity(newCycleRate)
            ? 0f
            : Mathf.Clamp(newCycleRate, 0f, 1000f);
        cycleProgress = float.IsNaN(newCycleProgress)
            || float.IsInfinity(newCycleProgress)
            ? 0f
            : Mathf.Clamp01(newCycleProgress);
        producedCount = Mathf.Max(0, newProducedCount);
        outputCount = Mathf.Clamp(newOutputCount, 0, OutputCapacity);
        inputCount = Mathf.Clamp(newInputCount, 0, InputCapacity);
        var definition = FactoryEntityDefinitions.Get(definitionId);
        if (definition.IsProcessor)
        {
            cycleRate = 1f;
        }

        if (definition.IsTerminal)
        {
            cycleRate = 0f;
            cycleProgress = 0f;
            producedCount = 0;
            inputCount = 0;
        }
        else if (definition.IsStorage)
        {
            cycleRate = 0f;
            cycleProgress = 0f;
            producedCount = 0;
            inputCount = 0;
        }
    }

    public void ClampPosition(Vector2Int interiorSize)
    {
        var safePosition = SanitizeLogicalPosition(logicalPosition);
        var maximum = new Vector2(
            Mathf.Max(0.5f, interiorSize.x - 0.5f),
            Mathf.Max(0.5f, interiorSize.y - 0.5f));
        logicalPosition = new Vector2(
            Mathf.Clamp(safePosition.x, 0.5f, maximum.x),
            Mathf.Clamp(safePosition.y, 0.5f, maximum.y));
    }

    public void Advance(float deltaTime)
    {
        if (IsStorage || IsTerminal)
        {
            return;
        }

        if (IsProcessor)
        {
            AdvanceProcessor(deltaTime);
            return;
        }

        if (float.IsNaN(deltaTime)
            || float.IsInfinity(deltaTime)
            || deltaTime <= 0f
            || cycleRate <= 0f)
        {
            return;
        }

        if (outputCount >= OutputCapacity)
        {
            return;
        }

        var remainingTime = (double)deltaTime;
        var rate = (double)cycleRate;
        while (remainingTime > 0d && outputCount < OutputCapacity)
        {
            var timeToComplete = (1d - cycleProgress) / rate;
            if (timeToComplete > remainingTime)
            {
                cycleProgress += (float)(rate * remainingTime);
                return;
            }

            cycleProgress = 0f;
            remainingTime -= timeToComplete;
            outputCount++;
            if (producedCount < int.MaxValue)
            {
                producedCount++;
            }
        }
    }

    public bool CanAcceptItem(string itemId)
    {
        return IsReceiver
            && !string.IsNullOrWhiteSpace(itemId)
            && AcceptedItemId == itemId;
    }

    public bool CanSupplyItem(string itemId)
    {
        return IsSupplier
            && !string.IsNullOrWhiteSpace(itemId)
            && SuppliedItemId == itemId;
    }

    public int GetSupplyCapacity(string itemId)
    {
        return CanSupplyItem(itemId) ? outputCount : 0;
    }

    public int GetAcceptCapacity(string itemId)
    {
        if (!CanAcceptItem(itemId))
        {
            return 0;
        }

        return IsProcessor
            ? InputCapacity - inputCount
            : OutputCapacity - outputCount;
    }

    public int AddInput(int quantity)
    {
        if (!IsProcessor || quantity <= 0)
        {
            return 0;
        }

        var accepted = Mathf.Min(quantity, InputCapacity - inputCount);
        inputCount += accepted;
        return accepted;
    }

    public bool TryAddInput(int quantity, out int accepted)
    {
        accepted = AddInput(quantity);
        return quantity > 0 && accepted == quantity;
    }

    public void SetInputCount(int quantity)
    {
        inputCount = IsProcessor
            ? Mathf.Clamp(quantity, 0, InputCapacity)
            : 0;
    }

    public int TryAcceptItem(string itemId, int quantity)
    {
        if (!CanAcceptItem(itemId))
        {
            return 0;
        }

        return IsProcessor ? AddInput(quantity) : AddOutput(quantity);
    }

    public bool TryAcceptItem(string itemId, int quantity, out int accepted)
    {
        accepted = TryAcceptItem(itemId, quantity);
        return quantity > 0 && accepted == quantity;
    }

    public bool TryExtractItem(string itemId, int quantity, out int removed)
    {
        removed = 0;
        if (!CanSupplyItem(itemId) || quantity <= 0 || outputCount < quantity)
        {
            return false;
        }

        outputCount -= quantity;
        removed = quantity;
        return true;
    }

    public bool TryRemoveAcceptedItem(string itemId, int quantity, out int removed)
    {
        removed = 0;
        if (!CanAcceptItem(itemId) || quantity <= 0)
        {
            return false;
        }

        if (IsProcessor)
        {
            return TryRemoveInput(quantity, out removed);
        }

        return TryRemoveOutput(quantity, out removed);
    }

    public bool TryRemoveInput(int quantity, out int removed)
    {
        removed = 0;
        if (!IsProcessor || quantity <= 0 || inputCount < quantity)
        {
            return false;
        }

        inputCount -= quantity;
        removed = quantity;
        return true;
    }

    public int DrainOutput()
    {
        var removed = outputCount;
        outputCount = 0;
        return removed;
    }

    public bool TryRemoveOutput(int quantity)
    {
        return TryRemoveOutput(quantity, out _);
    }

    public bool RemoveOutput(int quantity)
    {
        return TryRemoveOutput(quantity);
    }

    public bool TryRemoveOutput(int quantity, out int removed)
    {
        removed = 0;
        if (quantity <= 0 || outputCount < quantity)
        {
            return false;
        }

        outputCount -= quantity;
        removed = quantity;
        return true;
    }

    public int AddOutput(int quantity)
    {
        if (quantity <= 0)
        {
            return 0;
        }

        var accepted = Mathf.Min(quantity, OutputCapacity - outputCount);
        outputCount += accepted;
        return accepted;
    }

    public int AcceptOutput(int quantity)
    {
        return AddOutput(quantity);
    }

    public bool TryAddOutput(int quantity, out int accepted)
    {
        accepted = AddOutput(quantity);
        return quantity > 0 && accepted == quantity;
    }

    public FactoryEntityRecord Clone()
    {
        return new FactoryEntityRecord(
            entityId,
            definitionId,
            logicalPosition,
            cycleRate,
            cycleProgress,
            producedCount,
            outputCount,
            inputCount);
    }

    public FactoryEntitySnapshot ToSnapshot()
    {
        return new FactoryEntitySnapshot(
            entityId,
            definitionId,
            logicalPosition,
            cycleRate,
            cycleProgress,
            producedCount,
            outputCount,
            inputCount);
    }

    private void AdvanceProcessor(float deltaTime)
    {
        var definition = FactoryEntityDefinitions.Get(definitionId);
        if (float.IsNaN(deltaTime)
            || float.IsInfinity(deltaTime)
            || deltaTime <= 0f
            || !definition.IsProcessor
            || definition.CycleDuration <= 0f
            || outputCount >= OutputCapacity
            || inputCount < definition.InputQuantity)
        {
            return;
        }

        var remainingTime = (double)deltaTime;
        var cycleDuration = (double)definition.CycleDuration;
        while (remainingTime > 0d
            && outputCount < OutputCapacity
            && inputCount >= definition.InputQuantity)
        {
            var timeToComplete = (1d - cycleProgress) * cycleDuration;
            if (timeToComplete > remainingTime)
            {
                cycleProgress += (float)(remainingTime / cycleDuration);
                return;
            }

            cycleProgress = 0f;
            remainingTime -= timeToComplete;
            inputCount -= definition.InputQuantity;
            outputCount = Mathf.Min(
                OutputCapacity,
                outputCount + definition.OutputQuantity);
            if (producedCount < int.MaxValue)
            {
                producedCount++;
            }
        }
    }

    private static Vector2 SanitizeLogicalPosition(Vector2 position)
    {
        return new Vector2(
            float.IsNaN(position.x) || float.IsInfinity(position.x) ? 0.5f : position.x,
            float.IsNaN(position.y) || float.IsInfinity(position.y) ? 0.5f : position.y);
    }
}
