using System;

namespace Game.Client.Fx
{
    /// <summary>Camadas do som ambiente; cada lugar mistura algumas (<see cref="AmbientPlayer"/>).</summary>
    public enum AmbientLayer
    {
        /// <summary>Vento: ruído grave que sobe e desce devagar.</summary>
        Wind,

        /// <summary>Pássaros de dia: assobios curtos, em frases de dois a cinco.</summary>
        Birds,

        /// <summary>Grilos à noite: trens de pulsos agudos, dois grilos em ritmos diferentes.</summary>
        Crickets,

        /// <summary>Chuva: chiado com gotas estalando.</summary>
        Rain,

        /// <summary>Zumbido mágico das ruínas: grave pulsando devagar, ar e um sino de vez em quando.</summary>
        Drone,

        /// <summary>Cripta: ronco bem grave e gotas pingando com eco.</summary>
        Drips,
    }

    /// <summary>
    /// Gera em código as camadas do som ambiente (até existirem gravações), em loop sem emenda: o ruído passa duas vezes
    /// pelos filtros (a segunda começa do estado em que a primeira terminou) e os eventos que passam do fim voltam ao
    /// começo. Roda fora da thread principal (só números).
    /// </summary>
    public static class AmbientSynth
    {
        public const int SampleRate = 22050;
        public const float Seconds = 16f;

        public static float[] Render(AmbientLayer layer)
        {
            var length = (int)(Seconds * SampleRate);
            var buffer = new float[length];
            var random = new Random(101 + (int)layer * 17);
            switch (layer)
            {
                case AmbientLayer.Wind: Wind(buffer, random); break;
                case AmbientLayer.Birds: Birds(buffer, random); break;
                case AmbientLayer.Crickets: Crickets(buffer, random); break;
                case AmbientLayer.Rain: Rain(buffer, random); break;
                case AmbientLayer.Drone: Drone(buffer, random); break;
                case AmbientLayer.Drips: Drips(buffer, random); break;
            }
            Normalize(buffer, layer == AmbientLayer.Birds || layer == AmbientLayer.Crickets ? 0.5f : 0.7f);
            return buffer;
        }

        /// <summary>Uma onda que completa <paramref name="cycles"/> voltas no loop inteiro (fecha certinho no fim).</summary>
        private static float Loop(int i, int length, float cycles, float phase = 0f) =>
            (float)Math.Sin(2 * Math.PI * (cycles * i / (double)length + phase));

        private static void Add(float[] buffer, int index, float value) => buffer[((index % buffer.Length) + buffer.Length) % buffer.Length] += value;

        private static float[] Noise(int length, Random random)
        {
            var noise = new float[length];
            for (var i = 0; i < length; i++) noise[i] = (float)(random.NextDouble() * 2 - 1);
            return noise;
        }

