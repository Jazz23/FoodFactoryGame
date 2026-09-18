// Adapts the disposable prototype player to the reusable 3D traversal controller.
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class Factory3DPrototypePlayer : MonoBehaviour
{
    private Factory3DTraversalController traversal;
    private Factory3DPrototypeController prototype;
    private int floorIndex;

    public int FloorIndex => floorIndex;
    public bool IsGrounded => traversal is not null && traversal.IsGrounded;
    public float MoveSpeed => traversal is not null ? traversal.MoveSpeed : 0f;
    public string InteractionPrompt => prototype.GetInteractionPrompt(this);

    public void Configure(float newMoveSpeed, int newFloorIndex, Factory3DPrototypeController newPrototype)
    {
        floorIndex = newFloorIndex;
        prototype = newPrototype;
        traversal = GetComponent<Factory3DTraversalController>();
        if (traversal is null)
        {
            traversal = gameObject.AddComponent<Factory3DTraversalController>();
        }

        traversal.ConfigureSpatialContext(
            newPrototype.SpatialAdapter,
            newPrototype.BuildingInstanceId,
            newFloorIndex,
            newPrototype.StoryHeight);
        traversal.SetMoveSpeed(newMoveSpeed);
        traversal.InteractionRequested += OnInteractionRequested;
        traversal.Set3DOwnership(true);
    }

    public void Teleport(Vector3 position, int newFloorIndex)
    {
        floorIndex = newFloorIndex;
        traversal.ConfigureSpatialContext(
            prototype.SpatialAdapter,
            prototype.BuildingInstanceId,
            newFloorIndex,
            prototype.StoryHeight);
        traversal.Teleport(position, newFloorIndex);
    }

    private void OnInteractionRequested(Vector3 _)
    {
        prototype.InteractForTest();
    }

    private void OnDestroy()
    {
        if (traversal is not null)
        {
            traversal.InteractionRequested -= OnInteractionRequested;
        }
    }
}
