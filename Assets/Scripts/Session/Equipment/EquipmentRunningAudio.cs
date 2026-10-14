// Presentation only: a machine's positional working loop (sizzle, hum, motor) that fades in while EquipmentVisual shows it
// running and out when it stops. Like every running display it follows the replicated baseline and never decides state.
using FoodFactoryGame.Session.Audio;
using UnityEngine;

namespace FoodFactoryGame.Session.Equipment
{
    [DisallowMultipleComponent]
    public sealed class EquipmentRunningAudio : MonoBehaviour, IEquipmentRunningDisplay
    {
        private const float FadeRate = 1.5f;
        private const float HearingDistance = 14f;

        private AudioSource _source;
        private float _volume;
        private bool _running;

        // The loop for a machine kind, or null for kinds that make no working sound.
        public static string LoopFor(string kind) => kind switch
        {
            "fryer" or "grill" or "stove" => "sizzle",
            "oven" => "hum",
            "mixer" => "mixer",
            _ => null
        };

        public static void AddTo(GameObject machine, string kind, float volume = 0.5f)
        {
            var name = LoopFor(kind);
            var clip = name == null ? null : GameAudio.Clip(name);
            if (clip == null) return;
            var audio = machine.AddComponent<EquipmentRunningAudio>();
            audio._volume = volume;
            audio._source = GameAudio.Spatial(machine, clip, HearingDistance, loop: true);
            audio._source.volume = 0f;
            // Neighbouring machines of one kind should not phase in lockstep.
            audio._source.pitch = Random.Range(0.95f, 1.05f);
        }

        public void SetRunning(bool running) => _running = running;

        private void Update()
        {
            if (_source == null) return;
            var target = _running ? _volume * GameAudio.Volume(AudioChannel.Effects) : 0f;
            _source.volume = Mathf.MoveTowards(_source.volume, target, FadeRate * _volume * Time.deltaTime);
            if (_source.volume > 0f && !_source.isPlaying)
            {
                _source.time = Random.Range(0f, _source.clip.length);
                _source.Play();
            }
            else if (_source.volume <= 0f && _source.isPlaying) _source.Stop();
        }
    }
}
