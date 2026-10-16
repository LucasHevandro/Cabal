using System.Collections.Generic;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// Toca os sons do protótipo com uma pequena variação de tom, para não soarem repetitivos.
    /// Os clipes são gerados uma vez, na primeira vez que cada som é pedido.
    /// </summary>
    public sealed class SoundPlayer : MonoBehaviour
    {
        private const int Voices = 12;

        private static SoundPlayer _instance;

        [SerializeField, Range(0f, 1f)] private float masterVolume = 0.7f;

        private readonly Dictionary<SoundId, AudioClip> _clips = new Dictionary<SoundId, AudioClip>();
        private AudioSource[] _sources;
        private int _next;

        /// <summary>Toca um som. Não faz nada se não houver SoundPlayer na cena.</summary>
        public static void Play(SoundId id, float volume = 1f, float pitchJitter = 0.06f)
        {
            if (_instance != null) _instance.PlayInternal(id, volume, pitchJitter);
        }

        private void Awake()
        {
            _instance = this;
            _sources = new AudioSource[Voices];
            for (var i = 0; i < Voices; i++)
            {
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                _sources[i] = source;
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void PlayInternal(SoundId id, float volume, float pitchJitter)
        {
            if (!_clips.TryGetValue(id, out var clip))
            {
                clip = ProceduralSounds.Create(id);
                _clips[id] = clip;
            }

            // Reaproveita as vozes em rodízio; o som mais antigo é cortado se todas estiverem ocupadas.
            var source = _sources[_next];
            _next = (_next + 1) % _sources.Length;
            source.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            source.volume = volume * masterVolume;
            source.clip = clip;
            source.Play();
        }
    }
}
