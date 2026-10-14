// Presentation only: the 2D background sound of this client. Menu music plays while no session runs (SessionRoot.Mode is None)
// and fades out when one starts. While the local player is in the world, the city, farmland and factory beds crossfade by
// where that player stands on the shown layout (AudioZones), with occasional positional incidentals: car horns in town, birds
// over farmland, clanks by a factory. It only reads session state, the shown layout and the player's position; it never
// writes gameplay state, and nothing in the simulation depends on whether it plays.
using FoodFactoryGame.Session.Player;
using FoodFactoryGame.Session.WorldMap;
using UnityEngine;

namespace FoodFactoryGame.Session.Audio
{
    [DisallowMultipleComponent]
    public sealed class AmbienceDirector : MonoBehaviour
    {
        private const string MusicClip = "menu_music";
        private const float MusicLevel = 0.4f;
        // Volume change per second: music swells in over ~2.5 s and leaves over ~1.5 s once a session starts.
        private const float MusicFadeIn = MusicLevel / 2.5f;
        private const float MusicFadeOut = MusicLevel / 1.5f;
        // Bed weights move this much per second, so walking across an area edge crossfades over a couple of seconds.
        private const float MixRate = 0.5f;
        private const float BedFadeRate = 0.4f;
        private const float LookupSeconds = 0.5f;
        private const float MixSeconds = 0.25f;

        private sealed class Bed
        {
            public AudioSource Source;
            public float Level;
            public float Weight;
        }

        private sealed class Incidental
        {
            public string[] Clips;
            public float MinGap;
            public float MaxGap;
            public float MinDistance;
            public float MaxDistance;
            public float Volume;
            public float Next;
        }

        private readonly Incidental _honks = new()
        {
            Clips = new[] { "honk_1", "honk_2", "honk_3" }, MinGap = 12f, MaxGap = 35f, MinDistance = 20f, MaxDistance = 50f, Volume = 0.5f
        };
        private readonly Incidental _birds = new()
        {
            Clips = new[] { "bird_1", "bird_2", "bird_3" }, MinGap = 4f, MaxGap = 12f, MinDistance = 8f, MaxDistance = 30f, Volume = 0.35f
        };
        private readonly Incidental _clanks = new()
        {
            Clips = new[] { "clank_1", "clank_2", "clank_3" }, MinGap = 3f, MaxGap = 9f, MinDistance = 8f, MaxDistance = 25f, Volume = 0.4f
        };

        private AudioSource _music;
        private Bed _city;
        private Bed _farm;
        private Bed _factory;
        private SessionRoot _session;
        private WorldLayoutPresenter _presenter;
        private Transform _player;
        private AudioZones _zones;
        private AmbienceMix _target = AmbienceMix.CityOnly;
        private float _nextLookup;
        private float _nextMix;
        private bool _wasInWorld;

        private void Awake()
        {
            _music = Source(GameAudio.Clip(MusicClip));
            _city = new Bed { Source = Source(GameAudio.Clip("city")), Level = 0.32f };
            _farm = new Bed { Source = Source(GameAudio.Clip("farm")), Level = 0.36f };
            _factory = new Bed { Source = Source(GameAudio.Clip("factory")), Level = 0.3f };
            var now = Time.unscaledTime;
            foreach (var incidental in new[] { _honks, _birds, _clanks }) incidental.Next = now + Random.Range(incidental.MinGap, incidental.MaxGap);
        }

        private AudioSource Source(AudioClip clip)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.volume = 0f;
            source.clip = clip;
            return source;
        }

