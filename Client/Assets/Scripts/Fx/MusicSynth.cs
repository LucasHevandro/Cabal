using System;

namespace Game.Client.Fx
{
    /// <summary>Clima de cada trilha.</summary>
    public enum MusicTrack
    {
        None,

        /// <summary>Vila: calma, maior, sem bateria.</summary>
        City,

        /// <summary>Campos do Vento: aventura, dórico, bateria leve.</summary>
        Field,

        /// <summary>Mata Sombria: menor, devagar, arpejo baixinho e sinos esparsos.</summary>
        Forest,

        /// <summary>Ruínas Esquecidas e Arena do Guardião: escala "antiga" (frígio maior), tambores e sinos.</summary>
        Ruins,

        /// <summary>Cripta: sombria, frígio, sinos e um "coração" no grave.</summary>
        Dungeon,

        /// <summary>Santuário das Ruínas: lídio, pad aberto e sinos em todo compasso (sagrado e estranho).</summary>
        Sanctuary,

        /// <summary>Chefe e arena: rápida, menor, baixo marcado e bateria cheia.</summary>
        Boss,
    }

    /// <summary>
    /// Compõe e sintetiza as músicas do protótipo em código (até existirem músicas de verdade): progressão de acordes,
    /// pad, baixo, arpejo ou melodia e bateria, com a mesma semente sempre (a música é sempre a mesma). O fim de cada
    /// trilha emenda no começo, para tocar em loop sem corte. Roda fora da thread principal (só números).
    /// </summary>
    public static class MusicSynth
    {
        public const int SampleRate = 22050;

        private sealed class Style
        {
            public float Bpm;
            public int Root; // nota MIDI da tônica
            public int[] Scale = Array.Empty<int>();
            public int[] Progression = Array.Empty<int>(); // graus (0 = tônica), um acorde por compasso
            public int Bars;
            public float Pad, Bass, Pluck, Bell, Lead, Kick, Snare, Hat;
            public int[] BassPattern = Array.Empty<int>(); // por oitava de compasso: 1 toca, 0 pausa
            public int[] KickPattern = Array.Empty<int>(); // por dezesseis avos
            public int[] SnarePattern = Array.Empty<int>();
            public int[] HatPattern = Array.Empty<int>();
            public int Seed;
            public float Brightness; // corte do filtro do pad (0..1)
        }

        private static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        private static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
        private static readonly int[] Phrygian = { 0, 1, 3, 5, 7, 8, 10 };
        private static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };
        private static readonly int[] PhrygianDominant = { 0, 1, 4, 5, 7, 8, 10 };
        private static readonly int[] Lydian = { 0, 2, 4, 6, 7, 9, 11 };

