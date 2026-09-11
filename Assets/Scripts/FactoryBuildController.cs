// Provides play-mode factory equipment selection, placement preview, rotation, and removal on the current floor.
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class FactoryBuildController : MonoBehaviour
{
    private PlayerSceneTransition owner = null!;
    private InputActionMap actions = null!;
    private GameObject preview = null!;
    private SpriteRenderer previewRenderer = null!;
    private Sprite previewSprite = null!;
    private FactoryConveyorArt art = null!;
    private TextMesh previewLabel = null!;
    private int selection;
    private int direction;
    private bool building;
    private bool activeFloor;
    private bool validCell;
    private Vector2 position;
    private uint hoveredId;
    private string status = "Place a source, belts, then storage. Belts send toward their arrow.";
    private readonly string[] labels = { "1 Source", "2 Conveyor", "3 Storage" };
    private readonly string[] arrows = { "East >", "North ^", "West <", "South v" };
    private Rect Toolbar => new(12f, Screen.height - 116f, Mathf.Min(680f, Screen.width - 24f), 104f);
    private string Definition => selection == 0 ? FactoryEntityDefinitions.TestMachineDefinitionId
        : selection == 2 ? FactoryEntityDefinitions.TestStorageDefinitionId : FactoryConveyor.Definitions[direction];

    public void Initialize(PlayerSceneTransition newOwner)
    {
        owner = newOwner;
        actions = InputSystem.actions.FindActionMap("FactoryBuild", true).Clone();
        actions.Enable();
        preview = new GameObject("Factory Placement Preview");
        preview.transform.SetParent(transform, false);
        previewRenderer = preview.AddComponent<SpriteRenderer>();
        previewSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), Vector2.one * 0.5f, 1f);
        previewRenderer.sprite = previewSprite;
        previewRenderer.sortingOrder = FactoryConveyor.EquipmentSortingOrder;
        preview.transform.localScale = Vector3.one * 0.65f;
        var labelObject = new GameObject("Placement Label");
        labelObject.transform.SetParent(preview.transform, false);
        previewLabel = labelObject.AddComponent<TextMesh>();
        previewLabel.anchor = TextAnchor.MiddleCenter;
        previewLabel.fontSize = 24;
        previewLabel.characterSize = 0.06f;
        previewLabel.GetComponent<MeshRenderer>().sortingOrder = FactoryConveyor.ItemSortingOrder;
        art = Resources.Load<FactoryConveyorArt>("FactoryConveyorArt");
        preview.SetActive(false);
    }

    public void SetStatus(string message) => status = message;

    private void Update()
    {
        activeFloor = !owner.IsTransitioning && owner.TryGetCurrentOutsideTestFloor(out _, out _);
        if (!activeFloor)
        {
            preview.SetActive(false);
            building = false;
            return;
        }
        if (EventSystem.current is not null && EventSystem.current.currentSelectedGameObject is not null
            && EventSystem.current.currentSelectedGameObject && EventSystem.current.currentSelectedGameObject.TryGetComponent<InputField>(out var field) && field.isFocused)
            return;
        if (actions["Toggle"].WasPressedThisFrame()) building = !building;
        if (actions["Cancel"].WasPressedThisFrame()) building = false;
        for (var index = 0; index < labels.Length; index++)
            if (actions[$"Select{index + 1}"].WasPressedThisFrame()) { selection = index; building = true; }
        if (actions["Rotate"].WasPressedThisFrame()) direction = (direction + 1) % 4;
        preview.SetActive(building);
        if (!building) return;
        var screen = actions["Point"].ReadValue<Vector2>();
        var guiPoint = new Vector2(screen.x, Screen.height - screen.y);
        var overUI = Toolbar.Contains(guiPoint) || TestUIVisibility.ButtonRect.Contains(guiPoint)
            || (EventSystem.current is not null && EventSystem.current.IsPointerOverGameObject());
        SceneGrid.TryGetForScene(gameObject.scene, out var grid);
        var camera = Camera.main!;
        var ray = camera.ScreenPointToRay(screen);
        var plane = new Plane(Vector3.forward, Vector3.zero);
        if (!plane.Raycast(ray, out var distance)) { preview.SetActive(false); return; }
        var logical = grid.WorldToLogical(ray.GetPoint(distance));
        position = (Vector2)Vector2Int.FloorToInt(logical) + Vector2.one * 0.5f;
        IndoorGrid.TryGetForScene(gameObject.scene, out var indoor);
        owner.TryGetCurrentOutsideTestFloor(out var buildingId, out var floorIndex);
        GameSceneManager.Instance.TryGetOutsideTestFloorState(buildingId, floorIndex, out var floor);
        hoveredId = 0;
        foreach (var entity in floor.Entities)
            if (Vector2Int.FloorToInt(entity.LogicalPosition) == Vector2Int.FloorToInt(position)) hoveredId = entity.EntityId;
        validCell = !overUI && hoveredId == 0 && position.x >= 0.5f && position.y >= 0.5f
            && position.x < indoor.Size.x && position.y < indoor.Size.y;
        preview.transform.position = grid.LogicalToWorld(position);
        previewRenderer.sprite = selection == 1 ? art.sprite : previewSprite;
        var delta = grid.LogicalToWorld(position + FactoryConveyor.Direction(Definition)) - grid.LogicalToWorld(position);
        preview.transform.rotation = selection == 1
            ? Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f) : Quaternion.identity;
        preview.transform.localScale = selection == 1 ? new Vector3(delta.magnitude * 0.8f, delta.magnitude, 1f) : Vector3.one * 0.65f;
        previewLabel.transform.rotation = Quaternion.identity;
        previewLabel.transform.position = preview.transform.position + Vector3.up * 0.65f;
        previewLabel.text = selection == 1 ? arrows[direction] : labels[selection].Substring(2);
        previewRenderer.color = validCell ? new Color(0.3f, 1f, 0.6f, 0.5f) : new Color(1f, 0.2f, 0.2f, 0.5f);
        if (!overUI && actions["Remove"].WasPressedThisFrame() && hoveredId != 0) owner.RequestRemoveCurrentFloorEntity(hoveredId);
        if (validCell && actions["Place"].WasPressedThisFrame()) owner.RequestPlaceEquipment(Definition, position);
    }

    private void OnGUI()
    {
        if (!activeFloor) return;
        GUILayout.BeginArea(Toolbar, GUI.skin.box);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(building ? "B: Stop building" : "B: Build")) building = !building;
        for (var index = 0; index < labels.Length; index++)
            if (GUILayout.Toggle(building && selection == index, labels[index], GUI.skin.button)) { selection = index; building = true; }
        if (GUILayout.Button($"R: {arrows[direction]}")) direction = (direction + 1) % 4;
        GUILayout.EndHorizontal();
        GUILayout.Label("Click: place | Right click: remove | Esc: cancel | F2: test UIs | F3: floor debug");
        GUILayout.Label(status);
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        actions.Disable();
        if (preview is not null) Destroy(preview);
        if (previewSprite is not null) Destroy(previewSprite);
    }
}