        private void Update()
        {
            var now = Time.unscaledTime;
            var delta = Time.unscaledDeltaTime;
            if (now >= _nextLookup || GameAudio.InWorld != _wasInWorld)
            {
                _nextLookup = now + LookupSeconds;
                Lookup();
            }

            var menu = _session != null && !_session.IsRunning;
            var ambience = GameAudio.Volume(AudioChannel.Ambience);
            var musicTarget = menu ? MusicLevel * ambience : 0f;
            _music.volume = Mathf.MoveTowards(_music.volume, musicTarget, (musicTarget > _music.volume ? MusicFadeIn : MusicFadeOut) * delta);
            if (_music.clip != null && _music.volume > 0f && !_music.isPlaying)
            {
                _music.time = 0f;
                _music.Play();
            }
            else if (_music.volume <= 0f && _music.isPlaying) _music.Stop();

            var inWorld = GameAudio.InWorld;
            if (inWorld && (now >= _nextMix || !_wasInWorld))
            {
                _nextMix = now + MixSeconds;
                _target = Mix();
                // Arriving in the world starts on the area's own beds rather than sliding over from the last area.
                if (!_wasInWorld) (_city.Weight, _farm.Weight, _factory.Weight) = (_target.City, _target.Farm, _target.Factory);
            }
            _wasInWorld = inWorld;
            _city.Weight = Mathf.MoveTowards(_city.Weight, _target.City, MixRate * delta);
            _farm.Weight = Mathf.MoveTowards(_farm.Weight, _target.Farm, MixRate * delta);
            _factory.Weight = Mathf.MoveTowards(_factory.Weight, _target.Factory, MixRate * delta);
            Play(_city, inWorld, ambience, delta);
            Play(_farm, inWorld, ambience, delta);
            Play(_factory, inWorld, ambience, delta);

            if (!inWorld || _player == null) return;
            Incidentals(_honks, _city.Weight, now);
            Incidentals(_birds, _farm.Weight, now);
            Incidentals(_clanks, _factory.Weight, now);
        }

        private static void Play(Bed bed, bool inWorld, float ambience, float delta)
        {
            var source = bed.Source;
            var target = inWorld ? bed.Level * bed.Weight * ambience : 0f;
            source.volume = Mathf.MoveTowards(source.volume, target, BedFadeRate * delta);
            if (source.clip != null && source.volume > 0f && !source.isPlaying)
            {
                // Start somewhere in the loop so a fresh session does not always open on the same car or bird.
                source.time = Random.Range(0f, source.clip.length);
                source.Play();
            }
            else if (source.volume <= 0f && source.isPlaying) source.Stop();
        }

        // The session, the layout presenter and the local player's avatar, found again only while missing or stale.
        private void Lookup()
        {
            if (_session == null) _session = FindAnyObjectByType<SessionRoot>();
            if (_presenter == null) _presenter = FindAnyObjectByType<WorldLayoutPresenter>();
            if (!GameAudio.InWorld)
            {
                _player = null;
                return;
            }
            if (_player != null && _player.gameObject.activeInHierarchy) return;
            _player = null;
            foreach (var avatar in FindObjectsByType<PlayerAvatar>())
                if (avatar.IsOwner)
                {
                    _player = avatar.transform;
                    break;
                }
        }

        private AmbienceMix Mix()
        {
            var layout = _presenter != null ? _presenter.Shown : null;
            if (layout == null) return AmbienceMix.CityOnly;
            if (_zones == null || !ReferenceEquals(_zones.Layout, layout)) _zones = new AudioZones(layout);
            var position = _player != null ? _player.position : GameAudio.Listener != null ? GameAudio.Listener.position : Vector3.zero;
            var origin = _presenter.LayoutOrigin;
            return _zones.MixAt(new Vector2(position.x - origin.x, position.z - origin.z));
        }

        // An incidental sound at a random spot around the player, more often the stronger its area is; none below a third.
        private void Incidentals(Incidental incidental, float weight, float now)
        {
            if (now < incidental.Next) return;
            incidental.Next = now + Random.Range(incidental.MinGap, incidental.MaxGap);
            if (weight < 0.33f || Random.value > weight) return;
            var direction = Random.insideUnitCircle.normalized * Random.Range(incidental.MinDistance, incidental.MaxDistance);
            var position = _player.position + new Vector3(direction.x, 1.5f, direction.y);
            var clip = incidental.Clips[Random.Range(0, incidental.Clips.Length)];
            GameAudio.PlayAt(clip, position, incidental.Volume * Random.Range(0.7f, 1f), incidental.MaxDistance * 1.6f,
                Random.Range(0.92f, 1.08f), AudioChannel.Ambience);
        }
    }
}
