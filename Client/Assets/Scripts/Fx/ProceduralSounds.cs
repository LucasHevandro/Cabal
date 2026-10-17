using System;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>Sons do protótipo.</summary>
    public enum SoundId
    {
        Cast,
        Hit,
        HitHeavy,
        Good,
        Excellent,
        Miss,
        Route,
        ComboStart,
        Finisher,
        Error,
        LevelUp,
        RareDrop,
        UpgradeSuccess,
        UpgradeFail,
    }

    /// <summary>
    /// Sintetiza os sons do protótipo em código (ondas simples, ruído e envelopes).
    /// Servem até existirem sons de verdade; trocar por AudioClips importados não muda quem toca.
    /// </summary>
    public static class ProceduralSounds
    {
        private const int SampleRate = 44100;

        private enum Wave
        {
            Sine,
            Square,
            Triangle,
        }

        public static AudioClip Create(SoundId id)
        {
            float[] data;
            switch (id)
            {
                case SoundId.Cast:
                    data = Noise(0.14f, 0.35f, attack: 0.06f, smoothing: 0.85f);
                    break;
                case SoundId.Hit:
                    data = Mix(Noise(0.07f, 0.6f, attack: 0.002f, smoothing: 0.5f),
                        Tone(140, 60, 0.11f, Wave.Sine, 0.9f));
                    break;
                case SoundId.HitHeavy:
                    data = Mix(Noise(0.14f, 0.7f, attack: 0.002f, smoothing: 0.6f),
                        Tone(110, 40, 0.2f, Wave.Sine, 1f));
                    break;
                case SoundId.Good:
                    data = Tone(660, 660, 0.12f, Wave.Triangle, 0.45f);
                    break;
                case SoundId.Excellent:
                    data = Mix(Tone(880, 880, 0.2f, Wave.Sine, 0.45f), Tone(1320, 1320, 0.2f, Wave.Sine, 0.25f));
                    break;
                case SoundId.Miss:
                    data = Tone(320, 140, 0.25f, Wave.Square, 0.25f);
                    break;
                case SoundId.Route:
                    data = Sequence(0.07f, Tone(660, 660, 0.09f, Wave.Triangle, 0.4f),
                        Tone(880, 880, 0.09f, Wave.Triangle, 0.4f), Tone(1100, 1100, 0.16f, Wave.Triangle, 0.45f));
                    break;
                case SoundId.ComboStart:
                    data = Tone(300, 900, 0.22f, Wave.Triangle, 0.4f);
                    break;
                case SoundId.Finisher:
                    data = Mix(Mix(Noise(0.35f, 0.8f, attack: 0.002f, smoothing: 0.7f), Tone(100, 35, 0.45f, Wave.Sine, 1f)),
                        Tone(1320, 1320, 0.5f, Wave.Sine, 0.2f));
                    break;
                case SoundId.Error:
                    data = Sequence(0.09f, Tone(180, 180, 0.06f, Wave.Square, 0.2f), Tone(180, 180, 0.06f, Wave.Square, 0.2f));
                    break;
                case SoundId.LevelUp:
                    // Arpejo maior subindo, com um brilho agudo no fim.
                    data = Mix(Sequence(0.08f, Tone(523, 523, 0.18f, Wave.Triangle, 0.4f), Tone(659, 659, 0.18f, Wave.Triangle, 0.4f),
                            Tone(784, 784, 0.18f, Wave.Triangle, 0.4f), Tone(1047, 1047, 0.45f, Wave.Triangle, 0.45f)),
                        Sequence(0.32f, new float[0], Tone(2093, 2093, 0.4f, Wave.Sine, 0.15f)));
                    break;
                case SoundId.RareDrop:
                    data = Sequence(0.06f, Tone(1175, 1175, 0.25f, Wave.Sine, 0.3f), Tone(1568, 1568, 0.35f, Wave.Sine, 0.3f));
                    break;
                case SoundId.UpgradeSuccess:
                    data = Mix(Noise(0.12f, 0.4f, attack: 0.002f, smoothing: 0.3f),
                        Sequence(0.07f, Tone(784, 784, 0.15f, Wave.Triangle, 0.4f), Tone(1175, 1175, 0.4f, Wave.Triangle, 0.45f)));
                    break;
                case SoundId.UpgradeFail:
                    data = Mix(Noise(0.25f, 0.5f, attack: 0.002f, smoothing: 0.75f), Tone(260, 90, 0.4f, Wave.Square, 0.2f));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, null);
            }

            var clip = AudioClip.Create("proc_" + id, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Tom com glissando linear de frequência e envelope de ataque curto e queda exponencial.</summary>
        private static float[] Tone(float startHz, float endHz, float seconds, Wave wave, float volume)
        {
            var samples = new float[Mathf.CeilToInt(seconds * SampleRate)];
            var phase = 0.0;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (float)i / samples.Length;
                var hz = Mathf.Lerp(startHz, endHz, t);
                phase += hz / SampleRate;
                var p = (float)(phase - Math.Floor(phase));
                float value;
                switch (wave)
                {
                    case Wave.Square: value = p < 0.5f ? 1f : -1f; break;
                    case Wave.Triangle: value = 4f * Mathf.Abs(p - 0.5f) - 1f; break;
                    default: value = Mathf.Sin(2f * Mathf.PI * p); break;
                }
                samples[i] = value * volume * Envelope(t, 0.01f);
            }
            return samples;
        }

        /// <summary>Ruído suavizado (passa-baixa simples): "smoothing" alto = som mais grave e abafado.</summary>
        private static float[] Noise(float seconds, float volume, float attack, float smoothing)
        {
            var samples = new float[Mathf.CeilToInt(seconds * SampleRate)];
            var random = new System.Random(1234);
            var previous = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = (float)i / samples.Length;
                var white = (float)(random.NextDouble() * 2 - 1);
                previous = Mathf.Lerp(white, previous, smoothing);
                samples[i] = previous * volume * Envelope(t, attack);
            }
            return samples;
        }

        private static float Envelope(float t, float attack)
        {
            var rise = attack <= 0 ? 1f : Mathf.Clamp01(t / attack);
            return rise * Mathf.Exp(-5f * t);
        }

        private static float[] Mix(float[] a, float[] b)
        {
            var result = new float[Mathf.Max(a.Length, b.Length)];
            for (var i = 0; i < result.Length; i++)
            {
                var value = (i < a.Length ? a[i] : 0f) + (i < b.Length ? b[i] : 0f);
                result[i] = Mathf.Clamp(value, -1f, 1f);
            }
            return result;
        }

        /// <summary>Toca as partes uma depois da outra, cada uma começando <paramref name="stepSeconds"/> após a anterior.</summary>
        private static float[] Sequence(float stepSeconds, params float[][] parts)
        {
            var step = Mathf.CeilToInt(stepSeconds * SampleRate);
            var length = 0;
            for (var i = 0; i < parts.Length; i++) length = Mathf.Max(length, i * step + parts[i].Length);
            var result = new float[length];
            for (var i = 0; i < parts.Length; i++)
            {
                for (var j = 0; j < parts[i].Length; j++)
                    result[i * step + j] = Mathf.Clamp(result[i * step + j] + parts[i][j], -1f, 1f);
            }
            return result;
        }
    }
}
