using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Gravação de assets e arquivos que tenta de novo por alguns segundos se o arquivo estiver ocupado. O indexador de
    /// busca e o antivírus do Windows abrem por um instante os arquivos recém-gravados, e o Unity falha ao apagar ou
    /// regravar ("Failed to delete", "Win32 IO 1224"), derrubando a montagem inteira da cena.
    /// </summary>
    internal static class AssetWrite
    {
        private const int Attempts = 30;
        private const int WaitMs = 300;

        public static void Create(Object asset, string path)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(asset, path);
                    return;
                }
                catch (UnityException) when (attempt < Attempts)
                {
                    Thread.Sleep(WaitMs);
                }
            }
        }

        public static void Bytes(string path, byte[] bytes)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.WriteAllBytes(path, bytes);
                    return;
                }
                catch (IOException) when (attempt < Attempts)
                {
                    Thread.Sleep(WaitMs);
                }
            }
        }
    }
}
