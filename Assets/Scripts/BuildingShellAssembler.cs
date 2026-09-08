// Builds and rebuilds the visual, collision, and door shell for an OutsideTest building.
using System.Collections.Generic;
using UnityEngine;

public sealed class BuildingShellAssembler
{
    private readonly List<TestBuildingCreator.WallPlacement> wallPlacements = new();
    private readonly List<TestBuildingCreator.ExteriorWallSpan> wallSpans = new();
    private BuildingRecord record = null!;
    private TestBuildingCreator creator = null!;
    private Transform targetParent = null!;

    public BuildingShellAssembler()
    {
    }

    public BuildingShellAssembler(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform newTargetParent)
    {
        record = newRecord;
        creator = newCreator;
        targetParent = newTargetParent;
    }

    public GameObject CreateShell()
    {
        return CreateShell(record, creator, targetParent);
    }

    public bool RebuildShell()
    {
        return RebuildShell(record, creator, targetParent);
    }

    public GameObject Create(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform newTargetParent)
    {
        return CreateShell(newRecord, newCreator, newTargetParent);
    }

    public GameObject CreateShell(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform newTargetParent)
    {
        if (!TryValidateInputs(
                newRecord,
                newCreator,
                newTargetParent,
                out var error))
        {
            Debug.LogError(error, newCreator);
            return null!;
        }

        var buildingObject = new GameObject(GetBuildingName(newRecord));
        buildingObject.transform.SetParent(newTargetParent, false);
        var layout = buildingObject.AddComponent<TestBuildingLayout>();
        layout.ApplyBuildingRecord(newRecord);
        EnsureGeneratedHierarchy(layout, out _, out _, out _);
        var presentation = buildingObject.GetComponent<TestBuildingPresentation>();
        RebuildShell(newRecord, newCreator, buildingObject.transform, true);
        presentation.RefreshRenderers();
        return buildingObject;
    }

    public bool Rebuild(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform shell)
    {
        return RebuildShell(newRecord, newCreator, shell);
    }

    public bool RebuildShell(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        GameObject shell)
    {
        return shell is not null
            && RebuildShell(newRecord, newCreator, shell.transform);
    }

    public bool RebuildShell(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform shell)
    {
        return RebuildShell(newRecord, newCreator, shell, false);
    }

    public static bool NeedsRebuild(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform shell)
    {
        if (!TryValidateInputs(
                newRecord,
                newCreator,
                shell,
                out _)
            || shell.GetComponent<TestBuildingLayout>() is not TestBuildingLayout layout)
        {
            return true;
        }

        var assembler = new BuildingShellAssembler();
        return assembler.NeedsRebuildInternal(newRecord, newCreator, shell, layout);
    }

    public static bool HasInteriorTemplate(BuildingRecord newRecord)
    {
        return newRecord is not null
            && newRecord.BuildingInstanceId != 0
            && Application.CanStreamedLevelBeLoaded(TestBuildingFloorScenes.TemplateSceneName);
    }

    private bool RebuildShell(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform shell,
        bool force)
    {
        if (!TryValidateInputs(
                newRecord,
                newCreator,
                shell,
                out var error))
        {
            Debug.LogError(error, newCreator);
            return false;
        }

        var layout = shell.GetComponent<TestBuildingLayout>();
        if (layout is null || !layout)
        {
            layout = shell.gameObject.AddComponent<TestBuildingLayout>();
        }

        var topologyChanged = !layout.ExportBuildingRecord().HasSameTopology(newRecord);
        layout.ApplyBuildingRecord(newRecord);
        var hierarchyChanged = EnsureGeneratedHierarchy(
            layout,
            out var generatedVisuals,
            out var generatedCollision,
            out var visualDoors);
        if (!force
            && !topologyChanged
            && !hierarchyChanged
            && !NeedsRebuildInternal(
                newRecord,
                newCreator,
                shell,
                layout,
                generatedVisuals,
                generatedCollision,
                visualDoors))
        {
            return false;
        }

        var secondCorner = newRecord.AnchorCell + new Vector3Int(
            newRecord.FootprintSize.x - 1,
            newRecord.FootprintSize.y - 1);
        TestBuildingCreator.GetWallPlacements(
            newRecord.AnchorCell,
            secondCorner,
            wallPlacements);
        ClearChildren(generatedVisuals);
        ClearChildren(generatedCollision);
        ClearChildren(visualDoors);

        for (var storyIndex = 0; storyIndex < newRecord.StoryCount; storyIndex++)
        {
            foreach (var placement in wallPlacements)
            {
                CreateWall(generatedVisuals, placement, storyIndex, newCreator);
            }
        }

        for (var storyIndex = 0; storyIndex < newRecord.StoryCount; storyIndex++)
        {
            CreateRoof(
                generatedVisuals,
                newRecord.AnchorCell,
                newRecord.FootprintSize,
                storyIndex,
                newCreator);
        }

        RebuildCollision(
            newRecord,
            newCreator,
            generatedCollision,
            wallPlacements);
        RebuildDoors(
            newRecord,
            newCreator,
            layout,
            visualDoors);
        var presentation = shell.GetComponent<TestBuildingPresentation>();
        if (presentation is not null && presentation)
        {
            presentation.RefreshRenderers();
        }

        return true;
    }

