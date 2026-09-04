// Provides the Scene View two-corner workflow for generating multi-story wall-and-slab test buildings.
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
    private readonly List<TestBuildingCreator.ExteriorWallSpan> wallSpans = new();
    private Vector3Int firstCorner;
    private Vector3Int hoveredCell;
    private TestBuildingCreator.ExteriorWallSpan hoveredDoorWall;
    private TestBuildingLayout hoveredDoorLayout = null!;
    private float hoveredDoorOffset;
    private bool hasFirstCorner;
    private bool hasHoveredCell;
    private bool hasHoveredDoorWall;
    private bool doorPlacementMode;
    private string statusMessage = string.Empty;

    private TestBuildingCreator Creator => (TestBuildingCreator)target;

    private void OnEnable()
    {
        if (target is not TestBuildingCreator || !target)
        {
            return;
        }

        MigrateLegacySettings();
        EnsureBuildingInstanceIds();
        EnsureFloorScenes();
        RefreshGeneratedBuildingWalls();
        RefreshGeneratedRoofs();
        SceneView.RepaintAll();
    }

    private void OnDisable()
    {
        hasFirstCorner = false;
        hasHoveredCell = false;
        hasHoveredDoorWall = false;
        doorPlacementMode = false;
        SceneView.RepaintAll();
    }

    public override void OnInspectorGUI()
    {
        if (target is not TestBuildingCreator || !target)
        {
            return;
        }

        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();
        EnsureFloorScenes();
        RefreshGeneratedBuildingWalls();
        RefreshGeneratedRoofs();

        EditorGUILayout.HelpBox(
            "Select the creator, then click two opposite ground cells in Scene View. "
            + "Each completed selection creates walls, floor/ceiling slabs, collision, and no door. "
            + "Use Place Door in Scene View to add one or more interior entrances.",
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

        DrawStoryControls();

        if (GUILayout.Button(doorPlacementMode
                ? "Cancel Door Placement"
                : "Place Door in Scene View"))
        {
            doorPlacementMode = !doorPlacementMode;
            hasHoveredDoorWall = false;
            statusMessage = doorPlacementMode
                ? "Click visible straight exterior walls to place doors; right-click or Escape when finished."
                : string.Empty;
            SceneView.RepaintAll();
            Repaint();
        }

        if (doorPlacementMode)
        {
            EditorGUILayout.HelpBox(
                "Only the topmost visible wall surface can be selected. "
                + "Corner pieces and positions near corners are rejected. Click again to add more doors.",
                MessageType.Info);
        }
    }

    private void DrawStoryControls()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Building Stories", EditorStyles.boldLabel);
        var layouts = Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true);
        if (layouts.Length == 0)
        {
            EditorGUILayout.HelpBox(
                "Create a building to manage its interior floor scenes.",
                MessageType.Info);
            return;
        }

        foreach (var layout in layouts)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"Building {layout.BuildingInstanceId} ({layout.Size.x} x {layout.Size.y})",
                $"{layout.StoryCount} {((layout.StoryCount == 1) ? "story" : "stories")}");
            if (GUILayout.Button("Add Story", GUILayout.Width(80f)))
            {
                if (TestBuildingFloorSceneUtility.AddStory(layout))
                {
                    RefreshGeneratedBuildingWalls();
                    RefreshGeneratedRoofs();
                    EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
                    statusMessage = $"Added story {layout.StoryCount - 1} to building {layout.BuildingInstanceId}.";
                }

                GUIUtility.ExitGUI();
            }

            var canDelete = layout.StoryCount > 1;
            using (new EditorGUI.DisabledScope(!canDelete))
            {
                if (GUILayout.Button("Delete Top", GUILayout.Width(90f)))
                {
                    if (!EditorUtility.DisplayDialog(
                            "Delete top story?",
                            $"Delete story {layout.StoryCount - 1} and its scene?",
                            "Delete",
                            "Cancel"))
                    {
                        GUIUtility.ExitGUI();
                    }

                    if (TestBuildingFloorSceneUtility.DeleteTopStory(layout))
                    {
                        RefreshGeneratedBuildingWalls();
                        RefreshGeneratedRoofs();
                        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
                        statusMessage = $"Deleted the top story from building {layout.BuildingInstanceId}.";
                    }
                    else
                    {
                        statusMessage = $"Could not delete the top story from building {layout.BuildingInstanceId}. Check the Console for details.";
                    }

                    Repaint();
                    SceneView.RepaintAll();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
            var lastFloorSceneName = TestBuildingFloorScenes.GetSceneName(
                layout.BuildingInstanceId,
                layout.StoryCount - 1);
            EditorGUILayout.LabelField(
                "Floor scenes",
                TestBuildingFloorScenes.GetSceneName(layout.BuildingInstanceId, 0)
                + $" through {lastFloorSceneName}");
        }
    }

    private void OnSceneGUI()
    {
        if (target is not TestBuildingCreator || !target || !IsReady())
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
            if (doorPlacementMode)
            {
                doorPlacementMode = false;
                hasHoveredDoorWall = false;
                statusMessage = string.Empty;
            }
            else
            {
                ResetPlacement();
            }

            currentEvent.Use();
            sceneView?.Repaint();
            Repaint();
            return;
        }

        if (doorPlacementMode)
        {
            HandleDoorPlacementEvent(currentEvent, sceneView);
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

    private void HandleDoorPlacementEvent(Event currentEvent, SceneView sceneView)
    {
        var foundDoorWall = hasHoveredDoorWall;
        var layout = hoveredDoorLayout;
        var wall = hoveredDoorWall;
        var normalizedOffset = hoveredDoorOffset;
        if (currentEvent.type is EventType.MouseMove or EventType.MouseDown)
        {
            foundDoorWall = TryGetVisibleDoorWall(
                currentEvent.mousePosition,
                out layout,
                out wall,
                out normalizedOffset);
            if (foundDoorWall)
            {
                hoveredDoorLayout = layout;
                hoveredDoorWall = wall;
                hoveredDoorOffset = normalizedOffset;
            }

            hasHoveredDoorWall = foundDoorWall;
        }

        if (currentEvent.type == EventType.MouseDown
            && currentEvent.button == 0
            && !currentEvent.alt)
        {
            if (!foundDoorWall)
            {
                statusMessage = "Select a visible straight exterior wall surface.";
            }
            else if (!IsValidDoorOffset(wall, normalizedOffset))
            {
                statusMessage = "Doors must be placed away from wall corners.";
            }
            else
            {
                PlaceDoor(layout, wall, normalizedOffset);
            }

            currentEvent.Use();
            sceneView?.Repaint();
            Repaint();
            return;
        }

        if (currentEvent.type == EventType.MouseDown && currentEvent.button == 1)
        {
            doorPlacementMode = false;
            hasHoveredDoorWall = false;
            statusMessage = string.Empty;
            currentEvent.Use();
            sceneView?.Repaint();
            Repaint();
            return;
        }

        if (currentEvent.type == EventType.Repaint && hasHoveredDoorWall)
        {
            DrawDoorPreview(hoveredDoorWall, hoveredDoorOffset);
        }
    }

    private bool TryGetVisibleDoorWall(
        Vector2 guiPosition,
        out TestBuildingLayout layout,
        out TestBuildingCreator.ExteriorWallSpan wall,
        out float normalizedOffset)
    {
        layout = null!;
        wall = default;
        normalizedOffset = 0.5f;

        var pickedObject = HandleUtility.PickGameObject(guiPosition, false);
        if (pickedObject is null || !pickedObject)
        {
            return false;
        }

        var pickedRenderer = pickedObject.GetComponent<MeshRenderer>();
        if (pickedRenderer is null || !pickedRenderer || !pickedObject.name.Contains(" Side "))
        {
            return false;
        }

        var pickedWall = pickedObject.GetComponentInParent<GridWall>();
        if (pickedWall is null || !pickedWall)
        {
            return false;
        }

        layout = pickedWall.GetComponentInParent<TestBuildingLayout>();
        if (layout is null || !layout)
        {
            return false;
        }

        if (!TryGetLogicalPoint(guiPosition, out var logicalPoint))
        {
            return false;
        }

        layout.GetExteriorWallSpans(wallSpans);
        var closestDistance = float.PositiveInfinity;
        var found = false;
        foreach (var candidate in wallSpans)
        {
            if (candidate.IsCorner
                || candidate.Kind != pickedWall.Kind
                || candidate.Cell != pickedWall.Cell
                || !TryGetSegmentOffset(candidate, logicalPoint, out var candidateOffset, out var distance))
            {
                continue;
            }

            if (distance >= closestDistance)
            {
                continue;
            }

            closestDistance = distance;
            wall = candidate;
            normalizedOffset = candidateOffset;
            found = true;
        }

        return found;
    }

    private bool TryGetLogicalPoint(Vector2 guiPosition, out Vector2 logicalPoint)
    {
        var ray = HandleUtility.GUIPointToWorldRay(guiPosition);
        var plane = new Plane(Vector3.forward, Creator.Grid.transform.position.z);
        if (!plane.Raycast(ray, out var distance))
        {
            logicalPoint = default;
            return false;
        }

        logicalPoint = Creator.Grid.WorldToLogical(ray.GetPoint(distance));
        return true;
    }

    private static bool TryGetSegmentOffset(
        TestBuildingCreator.ExteriorWallSpan wall,
        Vector2 logicalPoint,
        out float normalizedOffset,
        out float distance)
    {
        var delta = wall.LogicalEnd - wall.LogicalStart;
        var lengthSquared = delta.sqrMagnitude;
        if (lengthSquared <= Mathf.Epsilon)
        {
            normalizedOffset = 0.5f;
            distance = float.PositiveInfinity;
            return false;
        }

        normalizedOffset = Mathf.Clamp01(Vector2.Dot(
            logicalPoint - wall.LogicalStart,
            delta) / lengthSquared);
        var closestPoint = Vector2.Lerp(
            wall.LogicalStart,
            wall.LogicalEnd,
            normalizedOffset);
        distance = Vector2.Distance(logicalPoint, closestPoint);
        return true;
    }

    private bool IsValidDoorOffset(
        TestBuildingCreator.ExteriorWallSpan wall,
        float normalizedOffset)
    {
        var segmentLength = Vector2.Distance(wall.LogicalStart, wall.LogicalEnd);
        var minimumOffset = Creator.DoorCornerExclusionDistance / segmentLength;
        return !wall.IsCorner
            && normalizedOffset >= minimumOffset
            && normalizedOffset <= 1f - minimumOffset;
    }

    private void DrawDoorPreview(
        TestBuildingCreator.ExteriorWallSpan wall,
        float normalizedOffset)
    {
        var start = ToWorld(wall.LogicalStart);
        var end = ToWorld(wall.LogicalEnd);
        var point = Vector3.Lerp(start, end, normalizedOffset);
        var isValid = IsValidDoorOffset(wall, normalizedOffset);
        Handles.color = isValid
            ? new Color(0.25f, 1f, 0.35f, 1f)
            : new Color(1f, 0.2f, 0.2f, 1f);
        Handles.DrawAAPolyLine(5f, start, end);
        Handles.SphereHandleCap(
            0,
            point,
            Quaternion.identity,
            HandleUtility.GetHandleSize(point) * 0.12f,
            EventType.Repaint);
        Handles.Label(point, isValid ? "Door" : "Door too close to corner");
    }

    private void PlaceDoor(
        TestBuildingLayout layout,
        TestBuildingCreator.ExteriorWallSpan wall,
        float normalizedOffset)
    {
        var visualDoors = layout.transform.Find(TestBuildingLayout.VisualDoorsName)!;
        Undo.RecordObject(layout, "Place test building door");
        if (!layout.AddDoor(wall, normalizedOffset))
        {
            statusMessage = "A door is already placed at that position.";
            return;
        }

        RebuildDoors(layout, visualDoors);
        EditorUtility.SetDirty(layout);
        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        statusMessage = $"Placed a door on the {wall.Direction} exterior wall.";
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
        if (!TestBuildingCreator.IsSupportedSize(size))
        {
            statusMessage = "Test buildings must be at least 2 x 2 cells.";
            return;
        }

        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create test building");
        EnsureBuildingInstanceIds();
        var buildingInstanceId = Creator.GetNextBuildingInstanceId();
        var buildingObject = new GameObject(
            $"Test Building ({anchor.x}, {anchor.y}) {size.x}x{size.y}");
        buildingObject.transform.SetParent(Creator.GeneratedBuildings, false);
        Undo.RegisterCreatedObjectUndo(buildingObject, "Create test building");

        var layout = Undo.AddComponent<TestBuildingLayout>(buildingObject);
        layout.Configure(anchor, size);
        layout.SetBuildingInstanceId(buildingInstanceId);
        EditorUtility.SetDirty(layout);

        var generatedVisuals = CreateGeneratedRoot(
            buildingObject.transform,
            TestBuildingLayout.GeneratedVisualsName);
        var generatedCollision = CreateGeneratedRoot(
            buildingObject.transform,
            TestBuildingLayout.GeneratedCollisionName);
        CreateGeneratedRoot(buildingObject.transform, TestBuildingLayout.VisualDoorsName);

        TestBuildingCreator.GetWallPlacements(first, second, wallPlacements);
        for (var storyIndex = 0; storyIndex < layout.StoryCount; storyIndex++)
        {
            foreach (var placement in wallPlacements)
            {
                CreateWall(generatedVisuals, placement, storyIndex);
            }
        }

        for (var storyIndex = 0; storyIndex < layout.StoryCount; storyIndex++)
        {
            CreateRoof(generatedVisuals, first, second, storyIndex);
        }

        RebuildCollision(layout, generatedCollision, wallPlacements);
        Undo.AddComponent<TestBuildingPresentation>(buildingObject);
        TestBuildingFloorSceneUtility.EnsureFloorScenes(layout);
        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = Creator.gameObject;
        statusMessage = $"Created {size.x} x {size.y} test building.";
    }

    private Transform CreateGeneratedRoot(Transform parent, string rootName)
    {
        var rootObject = new GameObject(rootName);
        rootObject.transform.SetParent(parent, false);
        Undo.RegisterCreatedObjectUndo(rootObject, "Create test building output");
        return rootObject.transform;
    }

    private void CreateWall(
        Transform buildingTransform,
        TestBuildingCreator.WallPlacement placement,
        int storyIndex)
    {
        var wallObject = new GameObject(
            $"Story {storyIndex} Wall {placement.Kind} ({placement.Cell.x},{placement.Cell.y})");
        wallObject.transform.SetParent(buildingTransform, false);
        Undo.RegisterCreatedObjectUndo(wallObject, "Create test building wall");
        var wall = Undo.AddComponent<GridWall>(wallObject);
        ConfigureWall(wall, placement, storyIndex);
    }

    private void CreateRoof(
        Transform buildingTransform,
        Vector3Int first,
        Vector3Int second,
        int storyIndex)
    {
        var roofObject = new GameObject($"Grid Floor Ceiling {storyIndex}");
        roofObject.transform.SetParent(buildingTransform, false);
        Undo.RegisterCreatedObjectUndo(roofObject, "Create test building roof");
        var roof = Undo.AddComponent<GridRoof>(roofObject);
        ConfigureRoof(roof, first, second, storyIndex);
    }

    private bool ConfigureRoof(
        GridRoof roof,
        Vector3Int first,
        Vector3Int second,
        int storyIndex)
    {
        var desiredLogicalMin = TestBuildingCreator.GetRoofLogicalMin(first, second);
        var desiredLogicalMax = TestBuildingCreator.GetRoofLogicalMax(first, second);
        var desiredBaseHeight = TestBuildingCreator.GetStoryBaseHeight(
            Creator.WallHeight,
            storyIndex);
        var desiredTopHeight = TestBuildingCreator.GetStoryTopHeight(
            Creator.WallHeight,
            storyIndex);
        var desiredSortingOrder = Creator.RoofSortingOrder;
        if (roof.LogicalMin == desiredLogicalMin
            && roof.LogicalMax == desiredLogicalMax
            && Mathf.Approximately(roof.BaseHeight, desiredBaseHeight)
            && Mathf.Approximately(roof.TopHeight, desiredTopHeight)
            && Mathf.Approximately(roof.Thickness, Creator.RoofThickness)
            && roof.SortingOrder == desiredSortingOrder)
        {
            return false;
        }

        var serializedRoof = new SerializedObject(roof);
        serializedRoof.FindProperty("logicalMin").vector2Value = desiredLogicalMin;
        serializedRoof.FindProperty("logicalMax").vector2Value = desiredLogicalMax;
        serializedRoof.FindProperty("baseHeight").floatValue = desiredBaseHeight;
        serializedRoof.FindProperty("topHeight").floatValue = desiredTopHeight;
        serializedRoof.FindProperty("thickness").floatValue = Creator.RoofThickness;
        serializedRoof.FindProperty("topColor").colorValue = Creator.RoofTopColor;
        serializedRoof.FindProperty("sideColor").colorValue = Creator.RoofSideColor;
        serializedRoof.FindProperty("material").objectReferenceValue = Creator.Material;
        serializedRoof.FindProperty("sortingOrder").intValue = desiredSortingOrder;
        serializedRoof.ApplyModifiedPropertiesWithoutUndo();
        roof.enabled = false;
        roof.enabled = true;
        EditorUtility.SetDirty(roof);
        return true;
    }

    private void ConfigureWall(
        GridWall wall,
        TestBuildingCreator.WallPlacement placement,
        int storyIndex)
    {
        wall.gameObject.name = $"Story {storyIndex} Wall {placement.Kind} ({placement.Cell.x},{placement.Cell.y})";
        var serializedWall = new SerializedObject(wall);
        serializedWall.FindProperty("kind").enumValueIndex = (int)placement.Kind;
        serializedWall.FindProperty("cell").vector2IntValue = placement.Cell;
        serializedWall.FindProperty("wallHeight").floatValue = Creator.WallHeight;
        serializedWall.FindProperty("baseHeight").floatValue =
            TestBuildingCreator.GetStoryBaseHeight(Creator.WallHeight, storyIndex);
        serializedWall.FindProperty("storyIndex").intValue = storyIndex;
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

        MigrateLegacyDoors();

        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            var hierarchyChanged = EnsureGeneratedHierarchy(
                layout,
                out var generatedVisuals,
                out var generatedCollision,
                out var visualDoors);
            var secondCorner = layout.AnchorCell + new Vector3Int(layout.Size.x - 1, layout.Size.y - 1);
            TestBuildingCreator.GetWallPlacements(layout.AnchorCell, secondCorner, wallPlacements);
            var walls = generatedVisuals.GetComponentsInChildren<GridWall>(true);
            var expectedWallCount = wallPlacements.Count * layout.StoryCount;
            var needsRefresh = walls.Length != expectedWallCount;
            var sharedCount = Mathf.Min(walls.Length, expectedWallCount);
            for (var index = 0; index < sharedCount && !needsRefresh; index++)
            {
                var storyIndex = index / wallPlacements.Count;
                var placement = wallPlacements[index % wallPlacements.Count];
                needsRefresh = walls[index].Kind != placement.Kind
                    || walls[index].Cell != placement.Cell
                    || walls[index].StoryIndex != storyIndex
                    || !Mathf.Approximately(
                        walls[index].BaseHeight,
                        TestBuildingCreator.GetStoryBaseHeight(Creator.WallHeight, storyIndex))
                    || !Mathf.Approximately(walls[index].WallHeight, Creator.WallHeight);
            }

            if (needsRefresh)
            {
                for (var index = walls.Length - 1; index >= expectedWallCount; index--)
                {
                    Undo.DestroyObjectImmediate(walls[index].gameObject);
                }

                sharedCount = Mathf.Min(walls.Length, expectedWallCount);
                for (var index = 0; index < sharedCount; index++)
                {
                    var storyIndex = index / wallPlacements.Count;
                    ConfigureWall(
                        walls[index],
                        wallPlacements[index % wallPlacements.Count],
                        storyIndex);
                }

                for (var index = sharedCount; index < expectedWallCount; index++)
                {
                    var storyIndex = index / wallPlacements.Count;
                    CreateWall(
                        generatedVisuals,
                        wallPlacements[index % wallPlacements.Count],
                        storyIndex);
                }
            }

            var outputsNeedRefresh = hierarchyChanged
                || needsRefresh
                || generatedCollision.childCount != wallPlacements.Count
                || DoorNeedsRefresh(layout, visualDoors);
            if (!outputsNeedRefresh)
            {
                continue;
            }

            RebuildCollision(layout, generatedCollision, wallPlacements);
            RebuildDoors(layout, visualDoors);
            EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        }
    }

    private bool EnsureGeneratedHierarchy(
        TestBuildingLayout layout,
        out Transform generatedVisuals,
        out Transform generatedCollision,
        out Transform visualDoors)
    {
        var hierarchyChanged = false;
        generatedVisuals = layout.transform.Find(TestBuildingLayout.GeneratedVisualsName)!;
        if (generatedVisuals is null || !generatedVisuals)
        {
            generatedVisuals = CreateGeneratedRoot(
                layout.transform,
                TestBuildingLayout.GeneratedVisualsName);
            hierarchyChanged = true;
        }

        generatedCollision = layout.transform.Find(TestBuildingLayout.GeneratedCollisionName)!;
        if (generatedCollision is null || !generatedCollision)
        {
            generatedCollision = CreateGeneratedRoot(
                layout.transform,
                TestBuildingLayout.GeneratedCollisionName);
            hierarchyChanged = true;
        }

        visualDoors = layout.transform.Find(TestBuildingLayout.VisualDoorsName)!;
        if (visualDoors is null || !visualDoors)
        {
            visualDoors = CreateGeneratedRoot(layout.transform, TestBuildingLayout.VisualDoorsName);
            hierarchyChanged = true;
        }

        if (!layout.TryGetComponent<TestBuildingPresentation>(out _))
        {
            Undo.AddComponent<TestBuildingPresentation>(layout.gameObject);
            hierarchyChanged = true;
        }

        for (var index = layout.transform.childCount - 1; index >= 0; index--)
        {
            var child = layout.transform.GetChild(index);
            if (child == generatedVisuals
                || child == generatedCollision
                || child == visualDoors
                || (child.GetComponent<GridWall>() is null && child.GetComponent<GridRoof>() is null))
            {
                continue;
            }

            Undo.SetTransformParent(child, generatedVisuals, "Organize test building output");
            hierarchyChanged = true;
        }

        return hierarchyChanged;
    }

    private bool DoorNeedsRefresh(TestBuildingLayout layout, Transform visualDoors)
    {
        if (!layout.HasDoor)
        {
            return visualDoors.childCount != 0;
        }

        if (visualDoors.childCount != layout.Doors.Count)
        {
            return true;
        }

        layout.GetExteriorWallSpans(wallSpans);

        for (var index = 0; index < layout.Doors.Count; index++)
        {
            var door = layout.Doors[index];
            if (!layout.TryGetDoor(wallSpans, door.WallId, out var wall) || wall.IsCorner)
            {
                return true;
            }

            var doorObject = visualDoors.GetChild(index);
            var doorRenderer = doorObject.GetComponent<SpriteRenderer>();
            var depthSurface = doorObject.GetComponent<DepthOcclusionSurface>();
            var portal = doorObject.GetComponent<ScenePortal>();
            var factoryDoor = doorObject.GetComponent<OutsideTestFactoryDoor>();
            if (doorRenderer is null
                || !doorRenderer
                || depthSurface is null
                || !depthSurface
                || !depthSurface.IsConfigured
                || portal is null
                || !portal
                || factoryDoor is null
                || !factoryDoor
                || !factoryDoor.Matches(door.WallId, door.NormalizedOffset)
                || doorRenderer.flipX != Creator.VisualStyle.ShouldFlipEntranceX(wall.Direction))
            {
                return true;
            }
        }

        return false;
    }

    private void RebuildCollision(
        TestBuildingLayout layout,
        Transform generatedCollision,
        IReadOnlyList<TestBuildingCreator.WallPlacement> placements)
    {
        for (var index = generatedCollision.childCount - 1; index >= 0; index--)
        {
            Object.DestroyImmediate(generatedCollision.GetChild(index).gameObject);
        }

        foreach (var placement in placements)
        {
            var collisionObject = new GameObject(
                $"Wall Collision {placement.Kind} ({placement.Cell.x},{placement.Cell.y})");
            collisionObject.transform.SetParent(generatedCollision, false);
            var collider = collisionObject.AddComponent<PolygonCollider2D>();
            var logicalFootprint = GridWall.GetLogicalFootprint(placement.Kind, placement.Cell);
            var points = new Vector2[logicalFootprint.Count];
            for (var index = 0; index < logicalFootprint.Count; index++)
            {
                var worldPoint = Creator.Grid.LogicalToWorld(logicalFootprint[index]);
                points[index] = generatedCollision.InverseTransformPoint(worldPoint);
            }

            collider.pathCount = 1;
            collider.SetPath(0, points);
        }
    }

    private void RebuildDoors(TestBuildingLayout layout, Transform visualDoors)
    {
        for (var index = visualDoors.childCount - 1; index >= 0; index--)
        {
            Object.DestroyImmediate(visualDoors.GetChild(index).gameObject);
        }

        if (!layout.HasDoor || Creator.VisualStyle is null || !Creator.VisualStyle)
        {
            return;
        }

        layout.GetExteriorWallSpans(wallSpans);
        foreach (var door in layout.Doors)
        {
            if (!layout.TryGetDoor(wallSpans, door.WallId, out var wall) || wall.IsCorner)
            {
                continue;
            }

            CreateDoorVisual(layout, visualDoors, wall, door.NormalizedOffset);
        }
    }

    private void CreateDoorVisual(
        TestBuildingLayout layout,
        Transform visualDoors,
        TestBuildingCreator.ExteriorWallSpan wall,
        float normalizedOffset)
    {
        var doorObject = new GameObject($"Door {wall.StableId} {normalizedOffset:0.###}");
        doorObject.transform.SetParent(visualDoors, false);
        var logicalPosition = Vector2.Lerp(
            wall.LogicalStart,
            wall.LogicalEnd,
            normalizedOffset);
        var worldPosition = Creator.Grid.LogicalToWorld(logicalPosition);
        doorObject.transform.position = new Vector3(
            worldPosition.x,
            worldPosition.y,
            layout.transform.position.z);

        var renderer = doorObject.AddComponent<SpriteRenderer>();
        renderer.sprite = Creator.VisualStyle.EntranceSprite;
        renderer.color = Creator.VisualStyle.EntranceColor;
        renderer.flipX = Creator.VisualStyle.ShouldFlipEntranceX(wall.Direction);
        renderer.sortingOrder = GetDoorSortingOrder(wall);
        doorObject.transform.localScale = Vector3.one * (
            Creator.VisualStyle.EntranceHeight / GetVisibleSpriteHeight(renderer.sprite));

        var logicalFootprint = GridWall.GetLogicalFootprint(wall.Kind, wall.Cell);
        var groundPolygon = new List<Vector3>(logicalFootprint.Count);
        foreach (var logicalPoint in logicalFootprint)
        {
            var groundPoint = Creator.Grid.LogicalToWorld(logicalPoint);
            groundPolygon.Add(new Vector3(
                groundPoint.x,
                groundPoint.y,
                layout.transform.position.z));
        }

        var depthSurface = doorObject.AddComponent<DepthOcclusionSurface>();
        depthSurface.Configure(
            GetVisibleSpritePolygon(renderer),
            groundPolygon,
            new Vector3(worldPosition.x, worldPosition.y, layout.transform.position.z),
            wall.LogicalStart,
            wall.LogicalEnd);

        Undo.AddComponent<ScenePortal>(doorObject);
        var factoryDoor = Undo.AddComponent<OutsideTestFactoryDoor>(doorObject);
        factoryDoor.Configure(wall.StableId, normalizedOffset);
        EditorUtility.SetDirty(factoryDoor);
    }

    private static int GetDoorSortingOrder(TestBuildingCreator.ExteriorWallSpan wall)
    {
        var depth = (wall.LogicalStart.x + wall.LogicalStart.y
            + wall.LogicalEnd.x + wall.LogicalEnd.y) * 0.5f;
        return 1005 - Mathf.RoundToInt(depth * 10f);
    }

    private static List<Vector3> GetVisibleSpritePolygon(SpriteRenderer renderer)
    {
        var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var vertex in renderer.sprite.vertices)
        {
            var point = new Vector2(vertex.x, vertex.y);
            if (renderer.flipX)
            {
                point.x = -point.x;
            }

            if (renderer.flipY)
            {
                point.y = -point.y;
            }

            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }

        return new List<Vector3>
        {
            renderer.transform.TransformPoint(new Vector3(minimum.x, minimum.y, 0f)),
            renderer.transform.TransformPoint(new Vector3(maximum.x, minimum.y, 0f)),
            renderer.transform.TransformPoint(new Vector3(maximum.x, maximum.y, 0f)),
            renderer.transform.TransformPoint(new Vector3(minimum.x, maximum.y, 0f))
        };
    }

    private static float GetVisibleSpriteHeight(Sprite sprite)
    {
        var minimum = float.PositiveInfinity;
        var maximum = float.NegativeInfinity;
        foreach (var vertex in sprite.vertices)
        {
            minimum = Mathf.Min(minimum, vertex.y);
            maximum = Mathf.Max(maximum, vertex.y);
        }

        return maximum - minimum;
    }

    private void ClearGeneratedBuildings()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        var deletedSceneCount = 0;
        if (!TestBuildingFloorSceneUtility.DeleteAllFloorScenes(out deletedSceneCount))
        {
            statusMessage = "Could not clear generated test buildings. Close any open inside scenes and try again.";
            Repaint();
            SceneView.RepaintAll();
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
        statusMessage = deletedSceneCount > 0
            ? $"Cleared generated test buildings and deleted {deletedSceneCount} inside scenes."
            : "Cleared generated test buildings.";
        Repaint();
        SceneView.RepaintAll();
    }

    private void EnsureBuildingInstanceIds()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        var usedIds = new HashSet<uint>();
        var nextId = 1u;
        var changed = false;
        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            if (layout.BuildingInstanceId != 0 && usedIds.Add(layout.BuildingInstanceId))
            {
                if (layout.BuildingInstanceId >= nextId)
                {
                    nextId = layout.BuildingInstanceId + 1u;
                }

                continue;
            }

            while (usedIds.Contains(nextId))
            {
                nextId++;
            }

            layout.SetBuildingInstanceId(nextId);
            usedIds.Add(nextId);
            nextId++;
            EditorUtility.SetDirty(layout);
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        }
    }

    private void MigrateLegacyDoors()
    {
        var changed = false;
        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            if (!layout.MigrateLegacyDoor())
            {
                continue;
            }

            EditorUtility.SetDirty(layout);
            changed = true;
        }

        if (changed)
        {
            EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        }
    }

    private void RefreshGeneratedRoofs()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            var generatedVisuals = layout.transform.Find(TestBuildingLayout.GeneratedVisualsName);
            if (generatedVisuals is null || !generatedVisuals)
            {
                continue;
            }

            var roofs = generatedVisuals.GetComponentsInChildren<GridRoof>(true);
            var expectedRoofCount = layout.StoryCount;
            var roofsChanged = roofs.Length != expectedRoofCount;
            var secondCorner = layout.AnchorCell + new Vector3Int(
                layout.Size.x - 1,
                layout.Size.y - 1);
            for (var index = roofs.Length - 1; index >= expectedRoofCount; index--)
            {
                Undo.DestroyObjectImmediate(roofs[index].gameObject);
            }

            var sharedCount = Mathf.Min(roofs.Length, expectedRoofCount);
            var configurationChanged = false;
            for (var storyIndex = 0; storyIndex < sharedCount; storyIndex++)
            {
                configurationChanged |= ConfigureRoof(
                    roofs[storyIndex],
                    layout.AnchorCell,
                    secondCorner,
                    storyIndex);
            }

            for (var storyIndex = sharedCount; storyIndex < expectedRoofCount; storyIndex++)
            {
                CreateRoof(generatedVisuals, layout.AnchorCell, secondCorner, storyIndex);
            }

            if (roofsChanged || configurationChanged)
            {
                EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
            }
        }
    }

    private void EnsureFloorScenes()
    {
        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            TestBuildingFloorSceneUtility.EnsureFloorScenes(layout);
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

        var roofTopHeight = serializedCreator.FindProperty("roofTopHeight");
        roofTopHeight.floatValue = serializedCreator.FindProperty("wallHeight").floatValue;

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
