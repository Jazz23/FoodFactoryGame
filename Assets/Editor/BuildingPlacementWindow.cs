// Provides an editor-only two-corner SceneView workflow for placing modular buildings.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

public sealed class BuildingPlacementWindow : EditorWindow
{
    private static readonly Color ValidPreviewColor = new(0.35f, 1f, 0.45f, 0.7f);
    private static readonly Color InvalidPreviewColor = new(1f, 0.25f, 0.25f, 0.7f);

    private readonly List<Vector3Int> footprintCells = new();
    private readonly List<GridEdge> reservationEdges = new();
    private readonly List<Vector3Int> existingCells = new();
    private readonly List<GridEdge> existingEdges = new();
    private readonly BuildingOccupancy occupancy = new();

    private Tilemap ground = null!;
    private BuildingCatalog catalog = null!;
    private Builder builder = null!;
    private BuildingDefinition selectedDefinition = null!;
    private GameObject previewObject = null!;
    private BuildingVisualView previewView = null!;
    private BuildingDefinition previewDefinition = null!;
    private Vector3Int firstCorner;
    private Vector3Int hoveredCell;
    private Vector3Int previewAnchor;
    private Vector2Int previewSize;
    private int selectedDefinitionIndex;
    private GridEdgeDirection selectedDirection;
    private bool hasFirstCorner;
    private bool hasHoveredCell;
    private bool previewConfigured;
    private string statusMessage = string.Empty;

    [MenuItem("Food Factory/Building Placement")]
    private static void Open()
    {
        GetWindow<BuildingPlacementWindow>("Building Placement");
    }

    private void OnEnable()
    {
        Selection.selectionChanged += Repaint;
        SceneView.duringSceneGui += DuringSceneGUI;
        FindDefaults();
        SceneView.RepaintAll();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= Repaint;
        SceneView.duringSceneGui -= DuringSceneGUI;
        ClearPreview();
        SceneView.RepaintAll();
    }

    private void OnGUI()
    {
        if (!IsReady())
        {
            FindDefaults();
        }

        EditorGUILayout.LabelField("World Building Placement", EditorStyles.boldLabel);
        ground = (Tilemap)EditorGUILayout.ObjectField(
            "Ground",
            ground,
            typeof(Tilemap),
            true);
        catalog = (BuildingCatalog)EditorGUILayout.ObjectField(
            "Catalog",
            catalog,
            typeof(BuildingCatalog),
            false);

        if (catalog is not null && catalog && catalog.Count > 0)
        {
            var definitionNames = new string[catalog.Count];
            for (var index = 0; index < catalog.Count; index++)
            {
                var definition = catalog.GetDefinition(index);
                definitionNames[index] = $"{definition.name} ({definition.Id})";
            }

            selectedDefinitionIndex = Mathf.Clamp(
                selectedDefinitionIndex,
                0,
                catalog.Count - 1);
            var newDefinitionIndex = EditorGUILayout.Popup(
                "Building",
                selectedDefinitionIndex,
                definitionNames);
            if (newDefinitionIndex != selectedDefinitionIndex)
            {
                selectedDefinitionIndex = newDefinitionIndex;
                selectedDefinition = catalog.GetDefinition(selectedDefinitionIndex);
                previewConfigured = false;
                ClearPreview();
                SceneView.RepaintAll();
            }

            selectedDefinition = catalog.GetDefinition(selectedDefinitionIndex);
        }

        if (!IsReady())
        {
            EditorGUILayout.HelpBox(
                "Open World and assign a ground Tilemap plus a Building Catalog.",
                MessageType.Warning);
        }
        else
        {
            var instruction = selectedDefinition.PlacementKind == BuildingPlacementKind.WallSegment
                ? "Press R to rotate, then click to place one wall edge."
                : hasFirstCorner
                    ? "Click the opposing corner to place the building."
                    : "Click the first corner, then the opposing corner.";
            EditorGUILayout.HelpBox(instruction, MessageType.Info);
            EditorGUILayout.LabelField(
                "Selected",
                $"{selectedDefinition.name} ({selectedDefinition.Id})");

            if (selectedDefinition.PlacementKind == BuildingPlacementKind.WallSegment)
            {
                EditorGUILayout.LabelField("Direction", selectedDirection.ToString());
            }

            if (hasFirstCorner)
            {
                EditorGUILayout.LabelField(
                    "First corner",
                    $"({firstCorner.x}, {firstCorner.y})");
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(statusMessage, MessageType.Warning);
            }

            if (GUILayout.Button("Reset Corner"))
            {
                ResetPlacement();
                SceneView.RepaintAll();
            }
        }
    }

