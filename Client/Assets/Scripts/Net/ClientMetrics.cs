using System.Collections.Generic;
using Game.Client.UI;
using Game.Shared.Accounts;
using Game.Shared.Net;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>
    /// Métricas do cliente para o teste fechado (Docs/metricas.md): passos do tutorial, FPS médio a cada 5 minutos e
    /// erros do jogo (até 5 diferentes por sessão). Só online, com conta, e se a opção estiver ligada
    /// (<see cref="GameSettings.ClientMetrics"/>). Fica no mesmo objeto da <see cref="NetworkSession"/>.
    /// </summary>
    public sealed class ClientMetrics : MonoBehaviour
    {
        private const float FpsInterval = 300f;
        private const int MaxErrors = 5;

        private readonly HashSet<string> _errorsSent = new HashSet<string>();
        private float _fpsTime;
        private int _fpsFrames;

        /// <summary>Manda um relatório agora (ignorado offline, como convidado ou com a opção desligada).</summary>
        public static void Report(string kind, float value, string text = "")
        {
            var session = NetworkSession.Instance;
            if (!GameSettings.ClientMetrics || !GameMode.IsOnline || string.IsNullOrEmpty(GameMode.Token)) return;
            if (session == null || !session.IsWelcomed) return;
            session.Send(new ClientReport { Kind = kind, Value = value, Text = text ?? "" });
        }

        private void OnEnable() => Application.logMessageReceived += OnLog;

        private void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            // Saindo: manda o pedaço que faltou (se jogou pelo menos 1 minuto desde o último).
            if (_fpsTime >= 60f) SendFps();
        }

        private void Update()
        {
            var session = NetworkSession.Instance;
            if (session == null || !session.IsWelcomed) return;
            _fpsTime += Time.unscaledDeltaTime;
            _fpsFrames++;
            if (_fpsTime >= FpsInterval) SendFps();
        }

        private void SendFps()
        {
            if (_fpsTime > 0) Report(ClientReportRules.Fps, _fpsFrames / _fpsTime, $"{Screen.width}x{Screen.height} q{QualitySettings.GetQualityLevel()}");
            _fpsTime = 0;
            _fpsFrames = 0;
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (_errorsSent.Count >= MaxErrors || !_errorsSent.Add(message)) return;
            // Primeira linha da pilha ajuda a achar o lugar; o texto é cortado pelo servidor.
            var where = string.IsNullOrEmpty(stackTrace) ? "" : " @ " + stackTrace.Split('\n')[0].Trim();
            Report(ClientReportRules.Error, 0, message + where);
        }
    }
}
