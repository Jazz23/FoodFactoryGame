// Drives the fixed-camera 3D prototype through Input System actions and its test-safe presentation API.
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public sealed class Factory3DPrototypePlayModeTests
{
    [UnityTest]
    public IEnumerator PrototypeMovesCyclesFloorsAndPlacesOnTheActiveFloor()
    {
        yield return SceneManager.LoadSceneAsync("Factory3DPrototype", LoadSceneMode.Single);
        yield return null;

        var controller = Object.FindFirstObjectByType<Factory3DPrototypeController>();
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller.WorldState.FloorStates.Count(), Is.EqualTo(2));
        Assert.That(controller.WorldState.ConnectionCount, Is.EqualTo(1));
        Assert.That(controller.Player, Is.Not.Null);
        Assert.That(controller.ActiveFloor, Is.EqualTo(0));

        var keyboard = Keyboard.current;
        var addedKeyboard = keyboard is null;
        keyboard ??= InputSystem.AddDevice<Keyboard>();
        try
        {
            var startingX = controller.Player.transform.position.x;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            InputSystem.Update();
            yield return new WaitForSeconds(0.25f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            yield return null;
            Assert.That(controller.Player.transform.position.x, Is.GreaterThan(startingX + 0.01f));

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.R));
            InputSystem.Update();
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            yield return null;
            Assert.That(controller.ActiveFloor, Is.EqualTo(1));
            Assert.That(controller.Player.transform.position.y, Is.EqualTo(3.5f).Within(0.01f));

            Assert.That(
                controller.TryPlaceStorageAtLogicalPosition(new Vector2(0.5f, 0.5f), out var placementError),
                Is.True,
                placementError);
            Assert.That(
                controller.WorldState.TryGetFloorState(controller.WorldState.Buildings.Single().BuildingInstanceId, 1, out var upperFloor),
                Is.True);
            Assert.That(upperFloor.Entities.Any(entity => entity.IsStorage), Is.True);
        }
        finally
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            if (addedKeyboard)
            {
                InputSystem.RemoveDevice(keyboard);
            }
        }
    }
}
