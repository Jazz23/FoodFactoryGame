// PROTOTYPE: gives a goods location that has no machine (the dev storage) a place in the scene, so employees know where to
// walk to reach it. Presentation of an existing location only; it never creates, moves or owns goods.
using UnityEngine;

namespace FoodFactoryGame.Session.Employees
{
    [DisallowMultipleComponent]
    public sealed class SiteLocationMarker : MonoBehaviour
    {
        [SerializeField] private string locationId = DevWorld.StorageId;
        // Name scripts may use for the location besides its ID.
        [SerializeField] private string alias = "storage";
        // Footprint on the floor (metres, centred on the transform) that counts as the location's edge.
        [SerializeField] private Vector2 size = new(2f, 1f);

        public string LocationId => locationId;
        public string Alias => alias;

        public Rect Area
        {
            get
            {
                var center = transform.position;
                return new Rect(center.x - size.x * 0.5f, center.z - size.y * 0.5f, size.x, size.y);
            }
        }
    }
}
