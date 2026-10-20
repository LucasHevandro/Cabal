using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Game.Client.Combat;
using Game.Client.Net;
using Game.Client.UI;
using Game.Client.World;
using UnityEngine;
using PvpMode = Game.Shared.Net.PvpMode;

namespace Game.Client.Fx
{
    /// <summary>
    /// Som ambiente: camadas em loop (<see cref="AmbientLayer"/>) com o volume de cada uma seguindo o lugar, a hora e o
    /// tempo, sempre aos poucos.
    /// <list type="bullet">
    /// <item>Vila: pássaros de dia, grilos à noite e um pouco de vento.</item>
    /// <item>Campos do Vento: vento forte, mais nas rajadas.</item>
    /// <item>Mata Sombria: pássaros, muitos grilos à noite e chuva quando chove.</item>
    /// <item>Ruínas e Arena do Guardião: zumbido mágico e vento.</item>
    /// <item>Cripta: ronco grave e gotas pingando. Santuário: vento e zumbido.</item>
    /// </list>
    /// Se existir <c>Resources/Audio/ambient/&lt;camada em minúsculas&gt;</c>, usa o arquivo; senão gera em código
    /// (<see cref="AmbientSynth"/>), fora da thread principal.
    /// </summary>
    public sealed class AmbientPlayer : MonoBehaviour
    {
        private const float BaseVolume = 0.45f;
        private const float FadePerSecond = 0.5f;

        private sealed class Channel
        {
            public AudioSource Source;
            public Task<float[]> Rendering;
            public float Level;
            public float Target;
        }

        private readonly Dictionary<AmbientLayer, Channel> _channels = new Dictionary<AmbientLayer, Channel>();

        /// <summary>Nível de cada camada agora (para testes e para o log).</summary>
        public static AmbientPlayer Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            if (Instance != null) return;
            var go = new GameObject("Som ambiente");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<AmbientPlayer>();
        }

        private void Awake()
        {
            foreach (AmbientLayer layer in System.Enum.GetValues(typeof(AmbientLayer)))
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.loop = true;
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.volume = 0f;
                var channel = new Channel { Source = source };
                var file = Resources.Load<AudioClip>("Audio/ambient/" + layer.ToString().ToLowerInvariant());
                if (file != null) Begin(channel, file);
                else channel.Rendering = Task.Run(() => AmbientSynth.Render(layer));
                _channels[layer] = channel;
            }
        }

        private static void Begin(Channel channel, AudioClip clip)
        {
            channel.Source.clip = clip;
            // Cada camada começa num ponto diferente do loop (os eventos não se alinham).
            channel.Source.time = Random.Range(0f, clip.length * 0.9f);
            channel.Source.Play();
        }

        private void Update()
        {
            Choose();
            var volume = BaseVolume * GameSettings.AmbientVolume;
            foreach (var pair in _channels)
            {
                var channel = pair.Value;
                if (channel.Rendering != null && channel.Rendering.IsCompleted)
                {
                    var task = channel.Rendering;
                    channel.Rendering = null;
                    if (!task.IsFaulted)
                    {
                        var samples = task.Result;
                        var clip = AudioClip.Create("ambiente_" + pair.Key, samples.Length, 1, AmbientSynth.SampleRate, false);
                        clip.SetData(samples, 0);
                        Begin(channel, clip);
                    }
                }
                channel.Level = Mathf.MoveTowards(channel.Level, channel.Target, FadePerSecond * Time.unscaledDeltaTime);
                channel.Source.volume = channel.Level * volume;
            }
        }

        private void Set(AmbientLayer layer, float level) => _channels[layer].Target = Mathf.Clamp01(level);

        private void Choose()
        {
            foreach (var channel in _channels.Values) channel.Target = 0f;
            if (ConnectMenu.IsOpen)
            {
                Set(AmbientLayer.Birds, 0.35f);
                Set(AmbientLayer.Wind, 0.15f);
                return;
            }
            var player = PlayerCombat.Local;
            if (player == null) return;
            var position = player.transform.position;

            var dungeon = CharacterState.Dungeon;
            if (dungeon != null)
            {
                if (Game.Shared.Dungeon.DungeonCatalog.Find(dungeon.DungeonId)?.Theme == "santuario")
                {
                    Set(AmbientLayer.Wind, 0.5f);
                    Set(AmbientLayer.Drone, 0.45f);
                }
                else
                {
                    Set(AmbientLayer.Drips, 0.8f);
                }
                return;
            }
            var pvp = CharacterState.Pvp;
            if (pvp != null && pvp.Mode == PvpMode.Arena)
            {
                Set(AmbientLayer.Wind, 0.3f);
                return;
            }

            var night = Atmosphere.Night;
            var day = 1f - night;
            var village = RegionAmbience.ZoneWeight("vila", position);
            var fields = RegionAmbience.ZoneWeight("campos_do_vento", position);
            var forest = RegionAmbience.ZoneWeight("mata_sombria", position);
            var ruins = Mathf.Max(RegionAmbience.ZoneWeight("ruinas_esquecidas", position), RegionAmbience.ZoneWeight("arena_do_guardiao", position));
            var gust = Mathf.InverseLerp(0.4f, 1.8f, RegionAmbience.Gust);

            Set(AmbientLayer.Wind, village * 0.2f + fields * (0.45f + 0.5f * gust) + forest * 0.15f + ruins * 0.35f);
            Set(AmbientLayer.Birds, (village * 0.7f + fields * 0.4f + forest * 0.5f) * day);
            Set(AmbientLayer.Crickets, (village * 0.45f + fields * 0.4f + forest) * night);
            Set(AmbientLayer.Rain, forest * RegionAmbience.Rain);
            Set(AmbientLayer.Drone, ruins * 0.8f);
        }

        /// <summary>As camadas tocando agora, com o nível (ex.: "Wind 0,45 Birds 0,30").</summary>
        public static string Describe()
        {
            if (Instance == null) return "(sem som ambiente)";
            var text = new StringBuilder();
            foreach (var pair in Instance._channels)
            {
                if (pair.Value.Target < 0.01f && pair.Value.Level < 0.01f) continue;
                if (text.Length > 0) text.Append(' ');
                text.Append(pair.Key).Append(' ').Append(pair.Value.Target.ToString("0.00"));
                if (pair.Value.Source.clip == null) text.Append(" (gerando)");
            }
            return text.Length > 0 ? text.ToString() : "silêncio";
        }
    }
}
