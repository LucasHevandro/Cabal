using System;
using System.Collections;
using System.IO;
using Game.Client.Combat;
using Game.Shared.Combat;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Demo automática para verificar o protótipo sem ninguém no teclado.
    /// Só roda se o executável for aberto com <c>-autodemo &lt;pasta de saída&gt;</c>:
    /// seleciona um alvo, faz um combo apertando no centro da janela, usa o finalizador,
    /// salva screenshots e um relatório na pasta indicada e fecha o jogo.
    /// </summary>
    public sealed class AutoComboDemo : MonoBehaviour
    {
        private const string Flag = "-autodemo";
        private const int HitsBeforeFinisher = 6;

        private string _outputFolder;
        private StreamWriter _report;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, Flag);
            if (index < 0) return;

            var folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "autodemo");
            var host = new GameObject("AutoComboDemo");
            host.AddComponent<AutoComboDemo>()._outputFolder = folder;
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_outputFolder);
            _report = new StreamWriter(Path.Combine(_outputFolder, "relatorio.txt"));
            Log($"Demo iniciada em {DateTime.Now:yyyy-MM-dd HH:mm:ss}, Unity {Application.unityVersion}");

            yield return new WaitForSecondsRealtime(1.5f);

            var combat = FindAnyObjectByType<PlayerCombat>();
            var selector = combat != null ? combat.GetComponent<TargetSelector>() : null;
            if (combat == null || selector == null)
            {
                Finish("FALHA: PlayerCombat ou TargetSelector não encontrados na cena");
                yield break;
            }

            // Aproxima o jogador do boneco mais perto para as skills corpo a corpo alcançarem.
            selector.SelectNext();
            var target = selector.Current;
            if (target == null)
            {
                Finish("FALHA: nenhum alvo selecionado");
                yield break;
            }
            Teleport(combat.transform, target.transform.position + (combat.transform.position - target.transform.position).normalized * 2f);
            Log($"Alvo: {target.DisplayName}, HP {target.Hp.Current}/{target.Hp.Max}, SP {combat.Sp.Current}");
            yield return new WaitForSecondsRealtime(0.5f);
            Capture("1-antes-do-combo");

            combat.RequestStartCombo();
            if (combat.Combo.State != ComboState.Active)
            {
                Finish($"FALHA: combo não iniciou ({combat.Message})");
                yield break;
            }
            Log($"Combo iniciado; SP agora {combat.Sp.Current}");

            var hpBefore = target.Hp.Current;
            while (combat.Combo.State == ComboState.Active && combat.Combo.HitCount < HitsBeforeFinisher)
            {
                // Espera o marcador chegar ao centro da janela e aperta a skill 1.
                while (combat.Combo.State == ComboState.Active &&
                       combat.Combo.GetCursor(Time.time) < combat.Combo.Config.WindowCenter)
                    yield return null;
                if (combat.Combo.State != ComboState.Active) break;

                combat.RequestSkill(0);
                Log($"Aperto {combat.Combo.HitCount}: {combat.LastGrade}, janela {combat.Combo.WindowWidth:0.000}, " +
                    $"barra {combat.Combo.BarDurationSeconds:0.000}s, bônus {combat.Combo.ChainBonus:0.00}");
                if (combat.Combo.HitCount == 3) Capture("2-durante-o-combo");
                yield return null;
            }

            if (combat.Combo.HitCount < HitsBeforeFinisher)
            {
                Finish($"FALHA: combo terminou com {combat.Combo.HitCount} acertos ({combat.Combo.LastEndReason}; {combat.Message})");
                yield break;
            }

            yield return new WaitForSecondsRealtime(0.2f);
            combat.RequestFinisher();
            Log($"Finalizador: {combat.Message}; fim do combo = {combat.Combo.LastEndReason}");
            yield return new WaitForSecondsRealtime(0.15f);
            Capture("3-finalizador");

            var dealt = hpBefore - target.Hp.Current;
            var ok = combat.Combo.LastEndReason == ComboEndReason.Finished && dealt > 0;
            Finish(ok
                ? $"SUCESSO: {HitsBeforeFinisher} acertos + finalizador, {dealt} de dano no alvo"
                : $"FALHA: fim = {combat.Combo.LastEndReason}, dano {dealt}");
        }

        private static void Teleport(Transform player, Vector3 position)
        {
            var controller = player.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            player.position = new Vector3(position.x, player.position.y, position.z);
            if (controller != null) controller.enabled = true;
        }

        private void Capture(string name)
        {
            var path = Path.Combine(_outputFolder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log($"Screenshot: {path}");
        }

        private void Log(string line)
        {
            Debug.Log("[AutoDemo] " + line);
            _report?.WriteLine(line);
            _report?.Flush();
        }

        private void Finish(string result)
        {
            Log(result);
            StartCoroutine(QuitSoon(result.StartsWith("SUCESSO") ? 0 : 1));
        }

        private IEnumerator QuitSoon(int exitCode)
        {
            // Dá tempo para a última screenshot ser gravada.
            yield return new WaitForSecondsRealtime(1f);
            _report?.Dispose();
            _report = null;
            Application.Quit(exitCode);
        }
    }
}
