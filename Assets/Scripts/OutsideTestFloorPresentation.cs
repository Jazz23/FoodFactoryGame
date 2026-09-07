// Presents one OutsideTest floor record with a reusable marker inside the loaded interior scene.
using UnityEngine;

[DisallowMultipleComponent]
public sealed class OutsideTestFloorPresentation : MonoBehaviour
{
    private const float MarkerSize = 0.35f;
    private const float LabelHeight = 0.55f;

    private Transform marker = null!;
    private TextMesh markerLabel = null!;
    private Sprite markerSprite = null!;
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
        }

        markerLabel.text = $"{state.Label}\n{state.AccumulatedProduction:0.0}";
    }
}
