// Presents shared dock inventories on OutsideTest at their exterior approach cells.
using System.Collections.Generic;
using NotAI;
using UnityEngine;

public sealed class FactoryDockExteriorView : MonoBehaviour
{
    private readonly Dictionary<string, DockView> views = new();
    private readonly List<string> staleKeys = new();
    private Sprite markerSprite = null!;

    private void Update()
    {
        if (NAIStateManager.Instance is not { IsInitialized: true } manager
            || !SceneGrid.TryGetForScene(gameObject.scene, out var grid))
        {
            ClearViews();
            return;
        }

        var active = new HashSet<string>();
        foreach (var building in manager.BuildingRecords)
        {
            if (!manager.TryGetFloorState(building.BuildingInstanceId, 0, out var floor))
            {
                continue;
            }

            foreach (var entity in floor.Entities)
            {
                if (!entity.IsDock
                    || !FactoryDock.TryGetExteriorApproachCell(
                        building,
                        entity.LogicalPosition,
                        entity.DockDirection,
                        out var exteriorCell))
                {
                    continue;
                }

                var key = $"{building.BuildingInstanceId}/{entity.EntityId}";
                active.Add(key);
                var view = GetView(key);
                var world = grid.CellCenterWorld(exteriorCell);
                view.Object.transform.position = new Vector3(world.x, world.y, -0.3f);
                view.Object.transform.rotation = Quaternion.Euler(
                    0f,
                    0f,
                    FactoryDock.DirectionToRotation(entity.DockDirection));
                view.Renderer.color = entity.IsShippingDock
                    ? new Color(0.25f, 0.95f, 0.55f, 1f)
                    : new Color(0.35f, 0.65f, 1f, 1f);
                view.Label.transform.position = view.Object.transform.position + Vector3.up * 0.42f;
                view.Label.text = entity.IsShippingDock
                    ? $"Shipping Dock\n{entity.InventoryCount}/{entity.InventoryCapacity}"
                    : $"Receiving Dock\n{entity.InventoryCount}/{entity.InventoryCapacity}";
            }
        }

        staleKeys.Clear();
        foreach (var pair in views)
        {
            if (!active.Contains(pair.Key))
            {
                staleKeys.Add(pair.Key);
            }
        }

        foreach (var key in staleKeys)
        {
            Destroy(views[key].Object);
            views.Remove(key);
        }
    }

    private DockView GetView(string key)
    {
        if (views.TryGetValue(key, out var existing) && existing.Object)
        {
            return existing;
        }

        markerSprite ??= Sprite.Create(
            Texture2D.whiteTexture,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);
        var dockObject = new GameObject($"Factory Dock {key}");
        dockObject.transform.SetParent(transform, false);
        dockObject.transform.localScale = new Vector3(0.7f, 0.35f, 1f);
        var renderer = dockObject.AddComponent<SpriteRenderer>();
        renderer.sprite = markerSprite;
        renderer.sortingOrder = FactoryConveyor.EquipmentSortingOrder;

        var labelObject = new GameObject("Dock Label");
        labelObject.transform.SetParent(dockObject.transform, false);
        var label = labelObject.AddComponent<TextMesh>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.anchor = TextAnchor.MiddleCenter;
        label.fontSize = 24;
        label.characterSize = 0.045f;
        label.GetComponent<MeshRenderer>().sortingOrder = FactoryConveyor.ItemSortingOrder;
        var view = new DockView(dockObject, renderer, label);
        views[key] = view;
        return view;
    }

    private void ClearViews()
    {
        foreach (var view in views.Values)
        {
            if (view.Object)
            {
                Destroy(view.Object);
            }
        }

        views.Clear();
    }

    private void OnDestroy()
    {
        ClearViews();
        if (markerSprite is not null)
        {
            Destroy(markerSprite);
        }
    }

    private sealed class DockView
    {
        public DockView(GameObject newObject, SpriteRenderer newRenderer, TextMesh newLabel)
        {
            Object = newObject;
            Renderer = newRenderer;
            Label = newLabel;
        }

        public GameObject Object { get; }
        public SpriteRenderer Renderer { get; }
        public TextMesh Label { get; }
    }
}
