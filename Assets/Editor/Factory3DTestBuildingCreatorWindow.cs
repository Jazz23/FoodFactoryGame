// Provides an editor-only 3D companion view for selecting, previewing, and moving authored test buildings.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class Factory3DTestBuildingCreatorWindow : EditorWindow
{
    private readonly List<BuildingRecord> records = new();
    private readonly List<Vector3> outlinePoints = new();
    private TestBuildingCreator creator = null!;
    private FactorySpatialAdapter spatialAdapter = null!;
    private uint selectedBuildingId;
    private int activeFloor;
    private bool moveTool;
    private bool dragging;
    private bool hasPendingMove;
    private Vector3Int pendingAnchor;
    private Vector2Int dragOffset;
    private string selectedSavePath = string.Empty;
    private string statusMessage = string.Empty;

    [MenuItem("Food Factory/3D Test Building Creator")]
    public static void Open()
    {
        GetWindow<Factory3DTestBuildingCreatorWindow>("3D Building Creator");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += DuringSceneGui;
        Selection.selectionChanged += SelectionChanged;
        Undo.undoRedoPerformed += UndoRedoPerformed;
        selectedSavePath = FactoryWorldPaths.GetDefaultDatabasePath();
        ResolveCreator();
        RefreshRecords();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DuringSceneGui;
        Selection.selectionChanged -= SelectionChanged;
        Undo.undoRedoPerformed -= UndoRedoPerformed;
    }

    private void OnGUI()
    {
        ResolveCreator();
        EditorGUI.BeginChangeCheck();
        creator = (TestBuildingCreator)EditorGUILayout.ObjectField(
            "2D Creator",
            creator,
            typeof(TestBuildingCreator),
            true);
        if (EditorGUI.EndChangeCheck())
        {
            selectedBuildingId = 0;
            RefreshRecords();
        }

        if (creator is null || !creator)
        {
            EditorGUILayout.HelpBox(
                "Open the 2D Test Building Creator scene or assign its creator component.",
                MessageType.Info);
            return;
        }

        if (creator.Grid is null || !creator.Grid)
        {
            EditorGUILayout.HelpBox("The assigned creator has no SceneGrid.", MessageType.Error);
            return;
        }

        spatialAdapter = creator.Grid.CreateSpatialAdapter();
        DrawToolbar();
        DrawBuildingList();
        DrawSelectedBuildingControls();
        DrawPersistenceControls();

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
        }
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh 3D Preview"))
            {
                RefreshRecords();
            }

            if (GUILayout.Button("Frame Selected"))
            {
                FrameSelected();
            }
        }

        moveTool = EditorGUILayout.ToggleLeft(
            "Move selected building with snapped Scene View drag",
            moveTool);
        EditorGUILayout.HelpBox(
            "Click a building in the Scene View to select it. Enable Move, drag its footprint on the active floor, "
            + "then Commit Move or Cancel Move. Selection, camera framing, and preview rebuilds do not write a database.",
            MessageType.None);
    }

    private void DrawBuildingList()
    {
        EditorGUILayout.LabelField("Buildings", EditorStyles.boldLabel);
        if (records.Count == 0)
        {
            EditorGUILayout.HelpBox("No authored or generated buildings were found.", MessageType.Info);
            return;
        }

        foreach (var record in records)
        {
            var label = $"{record.BuildingInstanceId}: {record.FootprintSize.x} x {record.FootprintSize.y}, "
                + $"{record.StoryCount} floor{(record.StoryCount == 1 ? string.Empty : "s")} "
                + $"at ({record.AnchorCell.x}, {record.AnchorCell.y})";
            if (GUILayout.Button(label, "Button"))
            {
                SelectBuilding(record.BuildingInstanceId);
            }
        }
    }

    private void DrawSelectedBuildingControls()
    {
        if (!TryGetSelectedRecord(out var selectedRecord))
        {
            return;
        }

        activeFloor = EditorGUILayout.IntSlider(
            "Active Floor",
            Mathf.Clamp(activeFloor, 0, selectedRecord.StoryCount - 1),
            0,
            selectedRecord.StoryCount - 1);
        EditorGUILayout.LabelField(
            "Logical anchor",
            $"({selectedRecord.AnchorCell.x}, {selectedRecord.AnchorCell.y})");

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!hasPendingMove))
            {
                if (GUILayout.Button("Commit Move"))
                {
                    CommitPendingMove(selectedRecord);
                }

                if (GUILayout.Button("Cancel Move"))
                {
                    CancelPendingMove();
                }
            }
        }

        if (hasPendingMove)
        {
            EditorGUILayout.LabelField(
                "Preview anchor",
                $"({pendingAnchor.x}, {pendingAnchor.y})");
        }
    }

    private void DrawPersistenceControls()
    {
        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Selected Save", EditorStyles.boldLabel);
        selectedSavePath = EditorGUILayout.TextField("Database Path", selectedSavePath);
        EditorGUILayout.HelpBox(
            "Applying the authored layout to a save is explicit. Use an isolated path for experiments; this tool never "
            + "applies selection, camera, preview, or Undo operations to a database.",
            MessageType.Warning);

        using (new EditorGUI.DisabledScope(records.Count == 0 || string.IsNullOrWhiteSpace(selectedSavePath)))
        {
            if (GUILayout.Button("Apply Authored Topology to Selected Save"))
            {
                ApplyAuthoredTopologyToSave();
            }
        }
    }

    private void DuringSceneGui(SceneView sceneView)
    {
        if (creator is null || !creator || creator.gameObject.scene != SceneManager.GetActiveScene())
        {
            return;
        }

        if (creator.Grid is null || !creator.Grid)
        {
            return;
        }

        spatialAdapter = creator.Grid.CreateSpatialAdapter();
        RefreshRecords();
        DrawRecords();
        HandleSceneInput(sceneView);
    }

    private void DrawRecords()
    {
        foreach (var record in records)
        {
            for (var floorIndex = 0; floorIndex < record.StoryCount; floorIndex++)
            {
                var isSelected = record.BuildingInstanceId == selectedBuildingId;
                var isActiveFloor = isSelected && floorIndex == activeFloor;
                var isPending = isSelected && hasPendingMove;
                var displayAnchor = isPending ? pendingAnchor : record.AnchorCell;
                DrawRecord(record, displayAnchor, floorIndex, isActiveFloor, isPending);
            }
        }
    }

    private void DrawRecord(
        BuildingRecord record,
        Vector3Int anchor,
        int floorIndex,
        bool isActiveFloor,
        bool isPending)
    {
        var height = Mathf.Max(0.5f, creator.WallHeight);
        var elevation = floorIndex * height;
        var minimum = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                record.BuildingInstanceId,
                floorIndex,
                new Vector2(anchor.x, anchor.y)),
            elevation);
        var maximum = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                record.BuildingInstanceId,
                floorIndex,
                new Vector2(anchor.x + record.FootprintSize.x, anchor.y + record.FootprintSize.y)),
            elevation);
        var center = new Vector3(
            (minimum.x + maximum.x) * 0.5f,
            elevation + height * 0.5f,
            (minimum.z + maximum.z) * 0.5f);
        var size = new Vector3(
            Mathf.Abs(maximum.x - minimum.x),
            height,
            Mathf.Abs(maximum.z - minimum.z));
        var color = isPending
            ? new Color(0.2f, 0.9f, 1f, 1f)
            : isActiveFloor
                ? new Color(1f, 0.75f, 0.1f, 1f)
                : new Color(0.35f, 0.8f, 1f, 0.45f);
        Handles.color = color;
        Handles.DrawWireCube(center, size);

        if (isActiveFloor)
        {
            outlinePoints.Clear();
            outlinePoints.Add(new Vector3(minimum.x, elevation, minimum.z));
            outlinePoints.Add(new Vector3(maximum.x, elevation, minimum.z));
            outlinePoints.Add(new Vector3(maximum.x, elevation, maximum.z));
            outlinePoints.Add(new Vector3(minimum.x, elevation, maximum.z));
            Handles.DrawSolidRectangleWithOutline(
                outlinePoints.ToArray(),
                new Color(0.3f, 0.8f, 1f, 0.08f),
                color);
        }

        Handles.Label(
            center + Vector3.up * (height * 0.5f + 0.1f),
            $"Building {record.BuildingInstanceId} · Floor {floorIndex + 1}");
    }

    private void HandleSceneInput(SceneView sceneView)
    {
        var currentEvent = Event.current;
        if (currentEvent.type is EventType.MouseDown or EventType.MouseDrag or EventType.MouseUp)
        {
            if (currentEvent.alt || currentEvent.button != 0)
            {
                return;
            }

            if (!TryGetLogicalCell(currentEvent.mousePosition, out var cell))
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDown)
            {
                if (moveTool && TryGetSelectedRecord(out var selectedRecord)
                    && IsInsideRecord(cell, selectedRecord, selectedRecord.AnchorCell))
                {
                    dragging = true;
                    hasPendingMove = true;
                    dragOffset = new Vector2Int(
                        cell.x - selectedRecord.AnchorCell.x,
                        cell.y - selectedRecord.AnchorCell.y);
                    pendingAnchor = selectedRecord.AnchorCell;
                }
                else if (TryFindRecord(cell, out var pickedRecord))
                {
                    SelectBuilding(pickedRecord.BuildingInstanceId);
                }

                currentEvent.Use();
                SceneView.RepaintAll();
                return;
            }

            if (currentEvent.type == EventType.MouseDrag && dragging
                && TryGetSelectedRecord(out var draggingRecord))
            {
                pendingAnchor = new Vector3Int(
                    cell.x - dragOffset.x,
                    cell.y - dragOffset.y,
                    draggingRecord.AnchorCell.z);
                currentEvent.Use();
                SceneView.RepaintAll();
                return;
            }

            if (currentEvent.type == EventType.MouseUp && dragging)
            {
                dragging = false;
                currentEvent.Use();
                SceneView.RepaintAll();
            }
        }
    }

    private bool TryGetLogicalCell(Vector2 guiPosition, out Vector3Int cell)
    {
        cell = default;
        var ray = HandleUtility.GUIPointToWorldRay(guiPosition);
        var elevation = activeFloor * Mathf.Max(0.5f, creator.WallHeight);
        var plane = new Plane(Vector3.up, new Vector3(0f, elevation, 0f));
        if (!plane.Raycast(ray, out var distance))
        {
            return false;
        }

        var worldPosition = ray.GetPoint(distance);
        var logical = spatialAdapter.WorldToLogical3D(
            worldPosition,
            selectedBuildingId,
            activeFloor);
        cell = new Vector3Int(
            Mathf.FloorToInt(logical.FloorPosition.x),
            Mathf.FloorToInt(logical.FloorPosition.y),
            0);
        return true;
    }

    private bool TryFindRecord(Vector3Int cell, out BuildingRecord record)
    {
        foreach (var candidate in records)
        {
            if (IsInsideRecord(cell, candidate, candidate.AnchorCell))
            {
                record = candidate;
                return true;
            }
        }

        record = null!;
        return false;
    }

    private static bool IsInsideRecord(
        Vector3Int cell,
        BuildingRecord record,
        Vector3Int anchor)
    {
        return cell.x >= anchor.x
            && cell.x < anchor.x + record.FootprintSize.x
            && cell.y >= anchor.y
            && cell.y < anchor.y + record.FootprintSize.y;
    }

    private void CommitPendingMove(BuildingRecord selectedRecord)
    {
        if (!hasPendingMove || pendingAnchor == selectedRecord.AnchorCell)
        {
            statusMessage = "No changed snapped position is waiting to be committed.";
            return;
        }

        var updatedRecord = new BuildingRecord(
            selectedRecord.BuildingInstanceId,
            pendingAnchor,
            selectedRecord.FootprintSize,
            selectedRecord.StoryCount,
            selectedRecord.Doors);
        var candidateRecords = records
            .Where(record => record.BuildingInstanceId != selectedRecord.BuildingInstanceId)
            .Select(record => record.Clone())
            .Append(updatedRecord)
            .ToList();
        if (!BuildingShellValidation.TryValidateRecords(
                candidateRecords,
                creator.DoorCornerExclusionDistance,
                out var validationError))
        {
            statusMessage = validationError;
            return;
        }

        var layout = FindLayout(selectedRecord.BuildingInstanceId);
        if (layout is null || !layout)
        {
            statusMessage = "The selected building has no generated shell to rebuild.";
            return;
        }

        var authoredBefore = creator.HasAuthoredLayout
            ? creator.AuthoredLayout.CloneRecords()
            : null;
        var generatedBefore = selectedRecord.Clone();
        if (creator.HasAuthoredLayout)
        {
            Undo.RecordObject(creator.AuthoredLayout, "Move authored test building in 3D creator");
            if (!creator.AuthoredLayout.TryUpdateRecord(updatedRecord, out var assetError))
            {
                statusMessage = assetError;
                return;
            }
            EditorUtility.SetDirty(creator.AuthoredLayout);
        }
        else
        {
            Undo.RecordObject(layout, "Move test building in 3D creator");
            layout.ApplyBuildingRecord(updatedRecord);
            EditorUtility.SetDirty(layout);
        }

        if (!new BuildingShellAssembler().RebuildShell(updatedRecord, creator, layout.transform))
        {
            if (creator.HasAuthoredLayout && authoredBefore is not null)
            {
                creator.AuthoredLayout.ReplaceRecords(authoredBefore);
                EditorUtility.SetDirty(creator.AuthoredLayout);
            }
            else
            {
                layout.ApplyBuildingRecord(generatedBefore);
                EditorUtility.SetDirty(layout);
            }

            statusMessage = "The generated shell could not be rebuilt; the move was rolled back.";
            return;
        }

        EditorSceneManager.MarkSceneDirty(creator.gameObject.scene);
        records.Clear();
        records.AddRange(creator.GetAuthoredBuildingRecords());
        hasPendingMove = false;
        dragging = false;
        statusMessage = $"Moved building {updatedRecord.BuildingInstanceId} to "
            + $"({updatedRecord.AnchorCell.x}, {updatedRecord.AnchorCell.y}) in authored data.";
        SceneView.RepaintAll();
        Repaint();
    }

    private void ApplyAuthoredTopologyToSave()
    {
        if (!FactoryBuildingEditService.TryApplyTopologyToDatabase(
                selectedSavePath,
                creator.GetAuthoredBuildingRecords(),
                null,
                false,
                creator.DoorCornerExclusionDistance,
                out var result,
                out var error))
        {
            statusMessage = $"Topology apply failed: {error}";
            return;
        }

        statusMessage = result.Changed
            ? $"Applied authored topology to {result.DatabasePath}; preserved {result.PreservedEntityCount} entities."
            : "The selected save already matches authored topology.";
    }

    private void SelectBuilding(uint buildingInstanceId)
    {
        selectedBuildingId = buildingInstanceId;
        activeFloor = 0;
        hasPendingMove = false;
        dragging = false;
        var layout = FindLayout(buildingInstanceId);
        if (layout is not null && layout)
        {
            Selection.activeGameObject = layout.gameObject;
        }
        SceneView.RepaintAll();
        Repaint();
    }

    private void FrameSelected()
    {
        if (!TryGetSelectedRecord(out var record) || SceneView.lastActiveSceneView is null)
        {
            return;
        }

        var center = spatialAdapter.LogicalToWorld3D(
            new FactoryLogicalLocation(
                record.BuildingInstanceId,
                activeFloor,
                new Vector2(
                    record.AnchorCell.x + record.FootprintSize.x * 0.5f,
                    record.AnchorCell.y + record.FootprintSize.y * 0.5f)),
            activeFloor * Mathf.Max(0.5f, creator.WallHeight));
        SceneView.lastActiveSceneView.orthographic = true;
        SceneView.lastActiveSceneView.LookAt(
            center,
            Quaternion.Euler(45f, -45f, 0f),
            Mathf.Max(record.FootprintSize.x, record.FootprintSize.y) * 2f);
    }

    private void CancelPendingMove()
    {
        hasPendingMove = false;
        dragging = false;
        SceneView.RepaintAll();
        Repaint();
    }

    private bool TryGetSelectedRecord(out BuildingRecord record)
    {
        foreach (var candidate in records)
        {
            if (candidate.BuildingInstanceId == selectedBuildingId)
            {
                record = candidate;
                return true;
            }
        }

        record = null!;
        return false;
    }

    private TestBuildingLayout FindLayout(uint buildingInstanceId)
    {
        if (creator.GeneratedBuildings is null || !creator.GeneratedBuildings)
        {
            return null!;
        }

        foreach (var layout in creator.GeneratedBuildings.GetComponentsInChildren<TestBuildingLayout>(true))
        {
            if (layout.BuildingInstanceId == buildingInstanceId)
            {
                return layout;
            }
        }

        return null!;
    }

    private void ResolveCreator()
    {
        if (creator is not null && creator)
        {
            return;
        }

        var activeScene = SceneManager.GetActiveScene();
        foreach (var candidate in Object.FindObjectsByType<TestBuildingCreator>(FindObjectsSortMode.None))
        {
            if (candidate.gameObject.scene == activeScene)
            {
                creator = candidate;
                return;
            }
        }
    }

    private void RefreshRecords()
    {
        records.Clear();
        if (creator is null || !creator)
        {
            return;
        }

        records.AddRange(creator.GetAuthoredBuildingRecords());
        records.Sort((left, right) => left.BuildingInstanceId.CompareTo(right.BuildingInstanceId));
        if (selectedBuildingId != 0
            && !records.Any(record => record.BuildingInstanceId == selectedBuildingId))
        {
            selectedBuildingId = 0;
        }
    }

    private void SelectionChanged()
    {
        if (Selection.activeGameObject is null)
        {
            return;
        }

        var layout = Selection.activeGameObject.GetComponentInParent<TestBuildingLayout>();
        if (layout is not null && layout)
        {
            selectedBuildingId = layout.BuildingInstanceId;
            Repaint();
            SceneView.RepaintAll();
        }
    }

    private void UndoRedoPerformed()
    {
        hasPendingMove = false;
        dragging = false;
        RefreshRecords();
        SceneView.RepaintAll();
        Repaint();
    }
}
