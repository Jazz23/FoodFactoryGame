// Presentation only: a soft footstep for every stride a player's figure takes on the ground, from the motion PlayerAnimation
// observes, so remote players are heard walking too. Steps follow the animated feet: a step lands when the two feet are
// furthest apart along the figure's facing (heel strike), alternating feet, so sound and animation keep the same cadence at
// any blend of walk and run. A rig without the Foot.L/Foot.R bones falls back to a stride length that grows with speed. No
// step plays while airborne or standing; a landing sounds only after real air time, so a ground probe flickering over a
// kerb or slope is not heard as repeated thumps.
using FoodFactoryGame.Session.Audio;
using UnityEngine;

namespace FoodFactoryGame.Session.Player
{
    [DisallowMultipleComponent]
    public sealed class FootstepAudio : MonoBehaviour
    {
        private const int StepClips = 6;
        private const float MinSpeed = 0.5f;
        private const float MinStepSeconds = 0.18f;
        // Feet closer than this along the facing (metres) at their widest are shuffling, not striding.
        private const float MinFootSeparation = 0.08f;
        private const float LandingAirSeconds = 0.3f;
        private const float HearingDistance = 14f;
        private const float StepVolume = 0.2f;
        private const float WalkStride = 0.6f;
        private const float StrideGrowth = 0.18f;

        private readonly AudioClip[] _clips = new AudioClip[StepClips];
        private PlayerAnimation _motion;
        private AudioSource _source;
        private Transform _leftFoot;
        private Transform _rightFoot;
        private float _travelled;
        private float _airborne;
        private float _lastStep;
        private float _lastSeparation;
        private bool _widening;
        private int _lastSide;
        private int _last = -1;
        private float _nextFeetSearch;

        private void Awake()
        {
            _motion = GetComponentInChildren<PlayerAnimation>();
            for (var i = 0; i < StepClips; i++) _clips[i] = GameAudio.Clip($"footstep_{i + 1}");
            _source = GameAudio.Spatial(gameObject, null, HearingDistance);
            _source.priority = 180;
            FindFeet();
        }

        private void FindFeet()
        {
            foreach (var bone in GetComponentsInChildren<Transform>(true))
            {
                if (bone.name == "Foot.L") _leftFoot = bone;
                else if (bone.name == "Foot.R") _rightFoot = bone;
            }
        }

        private void Update()
        {
            if (_motion == null) return;
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;
            var speed = _motion.GroundSpeed;
            if (!_motion.Grounded)
            {
                _airborne += deltaTime;
                _travelled = 0f;
                return;
            }
            if (_airborne >= LandingAirSeconds) Step(Mathf.Max(speed, 3f), 1.15f, 0.88f);
            _airborne = 0f;

            if (_leftFoot != null && _rightFoot != null) FollowFeet(speed);
            else
            {
                // The figure's model may be attached after this component; look for its feet again now and then.
                if (Time.time >= _nextFeetSearch)
                {
                    _nextFeetSearch = Time.time + 1f;
                    FindFeet();
                }
                FollowDistance(speed, deltaTime);
            }
        }

        private void FollowFeet(float speed)
        {
            var separation = transform.InverseTransformPoint(_leftFoot.position).z - transform.InverseTransformPoint(_rightFoot.position).z;
            var width = Mathf.Abs(separation);
            var widening = width > _lastSeparation;
            // The feet just stopped spreading: the leading foot has struck the ground.
            if (_widening && !widening && _lastSeparation >= MinFootSeparation && speed >= MinSpeed)
            {
                var side = separation > 0f ? 1 : -1;
                if (side != _lastSide && Time.time - _lastStep >= MinStepSeconds)
                {
                    _lastSide = side;
                    Step(speed, 1f, 1f);
                }
            }
            _widening = widening;
            _lastSeparation = width;
        }

        private void FollowDistance(float speed, float deltaTime)
        {
            if (speed < MinSpeed)
            {
                _travelled = 0f;
                return;
            }
            _travelled += speed * deltaTime;
            var stride = WalkStride + StrideGrowth * speed;
            if (_travelled < stride) return;
            _travelled -= stride;
            if (Time.time - _lastStep >= MinStepSeconds) Step(speed, 1f, 1f);
        }

        // Runs land a little harder than walks; every step varies in clip, level and pitch so a walk never sounds looped.
        private void Step(float speed, float loudness, float pitch)
        {
            _lastStep = Time.time;
            var pick = Random.Range(0, StepClips);
            if (pick == _last) pick = (pick + 1) % StepClips;
            _last = pick;
            if (_clips[pick] == null) return;
            var pace = Mathf.InverseLerp(1f, 7f, speed);
            _source.pitch = pitch * Random.Range(0.9f, 1.1f) * Mathf.Lerp(1f, 0.95f, pace);
            var volume = StepVolume * loudness * Mathf.Lerp(0.7f, 1f, pace) * Random.Range(0.8f, 1f);
            _source.PlayOneShot(_clips[pick], volume * GameAudio.Volume(AudioChannel.Effects));
        }
    }
}
