// Tracks runtime-only shared interior scene instances without touching persistent floor records.
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using FishNet.Utility.Extension;
#if UNITY_6000_5_OR_NEWER
using SceneHandle = System.UInt64;
#else
using SceneHandle = System.Int32;
#endif

public sealed class OutsideTestFloorInstanceRegistry
{
    public readonly struct FloorInstance
    {
        internal FloorInstance(
            OutsideTestFloorKey newKey,
            Scene newScene)
        {
            Key = newKey;
            Scene = newScene;
            Handle = newScene.GetRawHandle();
        }

        public OutsideTestFloorKey Key { get; }
        public Scene Scene { get; }
        public SceneHandle Handle { get; }
        public bool IsValid => Scene.IsValid() && Scene.isLoaded && Handle != 0;
    }

    private readonly Dictionary<OutsideTestFloorKey, FloorInstance> loadedInstances = new();
    private readonly Dictionary<OutsideTestFloorKey, bool> pendingLoads = new();
    private readonly Dictionary<SceneHandle, OutsideTestFloorKey> keysByHandle = new();

    public int LoadedCount => loadedInstances.Count;
    public int PendingCount => pendingLoads.Count;
    public IEnumerable<FloorInstance> LoadedInstances => loadedInstances.Values;
    public IEnumerable<OutsideTestFloorKey> PendingKeys => pendingLoads.Keys;

    public bool TryGetLoaded(
        OutsideTestFloorKey key,
        out FloorInstance instance)
    {
        if (loadedInstances.TryGetValue(key, out instance)
            && instance.IsValid)
        {
            return true;
        }

        if (loadedInstances.ContainsKey(key))
        {
            Remove(key);
        }

        instance = default;
        return false;
    }

    public bool IsPending(OutsideTestFloorKey key)
    {
        return pendingLoads.ContainsKey(key);
    }

    public bool TryBeginLoad(OutsideTestFloorKey key)
    {
        if (loadedInstances.ContainsKey(key) || pendingLoads.ContainsKey(key))
        {
            return false;
        }

        pendingLoads.Add(key, true);
        return true;
    }

    public bool RegisterLoaded(
        OutsideTestFloorKey key,
        Scene scene)
    {
        var instance = new FloorInstance(key, scene);
        if (!scene.IsValid() || instance.Handle == 0)
        {
            return false;
        }

        if (loadedInstances.TryGetValue(key, out var existing))
        {
            if (existing.Handle != instance.Handle)
            {
                return false;
            }

            pendingLoads.Remove(key);
            return true;
        }

        if (keysByHandle.ContainsKey(instance.Handle))
        {
            return false;
        }

        loadedInstances.Add(key, instance);
        keysByHandle.Add(instance.Handle, key);
        pendingLoads.Remove(key);
        return true;
    }

    public bool FailLoad(OutsideTestFloorKey key)
    {
        return pendingLoads.Remove(key);
    }

    public bool TryGetKey(
        SceneHandle handle,
        out OutsideTestFloorKey key)
    {
        return keysByHandle.TryGetValue(handle, out key);
    }

    public bool Remove(SceneHandle handle)
    {
        if (!keysByHandle.TryGetValue(handle, out var key))
        {
            return false;
        }

        return Remove(key, handle);
    }

    public bool Remove(Scene scene)
    {
        return Remove(scene.GetRawHandle());
    }

    public bool Remove(OutsideTestFloorKey key)
    {
        if (!loadedInstances.TryGetValue(key, out var instance))
        {
            return false;
        }

        return Remove(key, instance.Handle);
    }

    public void Clear()
    {
        loadedInstances.Clear();
        pendingLoads.Clear();
        keysByHandle.Clear();
    }

    private bool Remove(
        OutsideTestFloorKey key,
        SceneHandle handle)
    {
        if (!loadedInstances.Remove(key))
        {
            return false;
        }

        keysByHandle.Remove(handle);
        return true;
    }
}
