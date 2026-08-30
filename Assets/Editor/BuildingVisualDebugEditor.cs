// Draws selected-building occupancy, wall footprints, collision, and sorting diagnostics.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BuildingVisualDebugEditor
{
    private const string EnabledPreference = "FoodFactory.BuildingVisualDebug.Enabled";

    [MenuItem("Food Factory/Building Debug Overlay")]
    private static void ToggleOverlay()
    {
        EditorPrefs.SetBool(EnabledPreference, !IsEnabled());
        Menu.SetChecked("Food Factory/Building Debug Overlay", IsEnabled());
        SceneView.RepaintAll();
    }

    [MenuItem("Food Factory/Building Debug Overlay", true)]
    private static bool ValidateToggleOverlay()
    {
        Menu.SetChecked("Food Factory/Building Debug Overlay", IsEnabled());
        return true;
    }

    [DrawGizmo(GizmoType.Selected | GizmoType.Active)]
    private static void DrawBuildingDebug(
        BuildingVisualView visualView,
        GizmoType _)
    {
        if (!IsEnabled())
        {
            return;
        }

        var definition = visualView.Definition;
        var instance = visualView.Instance;
        if ((definition is null || !definition)
            && visualView.TryGetComponent<PreplacedBuilding>(out var preplacedBuilding))
        {
            definition = preplacedBuilding.Definition;
            instance = new BuildingInstance(
                preplacedBuilding.InstanceId,
                definition.Id,
                preplacedBuilding.AnchorCell,
                preplacedBuilding.Size,
                -1,
                preplacedBuilding.Direction,
                preplacedBuilding.WallShape);
        }

        if (definition is null || !definition)
        {
            return;
        }

        Handles.Label(
            visualView.transform.position,
            $"{definition.Id} anchor "
            + $"({instance.AnchorCell.x}, {instance.AnchorCell.y}) "
            + $"size {instance.Size.x}x{instance.Size.y}");

        foreach (var segment in visualView.GetComponentsInChildren<CenteredWallSegmentRenderer>(true))
        {
            DrawCenteredSegment(segment);
        }

        foreach (var segment in visualView.GetComponentsInChildren<DirectionalWallSegmentRenderer>(true))
        {
            DrawSegment(segment);
        }

        var modularView = visualView.ModularView;
        if (modularView is not null
            && modularView
            && !Mathf.Approximately(
                modularView.Style.WallHeight,
                modularView.Style.RoofHeight))
        {
            Handles.color = new Color(1f, 0.65f, 0.1f);
            Handles.Label(
                visualView.transform.position + Vector3.up * modularView.Style.RoofHeight,
                $"Wall {modularView.Style.WallHeight:0.###} != roof {modularView.Style.RoofHeight:0.###}");
        }
    }

    private static void DrawCenteredSegment(CenteredWallSegmentRenderer segment)
    {
        var color = segment.IsDegenerate ? Color.red : new Color(0.15f, 0.9f, 1f);
        var cellBoundary = CloseLoop(segment.GetWorldCellBoundary());
        var footprint = CloseLoop(segment.GetWorldFootprint());

        Handles.color = new Color(color.r, color.g, color.b, 0.45f);
        Handles.DrawAAPolyLine(2f, cellBoundary);
        Handles.color = color;
        Handles.DrawAAPolyLine(4f, footprint);
        Handles.DrawWireCube(segment.WorldBounds.center, segment.WorldBounds.size);
        Handles.SphereHandleCap(
            0,
            segment.WorldCenter,
            Quaternion.identity,
            HandleUtility.GetHandleSize(segment.WorldCenter) * 0.06f,
            EventType.Repaint);

        if (segment.TryGetComponent<PolygonCollider2D>(out var collider) && collider.enabled)
        {
            var colliderPoints = new Vector3[collider.GetTotalPointCount() + 1];
            var path = collider.GetPath(0);
            for (var index = 0; index < path.Length; index++)
            {
                colliderPoints[index] = collider.transform.TransformPoint(path[index]);
            }

            colliderPoints[^1] = colliderPoints[0];
            Handles.color = new Color(1f, 0.25f, 0.85f);
            Handles.DrawAAPolyLine(2f, colliderPoints);
        }

        var warning = segment.IsDegenerate ? " DEGENERATE" : string.Empty;
        Handles.Label(
            segment.WorldCenter + Vector3.up * segment.WallHeight,
            $"{segment.Shape} cell {segment.AnchorCell.x},{segment.AnchorCell.y} "
            + $"thickness {segment.ThicknessInCells:0.##} order {segment.SortingOrder}{warning}");
    }

    private static void DrawSegment(DirectionalWallSegmentRenderer segment)
    {
        var color = segment.IsDegenerate
            ? Color.red
            : GetDirectionColor(segment.Direction);
        var start = segment.WorldStart;
        var end = segment.WorldEnd;
        var topStart = start + Vector3.up * segment.WallHeight;
        var topEnd = end + Vector3.up * segment.WallHeight;
        var lipStart = start + Vector3.up * segment.LipBottomHeight;
        var midpoint = (start + end) * 0.5f;
        var outward = GetOutwardDirection(segment.Direction, end - start);
        var arrowLength = HandleUtility.GetHandleSize(midpoint) * 0.35f;
        var footprint = CloseLoop(segment.GetWorldFootprint());

        Handles.color = new Color(color.r, color.g, color.b, 0.65f);
        Handles.DrawAAPolyLine(4f, footprint);
        Handles.color = color;
        Handles.DrawAAPolyLine(3f, start, end);
        Handles.DrawAAPolyLine(2f, topStart, topEnd);
        Handles.DrawAAPolyLine(2f, start, topStart);
        Handles.DrawAAPolyLine(2f, lipStart - Vector3.right * 0.04f, lipStart + Vector3.right * 0.04f);
        Handles.DrawWireCube(segment.WorldBounds.center, segment.WorldBounds.size);
        Handles.DrawAAPolyLine(2f, midpoint, midpoint + outward * arrowLength);
        Handles.ConeHandleCap(
            0,
            midpoint + outward * arrowLength,
            Quaternion.LookRotation(Vector3.forward, outward),
            arrowLength * 0.25f,
            EventType.Repaint);

        var warning = segment.IsDegenerate ? " DEGENERATE" : string.Empty;
        Handles.Label(
            topStart,
            $"{segment.Direction} {segment.Edge.Corner.x},{segment.Edge.Corner.y} -> "
            + $"{segment.Edge.EndCorner.x},{segment.Edge.EndCorner.y} "
            + $"thickness {segment.ThicknessInCells:0.##} order {segment.SortingOrder}{warning}");
    }

    private static Vector3 GetOutwardDirection(
        GridEdgeDirection direction,
        Vector3 edgeDelta)
    {
        var rightNormal = new Vector3(edgeDelta.y, -edgeDelta.x).normalized;
        return direction is GridEdgeDirection.South or GridEdgeDirection.East
            ? rightNormal
            : -rightNormal;
    }

    private static Color GetDirectionColor(GridEdgeDirection direction)
    {
        return direction switch
        {
            GridEdgeDirection.South => new Color(0.2f, 0.8f, 1f),
            GridEdgeDirection.East => new Color(1f, 0.75f, 0.15f),
            GridEdgeDirection.North => new Color(0.45f, 1f, 0.35f),
            GridEdgeDirection.West => new Color(0.85f, 0.4f, 1f),
            _ => Color.white
        };
    }

    private static Vector3[] CloseLoop(IReadOnlyList<Vector3> points)
    {
        var closedPoints = new Vector3[points.Count + 1];
        for (var index = 0; index < points.Count; index++)
        {
            closedPoints[index] = points[index];
        }

        closedPoints[^1] = points[0];
        return closedPoints;
    }

    private static bool IsEnabled()
    {
        return EditorPrefs.GetBool(EnabledPreference, true);
    }
}
