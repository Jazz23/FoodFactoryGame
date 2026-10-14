// Presentation-only sound for this client: loads the clips in Resources/Audio, plays 2D interface sounds and clicks for every
// UI Toolkit button on any panel, and hosts the AmbienceDirector (menu music and the area ambience beds). It keeps a fallback
// AudioListener enabled whenever no other listener is (the menu has no camera rig), so menu sounds are heard and two listeners
// are never enabled together. Nothing here reads or changes gameplay state, and a missing clip only makes that sound silent.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace FoodFactoryGame.Session.Audio
{
    // Volume groups the player sets apart in Settings; the master volume is the AudioListener's.
    public enum AudioChannel
    {
        // Machines, footsteps, vehicles and placement: positional sounds of the world.
        Effects,
        // Button clicks, the till and the phone.
        Interface,
        // The menu music and the area ambience beds (city, farmland, factory).
        Ambience
    }

    [DisallowMultipleComponent]
    public sealed class GameAudio : MonoBehaviour
    {
        public const string ClickClip = "ui_click";
        public const string OpenClip = "ui_open";
        public const string CashClip = "cash";
        public const string PlaceClip = "place";
        private const float PanelScanSeconds = 0.5f;
        private const float ListenerScanSeconds = 0.25f;

        private static readonly Dictionary<string, AudioClip> Clips = new();
        private static GameAudio _instance;

        private readonly HashSet<IPanel> _hookedPanels = new();
        private AudioSource _ui;
        private AudioListener _fallbackListener;
        private bool _otherListener;
        private bool _wasInWorld;
        private float _nextPanelScan;
        private float _nextListenerScan;

        private static readonly float[] Volumes = { 1f, 1f, 1f };

        // True while the local player's avatar is in the world; set by PlayerAvatar for its owner only.
        public static bool InWorld { get; set; }

        // The transform of the AudioListener this client hears through (the player's camera rig or the fallback); null when none.
        public static Transform Listener { get; private set; }

        // 0 (silent) to 1; sounds read it when they play, and loops every frame.
        public static float Volume(AudioChannel channel) => Volumes[(int)channel];

        public static void SetVolume(AudioChannel channel, float volume) => Volumes[(int)channel] = Mathf.Clamp01(volume);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (_instance != null) return;
            var root = new GameObject("Game Audio");
            DontDestroyOnLoad(root);
            _instance = root.AddComponent<GameAudio>();
        }

        // Null (silent) when the clip is not in Resources/Audio.
        public static AudioClip Clip(string name)
        {
            if (Clips.TryGetValue(name, out var clip)) return clip;
            clip = Resources.Load<AudioClip>("Audio/" + name);
            Clips[name] = clip;
            return clip;
        }

        public static void PlayUi(string clipName, float volume = 1f)
        {
            if (_instance == null || _instance._ui == null) return;
            var clip = Clip(clipName);
            if (clip != null) _instance._ui.PlayOneShot(clip, volume * Volume(AudioChannel.Interface));
        }

        // A one-off positional sound that does not follow anything.
        public static void PlayAt(string clipName, Vector3 position, float volume = 1f, float maxDistance = 25f, float pitch = 1f,
            AudioChannel channel = AudioChannel.Effects)
        {
            var clip = Clip(clipName);
            if (clip == null) return;
            var go = new GameObject("One-shot " + clipName);
            go.transform.position = position;
            var source = Spatial(go, clip, maxDistance);
            source.volume = volume * Volume(channel);
            source.pitch = pitch;
            source.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        // A 3D source on target, heard up to maxDistance metres away; looping sources do not start until played.
        public static AudioSource Spatial(GameObject target, AudioClip clip, float maxDistance, bool loop = false)
        {
            var source = target.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            return source;
        }

        private void Awake()
        {
            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ui.spatialBlend = 0f;
            // Enabled by UpdateListener only once no other listener is found, so two are never enabled together.
            _fallbackListener = gameObject.AddComponent<AudioListener>();
            _fallbackListener.enabled = false;
            gameObject.AddComponent<AmbienceDirector>();
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            UpdateListener();
            if (Time.unscaledTime < _nextPanelScan) return;
            _nextPanelScan = Time.unscaledTime + PanelScanSeconds;
            // Documents sharing PanelSettings share one panel; hook each panel's root once.
            foreach (var document in FindObjectsByType<UIDocument>())
            {
                var panel = document.rootVisualElement?.panel;
                if (panel == null || !_hookedPanels.Add(panel)) continue;
                panel.visualTree.RegisterCallback<ClickEvent>(OnClick, TrickleDown.TrickleDown);
            }
            _hookedPanels.RemoveWhere(x => x.visualTree?.panel == null);
        }

        // The player's camera rig enables its listener in the same call that sets InWorld, so a change of InWorld rescans at
        // once; the periodic scan covers a listener that appears or goes away on its own.
        private void UpdateListener()
        {
            if (InWorld != _wasInWorld || Time.unscaledTime >= _nextListenerScan)
            {
                _wasInWorld = InWorld;
                _nextListenerScan = Time.unscaledTime + ListenerScanSeconds;
                Transform other = null;
                foreach (var listener in FindObjectsByType<AudioListener>())
                    if (listener != _fallbackListener && listener.isActiveAndEnabled)
                    {
                        other = listener.transform;
                        break;
                    }
                _otherListener = other != null;
                Listener = other != null ? other : transform;
            }
            if (_fallbackListener.enabled == _otherListener) _fallbackListener.enabled = !_otherListener;
        }

        private static void OnClick(ClickEvent click)
        {
            if (click.target is VisualElement element && ButtonOf(element) is { } button && button.enabledInHierarchy)
                PlayUi(ClickClip, 0.6f);
        }

        private static Button ButtonOf(VisualElement element)
        {
            for (var current = element; current != null; current = current.parent)
                if (current is Button button) return button;
            return null;
        }
    }
}
