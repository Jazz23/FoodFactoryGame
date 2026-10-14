// Presentation only: a vehicle's positional engine loop whose pitch and level follow how fast its drawn model moves, so
// trucks and city cars idle when stopped and rev as they drive. A jump larger than a car could drive in a frame (pooling,
// snapping to a new pose) counts as a teleport, not speed.
using FoodFactoryGame.Session.Audio;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    [DisallowMultipleComponent]
    public sealed class VehicleEngineAudio : MonoBehaviour
    {
        private const float TeleportDistance = 4f;
        private const float TopSpeed = 14f;
        private const float Smoothing = 4f;

        private AudioSource _source;
        private float _volume;
        private float _basePitch;
        private Vector3 _last;
        private bool _hasLast;
        private float _speed;

        public static void AddTo(GameObject vehicle, float volume, float pitch, float hearingDistance)
        {
            var clip = GameAudio.Clip("engine");
            if (clip == null) return;
            var engine = vehicle.AddComponent<VehicleEngineAudio>();
            engine._volume = volume;
            engine._basePitch = pitch;
            engine._source = GameAudio.Spatial(vehicle, clip, hearingDistance, loop: true);
        }

        private void OnEnable()
        {
            _hasLast = false;
            _speed = 0f;
        }

        private void Update()
        {
            if (_source == null || Time.deltaTime <= 0f) return;
            if (!_source.isPlaying)
            {
                // Start somewhere in the loop so a row of cars does not run in lockstep.
                _source.time = Random.Range(0f, _source.clip.length);
                _source.Play();
            }
            var position = transform.position;
            var moved = _hasLast ? (position - _last).magnitude : 0f;
            _last = position;
            _hasLast = true;
            if (moved < TeleportDistance)
                _speed = Mathf.Lerp(_speed, moved / Time.deltaTime, 1f - Mathf.Exp(-Smoothing * Time.deltaTime));
            var load = Mathf.Clamp01(_speed / TopSpeed);
            _source.pitch = _basePitch * Mathf.Lerp(0.8f, 1.5f, load);
            _source.volume = _volume * Mathf.Lerp(0.45f, 1f, load) * GameAudio.Volume(AudioChannel.Effects);
        }
    }
}