    private void FindDefaults()
    {
        var sceneBuilder = FindFirstObjectByType<Builder>();
        if (sceneBuilder is not null && sceneBuilder.gameObject.scene == SceneManager.GetActiveScene())
        {
            builder = sceneBuilder;
            ground = builder.Ground;
            catalog = builder.Catalog;
        }

        if (catalog is not null && catalog && catalog.Count > 0)
        {
            selectedDefinitionIndex = Mathf.Clamp(
                selectedDefinitionIndex,
                0,
                catalog.Count - 1);
            selectedDefinition = catalog.GetDefinition(selectedDefinitionIndex);
        }
    }

    private void DuringSceneGUI(SceneView sceneView)
    {
        if (!IsReady())
        {
            FindDefaults();
        }

        if (!IsReady() || ground.gameObject.scene != SceneManager.GetActiveScene())
        {
            return;
        }

        var currentEvent = Event.current;
        if (currentEvent.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        }

        if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
        {
            ResetPlacement();
            currentEvent.Use();
            sceneView.Repaint();
            return;
        }

        if (currentEvent.type == EventType.KeyDown
            && currentEvent.keyCode == KeyCode.R
            && selectedDefinition.PlacementKind == BuildingPlacementKind.WallSegment)
        {
            selectedDirection = GridEdge.RotateClockwise(selectedDirection);
            previewConfigured = false;
            currentEvent.Use();
            sceneView.Repaint();
            Repaint();
            return;
        }

        if (TryGetCell(currentEvent.mousePosition, out var cell))
        {
            if (!hasHoveredCell || hoveredCell != cell)
            {
                hoveredCell = cell;
                hasHoveredCell = true;
                previewConfigured = false;
                sceneView.Repaint();
            }

            if (currentEvent.type == EventType.MouseDown
                && currentEvent.button == 0
                && !currentEvent.alt)
            {
                HandlePlacementClick(cell);
                currentEvent.Use();
                sceneView.Repaint();
                return;
            }
        }

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 1)
        {
            ResetPlacement();
            currentEvent.Use();
            sceneView.Repaint();
            return;
        }