    private bool NeedsRebuildInternal(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform shell,
        TestBuildingLayout layout)
    {
        var generatedVisuals = shell.Find(TestBuildingLayout.GeneratedVisualsName);
        var generatedCollision = shell.Find(TestBuildingLayout.GeneratedCollisionName);
        var visualDoors = shell.Find(TestBuildingLayout.VisualDoorsName);
        return generatedVisuals is null
            || generatedCollision is null
            || visualDoors is null
            || NeedsRebuildInternal(
                newRecord,
                newCreator,
                shell,
                layout,
                generatedVisuals,
                generatedCollision,
                visualDoors);
    }

    private bool NeedsRebuildInternal(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform shell,
        TestBuildingLayout layout,
        Transform generatedVisuals,
        Transform generatedCollision,
        Transform visualDoors)
    {
        var secondCorner = newRecord.AnchorCell + new Vector3Int(
            newRecord.FootprintSize.x - 1,
            newRecord.FootprintSize.y - 1);
        TestBuildingCreator.GetWallPlacements(
            newRecord.AnchorCell,
            secondCorner,
            wallPlacements);
        var walls = generatedVisuals.GetComponentsInChildren<GridWall>(true);
        var expectedWallCount = wallPlacements.Count * newRecord.StoryCount;
        if (walls.Length != expectedWallCount)
        {
            return true;
        }

        for (var index = 0; index < walls.Length; index++)
        {
            var storyIndex = index / wallPlacements.Count;
            var placement = wallPlacements[index % wallPlacements.Count];
            if (walls[index].Kind != placement.Kind
                || walls[index].Cell != placement.Cell
                || walls[index].StoryIndex != storyIndex
                || !Mathf.Approximately(
                    walls[index].BaseHeight,
                    TestBuildingCreator.GetStoryBaseHeight(
                        newCreator.WallHeight,
                        storyIndex))
                || !Mathf.Approximately(walls[index].WallHeight, newCreator.WallHeight)
                || walls[index].Material != newCreator.Material)
            {
                return true;
            }
        }

        var roofs = generatedVisuals.GetComponentsInChildren<GridRoof>(true);
        if (roofs.Length != newRecord.StoryCount)
        {
            return true;
        }

        var desiredLogicalMin = TestBuildingCreator.GetRoofLogicalMin(
            newRecord.AnchorCell,
            secondCorner);
        var desiredLogicalMax = TestBuildingCreator.GetRoofLogicalMax(
            newRecord.AnchorCell,
            secondCorner);
        for (var storyIndex = 0; storyIndex < roofs.Length; storyIndex++)
        {
            var roof = roofs[storyIndex];
            if (roof.LogicalMin != desiredLogicalMin
                || roof.LogicalMax != desiredLogicalMax
                || !Mathf.Approximately(
                    roof.BaseHeight,
                    TestBuildingCreator.GetStoryBaseHeight(
                        newCreator.WallHeight,
                        storyIndex))
                || !Mathf.Approximately(
                    roof.TopHeight,
                    TestBuildingCreator.GetStoryTopHeight(
                        newCreator.WallHeight,
                        storyIndex))
                || !Mathf.Approximately(roof.Thickness, newCreator.RoofThickness)
                || roof.TopColor != newCreator.RoofTopColor
                || roof.SideColor != newCreator.RoofSideColor
                || roof.Material != newCreator.Material
                || roof.SortingOrder != newCreator.RoofSortingOrder)
            {
                return true;
            }
        }

        if (generatedCollision.childCount != wallPlacements.Count
            || DoorNeedsRebuild(newRecord, newCreator, layout, visualDoors))
        {
            return true;
        }

        return false;
    }

