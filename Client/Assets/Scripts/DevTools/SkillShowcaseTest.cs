using System;
using System.Collections;
using System.IO;
using Game.Client.Fx;
using Game.Client.Net;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Progression;
using UnityEngine;

namespace Game.Client.DevTools
{
    /// <summary>
    /// Vitrine das skills das três classes no jogo de verdade: <c>-skilltest pasta</c> põe o modelo da classe de lado para a câmera,
    /// com um alvo na frente, e para cada skill toca o golpe (animação, rastro, carga), o projétil e o impacto, com
    /// capturas no meio do golpe e no acerto. Metade das skills sai como Excellent (rastro dourado). Depois fecha.
    /// </summary>
    public sealed class SkillShowcaseTest : MonoBehaviour
    {
        private string _folder = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-skilltest");
            if (index < 0) return;
            GameMode.SetOffline();
            var test = new GameObject("Vitrine das skills").AddComponent<SkillShowcaseTest>();
            test._folder = index + 1 < args.Length ? args[index + 1] : Path.Combine(Application.persistentDataPath, "skilltest");
            Directory.CreateDirectory(test._folder);
        }

        private IEnumerator Start()
        {
            yield return null;
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                var ns = behaviour.GetType().Namespace ?? "";
                if (behaviour != this && (ns.StartsWith("Game.Client.UI") || ns.StartsWith("Game.Client.Combat") || ns.StartsWith("Game.Client.Player")))
                    behaviour.enabled = false;
            }
            if (Camera.main != null) Camera.main.gameObject.SetActive(false);

            var spot = World.Ground.Point(0, 14);
            var target = new GameObject("Alvo").transform;
            target.position = spot + new Vector3(0, 0, 3.2f);
            target.rotation = Quaternion.LookRotation(spot - target.position);
            VisualCatalog.Spawn(VisualCatalog.Monster, "esqueleto_guerreiro", target);

            var camera = new GameObject("Câmera da vitrine").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 40;
            camera.transform.position = spot + new Vector3(6.5f, 2.4f, 1.6f);
            camera.transform.LookAt(spot + new Vector3(0, 1.1f, 1.6f));

            var report = new System.Text.StringBuilder();
            var shot = 0;
            foreach (var characterClass in ClassInfo.All)
            {
                var holder = new GameObject(characterClass.ToString());
                holder.transform.position = spot;
                holder.transform.rotation = Quaternion.LookRotation(target.position - spot);
                var model = VisualCatalog.Spawn(VisualCatalog.Class, ClassInfo.TypeId(characterClass), holder.transform);
                yield return new WaitForSeconds(0.5f);
                var skills = ClassCatalog.SkillsFor(characterClass);
                for (var i = 0; i < skills.Count; i++)
                {
                    var skill = skills[i].Skill;
                    var move = SkillMoves.For(skill.Id);
                    ComboGrade? grade = i % 2 == 1 ? ComboGrade.Excellent : ComboGrade.Good;
                    var pace = grade == ComboGrade.Excellent ? 1.15f : 1f;
                    var impact = (move != null ? move.FirstImpact : 0.2f) / pace;
                    var started = Time.time;
                    VisualAnimator.Attack(holder.transform, AttackStyle.For(skill), pace);
                    WeaponFx.Play(holder.transform, skill, grade);
                    var arrived = !SkillFx.IsProjectile(skill);
                    var arrivedAt = 0f;

                    // Meio do golpe (preparação ou carga).
                    yield return new WaitForSeconds(impact * 0.6f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(_folder, $"{shot++:00}-{skill.Id}-a-preparo.png"));

                    yield return new WaitForSeconds(Mathf.Max(0f, impact - (Time.time - started)));
                    if (SkillFx.IsProjectile(skill))
                    {
                        SkillFx.Launch(skill, holder.transform, target, Vector3.up * 1.1f, grade, () =>
                        {
                            arrived = true;
                            arrivedAt = Time.time;
                        }, WeaponFx.TipOf(holder.transform));
                        var deadline = Time.time + 3f;
                        while (!arrived && Time.time < deadline) yield return null;
                    }
                    else
                    {
                        SkillFx.OnCast(skill, holder.transform);
                        arrivedAt = Time.time;
                    }
                    SkillFx.OnImpact(skill, target.position + Vector3.up * 1.1f, grade, false);
                    yield return new WaitForSeconds(0.08f);
                    ScreenCapture.CaptureScreenshot(Path.Combine(_folder, $"{shot++:00}-{skill.Id}-b-acerto.png"));
                    report.AppendLine($"{skill.Id}: impacto {impact:0.00}s, chegada {(arrivedAt - started):0.00}s, {(move != null ? move.Steps.Length : 0)} passo(s)");
                    yield return new WaitForSeconds(1.1f);
                }
                Destroy(holder);
            }
            File.WriteAllText(Path.Combine(_folder, "relatorio.txt"), report + $"SUCESSO: {shot / 2} skills\n");
            Application.Quit();
        }
    }
}
