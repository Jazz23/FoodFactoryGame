// Provides the Scene View two-corner workflow for generating wall-and-roof test buildings.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[CustomEditor(typeof(TestBuildingCreator))]
public sealed class TestBuildingCreatorEditor : Editor
{
    private static readonly Color PreviewFillColor = new(0.35f, 1f, 0.45f, 0.18f);
    private static readonly Color PreviewLineColor = new(0.35f, 1f, 0.45f, 1f);

    private readonly List<TestBuildingCreator.WallPlacement> wallPlacements = new();
    private Vector3Int firstCorner;
    private Vector3Int hoveredCell;
    private bool hasFirstCorner;
    private bool hasHoveredCell;
    private string statusMessage = string.Empty;

    private TestBuildingCreator Creator => (TestBuildingCreator)target;

    private void OnEnable()
    {
        MigrateLegacySettings();
        RefreshGeneratedBuildingWalls();
        RefreshGeneratedRoofs();
        SceneView.RepaintAll();
    }

    private void OnDisable()
    {
        hasFirstCorner = false;
        hasHoveredCell = false;
        SceneView.RepaintAll();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();
        RefreshGeneratedBuildingWalls();
        RefreshGeneratedRoofs();

        EditorGUILayout.HelpBox(
            "Select the creator, then click two opposite ground cells in Scene View. "
            + "Each completed selection creates walls and a roof without a door or collider.",
            MessageType.Info);

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

        if (GUILayout.Button("Reset Corner Selection"))
        {
            ResetPlacement();
            SceneView.RepaintAll();
        }

        if (GUILayout.Button("Clear Generated Buildings"))
        {
            ClearGeneratedBuildings();
        }
    }

    private void OnSceneGUI()
    {
        if (!IsReady())
        {
            return;
        }

        var sceneView = SceneView.currentDrawingSceneView;
        var currentEvent = Event.current;
        if (currentEvent.type == EventType.Layout)
        {
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        }

        if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
        {
            ResetPlacement();
            currentEvent.Use();
            sceneView?.Repaint();
            Repaint();
            return;
        }

        if (TryGetCell(currentEvent.mousePosition, out var cell))
        {
            if (!hasHoveredCell || hoveredCell != cell)
            {
                hoveredCell = cell;
                hasHoveredCell = true;
                sceneView?.Repaint();
                Repaint();
            }

            if (currentEvent.type == EventType.MouseDown
                && currentEvent.button == 0
                && !currentEvent.alt)
            {
                HandlePlacementClick(cell);
                currentEvent.Use();
                sceneView?.Repaint();
                Repaint();
                return;
            }
        }

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 1)
        {
            ResetPlacement();
            currentEvent.Use();
            sceneView?.Repaint();
            Repaint();
            return;
        }

