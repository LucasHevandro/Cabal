using System;
using System.Collections;
using System.IO;
using Game.Client.Fx;
using Game.Client.Net;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Conferência dos modelos animados no jogo de verdade (a deformação da malha só acontece no loop do jogo):
    /// <c>-posetest pasta</c> põe as classes e os NPCs lado a lado na vila, de frente para uma câmera própria, e tira
    /// uma captura de cada estado do Animator (parado, andar, correr, os 10 golpes, dano, esquiva e morte). Depois fecha.
    /// </summary>
    public sealed class PoseSheetTest : MonoBehaviour
    {
        private static readonly string[] Models =
        {
            "class/blade", "class/arcanist", "npc/anciao_doran", "npc/ferreira_ilsa", "npc/guarda_volumes_bento",
            "npc/mercadora_lia", "npc/guardia_selene",
        };

        private string _folder = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-posetest");
            if (index < 0) return;
            GameMode.SetOffline();
            var test = new GameObject("Teste de poses").AddComponent<PoseSheetTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "posetest");
            Directory.CreateDirectory(test._folder);
        }

        private IEnumerator Start()
        {
            yield return null;
            // Sem HUD e sem a câmera do jogo: só os modelos.
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var ns = behaviour.GetType().Namespace ?? "";
                if (behaviour != this && (ns.StartsWith("Game.Client.UI") || ns.StartsWith("Game.Client.Combat") || ns.StartsWith("Game.Client.Player")))
                    behaviour.enabled = false;
            }
            if (Camera.main != null) Camera.main.gameObject.SetActive(false);

            var animators = new Animator[Models.Length];
            var origin = World.Ground.Point(0, 12);
            for (var i = 0; i < Models.Length; i++)
            {
                var slash = Models[i].IndexOf('/');
                var holder = new GameObject(Models[i]);
                holder.transform.position = World.Ground.Point(origin.x + (i - (Models.Length - 1) / 2f) * 2.2f, origin.z);
                holder.transform.rotation = Quaternion.Euler(0, 200, 0);
                var model = VisualCatalog.Spawn(Models[i].Substring(0, slash), Models[i].Substring(slash + 1), holder.transform);
                if (model == null) continue;
                var visual = model.GetComponent<VisualAnimator>();
                if (visual != null) visual.enabled = false; // o teste controla o Animator
                animators[i] = model.GetComponentInChildren<Animator>();
            }

            var camera = new GameObject("Câmera das poses").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 32;
            camera.transform.position = origin + new Vector3(0, 2.2f, -13f);
            camera.transform.LookAt(origin + Vector3.up * 0.9f);

            var states = new[]
            {
                ("Locomoção", 0f), ("Locomoção", 2.2f), ("Locomoção", 6f),
                ("Golpe 0", -1f), ("Golpe 1", -1f), ("Golpe 2", -1f), ("Golpe 3", -1f), ("Golpe 4", -1f), ("Golpe 5", -1f),
                ("Golpe 6", -1f), ("Golpe 7", -1f), ("Golpe 8", -1f), ("Golpe 9", -1f), ("Dano", -1f), ("Esquiva", -1f), ("Morte", -1f),
            };
            for (var s = 0; s < states.Length; s++)
            {
                var (name, speed) = states[s];
                foreach (var animator in animators)
                {
                    if (animator == null) continue;
                    var full = FindState(animator, name);
                    animator.speed = 1f;
                    if (speed >= 0) animator.SetFloat("Speed", speed);
                    animator.Play(full, 0, name == "Morte" ? 0.98f : 0.42f);
                    animator.Update(0f);
                    animator.speed = 0f; // congela a pose
                }
                yield return new WaitForSeconds(0.25f);
                var file = Path.Combine(_folder, $"{s:00}-{name.Replace(' ', '_')}{(speed > 0 ? "_" + speed : "")}.png");
                ScreenCapture.CaptureScreenshot(file);
                yield return new WaitForSeconds(0.35f);
            }
            File.WriteAllText(Path.Combine(_folder, "relatorio.txt"), "SUCESSO: " + states.Length + " poses\n");
            Application.Quit();
        }

        /// <summary>Hash do estado cujo nome começa com <paramref name="prefix"/> (os golpes têm o nome do clipe junto).</summary>
        private static int FindState(Animator animator, string prefix)
        {
            var controller = animator.runtimeAnimatorController;
            // Os nomes completos dos golpes são "Golpe N: Clipe"; procura pelo prefixo tentando os clipes conhecidos.
            foreach (var clip in controller.animationClips)
            {
                var hash = Animator.StringToHash($"Base Layer.{prefix}: {clip.name}");
                if (animator.HasState(0, hash)) return hash;
            }
            return Animator.StringToHash($"Base Layer.{prefix}");
        }
    }
}