    private bool DoorNeedsRebuild(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        TestBuildingLayout layout,
        Transform visualDoors)
    {
        if (visualDoors.childCount != newRecord.Doors.Count)
        {
            return true;
        }

        layout.GetExteriorWallSpans(wallSpans);
        for (var index = 0; index < newRecord.Doors.Count; index++)
        {
            var door = newRecord.Doors[index];
            if (door is null
                || !layout.TryGetDoor(wallSpans, door.WallId, out var wall)
                || wall.IsCorner)
            {
                return true;
            }

            var doorObject = visualDoors.GetChild(index);
            var renderer = doorObject.GetComponent<SpriteRenderer>();
            var depthSurface = doorObject.GetComponent<DepthOcclusionSurface>();
            var portal = doorObject.GetComponent<ScenePortal>();
            var factoryDoor = doorObject.GetComponent<OutsideTestFactoryDoor>();
            if (renderer is null
                || depthSurface is null
                || !depthSurface.IsConfigured
                || portal is null
                || factoryDoor is null
                || !factoryDoor.Matches(door.WallId, door.NormalizedOffset)
                || renderer.sprite != newCreator.VisualStyle.EntranceSprite
                || renderer.flipX != newCreator.VisualStyle.ShouldFlipEntranceX(wall.Direction)
                || portal.enabled != HasInteriorTemplate(newRecord))
            {
                return true;
            }
        }

        return false;
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
            visualDoors = CreateGeneratedRoot(
                layout.transform,
                TestBuildingLayout.VisualDoorsName);
            hierarchyChanged = true;
        }

        if (!layout.TryGetComponent<TestBuildingPresentation>(out _))
        {
            layout.gameObject.AddComponent<TestBuildingPresentation>();
            hierarchyChanged = true;
        }

        for (var index = layout.transform.childCount - 1; index >= 0; index--)
        {
            var child = layout.transform.GetChild(index);
            if (child == generatedVisuals
                || child == generatedCollision
                || child == visualDoors
                || (child.GetComponent<GridWall>() is null
                    && child.GetComponent<GridRoof>() is null))
            {
                continue;
            }

            child.SetParent(generatedVisuals, true);
            hierarchyChanged = true;
        }

