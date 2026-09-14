// Provides the Scene View authoring workflow for persistent multi-story test-building shells.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    private string selectedSavePath = string.Empty;
    private uint selectedTopologyBuildingId;
    private FactoryBuildingTopologyPlan topologyPlan = null!;

    private TestBuildingCreator Creator => (TestBuildingCreator)target;

    private void OnEnable()
    {
        if (target is not TestBuildingCreator || !target)
        {
            return;
        }

        Undo.undoRedoPerformed += UndoRedoPerformed;
        if (string.IsNullOrWhiteSpace(selectedSavePath))
        {
            selectedSavePath = FactoryWorldPaths.GetDefaultDatabasePath();
        }
        MigrateLegacySettings();
        EnsureBuildingInstanceIds();
        if (Creator.EnsureBuildingInstanceIdHighWaterMark())
        {
            EditorUtility.SetDirty(Creator);
        }
        MigrateLegacyDoors();
        RefreshGeneratedBuildings();
        RequestEditorViewRefresh();
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
        var propertiesChanged = serializedObject.ApplyModifiedProperties();
        if (propertiesChanged)
        {
            topologyPlan = null!;
            RefreshGeneratedBuildings();
        }

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

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
            if (GUILayout.Button("Clear Authored Buildings"))
            {
                ClearGeneratedBuildings();
            }
        }

        DrawStoryControls();
        DrawBuildingList();
        DrawSaveTopologyControls();

        using (new EditorGUI.DisabledScope(Application.isPlaying))
        {
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
        }

        if (doorPlacementMode)
        {
            EditorGUILayout.HelpBox(
                "Only the topmost visible wall surface can be selected. "
                + "Corner pieces and positions near corners are rejected. Click again to add more doors.",
                MessageType.Info);
        }
    }

    private void DrawBuildingList()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Authored Buildings", EditorStyles.boldLabel);
        var records = Creator.GetAuthoredBuildingRecords();
        foreach (var record in records)
        {
            var interior = BuildingFootprint.GetUsableInteriorSize(record.FootprintSize);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"{record.BuildingInstanceId} · ({record.AnchorCell.x}, {record.AnchorCell.y}) · "
                + $"{record.FootprintSize.x} x {record.FootprintSize.y} · "
                + $"{interior.x} x {interior.y} · {record.StoryCount} stories");
            if (GUILayout.Button("Select", GUILayout.Width(55f)))
            {
                SelectLayout(record.BuildingInstanceId);
            }

            if (GUILayout.Button("Frame", GUILayout.Width(50f)))
            {
                SelectLayout(record.BuildingInstanceId);
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Delete...", GUILayout.Width(65f)))
                {
                    var layout = FindLayout(record.BuildingInstanceId);
                    if (layout is not null && layout)
                    {
                        DeleteBuilding(layout);
                    }

                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        if (records.Count == 0)
        {
            EditorGUILayout.HelpBox("No authored buildings.", MessageType.Info);
        }
    }

    private void DrawSaveTopologyControls()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Selected Save Topology", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            "Target",
            string.IsNullOrWhiteSpace(selectedSavePath) ? "Not selected" : selectedSavePath);
        EditorGUILayout.HelpBox(
            "Building topology edits are saved to this database immediately after they succeed. "
            + "Preview remains available for inspecting differences that existed before the edit.",
            MessageType.Info);
        if (GUILayout.Button("Select Save Database..."))
        {
            var initialDirectory = string.IsNullOrWhiteSpace(selectedSavePath)
                ? FactoryWorldPaths.GetProjectRoot()
                : Path.GetDirectoryName(selectedSavePath);
            var path = EditorUtility.OpenFilePanel(
                "Select factory database",
                initialDirectory,
                "db");
            if (!string.IsNullOrWhiteSpace(path))
            {
                selectedSavePath = Path.GetFullPath(path);
                topologyPlan = null!;
                statusMessage = $"Selected save target: {selectedSavePath}";
            }
        }

        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(selectedSavePath)))
        {
            if (GUILayout.Button("Preview Changes..."))
            {
                PreviewTopologyChanges();
            }
        }

        if (topologyPlan is not null && topologyPlan.HasChanges)
        {
            foreach (var change in topologyPlan.Changes)
            {
                EditorGUILayout.BeginHorizontal();
                var changeLabel = change.IsDelete
                    ? $"Delete building {change.BuildingInstanceId}: {change.EquipmentCount} entities, "
                      + $"{change.AffectedFloorGuids.Count} floors, {change.AffectedConnectionGuids.Count} connections, "
                      + $"{change.AffectedRouteGuids.Count} routes, {change.AffectedTruckGuids.Count} trucks"
                    : change.IsCreate
                        ? $"Create building {change.BuildingInstanceId}: "
                          + $"{change.ProposedRecord.FootprintSize.x}x{change.ProposedRecord.FootprintSize.y}, "
                          + $"{change.ProposedRecord.StoryCount} floors"
                        : $"Building {change.BuildingInstanceId}: "
                          + $"{change.CurrentRecord.FootprintSize.x}x{change.CurrentRecord.FootprintSize.y} "
                          + $"-> {change.ProposedRecord.FootprintSize.x}x{change.ProposedRecord.FootprintSize.y}, "
                          + $"{change.EquipmentCount} entities";
                EditorGUILayout.LabelField(changeLabel);
                if (GUILayout.Button(
                        selectedTopologyBuildingId == change.BuildingInstanceId ? "Selected" : "Select",
                        GUILayout.Width(65f)))
                {
                    selectedTopologyBuildingId = change.BuildingInstanceId;
                }

                EditorGUILayout.EndHorizontal();
            }

            var playModeBlocksEdit = IsSelectedSaveActiveInPlayMode();
            using (new EditorGUI.DisabledScope(
                       playModeBlocksEdit || selectedTopologyBuildingId == 0))
            {
                if (GUILayout.Button("Apply Selected Save..."))
                {
                    ApplySelectedTopology();
                }
            }

            if (playModeBlocksEdit)
            {
                EditorGUILayout.HelpBox(
                    "Offline database editing is disabled while this save is active in Play Mode.",
                    MessageType.Warning);
            }
        }
        else if (topologyPlan is not null)
        {
            EditorGUILayout.HelpBox("The selected save already matches authored topology.", MessageType.Info);
        }
    }

    private void DrawStoryControls()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Building Stories", EditorStyles.boldLabel);
        var records = Creator.GetAuthoredBuildingRecords();
        if (records.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Create a building to manage its shared-template interior floors.",
                MessageType.Info);
            return;
        }

        foreach (var record in records)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"Building {record.BuildingInstanceId} ({record.FootprintSize.x} x {record.FootprintSize.y})",
                $"{record.StoryCount} {((record.StoryCount == 1) ? "story" : "stories")}");
            var isBlocked = Application.isPlaying;
            using (new EditorGUI.DisabledScope(isBlocked))
            {
            if (GUILayout.Button("Add Story", GUILayout.Width(80f)))
            {
                UpdateAuthoredStoryCount(record, record.StoryCount + 1, "Add Authored Story");

                GUIUtility.ExitGUI();
            }

            var canDelete = record.StoryCount > 1;
            using (new EditorGUI.DisabledScope(!canDelete))
            {
                if (GUILayout.Button("Delete Top", GUILayout.Width(90f)))
                {
                    if (!EditorUtility.DisplayDialog(
                            "Delete authored top story?",
                            $"Delete authored story {record.StoryCount - 1} from building {record.BuildingInstanceId}?",
                            "Delete Authored Top Story",
                            "Cancel"))
                    {
                        GUIUtility.ExitGUI();
                    }

                    UpdateAuthoredStoryCount(record, record.StoryCount - 1, "Delete Authored Top Story");

                    Repaint();
                    SceneView.RepaintAll();
                    GUIUtility.ExitGUI();
                }
            }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                "Interior",
                $"Shared template ({record.StoryCount} {(record.StoryCount == 1 ? "floor" : "floors")})");
        }
    }

    private void UpdateAuthoredStoryCount(
        BuildingRecord record,
        int storyCount,
        string undoName)
    {
        if (!Creator.HasAuthoredLayout || Application.isPlaying)
        {
            return;
        }

        var updatedRecord = record.Clone();
        updatedRecord.SetTopology(
            record.BuildingInstanceId,
            record.AnchorCell,
            record.FootprintSize,
            storyCount);
        var authoredBefore = Creator.AuthoredLayout.CloneRecords();
        Undo.RecordObject(Creator.AuthoredLayout, undoName);
        var rebuildError = string.Empty;
        if (!Creator.AuthoredLayout.TryUpdateRecord(updatedRecord, out var assetError)
            || !TryReconcileGeneratedBuildings(
                Creator.AuthoredLayout.CloneRecords(),
                out rebuildError))
        {
            Creator.AuthoredLayout.ReplaceRecords(authoredBefore);
            statusMessage = string.IsNullOrWhiteSpace(assetError) ? rebuildError : assetError;
            return;
        }

        EditorUtility.SetDirty(Creator.AuthoredLayout);
        topologyPlan = null!;
        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        if (PersistAuthoredBuildings(new[] { record.BuildingInstanceId }))
        {
            statusMessage = $"Updated authored story count for building {record.BuildingInstanceId}.";
        }

        RequestEditorViewRefresh();
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
        return !Application.isPlaying
            && Creator.gameObject.scene == SceneManager.GetActiveScene()
            && Creator.Grid is not null
            && Creator.Grid
            && Creator.Material is not null
            && Creator.Material
            && Creator.GeneratedBuildings is not null
            && Creator.GeneratedBuildings;
    }

    private TestBuildingLayout FindLayout(uint buildingInstanceId)
    {
        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            if (layout.BuildingInstanceId == buildingInstanceId)
            {
                return layout;
            }
        }

        return null!;
    }

    private void SelectLayout(uint buildingInstanceId)
    {
        var layout = FindLayout(buildingInstanceId);
        if (layout is null || !layout)
        {
            return;
        }

        Selection.activeGameObject = layout.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
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
        if (Creator.HasAuthoredLayout)
        {
            var updatedRecord = previousRecord.Clone();
            foreach (var existingDoor in updatedRecord.Doors)
            {
                if (existingDoor.WallId == wall.StableId
                    && Mathf.Approximately(existingDoor.NormalizedOffset, normalizedOffset))
                {
                    statusMessage = "A door is already placed at that position.";
                    return;
                }
            }

            var door = new BuildingRecord.DoorPlacement(wall.StableId, normalizedOffset);
            var doors = new List<BuildingRecord.DoorPlacement>(updatedRecord.Doors)
            {
                door
            };
            updatedRecord.SetDoors(doors);
            var authoredBefore = Creator.AuthoredLayout.CloneRecords();
            Undo.RecordObject(Creator.AuthoredLayout, "Place authored building door");
            var rebuildError = string.Empty;
            if (!Creator.AuthoredLayout.TryUpdateRecord(updatedRecord, out var assetError)
                || !TryReconcileGeneratedBuildings(
                    Creator.AuthoredLayout.CloneRecords(),
                    out rebuildError))
            {
                Creator.AuthoredLayout.ReplaceRecords(authoredBefore);
                statusMessage = string.IsNullOrWhiteSpace(assetError) ? rebuildError : assetError;
                return;
            }

            EditorUtility.SetDirty(Creator.AuthoredLayout);
            topologyPlan = null!;
            if (PersistAuthoredBuildings(new[] { layout.BuildingInstanceId }))
            {
                statusMessage = $"Placed a door on the {wall.Direction} exterior wall.";
            }

            RequestEditorViewRefresh();
            return;
        }

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
        if (PersistAuthoredBuildings(new[] { layout.BuildingInstanceId }))
        {
            statusMessage = $"Placed a door on the {wall.Direction} exterior wall.";
        }

        RequestEditorViewRefresh();
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
        Undo.RegisterCompleteObjectUndo(Creator, "Reserve test building ID");
        if (!Creator.TryReserveBuildingInstanceId(out buildingInstanceId))
        {
            statusMessage = "No building IDs are available.";
            return;
        }

        record = new BuildingRecord(
            buildingInstanceId,
            anchor,
            size,
            1);
        if (Creator.HasAuthoredLayout)
        {
            var authoredBefore = Creator.AuthoredLayout.CloneRecords();
            Undo.RecordObject(Creator.AuthoredLayout, "Create authored building");
            var rebuildError = string.Empty;
            if (!Creator.AuthoredLayout.TryAddRecord(record, out var assetError)
                || !TryReconcileGeneratedBuildings(
                    Creator.AuthoredLayout.CloneRecords(),
                    out rebuildError))
            {
                Creator.AuthoredLayout.ReplaceRecords(authoredBefore);
                statusMessage = string.IsNullOrWhiteSpace(assetError) ? rebuildError : assetError;
                return;
            }

            EditorUtility.SetDirty(Creator.AuthoredLayout);
            topologyPlan = null!;
            EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
            var authoredPersisted = PersistAuthoredBuildings(new[] { buildingInstanceId });
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = Creator.gameObject;
            if (authoredPersisted)
            {
                statusMessage = $"Created {size.x} x {size.y} test building {buildingInstanceId}.";
            }

            RequestEditorViewRefresh();
            return;
        }

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
        var generatedPersisted = PersistAuthoredBuildings(new[] { buildingInstanceId });
        Undo.CollapseUndoOperations(undoGroup);
        Selection.activeGameObject = Creator.gameObject;
        if (generatedPersisted)
        {
            statusMessage = $"Created {size.x} x {size.y} test building {buildingInstanceId}.";
        }

        RequestEditorViewRefresh();
    }

    private void RefreshGeneratedBuildings()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        if (Creator.HasAuthoredLayout)
        {
            if (!TryReconcileGeneratedBuildings(
                    Creator.AuthoredLayout.CloneRecords(),
                    out var authoredError))
            {
                statusMessage = authoredError;
            }

            RequestEditorViewRefresh();
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

        RequestEditorViewRefresh();
    }

    private bool TryReconcileGeneratedBuildings(
        IEnumerable<BuildingRecord> records,
        out string error)
    {
        error = string.Empty;
        var desiredRecords = new List<BuildingRecord>();
        foreach (var record in records)
        {
            if (record is not null)
            {
                desiredRecords.Add(record.Clone());
            }
        }

        if (!BuildingShellValidation.TryValidateRecords(
                desiredRecords,
                Creator.DoorCornerExclusionDistance,
                out error))
        {
            return false;
        }

        var existingLayouts = new Dictionary<uint, TestBuildingLayout>();
        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            if (!existingLayouts.TryAdd(layout.BuildingInstanceId, layout))
            {
                error = $"Duplicate generated building ID {layout.BuildingInstanceId} was found.";
                return false;
            }
        }

        var desiredIds = new HashSet<uint>();
        var assembler = new BuildingShellAssembler();
        foreach (var record in desiredRecords)
        {
            desiredIds.Add(record.BuildingInstanceId);
            if (existingLayouts.TryGetValue(record.BuildingInstanceId, out var layout))
            {
                if (!assembler.TryRebuildShell(
                        record,
                        Creator,
                        layout.transform,
                        out _))
                {
                    error = $"Generated shell for building {record.BuildingInstanceId} could not be rebuilt.";
                    return false;
                }

                EditorUtility.SetDirty(layout);
                continue;
            }

            var buildingObject = assembler.CreateShell(
                record,
                Creator,
                Creator.GeneratedBuildings);
            if (buildingObject is null || !buildingObject)
            {
                error = $"Generated shell for building {record.BuildingInstanceId} could not be created.";
                return false;
            }

            Undo.RegisterCreatedObjectUndo(buildingObject, "Create generated test building shell");
        }

        foreach (var pair in existingLayouts)
        {
            if (!desiredIds.Contains(pair.Key))
            {
                Undo.DestroyObjectImmediate(pair.Value.gameObject);
            }
        }

        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        return true;
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

    private void DeleteBuilding(TestBuildingLayout layout)
    {
        var record = layout.ExportBuildingRecord();
        if (Application.isPlaying)
        {
            statusMessage = "Authoring is disabled in Play Mode.";
            return;
        }
        if (!EditorUtility.DisplayDialog(
                "Delete authored building?",
                $"Delete building {record.BuildingInstanceId} at ({record.AnchorCell.x}, {record.AnchorCell.y})? "
                + $"The selected database will be updated immediately:\n{selectedSavePath}\n\n"
                + "This permanently removes the building and its dependent saved state from that database.",
                "Delete Authored Building",
                "Cancel"))
        {
            return;
        }

        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Delete authored test building");
        if (Creator.HasAuthoredLayout)
        {
            var authoredBefore = Creator.AuthoredLayout.CloneRecords();
            Undo.RecordObject(Creator.AuthoredLayout, "Delete authored building");
            var rebuildError = string.Empty;
            if (!Creator.AuthoredLayout.TryRemoveRecord(record.BuildingInstanceId, out var assetError)
                || !TryReconcileGeneratedBuildings(
                    Creator.AuthoredLayout.CloneRecords(),
                    out rebuildError))
            {
                Creator.AuthoredLayout.ReplaceRecords(authoredBefore);
                statusMessage = string.IsNullOrWhiteSpace(assetError) ? rebuildError : assetError;
                return;
            }

            EditorUtility.SetDirty(Creator.AuthoredLayout);
            topologyPlan = null!;
            EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
            var authoredPersisted = PersistAuthoredBuildings(
                new[] { record.BuildingInstanceId },
                true);
            Undo.CollapseUndoOperations(undoGroup);
            if (authoredPersisted)
            {
                statusMessage = $"Deleted authored building {record.BuildingInstanceId}.";
            }

            topologyPlan = null!;
            RequestEditorViewRefresh();
            return;
        }

        Undo.DestroyObjectImmediate(layout.gameObject);
        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        var generatedPersisted = PersistAuthoredBuildings(
            new[] { record.BuildingInstanceId },
            true);
        Undo.CollapseUndoOperations(undoGroup);
        if (generatedPersisted)
        {
            statusMessage = $"Deleted authored building {record.BuildingInstanceId}.";
        }

        topologyPlan = null!;
        RequestEditorViewRefresh();
    }

    private void PreviewTopologyChanges()
    {
        topologyPlan = null!;
        selectedTopologyBuildingId = 0;
        if (!FactoryBuildingEditService.TryCreateTopologyPlan(
                selectedSavePath,
                Creator.GetAuthoredBuildingRecords(),
                Creator.DoorCornerExclusionDistance,
                out var plan,
                out var error))
        {
            topologyPlan = plan;
            statusMessage = error;
            return;
        }

        topologyPlan = plan;
        if (plan.HasChanges)
        {
            selectedTopologyBuildingId = plan.Changes[0].BuildingInstanceId;
            statusMessage = $"Previewed {plan.Changes.Count} topology change(s) for {plan.DatabasePath}.";
        }
        else
        {
            statusMessage = "The selected save already matches authored topology.";
        }
    }

    private void ApplySelectedTopology()
    {
        if (topologyPlan is null
            || selectedTopologyBuildingId == 0
            || IsSelectedSaveActiveInPlayMode())
        {
            return;
        }

        if (!topologyPlan.TryGetChange(selectedTopologyBuildingId, out var change))
        {
            statusMessage = "Select a valid topology change before applying.";
            return;
        }

        var confirmation = change.IsDelete
            ? EditorUtility.DisplayDialog(
                "Permanently delete building from save?",
                $"This permanently removes building {change.BuildingInstanceId}, its floors, entities, connections, routes, and trucks from:\n{selectedSavePath}",
                "Delete From Save",
                "Cancel")
            : EditorUtility.DisplayDialog(
                "Apply topology to selected save?",
                $"Apply building {change.BuildingInstanceId} to:\n{selectedSavePath}\n\n"
                + "This changes the selected save only and preserves existing state where possible.",
                "Apply",
                "Cancel");
        if (!confirmation)
        {
            return;
        }

        if (!FactoryBuildingEditService.TryApplyTopologyPlan(
                selectedSavePath,
                topologyPlan,
                Creator.GetAuthoredBuildingRecords(),
                new[] { selectedTopologyBuildingId },
                change.IsDelete,
                Creator.DoorCornerExclusionDistance,
                out var result,
                out var error))
        {
            statusMessage = $"Topology application failed: {error}";
            return;
        }

        statusMessage = $"Applied building {selectedTopologyBuildingId} to {result.DatabasePath}. "
            + $"Preserved {result.PreservedEntityCount} entities; relocated {result.Relocations.Count}.";
        PreviewTopologyChanges();
    }

    private bool IsSelectedSaveActiveInPlayMode()
    {
        if (!Application.isPlaying || string.IsNullOrWhiteSpace(selectedSavePath))
        {
            return false;
        }

        var activePath = GameSceneManager.Instance is not null
            ? GameSceneManager.Instance.OutsideTestStatePath
            : GameSceneManager.GetDefaultOutsideTestStatePath();
        return string.Equals(
            Path.GetFullPath(activePath),
            Path.GetFullPath(selectedSavePath),
            System.StringComparison.OrdinalIgnoreCase);
    }

    private void SyncAuthoringAsset()
    {
        PersistCompactLayoutAsset();
    }

    private void ClearGeneratedBuildings()
    {
        if (Creator.GeneratedBuildings is null || !Creator.GeneratedBuildings)
        {
            return;
        }

        if (!EditorUtility.DisplayDialog(
                "Clear authored buildings?",
                $"This removes all authored building records and preview shells. "
                + $"The selected database will also be updated immediately:\n{selectedSavePath}\n\n"
                + "All exterior buildings and their dependent saved state will be permanently removed from that database.",
                "Clear Authored Buildings",
                "Cancel"))
        {
            return;
        }

        var undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Clear test buildings");
        if (Creator.HasAuthoredLayout)
        {
            var authoredBefore = Creator.AuthoredLayout.CloneRecords();
            Undo.RecordObject(Creator.AuthoredLayout, "Clear authored buildings");
            Creator.AuthoredLayout.ReplaceRecords(System.Array.Empty<BuildingRecord>());
            if (!TryReconcileGeneratedBuildings(
                    Creator.AuthoredLayout.CloneRecords(),
                    out var rebuildError))
            {
                Creator.AuthoredLayout.ReplaceRecords(authoredBefore);
                statusMessage = rebuildError;
                return;
            }

            EditorUtility.SetDirty(Creator.AuthoredLayout);
            topologyPlan = null!;
            EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
            Undo.CollapseUndoOperations(undoGroup);
            var authoredPersisted = PersistAuthoredBuildings(null, true, true);
            if (authoredPersisted)
            {
                statusMessage = "Cleared authored buildings.";
            }

            RequestEditorViewRefresh();
            return;
        }

        for (var index = Creator.GeneratedBuildings.childCount - 1; index >= 0; index--)
        {
            Undo.DestroyObjectImmediate(Creator.GeneratedBuildings.GetChild(index).gameObject);
        }

        EditorSceneManager.MarkSceneDirty(Creator.gameObject.scene);
        var generatedPersisted = PersistAuthoredBuildings(null, true, true);
        Undo.CollapseUndoOperations(undoGroup);
        if (generatedPersisted)
        {
            statusMessage = "Cleared authored buildings.";
        }

        RequestEditorViewRefresh();
    }

    private bool PersistAuthoredBuildings(
        IEnumerable<uint> changedBuildingIds,
        bool confirmDestructiveDeletes = false,
        bool applyAllChanges = false)
    {
        SyncAuthoringAsset();
        return TryPersistAuthoredTopology(
            changedBuildingIds,
            confirmDestructiveDeletes,
            applyAllChanges);
    }

    private void PersistCompactLayoutAsset()
    {
        if (!Creator.HasAuthoredLayout
            || Creator.GeneratedBuildings is null
            || !Creator.GeneratedBuildings)
        {
            return;
        }

        var records = new List<BuildingRecord>();
        foreach (var layout in Creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            records.Add(layout.ExportBuildingRecord());
        }

        Undo.RecordObject(Creator.AuthoredLayout, "Sync authored building layout");
        Creator.AuthoredLayout.ReplaceRecords(records);
        EditorUtility.SetDirty(Creator.AuthoredLayout);
    }

    private bool TryPersistAuthoredTopology(
        IEnumerable<uint> changedBuildingIds,
        bool confirmDestructiveDeletes,
        bool applyAllChanges)
    {
        if (Application.isPlaying || IsSelectedSaveActiveInPlayMode())
        {
            statusMessage = "Database topology persistence is disabled while the selected save is active in Play Mode.";
            return false;
        }

        var authoredRecords = Creator.GetAuthoredBuildingRecords();
        if (!FactoryBuildingEditService.TryCreateTopologyPlan(
                selectedSavePath,
                authoredRecords,
                Creator.DoorCornerExclusionDistance,
                out var plan,
                out var planError))
        {
            statusMessage = $"Database topology save failed: {planError}";
            return false;
        }

        var selectedIds = new HashSet<uint>();
        if (!applyAllChanges && changedBuildingIds is not null)
        {
            foreach (var buildingId in changedBuildingIds)
            {
                if (buildingId != 0)
                {
                    selectedIds.Add(buildingId);
                }
            }
        }

        foreach (var change in plan.Changes)
        {
            if (!change.IsDelete
                || (!applyAllChanges && !selectedIds.Contains(change.BuildingInstanceId)))
            {
                continue;
            }

            if (!confirmDestructiveDeletes)
            {
                statusMessage = $"Database topology save refused: deleting building {change.BuildingInstanceId} requires confirmation.";
                return false;
            }
        }

        if (!FactoryBuildingEditService.TryApplyTopologyPlan(
                selectedSavePath,
                plan,
                authoredRecords,
                applyAllChanges ? null : selectedIds,
                confirmDestructiveDeletes,
                Creator.DoorCornerExclusionDistance,
                out var result,
                out var error))
        {
            statusMessage = $"Database topology save failed: {error}";
            return false;
        }

        topologyPlan = null!;
        statusMessage = result.Changed
            ? $"Saved authored topology to {result.DatabasePath}. "
              + $"Preserved {result.PreservedEntityCount} entities; relocated {result.Relocations.Count}."
            : "The selected database already matches authored topology.";
        return true;
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

        topologyPlan = null!;
        RefreshGeneratedBuildings();
        RequestEditorViewRefresh();
    }

    private void RequestEditorViewRefresh()
    {
        EditorApplication.QueuePlayerLoopUpdate();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
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
