// PROTOTYPE employee: the server steers a NavMeshAgent toward the nearest player and NetworkTransform replicates the pose,
// so clients never path-find and camera or visibility cannot move it. Every peer drives the walk animation from observed
// movement. Runtime obstacles (placed machines, building walls) carve the baked NavMesh and are avoided by re-pathing.
using System.Linq;
using FishNet.Object;
using FoodFactoryGame.Session.Player;
using UnityEngine;
using UnityEngine.AI;

namespace FoodFactoryGame.Session.Employees
{
    [DisallowMultipleComponent]
    public sealed class EmployeeFollower : NetworkBehaviour
    {
        private static readonly int SpeedParameter = Animator.StringToHash("Speed");
        private static readonly int WalkRateParameter = Animator.StringToHash("WalkRate");

        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private Animator animator;
        [SerializeField] private float followDistance = 2f;
        [SerializeField] private float repathSeconds = 0.25f;
        // Ground speed at which the walk clip's feet do not slide; the clip is sped up or slowed to match actual speed.
        [SerializeField] private float walkClipSpeed = 1.5f;

        private Vector3 _lastPosition;
        private float _speed;
        private float _nextRepath;

        private void Awake()
        {
            // Only the server steers; enabled in OnStartServer.
            agent.enabled = false;
            _lastPosition = transform.position;
        }

        public override void OnStartServer()
        {
            agent.enabled = true;
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(transform.position, out var hit, 2f, NavMesh.AllAreas))
                agent.Warp(hit.position);
            agent.stoppingDistance = followDistance;
        }

        public override void OnStopServer() => agent.enabled = false;

        private void Update()
        {
            if (IsServerStarted && agent.enabled && agent.isOnNavMesh && Time.time >= _nextRepath)
            {
                _nextRepath = Time.time + repathSeconds;
                var target = NearestPlayer();
                if (target != null) agent.SetDestination(target.position);
                else if (agent.hasPath) agent.ResetPath();
            }
            Animate();
        }

        private Transform NearestPlayer()
        {
            var position = transform.position;
            return ServerManager.Objects.Spawned.Values
                .Select(x => x.GetComponent<PlayerAvatar>())
                .Where(x => x != null)
                .Select(x => x.transform)
                .OrderBy(x => (x.position - position).sqrMagnitude)
                .FirstOrDefault();
        }

        // Observed planar speed, so remote copies moved by NetworkTransform animate exactly like the server's.
        private void Animate()
        {
            var delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            delta.y = 0f;
            var measured = Time.deltaTime > 0f ? delta.magnitude / Time.deltaTime : 0f;
            _speed = Mathf.Lerp(_speed, measured, 1f - Mathf.Exp(-10f * Time.deltaTime));
            animator.SetFloat(SpeedParameter, _speed);
            animator.SetFloat(WalkRateParameter, Mathf.Max(0.5f, _speed / walkClipSpeed));
        }
    }
}
