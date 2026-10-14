// Presentation only: the solid body of a drawn vehicle (a city car or a truck). A kinematic box around its model on the
// Ignore Raycast layer, so the local player's CharacterController cannot walk through it while camera, ground and pointer
// raycasts (DefaultRaycastLayers) pass it by, and its velocity from how its drawn model moved, so VehicleBump can shove the
// player aside when the vehicle drives into them. Added at runtime; the vehicle prefabs themselves stay collider-free. The
// body never touches the simulation.
using System.Collections.Generic;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    // Runs before VehicleBump (+50) so the velocity it reads is this frame's.
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class VehicleBody : MonoBehaviour
    {
        // A move longer than this in one frame (pooling, snapping to a new pose) is a teleport, not speed.
        private const float TeleportDistance = 5f;
        private const string Layer = "Ignore Raycast";

        private static readonly List<VehicleBody> Live = new();

        private BoxCollider _box;
        private Vector3 _last;
        private bool _hasLast;

        // Every enabled vehicle body in the scene.
        public static IReadOnlyList<VehicleBody> All => Live;

        public BoxCollider Box => _box;
        // Half the body's length along the model's local Z (its direction of travel), in metres.
        public float HalfLength => _box.size.z * 0.5f * transform.lossyScale.z;
        public Vector3 Velocity { get; private set; }

        // Gives a vehicle model its body: a box around every mesh under it, measured in the vehicle's own space.
        public static VehicleBody AddTo(GameObject vehicle)
        {
            var root = vehicle.transform;
            var bounds = new Bounds(new Vector3(0f, 0.8f, 0f), new Vector3(1.8f, 1.6f, 4.4f));
            var any = false;
            foreach (var filter in vehicle.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var meshBounds = mesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                    var corner = meshBounds.center + Vector3.Scale(meshBounds.extents, sign);
                    var local = root.InverseTransformPoint(filter.transform.TransformPoint(corner));
                    if (!any) bounds = new Bounds(local, Vector3.zero);
                    else bounds.Encapsulate(local);
                    any = true;
                }
            }
            var box = vehicle.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            var rigidbody = vehicle.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;
            var layer = LayerMask.NameToLayer(Layer);
            if (layer >= 0) vehicle.layer = layer;
            var body = vehicle.AddComponent<VehicleBody>();
            body._box = box;
            return body;
        }

        private void OnEnable()
        {
            if (!Live.Contains(this)) Live.Add(this);
            _hasLast = false;
            Velocity = Vector3.zero;
        }

        private void OnDisable() => Live.Remove(this);

        private void LateUpdate()
        {
            var position = transform.position;
            var dt = Time.deltaTime;
            if (_hasLast && dt > 0f)
            {
                var moved = position - _last;
                Velocity = moved.magnitude < TeleportDistance ? moved / dt : Vector3.zero;
            }
            _last = position;
            _hasLast = true;
        }
    }
}
