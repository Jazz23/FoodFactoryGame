// Advances authoritative factory state on a fixed tick without depending on Unity scenes or visuals.
using System;

public sealed class FactorySimulation
{
    public const float DefaultTickInterval = 0.1f;

    private readonly Action<float> advanceState;
    private readonly double tickInterval;
    private double accumulatedTime;

    public FactorySimulation(
        Action<float> newAdvanceState,
        float newTickInterval = DefaultTickInterval)
    {
        if (float.IsNaN(newTickInterval)
            || float.IsInfinity(newTickInterval)
            || newTickInterval <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(newTickInterval));
        }

        advanceState = newAdvanceState;
        tickInterval = newTickInterval;
    }

    public int Advance(float deltaTime)
    {
        if (float.IsNaN(deltaTime)
            || float.IsInfinity(deltaTime)
            || deltaTime <= 0f)
        {
            return 0;
        }

        accumulatedTime += deltaTime;
        var tickEpsilon = Math.Max(1e-9d, tickInterval * 1e-6d);
        var tickCount = 0;
        while (accumulatedTime + tickEpsilon >= tickInterval)
        {
            advanceState((float)tickInterval);
            accumulatedTime -= tickInterval;
            tickCount++;
        }

        if (accumulatedTime < tickEpsilon)
        {
            accumulatedTime = 0d;
        }

        return tickCount;
    }

    public void Reset()
    {
        accumulatedTime = 0d;
    }
}
