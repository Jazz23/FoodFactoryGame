// Presents one OutsideTest floor record and its persisted entities inside the loaded interior scene.
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class OutsideTestFloorPresentation : MonoBehaviour
{
    private const float MarkerSize = 0.35f;
    private const float LabelHeight = 0.55f;

    private Transform marker = null!;
    private TextMesh markerLabel = null!;
    private Sprite markerSprite = null!;
    private readonly Dictionary<uint, EntityView> entityViews = new();
    private readonly HashSet<uint> activeEntityIds = new();
    private readonly List<uint> staleEntityIds = new();
    private uint buildingInstanceId;
    private int floorIndex;
    private bool isConfigured;

    public static int LoadedCount { get; private set; }

    private void OnEnable()
    {
        LoadedCount++;
    }

    private void OnDisable()
    {
        LoadedCount = Mathf.Max(0, LoadedCount - 1);
    }

    private void LateUpdate()
    {
        if (!isConfigured
            || !GameSceneManager.Instance.TryGetOutsideTestFloorState(
                buildingInstanceId,
                floorIndex,
                out var state))
        {
            return;
        }

        ApplyState(state);
    }

    public void Configure(uint buildingInstanceId, int newFloorIndex)
    {
        if (buildingInstanceId == 0 || newFloorIndex < 0)
        {
            return;
        }

        this.buildingInstanceId = buildingInstanceId;
        floorIndex = newFloorIndex;
        isConfigured = true;
        CreateVisuals();
        if (GameSceneManager.Instance.TryGetOutsideTestFloorState(
                buildingInstanceId,
                floorIndex,
                out var state))
        {
            ApplyState(state);
        }
    }

    private void CreateVisuals()
    {
        if (marker is null || !marker)
        {
            var markerObject = new GameObject("OutsideTest Marker");
            markerObject.transform.SetParent(transform, false);
            marker = markerObject.transform;
            var renderer = markerObject.AddComponent<SpriteRenderer>();
            markerSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            renderer.sprite = markerSprite;
            renderer.color = new Color(0.2f, 0.95f, 0.75f, 1f);
            renderer.sortingOrder = 10;
            marker.localScale = Vector3.one * MarkerSize;
        }

        if (markerLabel is null || !markerLabel)
        {
            var labelObject = new GameObject("OutsideTest Floor Label");
            labelObject.transform.SetParent(transform, false);
            markerLabel = labelObject.AddComponent<TextMesh>();
            markerLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            markerLabel.anchor = TextAnchor.MiddleCenter;
            markerLabel.alignment = TextAlignment.Center;
            markerLabel.fontSize = 32;
            markerLabel.characterSize = 0.05f;
            markerLabel.color = new Color(0.86f, 0.98f, 0.94f, 1f);
        }
    }

    private void ApplyState(OutsideTestFloorRecord state)
    {
        var markerPosition = state.MarkerPosition;
        if (SceneGrid.TryGetForScene(gameObject.scene, out var grid)
            && IndoorGrid.TryGetForScene(gameObject.scene, out var indoorGrid))
        {
            markerPosition = OutsideTestFloorRecord.ClampMarkerPosition(
                markerPosition,
                indoorGrid.Size);
            var worldPosition = grid.LogicalToWorld(markerPosition);
            var position = new Vector3(
                worldPosition.x,
                worldPosition.y,
                transform.position.z - 0.1f);
            marker.position = position;
            markerLabel.transform.position = position + Vector3.up * LabelHeight;
            ApplyEntityViews(state, grid, indoorGrid);
        }

        markerLabel.text = $"{state.Label}\n{state.AccumulatedProduction:0.0}";
    }

    private void ApplyEntityViews(
        OutsideTestFloorRecord state,
        SceneGrid grid,
        IndoorGrid indoorGrid)
    {
        activeEntityIds.Clear();
        foreach (var entity in state.Entities)
        {
            if (entity is null
                || entity.EntityId == 0
                || !activeEntityIds.Add(entity.EntityId))
            {
                continue;
            }

            var view = GetEntityView(entity);
            var logicalPosition = OutsideTestFloorRecord.ClampMarkerPosition(
                entity.LogicalPosition,
                indoorGrid.Size);
            var worldPosition = grid.LogicalToWorld(logicalPosition);
            var position = new Vector3(
                worldPosition.x,
                worldPosition.y,
                transform.position.z - 0.2f);
            view.Object.transform.position = position;
            view.Label.transform.position = position + Vector3.up * 0.45f;
            view.Renderer.color = GetEntityColor(entity.DefinitionId);
            view.Label.text = entity.IsProcessor
                ? $"{entity.DefinitionId}\n"
                    + $"Input {entity.InputCount}/{FactoryEntityRecord.InputCapacity} "
                    + $"{entity.AcceptedItemId}\n"
                    + $"Output {entity.OutputCount}/{FactoryEntityRecord.OutputCapacity} "
                    + $"{entity.ProducedItemId}\n"
                    + $"Lifetime {entity.ProducedCount} @ {entity.CycleProgress:0.00}\n"
                    + (entity.OutputCount >= FactoryEntityRecord.OutputCapacity
                        ? "Output full"
                        : entity.InputCount < entity.InputQuantity
                            ? "Waiting for input"
                            : "Processing")
                : entity.IsStorage
                    ? $"{entity.DefinitionId}\n"
                        + $"Stored {entity.OutputCount}/{FactoryEntityRecord.OutputCapacity} "
                        + $"{entity.AcceptedItemId}\n"
                        + "Lifetime 0\nStorage"
                    : $"{entity.DefinitionId}\n"
                        + $"{entity.OutputCount}/{FactoryEntityRecord.OutputCapacity} "
                        + $"{entity.ProducedItemId}\n"
                        + $"Lifetime {entity.ProducedCount} @ {entity.CycleProgress:0.00}\n"
                        + (entity.OutputCount >= FactoryEntityRecord.OutputCapacity
                            ? "Output full"
                            : "Producing");
        }

        staleEntityIds.Clear();
        foreach (var pair in entityViews)
        {
            if (!activeEntityIds.Contains(pair.Key))
            {
                staleEntityIds.Add(pair.Key);
            }
        }

        foreach (var entityId in staleEntityIds)
        {
            var view = entityViews[entityId];
            if (view.Object is not null && view.Object)
            {
                Destroy(view.Object);
            }

            if (view.LabelObject is not null && view.LabelObject)
            {
                Destroy(view.LabelObject);
            }

            entityViews.Remove(entityId);
        }
    }

    private EntityView GetEntityView(FactoryEntityRecord entity)
    {
        if (entityViews.TryGetValue(entity.EntityId, out var existingView)
            && existingView.Object is not null
            && existingView.Object)
        {
            return existingView;
        }

        var entityObject = new GameObject($"Factory Entity {entity.EntityId}");
        entityObject.transform.SetParent(transform, false);
        var renderer = entityObject.AddComponent<SpriteRenderer>();
        renderer.sprite = markerSprite;
        renderer.sortingOrder = 11;
        entityObject.transform.localScale = Vector3.one * 0.5f;

        var labelObject = new GameObject("Factory Entity Label");
        labelObject.transform.SetParent(entityObject.transform, false);
        var label = labelObject.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 24;
        label.characterSize = 0.04f;
        label.color = new Color(1f, 0.92f, 0.62f, 1f);

        var view = new EntityView(entityObject, labelObject, renderer, label);
        entityViews[entity.EntityId] = view;
        return view;
    }

    private static Color GetEntityColor(string definitionId)
    {
        return definitionId switch
        {
            "test-machine-ground" => new Color(0.95f, 0.58f, 0.2f, 1f),
            "test-machine-upper" => new Color(0.95f, 0.35f, 0.72f, 1f),
            FactoryEntityRecord.StorageDefinitionId => new Color(0.25f, 0.75f, 0.95f, 1f),
            _ => new Color(0.95f, 0.78f, 0.25f, 1f)
        };
    }

    private sealed class EntityView
    {
        public EntityView(
            GameObject newObject,
            GameObject newLabelObject,
            SpriteRenderer newRenderer,
            TextMesh newLabel)
        {
            Object = newObject;
            LabelObject = newLabelObject;
            Renderer = newRenderer;
            Label = newLabel;
        }

        public GameObject Object { get; }
        public GameObject LabelObject { get; }
        public SpriteRenderer Renderer { get; }
        public TextMesh Label { get; }
    }
}
