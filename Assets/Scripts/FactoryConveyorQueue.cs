// Stores a bounded, ordered conveyor queue and its versioned persistence payload.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class FactoryConveyorQueue
{
    public const int Capacity = 4;
    public const float Spacing = 0.25f;
    public const float TravelTime = 0.6f;
    private const float Epsilon = 0.000001f;
    [SerializeField] private List<float> positions = new();

    public IReadOnlyList<float> Positions => positions;
    public int Count => positions.Count;
    public int ReadyCount => Count > 0 && positions[0] >= 1f - Epsilon ? 1 : 0;
    public bool HasEntranceSpace => Count < Capacity && (Count == 0 || positions[Count - 1] >= Spacing - Epsilon);

    public void Restore(float[] savedPositions, int oldInput, int oldOutput, float oldProgress)
    {
        positions.Clear();
        if (savedPositions is not null)
        {
            Validate(savedPositions);
            positions.AddRange(savedPositions);
            return;
        }
        // Old belts stored either one in-flight input or one waiting output.
        if (oldOutput > 0) positions.Add(1f);
        if (oldInput > 0) positions.Add(Mathf.Min(Mathf.Clamp01(oldProgress), positions.Count == 0 ? 1f : 1f - Spacing));
    }

    public int Accept(int quantity)
    {
        if (quantity <= 0 || !HasEntranceSpace) return 0;
        positions.Add(0f);
        return 1;
    }

    public int AddReady(int quantity)
    {
        if (quantity <= 0 || Count >= Capacity || (Count > 0 && positions[0] > 1f - Spacing + Epsilon)) return 0;
        positions.Insert(0, 1f);
        return 1;
    }

    public bool RemoveReady(int quantity)
    {
        if (quantity != 1 || ReadyCount == 0) return false;
        positions.RemoveAt(0);
        return true;
    }

    public bool RemoveInputs(int quantity)
    {
        if (quantity <= 0 || quantity > Count - ReadyCount) return false;
        positions.RemoveRange(Count - quantity, quantity);
        return true;
    }

    public void Advance(float deltaTime)
    {
        if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return;
        var limit = 1f;
        for (var index = 0; index < Count; index++)
        {
            positions[index] = Mathf.Min(limit, positions[index] + deltaTime / TravelTime);
            if (positions[index] >= 1f - Epsilon) positions[index] = 1f;
            limit = positions[index] - Spacing;
        }
    }

    public void SetPositions(float[] values)
    {
        positions.Clear();
        positions.AddRange(values);
    }

    public float[] Snapshot() => positions.ToArray();

    public static byte[] Encode(IReadOnlyList<float> values)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(0x31514243); // CBQ1, followed by count and head-to-tail positions.
        writer.Write(values.Count);
        foreach (var value in values) writer.Write(value);
        return stream.ToArray();
    }

    public static float[] Decode(byte[] data)
    {
        if (data is null || data.Length == 0) return null!;
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        if (data.Length < 8 || reader.ReadInt32() != 0x31514243) throw new InvalidDataException("Invalid conveyor queue payload.");
        var count = reader.ReadInt32();
        if (count < 0 || count > Capacity || data.Length != 8 + count * 4) throw new InvalidDataException("Invalid conveyor queue size.");
        var values = new float[count];
        for (var index = 0; index < count; index++) values[index] = reader.ReadSingle();
        Validate(values);
        return values;
    }

    private static void Validate(float[] values)
    {
        if (values.Length > Capacity) throw new InvalidDataException("Conveyor queue exceeds capacity.");
        var limit = 1f;
        foreach (var value in values)
        {
            if (!float.IsFinite(value) || value < -Epsilon || value > limit + Epsilon)
                throw new InvalidDataException("Conveyor positions must be finite, ordered, and spaced apart.");
            limit = value - Spacing;
        }
    }
}
