using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Monstros e bichos animados do Quaternius (Ultimate Monsters e Ultimate Animated Animals, CC0), em
    /// <c>Assets/Art/Quaternius</c>. Monta o Animator com os parâmetros do <see cref="Fx.VisualAnimator"/> (Speed, Attack,
    /// Hit, Dead) e grava <c>Resources/Visuals/monster/&lt;id&gt;.prefab</c> no lugar do bicho feito em código. Os bichos de
    /// enfeite dos campos (só parados e comendo) vão para <c>Resources/Visuals/fauna</c>.
    /// Menu: Jogo &gt; Visuais &gt; Montar monstros (Quaternius).
    /// </summary>
    internal static class QuaterniusBuilder
    {
        private const string Root = "Assets/Art/Quaternius";
        private const string Generated = Root + "/Gerado";
        private const string MonsterFolder = "Assets/Resources/Visuals/monster";
        private const string FaunaFolder = "Assets/Resources/Visuals/fauna";

        private sealed class Spec
        {
            public string Id = "";
            public string Model = "";
            public float Height;
            public string Idle = "Idle";
            public string Walk = "Walk";
            public string Run = "Run";
            public string Attack = "Punch";
            public string Hit = "HitReact";
            public string Death = "Death";

            /// <summary>Velocidade (m/s) em que o andar e a corrida batem com o chão.</summary>
            public float WalkSpeed = 1.6f;
            public float RunSpeed = 4.5f;
        }

        private static readonly Spec[] Monsters =
        {
            new Spec { Id = "coelho_saltador", Model = "Monstros/Bunny", Height = 1.15f, WalkSpeed = 1.3f, RunSpeed = 3.5f },
            new Spec
            {
                Id = "lobo_cinzento", Model = "Animais/Wolf", Height = 1.25f, Walk = "Walk", Run = "Gallop", Attack = "Attack",
                Hit = "Idle_HitReact_Left", WalkSpeed = 1.8f, RunSpeed = 5f,
            },
            // O javali furioso vira o Touro Furioso (os pacotes não têm javali; os ids do jogo ficam os mesmos).
            new Spec
            {
                Id = "javali_furioso", Model = "Animais/Bull", Height = 1.6f, Walk = "Walk", Run = "Gallop", Attack = "Attack_Headbutt",
                Hit = "Idle_HitReact_Left", WalkSpeed = 1.6f, RunSpeed = 5.5f,
            },
        };

        private static readonly (string Id, string Model, float Height)[] Fauna =
        {
            ("cervo", "Animais/Deer", 1.7f),
            ("veado", "Animais/Stag", 2.1f),
            ("raposa", "Animais/Fox", 0.8f),
        };

        [MenuItem("Jogo/Visuais/Montar monstros (Quaternius)")]
        public static void Build()
        {
            if (!Directory.Exists(Root))
            {
                Debug.LogWarning("[Quaternius] Pasta Assets/Art/Quaternius não encontrada: ficam os bichos feitos em código.");
                return;
            }
            Directory.CreateDirectory(Generated);
            Directory.CreateDirectory(MonsterFolder);
            Directory.CreateDirectory(FaunaFolder);
            foreach (var spec in Monsters) BuildMonster(spec);
            foreach (var (id, model, height) in Fauna) BuildFauna(id, model, height);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Quaternius] {Monsters.Length} monstros e {Fauna.Length} bichos de enfeite montados.");
        }

        /// <summary>Animações em loop (andar, correr, parado, comer) e o resto uma vez só.</summary>
        private static void PrepareImport(string path)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var changed = false;
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                changed = true;
            }
            var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            foreach (var clip in clips)
            {
                var name = clip.name.Split('|').Last();
                var loop = name is "Idle" or "Idle_2" or "Walk" or "Run" or "Gallop" or "Eating" or "Flying_Idle" or "Fast_Flying" or "Idle_Headlow";
                if (clip.loopTime == loop) continue;
                clip.loopTime = loop;
                changed = true;
            }
            if (!changed) return;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
        }

        private static Dictionary<string, AnimationClip> Clips(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"))
                .GroupBy(c => c.name.Split('|').Last()).ToDictionary(g => g.Key, g => g.First());

        private static void BuildMonster(Spec spec)
        {
            var path = $"{Root}/{spec.Model}.fbx";
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[Quaternius] Falta {path}");
                return;
            }
            PrepareImport(path);
            var clips = Clips(path);
            AnimationClip Get(string name) => clips.TryGetValue(name, out var clip) ? clip : clips.Values.First();

            var controllerPath = $"{Generated}/{spec.Id}.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            var machine = controller.layers[0].stateMachine;

            // Parado → andando → correndo, pela velocidade em m/s.
            var move = machine.AddState("Movimento");
            var tree = new BlendTree { name = "Andar", blendParameter = "Speed", useAutomaticThresholds = false, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(tree, controller);
            tree.AddChild(Get(spec.Idle), 0f);
            tree.AddChild(Get(spec.Walk), spec.WalkSpeed);
            tree.AddChild(Get(spec.Run), spec.RunSpeed);
            move.motion = tree;
            machine.defaultState = move;

            AnimatorState Once(string name, AnimationClip clip, string condition, bool fromAny)
            {
                var state = machine.AddState(name);
                state.motion = clip;
                var enter = fromAny ? machine.AddAnyStateTransition(state) : move.AddTransition(state);
                enter.hasExitTime = false;
                enter.duration = 0.06f;
                enter.AddCondition(AnimatorConditionMode.If, 0, condition);
                if (condition != "Dead") enter.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                enter.canTransitionToSelf = false;
                var back = state.AddTransition(move);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.12f;
                return state;
            }
            Once("Golpe", Get(spec.Attack), "Attack", true);
            Once("Dano", Get(spec.Hit), "Hit", true);

            var death = machine.AddState("Morte");
            death.motion = Get(spec.Death);
            var toDeath = machine.AddAnyStateTransition(death);
            toDeath.hasExitTime = false;
            toDeath.duration = 0.1f;
            toDeath.canTransitionToSelf = false;
            toDeath.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            var revive = death.AddTransition(move);
            revive.hasExitTime = false;
            revive.duration = 0.1f;
            revive.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");

            SavePrefab(path, spec.Id, spec.Height, controller, $"{MonsterFolder}/{spec.Id}.prefab");
        }

        private static void BuildFauna(string id, string model, float height)
        {
            var path = $"{Root}/{model}.fbx";
            if (!File.Exists(path)) return;
            PrepareImport(path);
            var clips = Clips(path);
            var controllerPath = $"{Generated}/fauna_{id}.controller";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var machine = controller.layers[0].stateMachine;
            // Fica parado e de vez em quando abaixa para comer.
            var idle = machine.AddState("Parado");
            idle.motion = clips.TryGetValue("Idle", out var i) ? i : clips.Values.First();
            var eat = machine.AddState("Comendo");
            eat.motion = clips.TryGetValue("Eating", out var e) ? e : idle.motion;
            machine.defaultState = idle;
            var toEat = idle.AddTransition(eat);
            toEat.hasExitTime = true;
            toEat.exitTime = 2f;
            toEat.duration = 0.4f;
            var toIdle = eat.AddTransition(idle);
            toIdle.hasExitTime = true;
            toIdle.exitTime = 1f;
            toIdle.duration = 0.4f;
            SavePrefab(path, id, height, controller, $"{FaunaFolder}/{id}.prefab");
        }

        private static void SavePrefab(string modelPath, string id, float height, RuntimeAnimatorController controller, string prefabPath)
        {
            var root = new GameObject(id);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.transform.SetParent(root.transform, false);

            // Escala pela altura pedida, com os pés no chão.
            var renderers = model.GetComponentsInChildren<Renderer>();
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            var scale = height / Mathf.Max(0.01f, bounds.size.y);
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = new Vector3(0, -bounds.min.y * scale, 0);

            if (!model.TryGetComponent<Animator>(out var animator)) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>()) skinned.updateWhenOffscreen = false;

            AssetDatabase.DeleteAsset(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            Object.DestroyImmediate(root);
        }
    }
}
