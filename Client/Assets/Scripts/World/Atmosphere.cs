using Game.Client.Combat;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Client.World
{
    /// <summary>
    /// Clima de cada lugar: cor e densidade da névoa, luz do ambiente, cor e força do sol e o "filtro" de cor do
    /// pós-processamento. Vai misturando aos poucos conforme o jogador anda (pesos das zonas em <see cref="Ground"/>),
    /// e troca para a dungeon e a arena (instâncias longe do mapa). Na qualidade baixa desliga o pós-processamento.
    /// </summary>
    public sealed class Atmosphere : MonoBehaviour
    {
        [System.Serializable]
        public struct Look
        {
            public Color Fog;
            public float FogDensity;
            public Color Sky;
            public Color Equator;
            public Color GroundLight;
            public Color Sun;
            public float SunIntensity;
            public Color Filter;
            public float Saturation;
            public float Exposure;

            public static Look Lerp(Look a, Look b, float t) => new Look
            {
                Fog = Color.Lerp(a.Fog, b.Fog, t), FogDensity = Mathf.Lerp(a.FogDensity, b.FogDensity, t),
                Sky = Color.Lerp(a.Sky, b.Sky, t), Equator = Color.Lerp(a.Equator, b.Equator, t),
                GroundLight = Color.Lerp(a.GroundLight, b.GroundLight, t), Sun = Color.Lerp(a.Sun, b.Sun, t),
                SunIntensity = Mathf.Lerp(a.SunIntensity, b.SunIntensity, t), Filter = Color.Lerp(a.Filter, b.Filter, t),
                Saturation = Mathf.Lerp(a.Saturation, b.Saturation, t), Exposure = Mathf.Lerp(a.Exposure, b.Exposure, t),
            };

            public Look Add(Look other, float weight) => new Look
            {
                Fog = Fog + other.Fog * weight, FogDensity = FogDensity + other.FogDensity * weight,
                Sky = Sky + other.Sky * weight, Equator = Equator + other.Equator * weight,
                GroundLight = GroundLight + other.GroundLight * weight, Sun = Sun + other.Sun * weight,
                SunIntensity = SunIntensity + other.SunIntensity * weight, Filter = Filter + other.Filter * weight,
                Saturation = Saturation + other.Saturation * weight, Exposure = Exposure + other.Exposure * weight,
            };
        }

        private static Look Make(Color fog, float density, Color sky, Color equator, Color ground, Color sun, float sunIntensity,
            Color filter, float saturation, float exposure) => new Look
        {
            Fog = fog, FogDensity = density, Sky = sky, Equator = equator, GroundLight = ground, Sun = sun,
            SunIntensity = sunIntensity, Filter = filter, Saturation = saturation, Exposure = exposure,
        };

        // Entre zonas e nos Campos: dia claro. Cada zona puxa para o seu clima.
        public static readonly Look Open = Make(new Color(0.64f, 0.73f, 0.84f), 0.0024f, new Color(0.62f, 0.72f, 0.9f),
            new Color(0.55f, 0.58f, 0.55f), new Color(0.3f, 0.27f, 0.22f), new Color(1f, 0.95f, 0.85f), 1.25f, Color.white, 3f, 0f);

        public static readonly Look Village = Make(new Color(0.7f, 0.72f, 0.74f), 0.0022f, new Color(0.66f, 0.72f, 0.88f),
            new Color(0.62f, 0.58f, 0.5f), new Color(0.33f, 0.28f, 0.22f), new Color(1f, 0.92f, 0.78f), 1.3f, new Color(1f, 0.97f, 0.92f), 4f, 0.05f);

        public static readonly Look Fields = Make(new Color(0.64f, 0.75f, 0.86f), 0.0022f, new Color(0.6f, 0.74f, 0.95f),
            new Color(0.55f, 0.62f, 0.52f), new Color(0.28f, 0.3f, 0.2f), new Color(1f, 0.97f, 0.88f), 1.3f, Color.white, 4f, 0f);

        public static readonly Look Forest = Make(new Color(0.36f, 0.48f, 0.4f), 0.016f, new Color(0.38f, 0.5f, 0.48f),
            new Color(0.3f, 0.38f, 0.3f), new Color(0.15f, 0.17f, 0.12f), new Color(0.85f, 0.92f, 0.75f), 0.95f, new Color(0.88f, 1f, 0.9f), 0f, -0.15f);

        public static readonly Look Ruins = Make(new Color(0.62f, 0.6f, 0.66f), 0.011f, new Color(0.55f, 0.56f, 0.66f),
            new Color(0.48f, 0.47f, 0.5f), new Color(0.22f, 0.2f, 0.22f), new Color(0.92f, 0.9f, 0.95f), 1.05f, new Color(0.94f, 0.93f, 1f), -12f, -0.05f);

        public static readonly Look Arena = Make(new Color(0.68f, 0.5f, 0.42f), 0.009f, new Color(0.62f, 0.5f, 0.48f),
            new Color(0.55f, 0.42f, 0.35f), new Color(0.25f, 0.16f, 0.12f), new Color(1f, 0.78f, 0.6f), 1.2f, new Color(1f, 0.9f, 0.82f), 6f, 0f);

        public static readonly Look Dungeon = Make(new Color(0.08f, 0.07f, 0.1f), 0.03f, new Color(0.25f, 0.24f, 0.32f),
            new Color(0.18f, 0.17f, 0.2f), new Color(0.08f, 0.07f, 0.08f), new Color(0.65f, 0.6f, 0.8f), 0.55f, new Color(0.9f, 0.88f, 1f), -10f, 0.1f);

        public static readonly Look PvpArena = Make(new Color(0.55f, 0.6f, 0.7f), 0.006f, new Color(0.55f, 0.62f, 0.8f),
            new Color(0.5f, 0.5f, 0.52f), new Color(0.22f, 0.22f, 0.24f), new Color(1f, 0.95f, 0.88f), 1.25f, Color.white, 10f, 0f);

        /// <summary>Duração de um dia inteiro (manhã, tarde, pôr do sol, noite e amanhecer), em minutos.</summary>
        public const float DayMinutes = 40f;

        /// <summary>0 = dia, 1 = noite cheia (lanternas e tochas acompanham).</summary>
        public static float Night { get; private set; }

        /// <summary>Tempo fechado de 0 a 1 (chuva onde o jogador está, vinda do <see cref="RegionAmbience"/>).</summary>
        public static float Overcast { get; set; }

        /// <summary>Hora do dia de 0 a 1 (0,25 = meio da manhã, 0,62 = pôr do sol, 0,8 = meia-noite). Para os testes.</summary>
        public static float? ForcedTime { get; set; }

        [SerializeField] private Light sun;
        [SerializeField] private Volume volume;

        private Look _current;
        private bool _started;
        private ColorAdjustments _color;
        private int _quality = -1;

        public void Configure(Light sunLight, Volume postVolume)
        {
            sun = sunLight;
            volume = postVolume;
        }

        /// <summary>O clima no ponto (para a cena começar já com a cor certa).</summary>
        public static Look At(Vector3 position)
        {
            if (position.z > 500f) return Dungeon;
            if (position.z < -500f) return PvpArena;
            var result = default(Look);
            var used = 0f;
            // Sem closures nem listas: roda todo quadro e não pode gerar lixo.
            Mix(ref result, ref used, "vila", Village, position);
            Mix(ref result, ref used, "campos_do_vento", Fields, position);
            Mix(ref result, ref used, "mata_sombria", Forest, position);
            Mix(ref result, ref used, "ruinas_esquecidas", Ruins, position);
            Mix(ref result, ref used, "arena_do_guardiao", Arena, position);
            return result.Add(Open, Mathf.Max(0f, 1f - used));
        }

        private static void Mix(ref Look result, ref float used, string zone, in Look look, Vector3 position)
        {
            var w = Ground.ZoneWeight(zone, position.x, position.z);
            result = result.Add(look, w);
            used += w;
        }

        private void Start()
        {
            // Cópia do céu: o ciclo do dia mexe nele sem alterar o arquivo do projeto.
            if (RenderSettings.skybox != null) RenderSettings.skybox = Instantiate(RenderSettings.skybox);
            if (volume != null && volume.profile != null)
            {
                // Cópia do perfil: mexer nele não altera o arquivo do projeto.
                volume.profile = Instantiate(volume.profile);
                volume.profile.TryGet(out _color);
            }
        }

        private void LateUpdate()
        {
            var focus = PlayerCombat.Local != null ? PlayerCombat.Local.transform.position
                : Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            var target = At(focus);
            // Ciclo do dia só no mapa aberto (a dungeon e a arena têm o próprio clima).
            var outdoors = focus.z > -500f && focus.z < 500f;
            var hour = DayTime();
            if (outdoors) target = WithTimeOfDay(target, hour, out _sunsetGlow);
            if (outdoors && Overcast > 0.001f) target = Look.Lerp(target, Overcasted(target), Overcast);
            Night = outdoors ? NightAmount(hour) : 0f;
            // Entrar na dungeon ou na arena é um corte (teleporte); andando pelo mapa a troca é suave.
            var jump = !_started || Mathf.Abs(target.FogDensity - _current.FogDensity) > 0.012f;
            _current = jump ? target : Look.Lerp(_current, target, 1f - Mathf.Exp(-Time.deltaTime * 1.5f));
            _started = true;
            Apply(_current);
            ApplySun(outdoors ? hour : 0.3f);
            ApplyQuality();
        }

        private float _sunsetGlow;
        private Quaternion _sunRotation;
        private bool _sunCaptured;

        /// <summary>Hora do dia (0 a 1). No online vem do relógio do servidor: todos veem o mesmo céu.</summary>
        private static float DayTime()
        {
            if (ForcedTime is { } forced) return forced;
            var session = Net.NetworkSession.Instance;
            var seconds = session != null && session.IsWelcomed ? session.ServerNow : Time.timeSinceLevelLoad + DayMinutes * 60f * 0.2f;
            return (float)(seconds / (DayMinutes * 60.0) % 1.0);
        }

        /// <summary>Noite: entre 0,7 e 0,9, com meia hora de transição no pôr do sol e no amanhecer.</summary>
        private static float NightAmount(float hour)
        {
            if (hour < 0.62f) return 0f;
            if (hour < 0.7f) return Mathf.SmoothStep(0f, 1f, (hour - 0.62f) / 0.08f);
            if (hour < 0.9f) return 1f;
            return 1f - Mathf.SmoothStep(0f, 1f, (hour - 0.9f) / 0.1f);
        }

        private static readonly Look NightLook = Make(new Color(0.1f, 0.13f, 0.22f), 0.008f, new Color(0.16f, 0.2f, 0.34f),
            new Color(0.12f, 0.14f, 0.22f), new Color(0.06f, 0.06f, 0.09f), new Color(0.55f, 0.65f, 0.95f), 0.35f, new Color(0.75f, 0.82f, 1f), -18f, -0.25f);

        private static Look WithTimeOfDay(Look look, float hour, out float sunset)
        {
            // Pôr do sol (0,55 a 0,7) e amanhecer (0,88 a 0,98): tudo puxa para o laranja e rosa.
            sunset = Mathf.Max(Bump(hour, 0.55f, 0.64f, 0.71f), Bump(hour, 0.87f, 0.92f, 0.99f));
            var warm = look;
            warm.Fog = Color.Lerp(look.Fog, new Color(0.9f, 0.6f, 0.45f), 0.6f);
            warm.Sun = new Color(1f, 0.6f, 0.35f);
            warm.Sky = Color.Lerp(look.Sky, new Color(0.75f, 0.5f, 0.45f), 0.5f);
            warm.Filter = Color.Lerp(look.Filter, new Color(1f, 0.85f, 0.75f), 0.6f);
            var result = Look.Lerp(look, warm, sunset);
            return Look.Lerp(result, NightLook, NightAmount(hour) * 0.85f);
        }

        /// <summary>O mesmo clima com o tempo fechado: sol fraco, neblina cinza mais densa e cores apagadas.</summary>
        private static Look Overcasted(Look look)
        {
            var grey = look;
            grey.Fog = Color.Lerp(look.Fog, new Color(0.42f, 0.47f, 0.5f) * Mathf.Max(0.35f, look.Fog.maxColorComponent), 0.8f);
            grey.FogDensity = Mathf.Max(look.FogDensity * 1.6f, 0.026f);
            grey.SunIntensity = look.SunIntensity * 0.35f;
            grey.Sky = Color.Lerp(look.Sky, new Color(0.45f, 0.48f, 0.52f) * look.Sky.maxColorComponent, 0.7f);
            grey.Filter = Color.Lerp(look.Filter, new Color(0.85f, 0.9f, 0.95f), 0.5f);
            grey.Saturation = look.Saturation - 18f;
            return grey;
        }

        private static float Bump(float x, float start, float peak, float end)
        {
            if (x <= start || x >= end) return 0f;
            return x < peak ? Mathf.SmoothStep(0f, 1f, (x - start) / (peak - start)) : 1f - Mathf.SmoothStep(0f, 1f, (x - peak) / (end - peak));
        }

        /// <summary>O sol (de noite, a lua) percorre o céu: alto ao meio-dia, rente no pôr do sol, e o céu escurece.</summary>
        private void ApplySun(float hour)
        {
            if (sun == null) return;
            if (!_sunCaptured)
            {
                _sunRotation = sun.transform.rotation;
                _sunCaptured = true;
            }
            var night = NightAmount(hour);
            var dayArc = Mathf.Clamp01(hour / 0.7f);
            var elevation = Mathf.Lerp(Mathf.Sin(dayArc * Mathf.PI) * 52f + 6f, 38f, night);
            var heading = Mathf.Lerp(-80f, 80f, dayArc) + night * 140f;
            sun.transform.rotation = Quaternion.Euler(elevation, _sunRotation.eulerAngles.y + heading * 0.5f, 0f);
            if (RenderSettings.skybox != null)
            {
                if (RenderSettings.skybox.HasProperty("_Exposure"))
                    RenderSettings.skybox.SetFloat("_Exposure", Mathf.Lerp(1.25f, 0.18f, night) * Mathf.Lerp(1f, 0.55f, Overcast));
                if (RenderSettings.skybox.HasProperty("_SkyTint"))
                    RenderSettings.skybox.SetColor("_SkyTint", Color.Lerp(Color.Lerp(new Color(0.48f, 0.56f, 0.7f), new Color(0.75f, 0.45f, 0.4f), _sunsetGlow),
                        new Color(0.42f, 0.44f, 0.47f), Overcast * 0.85f));
            }
        }

        private void Apply(in Look look)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = look.Fog;
            RenderSettings.fogDensity = look.FogDensity;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.Sky;
            RenderSettings.ambientEquatorColor = look.Equator;
            RenderSettings.ambientGroundColor = look.GroundLight;
            if (sun != null)
            {
                sun.color = look.Sun;
                sun.intensity = look.SunIntensity;
            }
            if (_color != null)
            {
                _color.colorFilter.Override(look.Filter);
                _color.saturation.Override(look.Saturation);
                _color.postExposure.Override(look.Exposure);
            }
        }

        /// <summary>Qualidade baixa: sem pós-processamento (mais FPS em PC fraco).</summary>
        private void ApplyQuality()
        {
            var quality = QualitySettings.GetQualityLevel();
            if (quality == _quality) return;
            _quality = quality;
            var camera = Camera.main;
            if (camera == null) return;
            var high = quality > 0 || QualitySettings.names.Length <= 1;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = high;
            if (volume != null) volume.enabled = high;
        }
    }
}
