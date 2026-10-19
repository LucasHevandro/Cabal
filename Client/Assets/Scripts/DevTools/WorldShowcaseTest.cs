using System;
using System.Collections;
using System.IO;
using Game.Client.Net;
using Game.Client.World;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Vitrine do mundo: <c>-worldtest pasta</c> fotografa a praça da vila (os cantos dos NPCs), a ponte sobre o rio e os
    /// campos, cada lugar de dia, no pôr do sol e à noite (hora forçada em <see cref="Atmosphere.ForcedTime"/>). Depois fecha.
    /// </summary>
    public sealed class WorldShowcaseTest : MonoBehaviour
    {
        private string _folder = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-worldtest");
            if (index < 0) return;
            GameMode.SetOffline();
            var test = new GameObject("Vitrine do mundo").AddComponent<WorldShowcaseTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "worldtest");
            Directory.CreateDirectory(test._folder);
        }

        private IEnumerator Start()
        {
            yield return null;
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var ns = behaviour.GetType().Namespace ?? "";
                if (behaviour != this && (ns.StartsWith("Game.Client.UI") || ns.StartsWith("Game.Client.Player")))
                    behaviour.enabled = false;
            }
            var camera = Camera.main;
            if (camera == null) yield break;
            foreach (var orbit in camera.GetComponents<MonoBehaviour>()) orbit.enabled = false;

            var shots = new[]
            {
                ("praca", new Vector3(-3f, 6f, -17f), new Vector3(1f, 1f, 2f)),
                ("ponte", new Vector3(9f, 4.5f, Ground.RiverZ(9f) - 9f), new Vector3(0f, 0f, Ground.RiverZ(0f))),
                ("campos", new Vector3(-14f, 7f, 20f), new Vector3(2f, 1f, 44f)),
                ("mata", new Vector3(34f, 3.5f, 72f), new Vector3(46f, 1.5f, 86f)),
                ("ruinas", new Vector3(-34f, 4f, 112f), new Vector3(-46f, 1.5f, 126f)),
            };
            var times = new[] { ("dia", 0.3f), ("por_do_sol", 0.645f), ("noite", 0.8f) };
            foreach (var (name, eye, look) in shots)
            {
                camera.transform.position = Ground.Point(eye.x, eye.z) + Vector3.up * eye.y;
                camera.transform.LookAt(Ground.Point(look.x, look.z) + Vector3.up * look.y);
                foreach (var (label, hour) in times)
                {
                    Atmosphere.ForcedTime = hour;
                    yield return new WaitForSeconds(1.5f); // tempo para o clima da região aparecer
                    ScreenCapture.CaptureScreenshot(Path.Combine(_folder, $"{name}-{label}.png"));
                    yield return new WaitForSeconds(0.3f);
                }
            }
            File.WriteAllText(Path.Combine(_folder, "relatorio.txt"), $"SUCESSO: {shots.Length * times.Length} vistas\n");
            Application.Quit();
        }
    }
}
