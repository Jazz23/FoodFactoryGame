// Draws selected-building edge metadata, bounds, sorting, and height diagnostics in Scene view.
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
        if (!IsEnabled() || visualView.Definition is null || !visualView.Definition)
        {
            return;
        }

        Handles.Label(
            visualView.transform.position,
            $"{visualView.Definition.Id} anchor "
            + $"({visualView.Instance.AnchorCell.x}, {visualView.Instance.AnchorCell.y}) "
            + $"size {visualView.Instance.Size.x}x{visualView.Instance.Size.y}");

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
            + $"order {segment.SortingOrder}{warning}");
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

    private static bool IsEnabled()
    {
        return EditorPrefs.GetBool(EnabledPreference, true);
    }
}
