// Provides proximity checks and local prompt state for the insidefactory elevator.
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class InsideFactoryElevator : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float interactionRadius = 0.9f;

    private uint buildingInstanceId;
    private Vector2Int buildingSize;
    private int storyCount = 1;
    private int floorIndex;
    private bool isConfigured;
    private ElevatorPromptView prompt = null!;

    public uint BuildingInstanceId => buildingInstanceId;
    public int StoryCount => storyCount;
    public int CurrentFloor => floorIndex;
    public bool CanGoUp => IsFloorAvailable(floorIndex + 1);
    public bool CanGoDown => IsFloorAvailable(floorIndex - 1);
    public bool CanOpenPrompt => isConfigured && storyCount > 1;
    public Vector2 InteractionLogicalPosition => GetInteractionLogicalPosition(buildingSize);
    public bool IsPromptOpen => prompt is not null && prompt.gameObject.activeSelf;

    public void Configure(
        uint newBuildingInstanceId,
        Vector2Int newBuildingSize,
        int newStoryCount,
        int newFloorIndex)
    {
        buildingInstanceId = newBuildingInstanceId;
        buildingSize = newBuildingSize;
        storyCount = Mathf.Max(1, newStoryCount);
        floorIndex = Mathf.Clamp(newFloorIndex, 0, storyCount - 1);
        isConfigured = BuildingFootprint.IsValid(buildingSize);
        if (prompt is not null && prompt)
        {
            prompt.SetFloorOptions(floorIndex, storyCount);
        }
    }

    public bool IsFloorAvailable(int targetFloorIndex)
    {
        return isConfigured
            && targetFloorIndex >= 0
            && targetFloorIndex < storyCount
            && targetFloorIndex != floorIndex;
    }

    public bool CanUse(Vector2 playerPosition)
    {
        if (!isConfigured || !SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            return false;
        }

        var interactionPosition = grid.LogicalToWorld(InteractionLogicalPosition);
        return (playerPosition - interactionPosition).sqrMagnitude
            <= interactionRadius * interactionRadius;
    }

    public void OpenPrompt()
    {
        if (!CanOpenPrompt)
        {
            return;
        }

        var view = GetPrompt();
        view.SetFloorOptions(floorIndex, storyCount);
        view.Show();
    }

    public void ClosePrompt()
    {
        if (prompt is not null && prompt)
        {
            prompt.Hide();
        }
    }

    public static Vector2 GetInteractionLogicalPosition(Vector2Int size)
    {
        return new Vector2(
            size.x * 0.5f,
            Mathf.Max(0.5f, size.y - 0.5f));
    }

    public static bool TryGetForScene(Scene scene, out InsideFactoryElevator elevator)
    {
        elevator = null!;
        var elevators = FindObjectsByType<InsideFactoryElevator>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var candidate in elevators)
        {
            if (candidate.gameObject.scene != scene || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            elevator = candidate;
            return true;
        }

        return false;
    }

    private ElevatorPromptView GetPrompt()
    {
        if (prompt is null || !prompt)
        {
            var promptObject = new GameObject(
                "Elevator Prompt",
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster),
                typeof(ElevatorPromptView));
            promptObject.transform.SetParent(transform, false);
            prompt = promptObject.GetComponent<ElevatorPromptView>();
            prompt.Initialize();
            promptObject.SetActive(false);
        }

        return prompt;
    }
}
