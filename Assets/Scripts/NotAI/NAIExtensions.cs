using UnityEngine;

namespace DefaultNamespace
{
    public static class NAIExtensions
    {
        public static BoundsInt GetBoundsFromCenter(Vector3Int center, int size)
        {
            var halfSize = size / 2;
            var min = new Vector3Int(center.x - halfSize, center.y - halfSize, 0);
            var max = new Vector3Int(center.x + halfSize, center.y + halfSize, 0);
            return new BoundsInt(min, max - min);
        }
    }
}