        if (currentEvent.type == EventType.Repaint && hasHoveredCell)
        {
            DrawPreview();
        }
    }

    private void HandlePlacementClick(Vector3Int cell)
    {
        statusMessage = string.Empty;
        if (selectedDefinition.PlacementKind == BuildingPlacementKind.WallSegment)
        {
            if (!TryValidatePlacement(cell, Vector2Int.one, selectedDirection, out var wallReason))
            {
                statusMessage = wallReason;
                return;
            }

            CreatePlacement(cell, Vector2Int.one, selectedDirection);
            return;
        }

        if (!hasFirstCorner)
        {
            firstCorner = cell;
            hasFirstCorner = true;
            previewConfigured = false;
            return;
        }

        var anchorCell = BuildingFootprint.GetLowerLeftAnchorCell(firstCorner, cell);
        var size = BuildingFootprint.GetInclusiveSize(firstCorner, cell);
        if (!TryValidatePlacement(anchorCell, size, GridEdgeDirection.South, out var reason))
        {
            statusMessage = reason;
            return;
        }

        CreatePlacement(anchorCell, size, GridEdgeDirection.South);
        ResetPlacement();
    }

    private void DrawPreview()
    {
        var isWallSegment = selectedDefinition.PlacementKind == BuildingPlacementKind.WallSegment;
        var anchorCell = !isWallSegment && hasFirstCorner
            ? BuildingFootprint.GetLowerLeftAnchorCell(firstCorner, hoveredCell)
            : hoveredCell;
        var size = !isWallSegment && hasFirstCorner
            ? BuildingFootprint.GetInclusiveSize(firstCorner, hoveredCell)
            : Vector2Int.one;
        var direction = isWallSegment ? selectedDirection : GridEdgeDirection.South;
        var valid = TryValidatePlacement(anchorCell, size, direction, out var reason);
        statusMessage = valid ? string.Empty : reason;

        var color = valid ? ValidPreviewColor : InvalidPreviewColor;
        var boundaryPoints = GetBoundaryWorldPoints(anchorCell, size);
        if (isWallSegment)
        {
            var edge = GridEdge.FromCellSide(anchorCell, direction);
            Handles.color = color;
            Handles.DrawAAPolyLine(
                6f,
                ground.CellToWorld(edge.Corner),
                ground.CellToWorld(edge.EndCorner));
            Handles.Label(
                (ground.CellToWorld(edge.Corner) + ground.CellToWorld(edge.EndCorner)) * 0.5f,
                $"{direction} {edge}");
        }
        else
        {
            Handles.color = new Color(color.r, color.g, color.b, 0.2f);
            Handles.DrawAAConvexPolygon(
                boundaryPoints[0],
                boundaryPoints[1],
                boundaryPoints[2],
                boundaryPoints[3]);
            Handles.color = color;
            Handles.DrawAAPolyLine(4f, boundaryPoints);
        }

        if (!isWallSegment && hasFirstCorner)
        {
            var firstPoint = ground.CellToWorld(firstCorner);
            firstPoint.z -= 0.02f;
            Handles.SphereHandleCap(
                0,
                firstPoint,
                Quaternion.identity,
                HandleUtility.GetHandleSize(firstPoint) * 0.08f,
                EventType.Repaint);
        }

        if (!isWallSegment)
        {
            var labelPosition = (boundaryPoints[0] + boundaryPoints[1] + boundaryPoints[2] + boundaryPoints[3]) * 0.25f;
            Handles.Label(labelPosition, $"{size.x} x {size.y}");
        }

        UpdatePreview(anchorCell, size, direction, valid);
    }

    private void UpdatePreview(
        Vector3Int anchorCell,
        Vector2Int size,
        GridEdgeDirection direction,
        bool valid)
    {
        EnsurePreview();
        if (previewObject is null || !previewObject || previewView is null || !previewView)
        {
            return;
        }

        if (!previewConfigured
            || previewAnchor != anchorCell
            || previewSize != size
            || selectedDirection != direction)
        {
            var instance = new BuildingInstance(
                uint.MaxValue,
                selectedDefinition.Id,
                anchorCell,
                size,
                -1,
                direction);
            previewView.Configure(
                instance,
                selectedDefinition,
                ground,
                BuildingVisualMode.Preview);
            previewAnchor = anchorCell;
            previewSize = size;
            previewConfigured = true;
        }

        var color = valid ? ValidPreviewColor : InvalidPreviewColor;
        previewView.SetPresentation(color, 1);
        previewObject.SetActive(true);
    }

    private void EnsurePreview()
    {
        if (selectedDefinition is null
            || !selectedDefinition
            || selectedDefinition.PreviewPrefab is null
            || !selectedDefinition.PreviewPrefab)
        {
            return;
        }

        if (previewObject is not null && previewObject && previewDefinition == selectedDefinition)
        {
            return;
        }

        ClearPreview();
        previewObject = (GameObject)PrefabUtility.InstantiatePrefab(selectedDefinition.PreviewPrefab);
        previewObject.hideFlags = HideFlags.HideAndDontSave;
        previewView = previewObject.GetComponent<BuildingVisualView>();

        previewDefinition = selectedDefinition;
        previewConfigured = false;
    }

    private void ClearPreview()
    {
        if (previewObject is not null && previewObject)
        {
            DestroyImmediate(previewObject);
        }

        previewObject = null!;
        previewView = null!;
        previewDefinition = null!;
        previewConfigured = false;
    }

    private void CreatePlacement(
        Vector3Int anchorCell,
        Vector2Int size,
        GridEdgeDirection direction)
    {
        var instanceId = GetNextInstanceId();
        if (instanceId == 0)
        {
            statusMessage = "No instance IDs are available.";
            return;
        }

        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Place building");
        var placedObject = (GameObject)PrefabUtility.InstantiatePrefab(
            selectedDefinition.Prefab,
            ground.gameObject.scene);
        if (placedObject is null || !placedObject)
        {
            statusMessage = "The selected building prefab could not be instantiated.";
            return;
        }

        Undo.RegisterCreatedObjectUndo(placedObject, "Place building");
        placedObject.name = $"{selectedDefinition.name} ({anchorCell.x}, {anchorCell.y})";
        var preplacedBuilding = placedObject.GetComponent<PreplacedBuilding>();
        if (preplacedBuilding is null || !preplacedBuilding)
        {
            preplacedBuilding = Undo.AddComponent<PreplacedBuilding>(placedObject);
        }

        Undo.RecordObject(preplacedBuilding, "Configure building placement");
        preplacedBuilding.SetPlacementData(
            selectedDefinition,
            anchorCell,
            size,
            instanceId,
            direction);

        var buildingView = placedObject.GetComponent<BuildingView>();
        Undo.RecordObject(placedObject.transform, "Position building");
        buildingView.Configure(
            new BuildingInstance(
                instanceId,
                selectedDefinition.Id,
                anchorCell,
                size,
                -1,
                direction),
            selectedDefinition,
            ground);

        EditorUtility.SetDirty(preplacedBuilding);
        EditorUtility.SetDirty(buildingView);
        PrefabUtility.RecordPrefabInstancePropertyModifications(preplacedBuilding);
        PrefabUtility.RecordPrefabInstancePropertyModifications(placedObject.transform);
        EditorSceneManager.MarkSceneDirty(ground.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = placedObject;
        statusMessage = selectedDefinition.PlacementKind == BuildingPlacementKind.WallSegment
            ? $"Placed {selectedDefinition.name} {direction} at {anchorCell.x},{anchorCell.y}."
            : $"Placed {selectedDefinition.name} at {anchorCell.x},{anchorCell.y} ({size.x} x {size.y}).";
    }

    private bool TryValidatePlacement(
        Vector3Int anchorCell,
        Vector2Int size,
        GridEdgeDirection direction,
        out string reason)
    {
        reason = string.Empty;
        if (!BuildingFootprint.IsValid(size))
        {
            reason = "The selected area is not a valid rectangle.";
            return false;
        }

        if (selectedDefinition is null
            || !selectedDefinition
            || selectedDefinition.Prefab is null
            || !selectedDefinition.Prefab)
        {
            reason = "Select a building with a prefab.";
            return false;
        }

        var candidate = new BuildingInstance(
            uint.MaxValue,
            selectedDefinition.Id,
            anchorCell,
            size,
            -1,
            direction);
        BuildingPlacementRules.GetReservation(
            candidate,
            selectedDefinition,
            footprintCells,
            reservationEdges);
        var buildableTile = builder is not null && builder ? builder.BuildableTile : null;
        if (buildableTile is not null && buildableTile)
        {
            if (!BuildingPlacementRules.IsBuildable(
                    candidate,
                    selectedDefinition,
                    ground,
                    buildableTile,
                    footprintCells))
            {
                reason = "The selected placement is not next to buildable ground.";
                return false;
            }
        }
        else if (!HasAnyGround(candidate, selectedDefinition))
        {
            reason = "The selected placement is not on the ground tilemap.";
            return false;
        }

        occupancy.Clear();
        var preplacedBuildings = FindObjectsByType<PreplacedBuilding>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var preplacedBuilding in preplacedBuildings)
        {
            if (preplacedBuilding.gameObject.scene != ground.gameObject.scene)
            {
                continue;
            }

            var existing = new BuildingInstance(
                preplacedBuilding.InstanceId,
                preplacedBuilding.Definition.Id,
                preplacedBuilding.AnchorCell,
                preplacedBuilding.Size,
                -1,
                preplacedBuilding.Direction);
            BuildingPlacementRules.GetReservation(
                existing,
                preplacedBuilding.Definition,
                existingCells,
                existingEdges);
            if (!occupancy.TryReserve(
                    preplacedBuilding.InstanceId,
                    existingCells,
                    existingEdges))
            {
                reason = $"Existing placement '{preplacedBuilding.name}' has conflicting occupancy.";
                return false;
            }
        }

        BuildingPlacementRules.GetReservation(
            candidate,
            selectedDefinition,
            footprintCells,
            reservationEdges);
        if (!occupancy.CanReserve(uint.MaxValue, footprintCells, reservationEdges))
        {
            reason = "The selected cells or wall edge overlap an existing building.";
            return false;
        }

        return true;
    }

    private bool HasAnyGround(
        BuildingInstance instance,
        BuildingDefinition definition)
    {
        if (definition.PlacementKind == BuildingPlacementKind.WallSegment)
        {
            var edge = BuildingPlacementRules.GetWallEdge(instance);
            return ground.GetTile(edge.FirstAdjacentCell) is not null
                || ground.GetTile(edge.SecondAdjacentCell) is not null;
        }

        BuildingFootprint.GetCells(instance.AnchorCell, instance.Size, footprintCells);
        foreach (var cell in footprintCells)
        {
            if (ground.GetTile(cell) is null)
            {
                return false;
            }
        }

        return footprintCells.Count > 0;
    }

    private uint GetNextInstanceId()
    {
        var nextId = 1u;
        var preplacedBuildings = FindObjectsByType<PreplacedBuilding>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (var preplacedBuilding in preplacedBuildings)
        {
            if (preplacedBuilding.gameObject.scene != ground.gameObject.scene
                || preplacedBuilding.InstanceId < nextId
                || preplacedBuilding.InstanceId == uint.MaxValue)
            {
                continue;
            }

            nextId = preplacedBuilding.InstanceId + 1;
        }

        return nextId;
    }

    private Vector3[] GetBoundaryWorldPoints(Vector3Int anchorCell, Vector2Int size)
    {
        var points = new Vector3[5];
        points[0] = ground.CellToWorld(anchorCell);
        points[1] = ground.CellToWorld(anchorCell + new Vector3Int(size.x, 0));
        points[2] = ground.CellToWorld(anchorCell + new Vector3Int(size.x, size.y));
        points[3] = ground.CellToWorld(anchorCell + new Vector3Int(0, size.y));
        points[4] = points[0];
        return points;
    }

    private bool TryGetCell(Vector2 guiPosition, out Vector3Int cell)
    {
        var ray = HandleUtility.GUIPointToWorldRay(guiPosition);
        var plane = new Plane(Vector3.forward, ground.transform.position.z);
        if (!plane.Raycast(ray, out var distance))
        {
            cell = default;
            return false;
        }

        cell = ground.WorldToCell(ray.GetPoint(distance));
        return true;
    }

    private bool IsReady()
    {
        return ground is not null
            && ground
            && catalog is not null
            && catalog
            && catalog.Count > 0
            && ground.gameObject.scene.IsValid();
    }

    private void ResetPlacement()
    {
        hasFirstCorner = false;
        statusMessage = string.Empty;
        previewConfigured = false;
    }

}
