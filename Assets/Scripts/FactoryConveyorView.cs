// Renders a bounded pool of item dots at the authoritative conveyor queue positions.
using System.Collections.Generic;
using UnityEngine;

public sealed class FactoryConveyorView : MonoBehaviour
{
    private readonly Transform[] dots = new Transform[FactoryConveyorQueue.Capacity];
    private Sprite dotSprite = null!;
    private Texture2D dotTexture = null!;
    private TextMesh arrow = null!;

    private void Awake()
    {
        var art = Resources.Load<FactoryConveyorArt>("FactoryConveyorArt");
        GetComponent<SpriteRenderer>().sprite = art.sprite;
        var animator = gameObject.AddComponent<Animator>();
        animator.runtimeAnimatorController = art.controller;
        dotTexture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
        var pixels = new Color[256];
        for (var y = 0; y < 16; y++)
            for (var x = 0; x < 16; x++)
                pixels[y * 16 + x] = Vector2.Distance(new Vector2(x, y), new Vector2(7.5f, 7.5f)) < 7f ? Color.white : Color.clear;
        dotTexture.SetPixels(pixels);
        dotTexture.Apply();
        dotSprite = Sprite.Create(dotTexture, new Rect(0, 0, 16, 16), Vector2.one * 0.5f, 100f);
        for (var index = 0; index < dots.Length; index++)
        {
            var dotObject = new GameObject(index == 0 ? "Item Dot" : $"Item Dot {index + 1}");
            dots[index] = dotObject.transform;
            dots[index].SetParent(transform, false);
            var renderer = dotObject.AddComponent<SpriteRenderer>();
            renderer.sprite = dotSprite;
            renderer.color = new Color(1f, 0.9f, 0.3f);
            renderer.sortingOrder = FactoryConveyor.ItemSortingOrder;
            dotObject.SetActive(false);
        }
        var arrowObject = new GameObject("Flow Arrow");
        arrowObject.transform.SetParent(transform, false);
        arrow = arrowObject.AddComponent<TextMesh>();
        arrow.text = "^";
        arrow.anchor = TextAnchor.MiddleCenter;
        arrow.fontSize = 32;
        arrow.characterSize = 0.08f;
        arrow.color = Color.white;
        arrow.GetComponent<MeshRenderer>().sortingOrder = FactoryConveyor.EquipmentSortingOrder + 1;
    }

    public void Apply(FactoryEntityRecord entity, SceneGrid grid, IReadOnlyList<FactoryEntityRecord> entities, float[] positions)
    {
        var center = grid.LogicalToWorld(entity.LogicalPosition);
        var delta = grid.LogicalToWorld(entity.LogicalPosition + FactoryConveyor.Direction(entity.DefinitionId)) - center;
        transform.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
        transform.localScale = new Vector3(delta.magnitude * 0.8f, delta.magnitude, 1f);
        for (var index = 0; index < dots.Length; index++)
        {
            dots[index].gameObject.SetActive(index < positions.Length);
            if (index >= positions.Length) continue;
            var itemPosition = grid.LogicalToWorld(FactoryConveyor.ItemPosition(entity, entities, positions[index]));
            dots[index].position = new Vector3(itemPosition.x, itemPosition.y, transform.position.z - 0.1f);
        }
        arrow.transform.localPosition = new Vector3(0.3f, 0, -0.05f);
    }

    private void OnDestroy()
    {
        Destroy(dotSprite);
        Destroy(dotTexture);
    }
}
