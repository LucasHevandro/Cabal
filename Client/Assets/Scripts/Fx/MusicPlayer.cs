using System.Collections.Generic;
using System.Threading.Tasks;
using Game.Client.Combat;
using Game.Client.Monsters;
using Game.Client.Net;
using Game.Client.UI;
using Game.Client.World;
using Game.Shared.World;
using UnityEngine;
using PvpMode = Game.Shared.Net.PvpMode;

namespace Game.Client.Fx
{
    /// <summary>
    /// Toca a música do lugar: vila (e menu), mapa de caça, cripta, e chefe (chefe de campo por perto, sala do chefe da
    /// cripta ou arena). Troca com um cruzamento suave de 2 s. Se existir um arquivo em
    /// <c>Resources/Audio/music/&lt;city|field|dungeon|boss&gt;</c>, usa o arquivo; senão gera a música em código
    /// (<see cref="MusicSynth"/>), fora da thread principal.
    /// </summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        private const float FadeSeconds = 2f;
        private const float BossRange = 28f;
        private const float BaseVolume = 0.55f;

        private readonly Dictionary<MusicTrack, AudioClip> _clips = new Dictionary<MusicTrack, AudioClip>();
        private readonly Dictionary<MusicTrack, Task<float[]>> _rendering = new Dictionary<MusicTrack, Task<float[]>>();
        private readonly HashSet<string> _bossIds = new HashSet<string>();
        private AudioSource _current;
        private AudioSource _previous;
        private MusicTrack _playing = MusicTrack.None;
        private MusicTrack _wanted = MusicTrack.None;
        private float _fade = 1f;
        private float _nextCheck;

        /// <summary>A trilha tocando agora (para testes).</summary>
        public static MusicTrack Playing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            var go = new GameObject("Música");
            DontDestroyOnLoad(go);
            go.AddComponent<MusicPlayer>();
        }

        private void Awake()
        {
            _current = NewSource();
            _previous = NewSource();
            foreach (var spawn in WorldContent.Spawns()) if (spawn.IsBoss) _bossIds.Add(spawn.Data.Id);
            foreach (var dungeon in Game.Shared.Dungeon.DungeonCatalog.All()) _bossIds.Add(dungeon.BossId);
            // Começa a gerar as trilhas já: a da vila primeiro, as outras logo depois.
            foreach (var track in new[] { MusicTrack.City, MusicTrack.Field, MusicTrack.Dungeon, MusicTrack.Boss }) Prepare(track);
        }

        private AudioSource NewSource()
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }

        private void Prepare(MusicTrack track)
        {
            if (_clips.ContainsKey(track) || _rendering.ContainsKey(track)) return;
            var file = Resources.Load<AudioClip>("Audio/music/" + track.ToString().ToLowerInvariant());
            if (file != null)
            {
                _clips[track] = file;
                return;
            }
            _rendering[track] = Task.Run(() => MusicSynth.Render(track));
        }

        private void Update()
        {
            // Trilhas que terminaram de ser geradas viram AudioClip (isso só pode na thread principal).
            if (_rendering.Count > 0)
            {
                foreach (var pair in new List<KeyValuePair<MusicTrack, Task<float[]>>>(_rendering))
                {
                    if (!pair.Value.IsCompleted) continue;
                    _rendering.Remove(pair.Key);
                    if (pair.Value.IsFaulted) continue;
                    var samples = pair.Value.Result;
                    var clip = AudioClip.Create("musica_" + pair.Key, samples.Length, 1, MusicSynth.SampleRate, false);
                    clip.SetData(samples, 0);
                    _clips[pair.Key] = clip;
                }
            }

            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.5f;
                _wanted = Choose();
            }

            if (_wanted != _playing && (_wanted == MusicTrack.None || _clips.ContainsKey(_wanted))) Switch(_wanted);

            _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime / FadeSeconds);
            var volume = BaseVolume * GameSettings.MusicVolume;
            _current.volume = volume * _fade;
            _previous.volume = volume * (1f - _fade);
            if (_fade >= 1f && _previous.isPlaying) _previous.Stop();
        }

        private void Switch(MusicTrack track)
        {
            // A que tocava passa a sumir; a nova entra do zero.
            (_current, _previous) = (_previous, _current);
            _fade = 0f;
            _playing = track;
            Playing = track;
            _current.Stop();
            if (track == MusicTrack.None) return;
            _current.clip = _clips[track];
            _current.time = 0f;
            _current.volume = 0f;
            _current.Play();
        }

        private MusicTrack Choose()
        {
            if (ConnectMenu.IsOpen) return MusicTrack.City;
            var player = PlayerCombat.Local;
            if (player == null) return MusicTrack.None;
            var position = player.transform.position;

            var pvp = CharacterState.Pvp;
            if (pvp != null && pvp.Mode == PvpMode.Arena) return MusicTrack.Boss;

            var dungeonState = CharacterState.Dungeon;
            if (dungeonState != null)
            {
                var bossRoom = dungeonState.Room >= dungeonState.Rooms - 1;
                return bossRoom && !dungeonState.Completed ? MusicTrack.Boss : MusicTrack.Dungeon;
            }

            if (BossNearby(position)) return MusicTrack.Boss;
            var zone = ZoneArea.At(position);
            return zone != null && zone.Key == "zone.vila" ? MusicTrack.City : MusicTrack.Field;
        }

        private bool BossNearby(Vector3 position)
        {
            var session = NetworkSession.Instance;
            if (session != null)
            {
                foreach (var view in session.Views.Values)
                {
                    if (view == null || view.IsDead || !_bossIds.Contains(view.TypeId)) continue;
                    if ((view.transform.position - position).sqrMagnitude < BossRange * BossRange) return true;
                }
                return false;
            }
            foreach (var target in Targetable.All)
            {
                if (target == null || !target.IsAlive || target.GetComponent<BossAbilities>() == null) continue;
                if ((target.transform.position - position).sqrMagnitude < BossRange * BossRange) return true;
            }
            return false;
        }
    }
}
