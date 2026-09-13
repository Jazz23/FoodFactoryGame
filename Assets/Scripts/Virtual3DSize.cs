// Defines the player's screen projection and ground footprint used for grid depth decisions.
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CapsuleCollider2D), typeof(SpriteRenderer))]
public sealed class Virtual3DSize : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float width = 0.6f;
    [SerializeField, Min(0.01f)] private float groundDepth = 0.3f;
    [SerializeField] private bool synchronizeWithCollider = true;

    private CapsuleCollider2D bodyCollider;
    private SpriteRenderer spriteRenderer;

    public Vector3 Size => new(width, groundDepth, VisibleHeight);
    public float Width => width;
    // Bottom-pivot sprites share this stable foot position, independent of animation bounds.
    public Vector2 GroundAnchor => transform.position;
    public float FrontY => GroundAnchor.y;
    public float DepthY => GroundAnchor.y;
    public Bounds FootprintBounds => new(
        new Vector3(GroundAnchor.x, GroundAnchor.y + groundDepth * 0.5f, transform.position.z),
        new Vector3(width, groundDepth, 0.2f));

    public Bounds ProjectedBounds => new(
        spriteRenderer is not null ? spriteRenderer.bounds.center : transform.position,
        new Vector3(width, VisibleHeight, 0.2f));

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

    public void SetGroundAnchor(Vector2 position)
    {
        var delta = position - GroundAnchor;
        transform.position += new Vector3(delta.x, delta.y, 0f);
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
        var localFootprint = transform.InverseTransformVector(
            new Vector3(width, groundDepth, 0f));
        bodyCollider.direction = CapsuleDirection2D.Horizontal;
        bodyCollider.size = new Vector2(
            Mathf.Abs(localFootprint.x),
            Mathf.Abs(localFootprint.y));

        bodyCollider.offset = new Vector2(0f, Mathf.Abs(localFootprint.y) * 0.5f);
    }

    private float VisibleHeight => spriteRenderer is not null
        && spriteRenderer.sprite is not null
        ? spriteRenderer.bounds.size.y
        : bodyCollider is not null
            ? bodyCollider.bounds.size.y
            : width;
}
