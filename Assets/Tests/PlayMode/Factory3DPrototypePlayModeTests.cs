// Exercises the bounded two-floor prototype loop through deterministic Input System device events.
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
    public IEnumerator PrototypeCompletesMovementCollisionInteractionPlacementAndIsolatedTopologyCheck()
    {
        yield return SceneManager.LoadSceneAsync("Factory3DPrototype", LoadSceneMode.Single);
        yield return null;
        yield return null;

        var controller = Object.FindFirstObjectByType<Factory3DPrototypeController>();
        Assert.That(controller, Is.Not.Null);
        Assert.That(controller.PrototypeCamera.orthographic, Is.True);
        Assert.That(controller.WorldState.FloorStates.Count(), Is.EqualTo(2));
        Assert.That(controller.WorldState.ConnectionCount, Is.EqualTo(1));
        Assert.That(controller.IsolatedSaveLoadPreservedTopology, Is.True);
        Assert.That(controller.Player, Is.Not.Null);
        Assert.That(controller.Player.IsGrounded, Is.True);
        Assert.That(controller.Player.transform.position.y, Is.EqualTo(0f).Within(0.1f));
        Assert.That(controller.ActiveFloor, Is.EqualTo(0));
        Assert.That(controller.IsFloorVisible(0), Is.True);
        Assert.That(controller.IsFloorVisible(1), Is.False);

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

            var characterController = controller.Player.GetComponent<CharacterController>();
            controller.TeleportPlayer(
                controller.Player,
                controller.LogicalToWorldPositionForTest(new Vector2(4f, 5f), 0),
                0);
            controller.Player.enabled = false;
            characterController.Move(Vector3.forward * 10f);
            controller.Player.enabled = true;
            Assert.That(
                controller.Player.transform.position.z,
                Is.LessThan(controller.GetFloorBoundsForTest(0).max.z - characterController.radius));

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            InputSystem.Update();
            yield return new WaitForSeconds(2f);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            yield return null;
            Assert.That(controller.Player.transform.position.z, Is.LessThan(controller.GetFloorBoundsForTest(0).max.z));

            Assert.That(
                controller.WorldState.TryGetFloorState(9001u, 0, out var lowerFloor),
                Is.True);
            var machine = lowerFloor.Entities.Single(
                entity => entity.DefinitionId == FactoryEntityDefinitions.TestMachineDefinitionId);
            var producedBeforeInteraction = machine.ProducedCount;
            controller.TeleportPlayer(
                controller.Player,
                controller.LogicalToWorldPositionForTest(machine.LogicalPosition, 0),
                0);
            Assert.That(controller.Player.InteractionPrompt, Does.Contain("machine"));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            InputSystem.Update();
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Assert.That(controller.LastInteraction, Does.StartWith("Machine:"));
            Assert.That(controller.LastInteractedProducedCount, Is.GreaterThan(producedBeforeInteraction));

            var elevator = lowerFloor.Entities.Single(entity => entity.IsElevator);
            controller.TeleportPlayer(
                controller.Player,
                controller.LogicalToWorldPositionForTest(elevator.LogicalPosition, 0),
                0);
            Assert.That(controller.Player.InteractionPrompt, Does.Contain("elevator"));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            InputSystem.Update();
            yield return null;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            Assert.That(controller.ActiveFloor, Is.EqualTo(1));
            Assert.That(controller.Player.FloorIndex, Is.EqualTo(1));
            Assert.That(controller.IsFloorVisible(0), Is.False);
            Assert.That(controller.IsFloorVisible(1), Is.True);

            Assert.That(
                controller.TryPlaceStorageAtLogicalPosition(new Vector2(0.5f, 0.5f), out var placementError),
                Is.True,
                placementError);
            Assert.That(
                controller.WorldState.TryGetFloorState(9001u, 1, out var upperFloor),
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