        return hierarchyChanged;
    }

    private static Transform CreateGeneratedRoot(Transform parent, string rootName)
    {
        var rootObject = new GameObject(rootName);
        rootObject.transform.SetParent(parent, false);
        return rootObject.transform;
    }

    private void CreateWall(
        Transform parent,
        TestBuildingCreator.WallPlacement placement,
        int storyIndex,
        TestBuildingCreator newCreator)
    {
        var wallObject = new GameObject(
            $"Story {storyIndex} Wall {placement.Kind} ({placement.Cell.x},{placement.Cell.y})");
        wallObject.transform.SetParent(parent, false);
        var wall = wallObject.AddComponent<GridWall>();
        wall.Configure(
            placement.Kind,
            placement.Cell,
            newCreator.WallHeight,
            TestBuildingCreator.GetStoryBaseHeight(
                newCreator.WallHeight,
                storyIndex),
            storyIndex,
            newCreator.Material);
    }

    private void CreateRoof(
        Transform parent,
        Vector3Int anchorCell,
        Vector2Int size,
        int storyIndex,
        TestBuildingCreator newCreator)
    {
        var roofObject = new GameObject($"Grid Floor Ceiling {storyIndex}");
        roofObject.transform.SetParent(parent, false);
        var roof = roofObject.AddComponent<GridRoof>();
        var secondCorner = anchorCell + new Vector3Int(size.x - 1, size.y - 1);
        roof.Configure(
            TestBuildingCreator.GetRoofLogicalMin(anchorCell, secondCorner),
            TestBuildingCreator.GetRoofLogicalMax(anchorCell, secondCorner),
            TestBuildingCreator.GetStoryBaseHeight(
                newCreator.WallHeight,
                storyIndex),
            TestBuildingCreator.GetStoryTopHeight(
                newCreator.WallHeight,
                storyIndex),
            newCreator.RoofThickness,
            newCreator.RoofTopColor,
            newCreator.RoofSideColor,
            newCreator.Material,
            newCreator.RoofSortingOrder);
    }

    private static void RebuildCollision(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform generatedCollision,
        IReadOnlyList<TestBuildingCreator.WallPlacement> placements)
    {
        foreach (var placement in placements)
        {
            var collisionObject = new GameObject(
                $"Wall Collision {placement.Kind} ({placement.Cell.x},{placement.Cell.y})");
            collisionObject.transform.SetParent(generatedCollision, false);
            var collider = collisionObject.AddComponent<PolygonCollider2D>();
            var logicalFootprint = GridWall.GetLogicalFootprint(
                placement.Kind,
                placement.Cell);
            var points = new Vector2[logicalFootprint.Count];
            for (var index = 0; index < logicalFootprint.Count; index++)
            {
                var worldPoint = newCreator.Grid.LogicalToWorld(logicalFootprint[index]);
                points[index] = generatedCollision.InverseTransformPoint(worldPoint);
            }

            collider.pathCount = 1;
            collider.SetPath(0, points);
        }
    }

    private void RebuildDoors(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        TestBuildingLayout layout,
        Transform visualDoors)
    {
        if (newRecord.Doors.Count == 0
            || newCreator.VisualStyle is null
            || !newCreator.VisualStyle)
        {
            return;
        }

        layout.GetExteriorWallSpans(wallSpans);
        foreach (var door in newRecord.Doors)
        {
            if (door is null
                || !layout.TryGetDoor(wallSpans, door.WallId, out var wall)
                || wall.IsCorner)
            {
                continue;
            }

            CreateDoorVisual(
                newRecord,
                newCreator,
                visualDoors,
                wall,
                door.NormalizedOffset);
        }
    }

    private static void CreateDoorVisual(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Transform visualDoors,
        TestBuildingCreator.ExteriorWallSpan wall,
        float normalizedOffset)
    {
        var doorObject = new GameObject(
            $"Door {wall.StableId} {normalizedOffset:0.###}");
        doorObject.SetActive(false);
        doorObject.transform.SetParent(visualDoors, false);
        var logicalPosition = Vector2.Lerp(
            wall.LogicalStart,
            wall.LogicalEnd,
            normalizedOffset);
        var worldPosition = newCreator.Grid.LogicalToWorld(logicalPosition);
        doorObject.transform.position = new Vector3(
            worldPosition.x,
            worldPosition.y,
            visualDoors.position.z);

        var renderer = doorObject.AddComponent<SpriteRenderer>();
        renderer.sprite = newCreator.VisualStyle.EntranceSprite;
        renderer.color = newCreator.VisualStyle.EntranceColor;
        renderer.flipX = newCreator.VisualStyle.ShouldFlipEntranceX(wall.Direction);
        renderer.sortingOrder = GetDoorSortingOrder(wall);
        renderer.transform.localScale = Vector3.one * (
            newCreator.VisualStyle.EntranceHeight / GetVisibleSpriteHeight(renderer.sprite));

        var logicalFootprint = GridWall.GetLogicalFootprint(wall.Kind, wall.Cell);
        var groundPolygon = new List<Vector3>(logicalFootprint.Count);
        foreach (var logicalPoint in logicalFootprint)
        {
            var groundPoint = newCreator.Grid.LogicalToWorld(logicalPoint);
            groundPolygon.Add(new Vector3(
                groundPoint.x,
                groundPoint.y,
                visualDoors.position.z));
        }

        var depthSurface = doorObject.AddComponent<DepthOcclusionSurface>();
        depthSurface.Configure(
            GetVisibleSpritePolygon(renderer),
            groundPolygon,
            new Vector3(worldPosition.x, worldPosition.y, visualDoors.position.z),
            wall.LogicalStart,
            wall.LogicalEnd);

        var portal = doorObject.AddComponent<ScenePortal>();
        var factoryDoor = doorObject.AddComponent<OutsideTestFactoryDoor>();
        factoryDoor.Configure(
            wall.StableId,
            normalizedOffset,
            HasInteriorTemplate(newRecord));
        factoryDoor.Initialize();
        portal.enabled = HasInteriorTemplate(newRecord);
        doorObject.SetActive(true);
    }

    private static void ClearChildren(Transform parent)
    {
        for (var index = parent.childCount - 1; index >= 0; index--)
        {
            Object.DestroyImmediate(parent.GetChild(index).gameObject);
        }
    }

    private static bool TryValidateInputs(
        BuildingRecord newRecord,
        TestBuildingCreator newCreator,
        Object target,
        out string error)
    {
        error = string.Empty;
        if (newCreator is null || !newCreator)
        {
            error = "A TestBuildingCreator is required to assemble a shell.";
            return false;
        }

        if (target is null || !target)
        {
            error = "A shell target is required to assemble a building.";
            return false;
        }

        return BuildingShellValidation.TryValidate(
            newRecord,
            System.Array.Empty<BuildingRecord>(),
            newCreator.DoorCornerExclusionDistance,
            out error);
    }

    private static string GetBuildingName(BuildingRecord newRecord)
    {
        return $"Test Building ({newRecord.AnchorCell.x}, {newRecord.AnchorCell.y}) "
            + $"{newRecord.FootprintSize.x}x{newRecord.FootprintSize.y}";
    }

    private static int GetDoorSortingOrder(
        TestBuildingCreator.ExteriorWallSpan wall)
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
}
