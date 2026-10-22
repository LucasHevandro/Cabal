using System;
using System.Collections;
using System.IO;
using Game.Client.Net;
using Game.Client.Player;
using Game.Client.World;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Travessia da ponte: <c>-bridgetest pasta</c> leva o jogador (offline) de uma margem à outra pela ponte, anota a
    /// altura dos pés contra a das tábuas e fotografa no meio. Falha se ele afundar nas tábuas. Depois fecha.
    /// </summary>
    public sealed class BridgeWalkTest : MonoBehaviour
    {
        private string _folder = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-bridgetest");
            if (index < 0) return;
            GameMode.SetOffline();
            var test = new GameObject("Travessia da ponte").AddComponent<BridgeWalkTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "bridgetest");
            Directory.CreateDirectory(test._folder);
        }

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(1f);
            var motor = FindFirstObjectByType<PlayerMotor>();
            var controller = motor != null ? motor.GetComponent<CharacterController>() : null;
            var report = new System.Text.StringBuilder();
            if (controller == null)
            {
                File.WriteAllText(Path.Combine(_folder, "relatorio.txt"), "FALHA: sem jogador\n");
                Application.Quit();
                yield break;
            }

            var river = Ground.RiverZ(0f);
            motor.Teleport(new Vector3(0.6f, 0f, river - 9f));
            yield return new WaitForSeconds(0.5f);
            var worst = 0f;
            var shot = false;
            var camera = Camera.main;
            while (motor.transform.position.z < river + 9f)
            {
                controller.Move(Vector3.forward * 4f * Time.deltaTime);
                var feet = motor.transform.position;
                var deck = Ground.BridgeDeck(feet.x, feet.z);
                if (!float.IsNegativeInfinity(deck))
                {
                    // Um quadro de atraso: a gravidade puxa e o motor devolve às tábuas no Update seguinte.
                    worst = Mathf.Max(worst, deck - feet.y);
                    if (!shot && feet.z > river && camera != null)
                    {
                        foreach (var behaviour in camera.GetComponents<MonoBehaviour>()) behaviour.enabled = false;
                        camera.transform.position = feet + new Vector3(4.5f, 1.6f, -2.5f);
                        camera.transform.LookAt(feet + Vector3.up * 0.6f);
                        yield return null;
                        ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "meio-da-ponte.png"));
                        shot = true;
                    }
                }
                report.AppendLine($"z {feet.z - river:0.00}  pés {feet.y:0.000}  tábua {(float.IsNegativeInfinity(deck) ? "-" : deck.ToString("0.000"))}");
                yield return null;
            }
            // Pela lateral não sai: no meio da ponte, tenta andar 4 m para cada lado (o corrimão segura).
            var widest = 0f;
            foreach (var side in new[] { 1f, -1f })
            {
                motor.Teleport(new Vector3(0f, 0f, river));
                yield return new WaitForSeconds(0.2f);
                for (var t = 0f; t < 1f; t += Time.deltaTime)
                {
                    controller.Move(Vector3.right * side * 4f * Time.deltaTime);
                    yield return null;
                }
                widest = Mathf.Max(widest, Mathf.Abs(motor.transform.position.x));
            }

            // Vista da margem: a água passando por baixo da ponte.
            if (camera != null)
            {
                camera.transform.position = new Vector3(7f, 2.2f, river - 7f);
                camera.transform.LookAt(new Vector3(0f, -0.3f, river));
                yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(_folder, "margem.png"));
                yield return null;
            }

            // As tábuas de verdade (a malha na cena) têm que estar onde o jogador pisa: o topo do arco igual ao da fórmula.
            var planks = 0f;
            foreach (var renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (renderer.name.StartsWith("Tábuas")) planks = renderer.bounds.max.y;
            var gap = Mathf.Abs(planks - Ground.BridgeDeck(0f, river));

            var ok = worst < 0.08f && widest < Ground.BridgeHalfWidth && gap < 0.03f;
            report.Insert(0, (ok ? "SUCESSO" : "FALHA") + $": afundou no máximo {worst:0.000} m nas tábuas; " +
                             $"de lado foi até {widest:0.00} m do meio (corrimão em {Ground.BridgeHalfWidth:0.00} m); " +
                             $"topo das tábuas {planks:0.000} contra {Ground.BridgeDeck(0f, river):0.000} onde se pisa\n");
            File.WriteAllText(Path.Combine(_folder, "relatorio.txt"), report.ToString());
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }
    }
}