        private static void Wind(float[] buffer, Random random)
        {
            var n = buffer.Length;
            var noise = Noise(n, random);
            float low = 0f, lower = 0f;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < n; i++)
                {
                    // O corte do filtro e o volume sobem e descem juntos: o "uivo" das rajadas.
                    var swell = 0.5f + 0.3f * Loop(i, n, 2f) + 0.2f * Loop(i, n, 5f, 0.3f);
                    var cutoff = 0.012f + 0.03f * swell;
                    low += (noise[i] - low) * cutoff;
                    lower += (low - lower) * cutoff;
                    if (pass == 1) buffer[i] = lower * (0.35f + 0.65f * swell);
                }
            }
        }

        private static void Birds(float[] buffer, Random random)
        {
            var at = 0.4f;
            while (at < Seconds)
            {
                var hz = 2300f + (float)random.NextDouble() * 2200f;
                var notes = random.Next(2, 6);
                var volume = 0.4f + 0.6f * (float)random.NextDouble();
                var glide = random.NextDouble() < 0.5 ? -1f : 1f;
                for (var k = 0; k < notes; k++)
                {
                    Chirp(buffer, at, 0.06f + 0.06f * (float)random.NextDouble(), hz * (1f + 0.08f * k * glide), glide * 900f, volume);
                    at += 0.11f + 0.07f * (float)random.NextDouble();
                }
                at += 0.8f + 2.4f * (float)random.NextDouble();
            }
        }

        private static void Chirp(float[] buffer, float start, float seconds, float hz, float sweep, float volume)
        {
            var begin = (int)(start * SampleRate);
            var count = (int)(seconds * SampleRate);
            var phase = 0.0;
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)SampleRate;
                var f = hz + sweep * t / seconds;
                phase += 2 * Math.PI * f / SampleRate;
                var env = (float)Math.Sin(Math.PI * t / seconds);
                Add(buffer, begin + i, (float)Math.Sin(phase) * env * env * volume);
            }
        }

        private static void Crickets(float[] buffer, Random random)
        {
            // Dois grilos: cada um repete trens de 3 ou 4 pulsos, com o próprio tom e ritmo.
            foreach (var (hz, every, pulses, volume) in new[] { (4400f, 0.62f, 3, 1f), (5100f, 0.83f, 4, 0.7f) })
            {
                for (var at = (float)random.NextDouble() * every; at < Seconds; at += every * (0.95f + 0.1f * (float)random.NextDouble()))
                {
                    for (var p = 0; p < pulses; p++)
                    {
                        var begin = (int)((at + p * 0.032f) * SampleRate);
                        var count = (int)(0.022f * SampleRate);
                        for (var i = 0; i < count; i++)
                        {
                            var t = i / (float)SampleRate;
                            var env = (float)Math.Sin(Math.PI * t / 0.022f);
                            Add(buffer, begin + i, (float)Math.Sin(2 * Math.PI * hz * t) * env * volume);
                        }
                    }
                }
            }
        }

        private static void Rain(float[] buffer, Random random)
        {
            var n = buffer.Length;
            var noise = Noise(n, random);
            float low = 0f, soft = 0f;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < n; i++)
                {
                    low += (noise[i] - low) * 0.08f;
                    var hiss = noise[i] - low; // só o chiado
                    soft += (hiss - soft) * 0.5f;
                    if (pass == 1) buffer[i] = soft * (0.55f + 0.08f * Loop(i, n, 3f));
                }
            }
            // Gotas batendo perto: estalos curtos em tons variados.
            for (var k = 0; k < Seconds * 22; k++)
            {
                var begin = random.Next(n);
                var hz = 1800f + (float)random.NextDouble() * 3500f;
                var volume = 0.15f + 0.35f * (float)random.NextDouble();
                var count = (int)(0.012f * SampleRate);
                for (var i = 0; i < count; i++)
                {
                    var t = i / (float)SampleRate;
                    Add(buffer, begin + i, (float)(Math.Sin(2 * Math.PI * hz * t) * Math.Exp(-t * 400)) * volume);
                }
            }
        }

        private static void Drone(float[] buffer, Random random)
        {
            var n = buffer.Length;
            var noise = Noise(n, random);
            var air = 0f;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < n; i++)
                {
                    air += (noise[i] - air) * 0.03f;
                    if (pass == 0) continue;
                    var t = i / (double)SampleRate;
                    // Notas graves com batimento lento: as frequências fecham voltas inteiras no loop (16 s).
                    var hum = Math.Sin(2 * Math.PI * 55 * t) + 0.6 * Math.Sin(2 * Math.PI * 82.5 * t) + 0.35 * Math.Sin(2 * Math.PI * 110.25 * t);
                    var pulse = 0.6f + 0.4f * Loop(i, n, 4f);
                    buffer[i] = (float)hum * 0.25f * pulse + air * 0.9f;
                }
            }
            // Sino mágico, alto e distante, de vez em quando.
            for (var at = 1.5f; at < Seconds; at += 3.5f + 2.5f * (float)random.NextDouble())
            {
                var hz = 880f * (float)Math.Pow(2, random.Next(0, 8) / 12.0);
                var begin = (int)(at * SampleRate);
                var count = (int)(3.5f * SampleRate);
                for (var i = 0; i < count; i++)
                {
                    var t = i / (float)SampleRate;
                    var phase = 2 * Math.PI * hz * t;
                    var value = Math.Sin(phase) * Math.Exp(-t * 1.1) + 0.4 * Math.Sin(phase * 2.76) * Math.Exp(-t * 2.2);
                    Add(buffer, begin + i, (float)value * Math.Min(1f, t / 0.01f) * 0.12f);
                }
            }
        }

        private static void Drips(float[] buffer, Random random)
        {
            var n = buffer.Length;
            var noise = Noise(n, random);
            float low = 0f, lower = 0f;
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < n; i++)
                {
                    low += (noise[i] - low) * 0.006f;
                    lower += (low - lower) * 0.006f;
                    if (pass == 1) buffer[i] = lower * 6f * (0.7f + 0.3f * Loop(i, n, 2f));
                }
            }
            // Gota: tom que sobe rápido e some, com dois ecos mais fracos (pedra em volta).
            for (var at = 0.7f; at < Seconds; at += 1.4f + 2.2f * (float)random.NextDouble())
            {
                var hz = 900f + (float)random.NextDouble() * 900f;
                var volume = 0.5f + 0.5f * (float)random.NextDouble();
                foreach (var (delay, echo) in new[] { (0f, 1f), (0.13f, 0.35f), (0.27f, 0.15f) })
                {
                    var begin = (int)((at + delay) * SampleRate);
                    var count = (int)(0.09f * SampleRate);
                    var phase = 0.0;
                    for (var i = 0; i < count; i++)
                    {
                        var t = i / (float)SampleRate;
                        phase += 2 * Math.PI * hz * (1 + 1.5 * t / 0.09) / SampleRate;
                        Add(buffer, begin + i, (float)(Math.Sin(phase) * Math.Exp(-t * 45)) * volume * echo);
                    }
                }
            }
        }

        private static void Normalize(float[] buffer, float target)
        {
            var peak = 0f;
            foreach (var value in buffer) peak = Math.Max(peak, Math.Abs(value));
            if (peak <= 0f) return;
            var gain = target / peak;
            for (var i = 0; i < buffer.Length; i++) buffer[i] *= gain;
        }
    }
}
