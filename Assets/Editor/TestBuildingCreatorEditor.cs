// Provides the Scene View authoring workflow for persistent multi-story test-building shells.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using OutsideTestFloorStateOwner = FactoryWorldState;

[CustomEditor(typeof(TestBuildingCreator))]
public sealed class TestBuildingCreatorEditor : Editor
{
    private static readonly Color PreviewFillColor = new(0.35f, 1f, 0.45f, 0.18f);
    private static readonly Color PreviewLineColor = new(0.35f, 1f, 0.45f, 1f);

    private readonly List<TestBuildingCreator.ExteriorWallSpan> wallSpans = new();
    private readonly List<BuildingRecord> buildingRecords = new();
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

        Undo.undoRedoPerformed += UndoRedoPerformed;
        MigrateLegacySettings();
        EnsureBuildingInstanceIds();
        MigrateLegacyDoors();
        RefreshGeneratedBuildings();
        PersistAuthoredBuildings();
        SceneView.RepaintAll();
    }

    private void OnDisable()
    {
        Undo.undoRedoPerformed -= UndoRedoPerformed;
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
        RefreshGeneratedBuildings();

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
                "Create a building to manage its shared-template interior floors.",
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
                Undo.RegisterCompleteObjectUndo(layout, "Add test building story");
                layout.SetStoryCount(layout.StoryCount + 1);
                EditorUtility.SetDirty(layout);
                RefreshGeneratedBuildings();
                PersistAuthoredBuildings();
                EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
                statusMessage = $"Added story {layout.StoryCount - 1} to building {layout.BuildingInstanceId}.";

                GUIUtility.ExitGUI();
            }

            var canDelete = layout.StoryCount > 1;
            using (new EditorGUI.DisabledScope(!canDelete))
            {
                if (GUILayout.Button("Delete Top", GUILayout.Width(90f)))
                {
                    if (!EditorUtility.DisplayDialog(
                            "Delete top story?",
                            $"Delete story {layout.StoryCount - 1}?",
                            "Delete",
                            "Cancel"))
                    {
                        GUIUtility.ExitGUI();
                    }

                    Undo.RegisterCompleteObjectUndo(layout, "Delete test building story");
                    layout.SetStoryCount(layout.StoryCount - 1);
                    EditorUtility.SetDirty(layout);
                    RefreshGeneratedBuildings();
                    PersistAuthoredBuildings();
                    EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
                    statusMessage = $"Deleted the top story from building {layout.BuildingInstanceId}.";

                    Repaint();
                    SceneView.RepaintAll();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "Interior",
                $"Shared template ({layout.StoryCount} {(layout.StoryCount == 1 ? "floor" : "floors")})");
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
        var previousRecord = layout.ExportBuildingRecord();
        Undo.RegisterCompleteObjectUndo(layout, "Place test building door");
        if (!layout.AddDoor(wall, normalizedOffset))
        {
            statusMessage = "A door is already placed at that position.";
            return;
        }

        var record = layout.ExportBuildingRecord();
        if (!TryValidateEditorRecord(record, layout, out var error))
        {
            layout.ApplyBuildingRecord(previousRecord);
            statusMessage = error;
            return;
        }

        var assembler = new BuildingShellAssembler();
        assembler.RebuildShell(record, Creator, layout.transform);
        EditorUtility.SetDirty(layout);
        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        PersistAuthoredBuildings();
        statusMessage = $"Placed a door on the {wall.Direction} exterior wall.";
    }

    private void CreateBuilding(Vector3Int first, Vector3Int second)
    {
        var anchor = TestBuildingCreator.GetAnchorCell(first, second);
        var size = TestBuildingCreator.GetSize(first, second);
        EnsureBuildingInstanceIds();
        var buildingInstanceId = Creator.GetNextBuildingInstanceId();
        if (buildingInstanceId == 0)
        {
            statusMessage = "No building IDs are available.";
            return;
        }

        var record = new BuildingRecord(
            buildingInstanceId,
            anchor,
            size,
            1);
        if (!TryValidateEditorRecord(record, null!, out var error))
        {
            statusMessage = error;
            return;
        }

        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create test building");
        var assembler = new BuildingShellAssembler();
        var buildingObject = assembler.CreateShell(
            record,
            Creator,
            Creator.GeneratedBuildings);
        if (buildingObject is null || !buildingObject)
        {
            statusMessage = "The building shell could not be assembled.";
            return;
        }

        Undo.RegisterCreatedObjectUndo(buildingObject, "Create test building");
        var layout = buildingObject.GetComponent<TestBuildingLayout>();
        EditorUtility.SetDirty(layout);
        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        PersistAuthoredBuildings();
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = Creator.gameObject;
        statusMessage = $"Created {size.x} x {size.y} test building {buildingInstanceId}.";
    }

    private void RefreshGeneratedBuildings()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        MigrateLegacyDoors();
        var layouts = Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true);
        buildingRecords.Clear();
        foreach (var layout in layouts)
        {
            buildingRecords.Add(layout.ExportBuildingRecord());
        }

        if (!BuildingShellValidation.TryValidateRecords(
                buildingRecords,
                Creator.DoorCornerExclusionDistance,
                out var validationError))
        {
            statusMessage = validationError;
            return;
        }

        var assembler = new BuildingShellAssembler();
        foreach (var layout in layouts)
        {
            var record = layout.ExportBuildingRecord();
            if (!assembler.RebuildShell(record, Creator, layout.transform))
            {
                continue;
            }

            EditorUtility.SetDirty(layout);
        }
    }

    private bool TryValidateEditorRecord(
        BuildingRecord record,
        TestBuildingLayout ignoredLayout,
        out string error)
    {
        var existingRecords = new List<BuildingRecord>();
        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            if (layout == ignoredLayout)
            {
                continue;
            }

            existingRecords.Add(layout.ExportBuildingRecord());
        }

        return BuildingShellValidation.TryValidate(
            record,
            existingRecords,
            Creator.DoorCornerExclusionDistance,
            out error);
    }

    private void ClearGeneratedBuildings()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        var authoredBuildingIds = new List<uint>();
        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            if (layout.BuildingInstanceId != 0)
            {
                authoredBuildingIds.Add(layout.BuildingInstanceId);
            }
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

        RemoveAuthoredBuildingsFromSave(authoredBuildingIds);
        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        Undo.CollapseUndoOperations(undoGroup);
        statusMessage = deletedSceneCount > 0
            ? $"Cleared generated test buildings and deleted {deletedSceneCount} inside scenes."
            : "Cleared generated test buildings.";
        Repaint();
        SceneView.RepaintAll();
    }

    private void PersistAuthoredBuildings()
    {
        var path = GameSceneManager.GetDefaultOutsideTestStatePath();
        var owner = new OutsideTestFloorStateOwner(GameSceneManager.LegacyOutsideTestBuildingId);
        if (File.Exists(path)
            && !owner.LoadFromFile(path, Creator.DoorCornerExclusionDistance))
        {
            statusMessage = "Could not update the OutsideTest save because its existing state is invalid.";
            return;
        }

        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            TestBuildingFloorSceneUtility.EnsureFloorScenes(layout);
            if (!owner.TryUpdateBuildingRecord(
                    layout.ExportBuildingRecord(),
                    Creator.DoorCornerExclusionDistance,
                    out var error))
            {
                statusMessage = error;
                return;
            }
        }

        if (!owner.SaveToFile(path))
        {
            statusMessage = "Could not persist authored OutsideTest building records.";
        }
    }

    private void RemoveAuthoredBuildingsFromSave(IEnumerable<uint> buildingIds)
    {
        var path = GameSceneManager.GetDefaultOutsideTestStatePath();
        if (!File.Exists(path))
        {
            return;
        }

        var owner = new OutsideTestFloorStateOwner(GameSceneManager.LegacyOutsideTestBuildingId);
        if (!owner.LoadFromFile(path, Creator.DoorCornerExclusionDistance))
        {
            statusMessage = "Could not update the OutsideTest save because its existing state is invalid.";
            return;
        }

        foreach (var buildingId in buildingIds)
        {
            owner.RemoveBuildingAndFloors(buildingId);
        }

        if (!owner.SaveToFile(path))
        {
            statusMessage = "Could not remove authored OutsideTest building records.";
        }
    }

    private void EnsureBuildingInstanceIds()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        var usedIds = new HashSet<uint>();
        var layouts = Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true);
        var nextId = Creator.GetNextBuildingInstanceId();
        var changed = false;
        foreach (var layout in layouts)
        {
            if (layout.BuildingInstanceId == 0)
            {
                if (nextId == 0)
                {
                    statusMessage = "No building IDs are available for authored layouts.";
                    continue;
                }

                layout.SetBuildingInstanceId(nextId);
                usedIds.Add(nextId);
                nextId = nextId == uint.MaxValue ? 0u : nextId + 1u;
                EditorUtility.SetDirty(layout);
                changed = true;
                continue;
            }

            if (!usedIds.Add(layout.BuildingInstanceId))
            {
                statusMessage = $"Duplicate authored building ID {layout.BuildingInstanceId} was rejected.";
            }
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

    private void UndoRedoPerformed()
    {
        if (target is not TestBuildingCreator || !target)
        {
            return;
        }

        RefreshGeneratedBuildings();
        SceneView.RepaintAll();
        Repaint();
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
        return new Vector3(
            worldPosition.x,
            worldPosition.y,
            Creator.Grid.transform.position.z - 0.02f);
    }

    private void ResetPlacement()
    {
        hasFirstCorner = false;
        statusMessage = string.Empty;
    }
}