        private static Style StyleFor(MusicTrack track) => track switch
        {
            MusicTrack.City => new Style
            {
                Bpm = 84, Root = 60, Scale = Major, Progression = new[] { 0, 4, 5, 3, 0, 4, 3, 4 }, Bars = 8,
                Pad = 0.22f, Bass = 0.16f, Pluck = 0.2f, Bell = 0.05f, Hat = 0.025f,
                BassPattern = new[] { 1, 0, 0, 0, 1, 0, 0, 0 }, HatPattern = new[] { 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 1 },
                Seed = 11, Brightness = 0.55f,
            },
            MusicTrack.Field => new Style
            {
                Bpm = 108, Root = 62, Scale = Dorian, Progression = new[] { 0, 6, 5, 6, 0, 3, 5, 4 }, Bars = 8,
                Pad = 0.16f, Bass = 0.2f, Pluck = 0.2f, Lead = 0.06f, Kick = 0.25f, Snare = 0.1f, Hat = 0.035f,
                BassPattern = new[] { 1, 0, 1, 0, 1, 0, 1, 1 },
                KickPattern = new[] { 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, 0 },
                SnarePattern = new[] { 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 },
                HatPattern = new[] { 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 1 },
                Seed = 23, Brightness = 0.7f,
            },
            MusicTrack.Forest => new Style
            {
                Bpm = 76, Root = 57, Scale = Minor, Progression = new[] { 0, 5, 3, 6, 0, 5, 2, 4 }, Bars = 8,
                Pad = 0.2f, Bass = 0.14f, Pluck = 0.15f, Bell = 0.06f, Kick = 0.12f, Hat = 0.018f,
                BassPattern = new[] { 1, 0, 0, 0, 1, 0, 0, 1 },
                KickPattern = new[] { 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0 },
                HatPattern = new[] { 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0 },
                Seed = 71, Brightness = 0.38f,
            },
            MusicTrack.Ruins => new Style
            {
                Bpm = 92, Root = 55, Scale = PhrygianDominant, Progression = new[] { 0, 1, 0, 6, 0, 1, 5, 4 }, Bars = 8,
                Pad = 0.18f, Bass = 0.18f, Bell = 0.1f, Lead = 0.06f, Kick = 0.22f, Snare = 0.06f, Hat = 0.02f,
                BassPattern = new[] { 1, 0, 0, 1, 0, 0, 1, 0 },
                KickPattern = new[] { 1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0 }, // tambor ritual
                SnarePattern = new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0 },
                HatPattern = new[] { 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0 },
                Seed = 89, Brightness = 0.45f,
            },
            MusicTrack.Sanctuary => new Style
            {
                Bpm = 72, Root = 62, Scale = Lydian, Progression = new[] { 0, 1, 4, 1, 0, 5, 1, 4 }, Bars = 8,
                Pad = 0.28f, Bass = 0.1f, Pluck = 0.1f, Bell = 0.13f, Kick = 0.14f,
                BassPattern = new[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                KickPattern = new[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                Seed = 97, Brightness = 0.65f,
            },
            MusicTrack.Dungeon => new Style
            {
                Bpm = 66, Root = 57, Scale = Phrygian, Progression = new[] { 0, 1, 0, 4, 0, 1, 5, 4 }, Bars = 8,
                Pad = 0.26f, Bass = 0.14f, Bell = 0.12f, Kick = 0.28f,
                BassPattern = new[] { 1, 0, 0, 0, 0, 0, 0, 0 },
                KickPattern = new[] { 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, // batida de coração
                Seed = 37, Brightness = 0.3f,
            },
            _ => new Style
            {
                Bpm = 140, Root = 52, Scale = Minor, Progression = new[] { 0, 5, 2, 6, 0, 5, 3, 4 }, Bars = 8,
                Pad = 0.12f, Bass = 0.24f, Lead = 0.11f, Kick = 0.3f, Snare = 0.16f, Hat = 0.04f,
                BassPattern = new[] { 1, 1, 1, 1, 1, 1, 1, 1 },
                KickPattern = new[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 1, 0 },
                SnarePattern = new[] { 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1 },
                HatPattern = new[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 },
                Seed = 53, Brightness = 0.8f,
            },
        };

        /// <summary>Amostras mono da trilha (em loop perfeito), ou vazio para <see cref="MusicTrack.None"/>.</summary>
        public static float[] Render(MusicTrack track)
        {
            if (track == MusicTrack.None) return Array.Empty<float>();
            var style = StyleFor(track);
            var random = new Random(style.Seed);
            var beat = 60f / style.Bpm;
            var bar = beat * 4;
            var length = (int)(bar * style.Bars * SampleRate);
            var buffer = new float[length];

            for (var b = 0; b < style.Bars; b++)
            {
                var degree = style.Progression[b % style.Progression.Length];
                var chord = new[] { Note(style, degree, 0), Note(style, degree + 2, 0), Note(style, degree + 4, 0) };
                var barStart = b * bar;

                if (style.Pad > 0)
                    foreach (var note in chord) Pad(buffer, barStart, bar, Hz(note), style.Pad / 3f, style.Brightness);

                if (style.Bass > 0)
                {
                    var root = Note(style, degree, -2);
                    for (var i = 0; i < 8; i++)
                    {
                        if (style.BassPattern[i % style.BassPattern.Length] == 0) continue;
                        var octave = track == MusicTrack.Boss && i % 2 == 1 ? 12 : 0;
                        Bass(buffer, barStart + i * beat / 2, beat * (track == MusicTrack.Boss ? 0.45f : 1.6f), Hz(root + octave), style.Bass);
                    }
                }

                if (style.Pluck > 0)
                {
                    // Arpejo de oitavas de compasso pelas notas do acorde, subindo e descendo, às vezes pulando uma.
                    var order = new[] { 0, 1, 2, 1, 0, 2, 1, 2 };
                    for (var i = 0; i < 8; i++)
                    {
                        if (random.NextDouble() < 0.18) continue;
                        var note = chord[order[i]] + (i >= 4 && random.NextDouble() < 0.4 ? 12 : 0);
                        Pluck(buffer, barStart + i * beat / 2, Hz(note + 12), style.Pluck * (0.7f + 0.3f * (float)random.NextDouble()));
                    }
                }

                var everyBar = track is MusicTrack.Dungeon or MusicTrack.Sanctuary or MusicTrack.Ruins;
                if (style.Bell > 0 && (b % 2 == 0 || everyBar))
                {
                    var note = chord[random.Next(3)] + 12;
                    Bell(buffer, barStart + beat * (everyBar ? 2 : 0), Hz(note), style.Bell);
                }

                if (style.Lead > 0)
                {
                    // Frase curta: notas da escala perto do acorde, colcheias com pausas.
                    var current = degree + 4;
                    for (var i = 0; i < 8; i++)
                    {
                        if (random.NextDouble() < (track == MusicTrack.Boss ? 0.25 : 0.55)) continue;
                        current += random.Next(-2, 3);
                        current = Math.Max(degree, Math.Min(degree + 9, current));
                        Lead(buffer, barStart + i * beat / 2, beat * 0.4f, Hz(Note(style, current, 1)), style.Lead);
                    }
                }

                for (var s = 0; s < 16; s++)
                {
                    var at = barStart + s * beat / 4;
                    if (style.Kick > 0 && style.KickPattern.Length > 0 && style.KickPattern[s] == 1) Kick(buffer, at, style.Kick);
                    if (style.Snare > 0 && style.SnarePattern.Length > 0 && style.SnarePattern[s] == 1) Snare(buffer, at, style.Snare, random);
                    if (style.Hat > 0 && style.HatPattern.Length > 0 && style.HatPattern[s] == 1)
                        Hat(buffer, at, style.Hat * (s % 4 == 0 ? 1f : 0.6f), random);
                }
            }

            // Suaviza os picos e deixa todas as trilhas com volume parecido.
            var peak = 0f;
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (float)Math.Tanh(buffer[i] * 1.3);
                peak = Math.Max(peak, Math.Abs(buffer[i]));
            }
            var gain = peak > 0 ? 0.7f / peak : 1f;
            for (var i = 0; i < buffer.Length; i++) buffer[i] *= gain;
            return buffer;
        }

        // ----- Notas -----

        private static int Note(Style style, int degree, int octave)
        {
            var steps = style.Scale.Length;
            var o = (int)Math.Floor(degree / (double)steps);
            var index = degree - o * steps;
            return style.Root + style.Scale[index] + 12 * (o + octave);
        }

        private static float Hz(int midi) => 440f * (float)Math.Pow(2, (midi - 69) / 12.0);

        /// <summary>Soma no buffer; o que passa do fim volta ao começo (loop sem corte).</summary>
        private static void Add(float[] buffer, int index, float value) => buffer[index % buffer.Length] += value;

        // ----- Instrumentos -----

        private static void Pad(float[] buffer, float start, float seconds, float hz, float volume, float brightness)
        {
            var begin = (int)(start * SampleRate);
            var count = (int)((seconds + 0.6f) * SampleRate); // um pouco de cauda por cima do próximo acorde
            var low = 0f;
            var cutoff = 0.05f + brightness * 0.3f;
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                var env = Math.Min(1f, t / 0.35f) * (t > seconds ? Math.Max(0f, 1f - (t - seconds) / 0.6f) : 1f);
                var phase = 2 * Math.PI * hz * t;
                var raw = (float)(Math.Sin(phase) + Math.Sin(phase * 1.006) + 0.4 * Math.Sin(phase * 2.003) + 0.2 * Math.Sin(phase * 3.01));
                low += (raw - low) * cutoff;
                Add(buffer, begin + i, low * env * volume * 0.5f);
            }
        }

