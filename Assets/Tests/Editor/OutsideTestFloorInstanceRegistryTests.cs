// Verifies runtime floor-instance identity and pending-load coalescing.
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using FishNet.Utility.Extension;
using NUnit.Framework;

public sealed class OutsideTestFloorInstanceRegistryTests
{
    [Test]
    public void PendingRequestsCoalesceByFloorKey()
    {
        var registry = new OutsideTestFloorInstanceRegistry();
        var key = new OutsideTestFloorKey(7, 1);

        Assert.That(registry.TryBeginLoad(key), Is.True);
        Assert.That(registry.TryBeginLoad(key), Is.False);
        Assert.That(registry.PendingCount, Is.EqualTo(1));
        Assert.That(registry.FailLoad(key), Is.True);
        Assert.That(registry.PendingCount, Is.Zero);
    }

    [Test]
    public void DistinctFloorKeysRegisterSeparateLoadedInstances()
    {
        var firstScene = OpenTestScene();
        try
        {
            var registry = new OutsideTestFloorInstanceRegistry();
            var firstKey = new OutsideTestFloorKey(7, 0);
            var secondKey = new OutsideTestFloorKey(7, 1);

            Assert.That(registry.RegisterLoaded(firstKey, firstScene), Is.True);
            Assert.That(registry.TryBeginLoad(secondKey), Is.True);
            Assert.That(registry.TryGetLoaded(firstKey, out var instance), Is.True);
            Assert.That(instance.Key, Is.EqualTo(firstKey));
            Assert.That(registry.TryGetLoaded(secondKey, out _), Is.False);
            Assert.That(registry.IsPending(secondKey), Is.True);
            Assert.That(registry.TryGetKey(instance.Handle, out var registeredKey), Is.True);
            Assert.That(registeredKey, Is.EqualTo(firstKey));
        }
        finally
        {
            EditorSceneManager.CloseScene(firstScene, true);
        }
    }

    [Test]
    public void RemovingAHandleInvalidatesItsFloorInstance()
    {
        var scene = OpenTestScene();
        try
        {
            var registry = new OutsideTestFloorInstanceRegistry();
            var key = new OutsideTestFloorKey(8, 0);
            Assert.That(registry.RegisterLoaded(key, scene), Is.True);

            Assert.That(registry.Remove(scene), Is.True);
            Assert.That(registry.TryGetLoaded(key, out _), Is.False);
            Assert.That(registry.TryGetKey(scene.GetRawHandle(), out _), Is.False);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static Scene OpenTestScene()
    {
        return EditorSceneManager.OpenScene(
            "Assets/Scenes/insidefactory0.unity",
            OpenSceneMode.Additive);
    }
}
