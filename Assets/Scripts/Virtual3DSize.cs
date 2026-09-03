// Defines the player's screen projection and ground footprint used for grid depth decisions.
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CapsuleCollider2D), typeof(SpriteRenderer))]
public sealed class Virtual3DSize : MonoBehaviour
{
    // The axes represent width, ground depth, and height respectively.
    [SerializeField] private Vector3 size = new(0.36f, 0.36f, 0.9f);
    [SerializeField] private bool synchronizeWithCollider = true;

    private CapsuleCollider2D bodyCollider;
    private SpriteRenderer spriteRenderer;

    public Vector3 Size => size;
    public float FrontY => bodyCollider is not null
        ? bodyCollider.bounds.min.y
        : transform.position.y - size.y * 0.5f;
    public float DepthY => FootprintBounds.center.y;
    public Bounds FootprintBounds => bodyCollider is not null
        ? bodyCollider.bounds
        : new Bounds(
            new Vector3(transform.position.x, FrontY + size.y * 0.5f, transform.position.z),
            new Vector3(size.x, size.y, 0.2f));

    public Bounds ProjectedBounds => new(
        spriteRenderer is not null ? spriteRenderer.bounds.center : transform.position,
        new Vector3(size.x, size.z, 0.2f));

    public void GetProjectedPolygon(List<Vector2> points)
    {
        points.Clear();
        foreach (var vertex in spriteRenderer.sprite.vertices)
        {
            var localPoint = new Vector3(vertex.x, vertex.y, 0f);
            if (spriteRenderer.flipX)
            {
                localPoint.x = -localPoint.x;
            }

            if (spriteRenderer.flipY)
            {
                localPoint.y = -localPoint.y;
            }

            var worldPoint = spriteRenderer.transform.TransformPoint(localPoint);
            points.Add(new Vector2(worldPoint.x, worldPoint.y));
        }
    }

    private void Awake()
    {
        bodyCollider = GetComponent<CapsuleCollider2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        SynchronizeWithCollider();
    }

    private void LateUpdate()
    {
        if (synchronizeWithCollider)
        {
            SynchronizeWithCollider();
        }
    }

    private void SynchronizeWithCollider()
    {
        Vector2 footprintSize = bodyCollider.bounds.size;
        float visibleHeight = spriteRenderer.bounds.size.y;
        size = new Vector3(footprintSize.x, footprintSize.y, visibleHeight);
    }
}
