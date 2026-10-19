using System;
using System.Collections;
using System.IO;
using Game.Client.Net;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Tela de entrada: <c>-logintest pasta</c> espera o fundo ficar pronto, fotografa a tela e fecha. Confere que a
    /// câmera do jogo ficou desligada atrás do menu (o fundo é uma foto parada).
    /// </summary>
    public sealed class LoginScreenTest : MonoBehaviour
    {
        private string _folder = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-logintest");
            if (index < 0) return;
            var test = new GameObject("Teste da tela de entrada").AddComponent<LoginScreenTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "logintest");
            Directory.CreateDirectory(test._folder);
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(2.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "entrada.png"));
            yield return new WaitForSecondsRealtime(0.5f);
            // Câmera desligada não conta como Camera.main.
            var stopped = ConnectMenu.IsOpen && Camera.main == null;
            var servers = string.Join("; ", ServerList.Entries.ConvertAll(e => $"{e.Name} {e.Address} = {e.Status} ({e.Players} jogadores, {e.PingMs} ms)"));
            // Escolheu jogar (offline): o jogo volta a ser desenhado.
            GameMode.SetOffline();
            yield return new WaitForSecondsRealtime(0.5f);
            var back = Camera.main != null;
            var ok = stopped && back;
            File.WriteAllText(Path.Combine(_folder, "relatorio.txt"),
                (ok ? "SUCESSO: tela de entrada com a foto do mundo ao fundo e a câmera parada; ao jogar, a câmera volta\n"
                    : $"FALHA: câmera parada no menu = {stopped}, câmera de volta ao jogar = {back}\n") + "Servidores: " + servers + "\n");
            Application.Quit(ok ? 0 : 1);
        }
    }
}
