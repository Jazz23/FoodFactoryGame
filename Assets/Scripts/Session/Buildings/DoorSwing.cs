// Presentation only: swings a restaurant door leaf (RestaurantShellModel) open while a DoorOpener stands near its doorway and
// closes it again when nobody is. It opens away from whoever came first, so the leaf never swings into them. While the leaf is
// nearly closed it carries a solid box, so walking into a closed door bumps into it; once it swings past ClosedDegrees the box is
// off and the doorway is clear. The simulation never reads it.
using UnityEngine;

namespace FoodFactoryGame.Session.Buildings
{
    [DisallowMultipleComponent]
    public sealed class DoorSwing : MonoBehaviour
    {
        private const float OpenDegrees = 95f;
        private const float DegreesPerSecond = 300f;
        // Below this angle the leaf counts as closed and blocks.
        private const float ClosedDegrees = 35f;
        // How near (metres, on the floor plane) an opener must come to the doorway's centre.
        private const float Reach = 1.8f;

        private Vector3 _doorway;
        private Quaternion _closed;
        private float _angle;
        private float _target;
        private BoxCollider _box;
        private bool _serviceOnly;

        public float Angle => _angle;
        public bool Blocking => _box != null && _box.enabled;
        public Vector3 Doorway => _doorway;

        // doorway: the centre of the opening (scene space). The leaf's current local rotation is its closed pose. A back door
        // (serviceOnly, decision 0037) never opens for a customer.
        public void Init(Vector3 doorway, bool serviceOnly = false)
        {
            _doorway = doorway;
            _serviceOnly = serviceOnly;
            _closed = transform.localRotation;
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            var bounds = new Bounds(transform.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
            foreach (var renderer in renderers)
            {
                var world = renderer.bounds;
                for (var corner = 0; corner < 8; corner++)
                    bounds.Encapsulate(transform.InverseTransformPoint(new Vector3(
                        (corner & 1) == 0 ? world.min.x : world.max.x, (corner & 2) == 0 ? world.min.y : world.max.y, (corner & 4) == 0 ? world.min.z : world.max.z)));
            }
            _box = gameObject.AddComponent<BoxCollider>();
            _box.center = bounds.center;
            // At least 10 cm thick so a fast avatar does not slip through.
            _box.size = new Vector3(bounds.size.x, bounds.size.y, Mathf.Max(0.1f, bounds.size.z));
        }

        private void Update()
        {
            var opener = DoorOpener.Nearest(_doorway, Reach, !_serviceOnly);
            if (opener == null) _target = 0f;
            else if (Mathf.Approximately(_target, 0f)) _target = AwayFrom(opener.transform.position) * OpenDegrees;
            _angle = Mathf.MoveTowards(_angle, _target, DegreesPerSecond * Time.deltaTime);
            transform.localRotation = _closed * Quaternion.Euler(0f, _angle, 0f);
            if (_box != null) _box.enabled = Mathf.Abs(_angle) < ClosedDegrees;
        }

        // +1 or -1: the swing whose free end ends up further from the opener. The leaf runs from its hinge toward local -X.
        private float AwayFrom(Vector3 opener)
        {
            var parent = transform.parent;
            var hinge = transform.localPosition;
            var width = _box != null ? Mathf.Abs(_box.center.x) * 2f : 1f;
            Vector3 FreeEnd(float sign)
            {
                var local = hinge + _closed * Quaternion.Euler(0f, sign * OpenDegrees, 0f) * (Vector3.left * width);
                return parent != null ? parent.TransformPoint(local) : local;
            }
            return (FreeEnd(1f) - opener).sqrMagnitude >= (FreeEnd(-1f) - opener).sqrMagnitude ? 1f : -1f;
        }
    }
}
