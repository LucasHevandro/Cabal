using System;
using System.Collections;
using System.IO;
using Game.Client.Combat;
using Game.Client.Net;
using Game.Shared.Net;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Verificação automática do modo online. Rodar com
    /// <c>-connect host:porta -name Nome -onlinetest pasta</c>: conecta, espera ver as entidades do servidor,
    /// tira screenshot, grava um relatório (SUCESSO/FALHA) e fecha. <c>-onlinewait N</c> mantém o cliente
    /// aberto N segundos antes de fechar (para outro cliente de teste enxergá-lo).
    /// </summary>
    public sealed class OnlineSmokeTest : MonoBehaviour
    {
        private string _folder;
        private float _stayOnline;
        private StreamWriter _report;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-onlinetest");
            if (index < 0 || !GameMode.IsOnline) return;

            var test = new GameObject("OnlineSmokeTest").AddComponent<OnlineSmokeTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "onlinetest");
            var wait = Array.IndexOf(args, "-onlinewait");
            if (wait >= 0 && wait + 1 < args.Length) float.TryParse(args[wait + 1], out test._stayOnline);
            DontDestroyOnLoad(test.gameObject);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_folder);
            _report = new StreamWriter(Path.Combine(_folder, "relatorio.txt"));
            Log($"Teste online: {GameMode.PlayerName} -> {GameMode.Host}:{GameMode.Port}");

            var giveUpAt = Time.realtimeSinceStartup + 15f;
            NetworkSession session = null;
            while (Time.realtimeSinceStartup < giveUpAt)
            {
                session = NetworkSession.Instance;
                if (session != null && !string.IsNullOrEmpty(session.Error)) break;
                if (session != null && session.IsWelcomed && session.Clock.IsSynced && session.Views.Count > 0) break;
                yield return null;
            }

            if (session == null || !session.IsWelcomed)
            {
                Finish($"FALHA: não entrou no mundo ({session?.Error ?? "sem NetworkSession"})");
                yield break;
            }

            // Dá tempo de chegarem alguns snapshots e a interpolação andar.
            yield return new WaitForSecondsRealtime(1.5f);

            var monsters = 0;
            var players = 0;
            foreach (var view in session.Views.Values)
            {
                if (view.Kind == EntityKind.Monster) monsters++;
                else players++;
            }
            Log($"Entrou como id {session.PlayerId}; RTT {session.Clock.LastRtt * 1000:0} ms; offset {session.Clock.Offset:0.000} s");
            Log($"Vendo {monsters} monstros e {players} outros jogadores");

            // Seleciona o monstro mais perto para a foto mostrar o anel e a plaquinha.
            var selector = PlayerCombat.Local != null ? PlayerCombat.Local.GetComponent<TargetSelector>() : null;
            if (selector != null) selector.SelectNext();

            Capture("1-online");
            yield return new WaitForEndOfFrame();
            yield return null;

            if (_stayOnline > 0)
            {
                Log($"Ficando online por {_stayOnline:0} s");
                yield return new WaitForSecondsRealtime(_stayOnline);
                players = 0;
                foreach (var view in session.Views.Values) if (view.Kind == EntityKind.Player) players++;
                Log($"Ao sair: vendo {players} outros jogadores");
                Capture("2-antes-de-sair");
                yield return new WaitForEndOfFrame();
                yield return null;
            }

            Finish(monsters > 0
                ? $"SUCESSO: online com {monsters} monstros e {players} jogadores à vista"
                : "FALHA: nenhum monstro recebido do servidor");
        }

        private void Capture(string name)
        {
            var path = Path.Combine(_folder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log($"Screenshot: {path}");
        }

        private void Log(string line)
        {
            Debug.Log("[OnlineTest] " + line);
            _report?.WriteLine(line);
            _report?.Flush();
        }

        private void Finish(string result)
        {
            Log(result);
            StartCoroutine(QuitSoon(result.StartsWith("SUCESSO") ? 0 : 1));
        }

        private IEnumerator QuitSoon(int code)
        {
            yield return new WaitForSecondsRealtime(1f);
            _report?.Dispose();
            _report = null;
            Application.Quit(code);
        }
    }
}
