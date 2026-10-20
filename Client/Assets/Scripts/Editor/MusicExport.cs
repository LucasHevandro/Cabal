using System;
using System.IO;
using Game.Client.Fx;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Salva as músicas e as camadas do som ambiente geradas em código como .wav, para ouvir fora do jogo (ou usar de base
    /// numa gravação de verdade).
    /// Em linha de comando: -executeMethod Game.Client.Editor.MusicExport.ExportAll -musicOut pasta
    /// </summary>
    public static class MusicExport
    {
        [MenuItem("Jogo/Áudio/Exportar músicas e ambiente gerados (.wav)")]
        public static void ExportAll()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-musicOut");
            var folder = index >= 0 && index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.dataPath, "..", "Builds", "Musicas");
            Directory.CreateDirectory(folder);
            foreach (MusicTrack track in Enum.GetValues(typeof(MusicTrack)))
            {
                if (track == MusicTrack.None) continue;
                var samples = MusicSynth.Render(track);
                var path = Path.Combine(folder, "musica_" + track.ToString().ToLowerInvariant() + ".wav");
                WriteWav(path, samples, MusicSynth.SampleRate, loops: 2);
                Debug.Log($"[Áudio] {track}: {samples.Length / (float)MusicSynth.SampleRate:0.0} s -> {path}");
            }
            foreach (AmbientLayer layer in Enum.GetValues(typeof(AmbientLayer)))
            {
                var samples = AmbientSynth.Render(layer);
                var path = Path.Combine(folder, "ambiente_" + layer.ToString().ToLowerInvariant() + ".wav");
                WriteWav(path, samples, AmbientSynth.SampleRate, loops: 2);
                Debug.Log($"[Áudio] ambiente {layer}: {samples.Length / (float)AmbientSynth.SampleRate:0.0} s -> {path}");
            }
        }

        /// <summary>WAV mono de 16 bits, com a trilha repetida <paramref name="loops"/> vezes (para ouvir a emenda).</summary>
        private static void WriteWav(string path, float[] samples, int rate, int loops)
        {
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new BinaryWriter(stream);
            var dataBytes = samples.Length * loops * 2;
            writer.Write("RIFF".ToCharArray());
            writer.Write(36 + dataBytes);
            writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());
            writer.Write(16);
            writer.Write((short)1); // PCM
            writer.Write((short)1); // mono
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data".ToCharArray());
            writer.Write(dataBytes);
            for (var l = 0; l < loops; l++)
                foreach (var sample in samples) writer.Write((short)Mathf.Clamp(sample * 32767f, -32768f, 32767f));
        }
    }
}