        if (currentEvent.type == EventType.Repaint && hasHoveredCell)
        {
            DrawPreview();
        }
    }

    private bool IsReady()
    {
        return Creator.gameObject.scene == SceneManager.GetActiveScene()
            && Creator.Grid is not null
            && Creator.Grid
            && Creator.Material is not null
            && Creator.Material
            && Creator.GeneratedBuildings is not null
            && Creator.GeneratedBuildings;
    }

    private bool TryGetCell(Vector2 guiPosition, out Vector3Int cell)
    {
        var ray = HandleUtility.GUIPointToWorldRay(guiPosition);
        var plane = new Plane(Vector3.forward, Creator.Grid.transform.position.z);
        if (!plane.Raycast(ray, out var distance))
        {
            cell = default;
            return false;
        }

        var logicalPosition = Creator.Grid.WorldToLogical(ray.GetPoint(distance));
        cell = new Vector3Int(
            Mathf.FloorToInt(logicalPosition.x),
            Mathf.FloorToInt(logicalPosition.y),
            0);
        return true;
    }

    private void HandlePlacementClick(Vector3Int cell)
    {
        statusMessage = string.Empty;
        if (!hasFirstCorner)
        {
            firstCorner = cell;
            hasFirstCorner = true;
            return;
        }

        CreateBuilding(firstCorner, cell);
        ResetPlacement();
    }

    private void DrawPreview()
    {
        var anchor = hasFirstCorner
            ? TestBuildingCreator.GetAnchorCell(firstCorner, hoveredCell)
            : hoveredCell;
        var size = hasFirstCorner
            ? TestBuildingCreator.GetSize(firstCorner, hoveredCell)
            : Vector2Int.one;
        var boundary = GetBoundaryWorldPoints(anchor, size);

        Handles.color = PreviewFillColor;
        Handles.DrawAAConvexPolygon(boundary[0], boundary[1], boundary[2], boundary[3]);
        Handles.color = PreviewLineColor;
        Handles.DrawAAPolyLine(4f, boundary);

        if (hasFirstCorner)
        {
            var firstPoint = ToWorld(new Vector2(firstCorner.x, firstCorner.y));
            Handles.SphereHandleCap(
                0,
                firstPoint,
                Quaternion.identity,
                HandleUtility.GetHandleSize(firstPoint) * 0.08f,
                EventType.Repaint);
        }

        var labelPosition = (boundary[0] + boundary[1] + boundary[2] + boundary[3]) * 0.25f;
        Handles.Label(labelPosition, $"{size.x} x {size.y}");
    }

    private Vector3[] GetBoundaryWorldPoints(Vector3Int anchor, Vector2Int size)
    {
        return new[]
        {
            ToWorld(new Vector2(anchor.x, anchor.y)),
            ToWorld(new Vector2(anchor.x + size.x, anchor.y)),
            ToWorld(new Vector2(anchor.x + size.x, anchor.y + size.y)),
            ToWorld(new Vector2(anchor.x, anchor.y + size.y))
        };
    }

    private Vector3 ToWorld(Vector2 logicalPosition)
    {
        var worldPosition = Creator.Grid.LogicalToWorld(logicalPosition);
        return new Vector3(worldPosition.x, worldPosition.y, Creator.Grid.transform.position.z - 0.02f);
    }

    private void CreateBuilding(Vector3Int first, Vector3Int second)
    {
        var anchor = TestBuildingCreator.GetAnchorCell(first, second);
        var size = TestBuildingCreator.GetSize(first, second);
        if (!BuildingFootprint.IsValid(size))
        {
            statusMessage = "The selected area is not a valid rectangle.";
            return;
        }

        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create test building");
        var buildingObject = new GameObject(
            $"Test Building ({anchor.x}, {anchor.y}) {size.x}x{size.y}");
        buildingObject.transform.SetParent(Creator.GeneratedBuildings, false);
        Undo.RegisterCreatedObjectUndo(buildingObject, "Create test building");

        var layout = Undo.AddComponent<TestBuildingLayout>(buildingObject);
        layout.Configure(anchor, size);
        EditorUtility.SetDirty(layout);

        TestBuildingCreator.GetWallPlacements(first, second, wallPlacements);
        foreach (var placement in wallPlacements)
        {
            CreateWall(buildingObject.transform, placement);
        }

        CreateRoof(buildingObject.transform, first, second);
        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = Creator.gameObject;
        statusMessage = $"Created {size.x} x {size.y} test building.";
    }

    private void CreateWall(
        Transform buildingTransform,
        TestBuildingCreator.WallPlacement placement)
    {
        var wallObject = new GameObject(
            $"Grid Wall {placement.Kind} ({placement.Cell.x},{placement.Cell.y})");
        wallObject.transform.SetParent(buildingTransform, false);
        Undo.RegisterCreatedObjectUndo(wallObject, "Create test building wall");
        var wall = Undo.AddComponent<GridWall>(wallObject);
        ConfigureWall(wall, placement);
    }

    private void CreateRoof(
        Transform buildingTransform,
        Vector3Int first,
        Vector3Int second)
    {
        var roofObject = new GameObject("Grid Roof");
        roofObject.transform.SetParent(buildingTransform, false);
        Undo.RegisterCreatedObjectUndo(roofObject, "Create test building roof");
        var roof = Undo.AddComponent<GridRoof>(roofObject);
        var serializedRoof = new SerializedObject(roof);
        serializedRoof.FindProperty("logicalMin").vector2Value =
            TestBuildingCreator.GetRoofLogicalMin(first, second);
        serializedRoof.FindProperty("logicalMax").vector2Value =
            TestBuildingCreator.GetRoofLogicalMax(first, second);
        serializedRoof.FindProperty("topHeight").floatValue = TestBuildingCreator.GetRoofTopHeight(
            Creator.WallHeight,
            Creator.RoofTopHeight,
            Creator.RoofThickness);
        serializedRoof.FindProperty("thickness").floatValue = Creator.RoofThickness;
        serializedRoof.FindProperty("topColor").colorValue = Creator.RoofTopColor;
        serializedRoof.FindProperty("sideColor").colorValue = Creator.RoofSideColor;
        serializedRoof.FindProperty("material").objectReferenceValue = Creator.Material;
        serializedRoof.FindProperty("sortingOrder").intValue =
            TestBuildingCreator.GetRoofSortingOrder(
                first,
                second,
                Creator.RoofSortingOrder);
        serializedRoof.ApplyModifiedPropertiesWithoutUndo();
        roof.enabled = false;
        roof.enabled = true;
        EditorUtility.SetDirty(roof);
    }

    private void ConfigureWall(
        GridWall wall,
        TestBuildingCreator.WallPlacement placement)
    {
        wall.gameObject.name = $"Grid Wall {placement.Kind} ({placement.Cell.x},{placement.Cell.y})";
        var serializedWall = new SerializedObject(wall);
        serializedWall.FindProperty("kind").enumValueIndex = (int)placement.Kind;
        serializedWall.FindProperty("cell").vector2IntValue = placement.Cell;
        serializedWall.FindProperty("wallHeight").floatValue = Creator.WallHeight;
        serializedWall.FindProperty("material").objectReferenceValue = Creator.Material;
        serializedWall.ApplyModifiedPropertiesWithoutUndo();
        wall.enabled = false;
        wall.enabled = true;
        EditorUtility.SetDirty(wall);
    }

    private void RefreshGeneratedBuildingWalls()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            var secondCorner = layout.AnchorCell + new Vector3Int(layout.Size.x - 1, layout.Size.y - 1);
            TestBuildingCreator.GetWallPlacements(layout.AnchorCell, secondCorner, wallPlacements);
            var walls = layout.GetComponentsInChildren<GridWall>(true);
            var needsRefresh = walls.Length != wallPlacements.Count;
            var sharedCount = Mathf.Min(walls.Length, wallPlacements.Count);
            for (var index = 0; index < sharedCount && !needsRefresh; index++)
            {
                needsRefresh = walls[index].Kind != wallPlacements[index].Kind
                    || walls[index].Cell != wallPlacements[index].Cell;
            }

            if (!needsRefresh)
            {
                continue;
            }

            for (var index = walls.Length - 1; index >= wallPlacements.Count; index--)
            {
                Undo.DestroyObjectImmediate(walls[index].gameObject);
            }

            sharedCount = Mathf.Min(walls.Length, wallPlacements.Count);
            for (var index = 0; index < sharedCount; index++)
            {
                ConfigureWall(walls[index], wallPlacements[index]);
            }

            for (var index = sharedCount; index < wallPlacements.Count; index++)
            {
                CreateWall(layout.transform, wallPlacements[index]);
            }

            EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        }
    }

    private void ClearGeneratedBuildings()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clear test buildings");
        for (var index = Creator.GeneratedBuildings.childCount - 1; index >= 0; index--)
        {
            Undo.DestroyObjectImmediate(Creator.GeneratedBuildings.GetChild(index).gameObject);
        }

        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
        statusMessage = "Cleared generated test buildings.";
        Repaint();
        SceneView.RepaintAll();
    }

    private void RefreshGeneratedRoofs()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            var roof = layout.GetComponentInChildren<GridRoof>(true);
            if (roof is null || !roof)
            {
                continue;
            }

            var desiredSortingOrder = TestBuildingCreator.GetRoofSortingOrder(
                layout.AnchorCell,
                layout.AnchorCell + new Vector3Int(layout.Size.x - 1, layout.Size.y - 1),
                Creator.RoofSortingOrder);
            var desiredLogicalMin = TestBuildingCreator.GetRoofLogicalMin(
                layout.AnchorCell,
                layout.AnchorCell + new Vector3Int(layout.Size.x - 1, layout.Size.y - 1));
            var desiredLogicalMax = TestBuildingCreator.GetRoofLogicalMax(
                layout.AnchorCell,
                layout.AnchorCell + new Vector3Int(layout.Size.x - 1, layout.Size.y - 1));
            var desiredTopHeight = TestBuildingCreator.GetRoofTopHeight(
                Creator.WallHeight,
                Creator.RoofTopHeight,
                Creator.RoofThickness);
            if (roof.SortingOrder == desiredSortingOrder
                && roof.LogicalMin == desiredLogicalMin
                && roof.LogicalMax == desiredLogicalMax
                && Mathf.Approximately(roof.TopHeight, desiredTopHeight))
            {
                continue;
            }

            var serializedRoof = new SerializedObject(roof);
            serializedRoof.FindProperty("logicalMin").vector2Value = desiredLogicalMin;
            serializedRoof.FindProperty("logicalMax").vector2Value = desiredLogicalMax;
            serializedRoof.FindProperty("sortingOrder").intValue = desiredSortingOrder;
            serializedRoof.FindProperty("topHeight").floatValue = desiredTopHeight;
            serializedRoof.ApplyModifiedPropertiesWithoutUndo();
            roof.enabled = false;
            roof.enabled = true;
            EditorUtility.SetDirty(roof);
            EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        }
    }

    private void MigrateLegacySettings()
    {
        var serializedCreator = new SerializedObject(Creator);
        var version = serializedCreator.FindProperty("settingsVersion");
        if (version.intValue >= TestBuildingCreator.CurrentSettingsVersion)
        {
            return;
        }

        var roofSortingOrder = serializedCreator.FindProperty("roofSortingOrder");
        if (roofSortingOrder.intValue == 40 || roofSortingOrder.intValue == 20)
        {
            roofSortingOrder.intValue = TestBuildingCreator.DefaultRoofSortingOrder;
        }

        version.intValue = TestBuildingCreator.CurrentSettingsVersion;
        serializedCreator.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(Creator);
        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
    }

    private void ResetPlacement()
    {
        hasFirstCorner = false;
        statusMessage = string.Empty;
    }
}
