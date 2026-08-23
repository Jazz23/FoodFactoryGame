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
    public float FrontY => bodyCollider != null
        ? bodyCollider.bounds.min.y
        : transform.position.y - size.y * 0.5f;
    public Bounds FootprintBounds => bodyCollider != null
        ? bodyCollider.bounds
        : new Bounds(
            new Vector3(transform.position.x, FrontY + size.y * 0.5f, transform.position.z),
            new Vector3(size.x, size.y, 0.2f));

    public Bounds ProjectedBounds => new(
        spriteRenderer != null ? spriteRenderer.bounds.center : transform.position,
        new Vector3(size.x, size.z, 0.2f));

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
