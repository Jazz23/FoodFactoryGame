// Presentation only: a vehicle's positional engine-and-tyre loop whose pitch and level follow how fast its drawn model moves,
// so trucks and city cars idle when stopped, rev as they drive and swell and fade as they pass. Only the MaxAudible vehicles
// nearest this client's listener (and within their hearing distance) have a playing source; the rest fade out and stop, so a
// busy city costs a handful of voices. Pitch rises while a vehicle closes on the listener and drops as it leaves (a mild,
// clamped Doppler computed here, because Unity's own Doppler would spike on pooled cars snapping to a new pose). A jump larger
// than a car could drive in a frame (pooling, snapping to a new pose) counts as a teleport, not speed.
using System.Collections.Generic;
using FoodFactoryGame.Session.Audio;
using UnityEngine;

namespace FoodFactoryGame.Session.Logistics
{
    [DisallowMultipleComponent]
    public sealed class VehicleEngineAudio : MonoBehaviour
    {
        private const string EngineClip = "engine";
        private const float TeleportDistance = 4f;
        private const float TopSpeed = 14f;
        private const float Smoothing = 4f;
        private const int MaxAudible = 10;
        private const float SelectSeconds = 0.2f;
        // Gain per second when a vehicle joins or leaves the audible set, so voices never start or stop with a click.
        private const float FadeRate = 2.5f;
        // Below this hearing distance (metres) a passing car would be heard only once it is nearly on top of the listener.
        private const float MinHearingDistance = 30f;
        private const float DopplerPerSpeed = 0.008f;
        private const float MaxDoppler = 0.1f;

        private static readonly List<VehicleEngineAudio> Engines = new();
        private static readonly VehicleEngineAudio[] Nearest = new VehicleEngineAudio[MaxAudible];
        private static readonly float[] NearestDistance = new float[MaxAudible];
        private static float _nextSelect;
        // Level against distance as a fraction of the hearing distance: close cars are loud, the far half is a faint hush.
        private static readonly AnimationCurve Rolloff = new(
            new Keyframe(0f, 1f), new Keyframe(0.08f, 0.75f), new Keyframe(0.2f, 0.4f), new Keyframe(0.45f, 0.13f),
            new Keyframe(0.75f, 0.035f), new Keyframe(1f, 0f));

        private AudioSource _source;
        private float _volume = 0.25f;
        private float _basePitch = 1f;
        private float _hearingDistance = 40f;
        private Vector3 _last;
        private bool _hasLast;
        private Vector3 _velocity;
        private float _gain;
        private bool _audible;

        // Adds an engine to a vehicle's drawn model; volume and pitch scale the shared loop, hearingDistance is in metres.
        public static VehicleEngineAudio AddTo(GameObject vehicle, float volume, float pitch, float hearingDistance)
        {
            var engine = vehicle.AddComponent<VehicleEngineAudio>();
            engine._volume = volume;
            engine._basePitch = pitch;
            engine._hearingDistance = Mathf.Max(hearingDistance, MinHearingDistance);
            return engine;
        }

        private void OnEnable()
        {
            _hasLast = false;
            _velocity = Vector3.zero;
            _gain = 0f;
            _audible = false;
            Engines.Add(this);
        }

        private void OnDisable()
        {
            Engines.Remove(this);
            _audible = false;
            _gain = 0f;
            if (_source != null) _source.Stop();
        }

        private void Update()
        {
            var deltaTime = Time.deltaTime;
            if (deltaTime <= 0f) return;
            var position = transform.position;
            if (_hasLast && (position - _last).sqrMagnitude < TeleportDistance * TeleportDistance)
                _velocity = Vector3.Lerp(_velocity, (position - _last) / deltaTime, 1f - Mathf.Exp(-Smoothing * deltaTime));
            else
                _velocity = Vector3.zero;
            _last = position;
            _hasLast = true;

            // The second test covers a clock that restarted under a stale schedule (play mode without a domain reload).
            if (Time.unscaledTime >= _nextSelect || _nextSelect - Time.unscaledTime > SelectSeconds) Select();
            _gain = Mathf.MoveTowards(_gain, _audible ? 1f : 0f, FadeRate * deltaTime);
            if (_gain <= 0f)
            {
                if (_source != null && _source.isPlaying) _source.Stop();
                return;
            }
            if (!EnsureSource()) return;
            if (!_source.isPlaying)
            {
                // Start somewhere in the loop so a row of cars does not run in lockstep.
                _source.time = Random.Range(0f, _source.clip.length);
                _source.Play();
            }
            var load = Mathf.Clamp01(new Vector2(_velocity.x, _velocity.z).magnitude / TopSpeed);
            _source.pitch = _basePitch * Mathf.Lerp(0.85f, 1.3f, load) * Doppler(position);
            _source.volume = _volume * Mathf.Lerp(0.35f, 1f, load) * _gain * GameAudio.Volume(AudioChannel.Effects);
        }

        // Above 1 while closing on the listener, below 1 while leaving it.
        private float Doppler(Vector3 position)
        {
            var listener = GameAudio.Listener;
            if (listener == null) return 1f;
            var toListener = listener.position - position;
            var distance = toListener.magnitude;
            if (distance < 0.01f) return 1f;
            var closing = Vector3.Dot(_velocity, toListener / distance);
            return 1f + Mathf.Clamp(closing * DopplerPerSpeed, -MaxDoppler, MaxDoppler);
        }

        private bool EnsureSource()
        {
            if (_source != null) return true;
            var clip = GameAudio.Clip(EngineClip);
            if (clip == null) return false;
            _source = GameAudio.Spatial(gameObject, clip, _hearingDistance, loop: true);
            _source.rolloffMode = AudioRolloffMode.Custom;
            _source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, Rolloff);
            // Soften hard left/right panning as a car passes close by.
            _source.spread = 60f;
            _source.priority = 200;
            _source.volume = 0f;
            return true;
        }

        // Shared by every engine, run by whichever updates first once per SelectSeconds: the MaxAudible nearest engines within
        // their own hearing distance of the listener are audible, every other one fades out.
        private static void Select()
        {
            _nextSelect = Time.unscaledTime + SelectSeconds;
            var count = 0;
            var listener = GameAudio.Listener;
            if (listener != null)
            {
                var at = listener.position;
                foreach (var engine in Engines)
                {
                    var distance = (engine.transform.position - at).sqrMagnitude;
                    if (distance > engine._hearingDistance * engine._hearingDistance) continue;
                    if (count == MaxAudible && distance >= NearestDistance[count - 1]) continue;
                    // Insertion into the short sorted list of nearest engines.
                    var slot = count < MaxAudible ? count++ : count - 1;
                    while (slot > 0 && NearestDistance[slot - 1] > distance)
                    {
                        Nearest[slot] = Nearest[slot - 1];
                        NearestDistance[slot] = NearestDistance[slot - 1];
                        slot--;
                    }
                    Nearest[slot] = engine;
                    NearestDistance[slot] = distance;
                }
            }
            foreach (var engine in Engines) engine._audible = false;
            for (var i = 0; i < count; i++) Nearest[i]._audible = true;
            System.Array.Clear(Nearest, 0, MaxAudible);
        }
    }
}