        private static void Bass(float[] buffer, float start, float seconds, float hz, float volume)
        {
            var begin = (int)(start * SampleRate);
            var count = (int)(seconds * SampleRate);
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                var p = hz * t % 1f;
                var triangle = 4f * Math.Abs(p - 0.5f) - 1f;
                var env = Math.Min(1f, t / 0.01f) * (float)Math.Exp(-t * 2.5f / seconds) * Math.Min(1f, (seconds - t) / 0.03f);
                Add(buffer, begin + i, (triangle * 0.8f + 0.3f * (float)Math.Sin(2 * Math.PI * hz * t)) * env * volume);
            }
        }

        private static void Pluck(float[] buffer, float start, float hz, float volume)
        {
            var begin = (int)(start * SampleRate);
            var count = (int)(0.9f * SampleRate);
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                var env = Math.Min(1f, t / 0.004f) * (float)Math.Exp(-t * 6f);
                var phase = 2 * Math.PI * hz * t;
                Add(buffer, begin + i, (float)(Math.Sin(phase) + 0.35 * Math.Sin(phase * 2) * Math.Exp(-t * 10)) * env * volume);
            }
        }

        private static void Bell(float[] buffer, float start, float hz, float volume)
        {
            var begin = (int)(start * SampleRate);
            var count = (int)(3f * SampleRate);
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                var phase = 2 * Math.PI * hz * t;
                var value = Math.Sin(phase) * Math.Exp(-t * 1.2) + 0.5 * Math.Sin(phase * 2.76) * Math.Exp(-t * 2.5) +
                            0.25 * Math.Sin(phase * 5.4) * Math.Exp(-t * 4);
                Add(buffer, begin + i, (float)value * Math.Min(1f, t / 0.003f) * volume);
            }
        }

        private static void Lead(float[] buffer, float start, float seconds, float hz, float volume)
        {
            var begin = (int)(start * SampleRate);
            var count = (int)(seconds * SampleRate);
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                var phase = 2 * Math.PI * hz * t * (1 + 0.004 * Math.Sin(2 * Math.PI * 5 * t)); // vibrato leve
                var value = Math.Sin(phase) + Math.Sin(phase * 3) / 3 + Math.Sin(phase * 5) / 6;
                var env = Math.Min(1f, t / 0.01f) * Math.Min(1f, (seconds - t) / 0.04f);
                Add(buffer, begin + i, (float)value * env * volume);
            }
        }

        private static void Kick(float[] buffer, float start, float volume)
        {
            var begin = (int)(start * SampleRate);
            var count = (int)(0.25f * SampleRate);
            var phase = 0.0;
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                var hz = 45 + 85 * Math.Exp(-t * 30);
                phase += 2 * Math.PI * hz / SampleRate;
                Add(buffer, begin + i, (float)(Math.Sin(phase) * Math.Exp(-t * 12)) * volume);
            }
        }

        private static void Snare(float[] buffer, float start, float volume, Random random)
        {
            var begin = (int)(start * SampleRate);
            var count = (int)(0.18f * SampleRate);
            var low = 0f;
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                var noise = (float)(random.NextDouble() * 2 - 1);
                low += (noise - low) * 0.5f;
                var tone = (float)Math.Sin(2 * Math.PI * 185 * t) * (float)Math.Exp(-t * 30);
                Add(buffer, begin + i, (low * (float)Math.Exp(-t * 18) + tone * 0.5f) * volume);
            }
        }

        private static void Hat(float[] buffer, float start, float volume, Random random)
        {
            var begin = (int)(start * SampleRate);
            var count = (int)(0.04f * SampleRate);
            var previous = 0f;
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                var noise = (float)(random.NextDouble() * 2 - 1);
                var high = noise - previous; // passa-alta simples: só o chiado
                previous = noise;
                Add(buffer, begin + i, high * (float)Math.Exp(-t * 80) * volume);
            }
        }
    }
}
