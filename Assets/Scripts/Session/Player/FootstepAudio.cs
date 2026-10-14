// Presentation only: plays a footstep for every stride a player's figure takes on the ground, from the motion PlayerAnimation
// observes, so remote players are heard walking too. Stride length grows with speed, so running steps come faster but not
// twice as fast.
using FoodFactoryGame.Session.Audio;
using UnityEngine;

namespace FoodFactoryGame.Session.Player
{
    [DisallowMultipleComponent]
    public sealed class FootstepAudio : MonoBehaviour
    {
        private const int StepClips = 4;
        private const float MinSpeed = 0.6f;
        private const float WalkStride = 0.7f;
        private const float StrideGrowth = 0.12f;
        private const float HearingDistance = 18f;

        private readonly AudioClip[] _clips = new AudioClip[StepClips];
        private PlayerAnimation _motion;
        private AudioSource _source;
        private float _travelled;
        private bool _wasGrounded = true;
        private int _last = -1;

        private void Awake()
        {
            _motion = GetComponentInChildren<PlayerAnimation>();
            for (var i = 0; i < StepClips; i++) _clips[i] = GameAudio.Clip($"footstep_{i + 1}");
            _source = GameAudio.Spatial(gameObject, null, HearingDistance);
            _source.volume = 0.45f;
        }

        private void Update()
        {
            if (_motion == null) return;
            var grounded = _motion.Grounded;
            var speed = _motion.GroundSpeed;
            // Landing from a jump or fall is one step.
            if (grounded && !_wasGrounded) Step(1.2f);
            _wasGrounded = grounded;
            if (!grounded || speed < MinSpeed)
            {
                _travelled = 0f;
                return;
            }
            _travelled += speed * Time.deltaTime;
            var stride = WalkStride + StrideGrowth * speed;
            if (_travelled < stride) return;
            _travelled -= stride;
            Step(Mathf.Lerp(0.8f, 1.1f, Mathf.InverseLerp(2f, 7f, speed)));
        }

        private void Step(float volume)
        {
            var pick = Random.Range(0, StepClips);
            if (pick == _last) pick = (pick + 1) % StepClips;
            _last = pick;
            if (_clips[pick] == null) return;
            _source.pitch = Random.Range(0.92f, 1.08f);
            _source.PlayOneShot(_clips[pick], volume * GameAudio.Volume(AudioChannel.Effects));
        }
    }
}
