using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.Client.Fx;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Game.Client.Editor
{
    /// <summary>
    /// Personagens do KayKit (Kay Lousberg, CC0) no jogo: configura a importação, cria os materiais, o Animator
    /// Controller (andar/correr, 10 estilos de golpe, dano, esquiva e morte) e os prefabs que o
    /// <see cref="VisualCatalog"/> procura: <c>Visuals/class/blade|arcanist</c> e <c>Visuals/npc/&lt;id&gt;</c>.
    /// Menu: Jogo &gt; Visuais &gt; Montar personagens KayKit (ou -executeMethod ...KayKitBuilder.Build).
    /// Os arquivos originais ficam em Assets/Art/KayKit (licenças junto).
    /// </summary>
    internal static class KayKitBuilder
    {
        private const string Root = "Assets/Art/KayKit";
        private const string Characters = Root + "/Personagens";
        private const string Animations = Root + "/Animacoes";
        private const string Weapons = Root + "/Armas";
        private const string Generated = Root + "/Gerado";
        private const string Visuals = "Assets/Resources/Visuals";

        /// <summary>Altura de um personagem no jogo (a cápsula de colisão tem 2 m).</summary>
        private const float CharacterHeight = 1.9f;

        private sealed class Look
        {
            public string Model = "";
            public string RightHand = "";
            public string LeftHand = "";
            public string[] Hide = System.Array.Empty<string>();
            public float Height = CharacterHeight;

            /// <summary>Material no lugar do atlas (ex.: "fantasma" para o espectro) e cor dos olhos.</summary>
            public string Override = "";
            public string Eyes = "glow";
        }

        private static readonly Dictionary<string, Look> ClassLooks = new Dictionary<string, Look>
        {
            ["blade"] = new Look { Model = "Knight", RightHand = "sword_1handed", LeftHand = "shield_badge_color" },
            ["arcanist"] = new Look { Model = "Mage", RightHand = "staff" },
        };

        private static readonly Dictionary<string, Look> NpcLooks = new Dictionary<string, Look>
        {
            ["anciao_doran"] = new Look { Model = "Rogue_Hooded", LeftHand = "spellbook_open" },
            ["ferreira_ilsa"] = new Look { Model = "Barbarian", RightHand = "axe_1handed" },
            ["guarda_volumes_bento"] = new Look { Model = "Ranger", RightHand = "mug_full" },
            ["mercadora_lia"] = new Look { Model = "Rogue", RightHand = "dagger" },
            ["guardia_selene"] = new Look { Model = "Knight", RightHand = "sword_2handed", Hide = new[] { "Knight_HelmetVisor" } },
        };

        /// <summary>Monstros mortos-vivos (o pacote Skeletons); os bichos ficam no CreatureBuilder.</summary>
        private static readonly Dictionary<string, Look> MonsterLooks = new Dictionary<string, Look>
        {
            ["esqueleto_guerreiro"] = new Look { Model = "Skeleton_Warrior", RightHand = "Skeleton_Blade", LeftHand = "Skeleton_Shield_Small_A", Height = 2f },
            ["esqueleto_arqueiro"] = new Look { Model = "Skeleton_Rogue", RightHand = "Skeleton_Crossbow", Height = 1.9f },
            ["cavaleiro_ossudo"] = new Look { Model = "Skeleton_Warrior", RightHand = "Skeleton_Axe", LeftHand = "Skeleton_Shield_Large_A", Height = 2.7f },
            ["servo_da_cripta"] = new Look { Model = "Skeleton_Minion", Height = 1.4f },
            ["senhor_da_cripta"] = new Look { Model = "Skeleton_Mage", RightHand = "Skeleton_Staff", Height = 4.4f, Eyes = "glow_roxo" },
            ["cavaleiro_caido"] = new Look { Model = "Skeleton_Warrior", RightHand = "Skeleton_Blade", LeftHand = "Skeleton_Shield_Large_A", Height = 2.3f },
            ["espectro"] = new Look { Model = "Skeleton_Mage", RightHand = "Skeleton_Staff", Height = 2.2f, Override = "fantasma", Eyes = "glow_roxo" },
            ["guardiao_das_ruinas"] = new Look { Model = "Skeleton_Warrior", RightHand = "Skeleton_Axe", LeftHand = "Skeleton_Shield_Large_A", Height = 4.6f, Eyes = "glow_vermelho" },
        };

        [MenuItem("Jogo/Visuais/Montar personagens KayKit")]
        public static void Build()
        {
            Directory.CreateDirectory(Generated);
            ConfigureImports();
            var materials = Materials();
            var controller = BuildController("Personagem", Clip("General", "Idle_A"), Clip("MovementBasic", "Walking_A"),
                Clip("MovementBasic", "Running_A"), Clip("General", "Death_A"));
            var skeleton = BuildController("Esqueleto", Clip("Special", "Skeletons_Idle"), Clip("Special", "Skeletons_Walking"),
                Clip("MovementBasic", "Running_A"), Clip("Special", "Skeletons_Death"));
            foreach (var (id, look) in ClassLooks) MakePrefab($"{Visuals}/class/{id}.prefab", look, materials, controller, weaponFx: true);
            foreach (var (id, look) in NpcLooks) MakePrefab($"{Visuals}/npc/{id}.prefab", look, materials, controller);
            foreach (var (id, look) in MonsterLooks) MakePrefab($"{Visuals}/monster/{id}.prefab", look, materials, skeleton);
            AssetDatabase.SaveAssets();
            Debug.Log($"[KayKit] {ClassLooks.Count} classes, {NpcLooks.Count} NPCs e {MonsterLooks.Count} monstros montados.");
        }

        // ===== Importação =====

        private static readonly string[] Looping = { "Idle", "Walking", "Running", "Blocking", "Skeletons_Walking" };

        private static void ConfigureImports()
        {
            foreach (var path in Directory.GetFiles(Root, "*.fbx", SearchOption.AllDirectories).Select(p => p.Replace('\\', '/')))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var changed = false;
                if (importer.animationType != ModelImporterAnimationType.Generic)
                {
                    importer.animationType = ModelImporterAnimationType.Generic;
                    changed = true;
                }
                var isAnimation = path.StartsWith(Animations);
                importer.importAnimation = isAnimation;
                if (isAnimation)
                {
                    var clips = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
                    foreach (var clip in clips)
                    {
                        var loop = Looping.Any(prefix => clip.name.StartsWith(prefix) || clip.name.Contains("_Idle"));
                        if (clip.loopTime != loop) changed = true;
                        clip.loopTime = loop;
                        clip.lockRootPositionXZ = true;
                        clip.keepOriginalPositionXZ = true;
                    }
                    importer.clipAnimations = clips;
                    changed = true;
                }
                if (changed) importer.SaveAndReimport();
            }
        }

        private static Dictionary<string, Material> Materials()
        {
            var result = new Dictionary<string, Material>();
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var texturePath in Directory.GetFiles(Characters, "*_texture.png"))
            {
                var name = Path.GetFileNameWithoutExtension(texturePath).Replace("_texture", "");
                var path = $"{Generated}/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader);
                    AssetDatabase.CreateAsset(material, path);
                }
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath.Replace('\\', '/')));
                material.SetColor("_BaseColor", Color.white);
                material.SetFloat("_Smoothness", 0.15f);
                material.enableInstancing = true;
                EditorUtility.SetDirty(material);
                result[name] = material;
            }
            // Olhos dos esqueletos.
            var glowPath = $"{Generated}/glow.mat";
            var glow = AssetDatabase.LoadAssetAtPath<Material>(glowPath);
            if (glow == null)
            {
                glow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(glow, glowPath);
            }
            glow.SetColor("_BaseColor", new Color(0.45f, 0.95f, 1f) * 2f);
            result["glow"] = glow;
            result["glow_roxo"] = Solid("glow_roxo", "Universal Render Pipeline/Unlit", new Color(0.8f, 0.45f, 1f) * 2f);
            result["glow_vermelho"] = Solid("glow_vermelho", "Universal Render Pipeline/Unlit", new Color(1f, 0.25f, 0.15f) * 2f);

            // Espectro: o atlas do esqueleto, roxo e translúcido.
            // Unlit: brilho próprio e sem sombra (o Lit translúcido com emissão estoura o compilador de shader).
            var ghost = Solid("fantasma", "Universal Render Pipeline/Unlit", new Color(0.8f, 0.65f, 1f, 0.55f));
            ghost.SetTexture("_BaseMap", result["skeleton"].GetTexture("_BaseMap"));
            ghost.SetFloat("_Surface", 1f);
            ghost.SetFloat("_Blend", 0f);
            ghost.SetFloat("_ZWrite", 0f);
            ghost.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ghost.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ghost.SetOverrideTag("RenderType", "Transparent");
            ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ghost.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            result["fantasma"] = ghost;
            return result;
        }

        /// <summary>Material do rastro da arma: partícula sem textura, aditiva (a cor vem do TrailRenderer).</summary>
        private static Material TrailMaterial()
        {
            var material = Solid("rastro", "Universal Render Pipeline/Particles/Unlit", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f); // aditivo
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetTexture("_BaseMap", null); // cor pura: o rastro some pela transparência do fim
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material Solid(string name, string shader, Color color)
        {
            var path = $"{Generated}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.shader != Shader.Find(shader)) material.shader = Shader.Find(shader);
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ===== Animator =====

        private static AnimationClip Clip(string file, string name)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{Animations}/Rig_Medium_{file}.fbx").OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == name);
            if (clip == null) throw new System.InvalidOperationException($"Animação {name} não encontrada em Rig_Medium_{file}.");
            return clip;
        }

        private static AnimatorController BuildController(string name, AnimationClip idle, AnimationClip walk, AnimationClip run, AnimationClip dying)
        {
            var path = $"{Generated}/{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("AttackKind", AnimatorControllerParameterType.Int);
            controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dodge", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = "AttackSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f,
            });
            var machine = controller.layers[0].stateMachine;

            // Parado → andar → correr pela velocidade (m/s) que o jogo passa.
            var locomotion = controller.CreateBlendTreeInController("Locomoção", out var tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idle, 0f);
            tree.AddChild(walk, 2.2f);
            tree.AddChild(run, 6f);
            machine.defaultState = locomotion;

            // Golpes: um estado por estilo (AttackStyle), todos bem rápidos (o combo pede resposta imediata).
            var attacks = new (int Style, string File, string Clip, float Seconds)[]
            {
                (AttackStyle.Slice, "CombatMelee", "Melee_1H_Attack_Slice_Horizontal", 0.55f),
                (AttackStyle.Chop, "CombatMelee", "Melee_1H_Attack_Chop", 0.55f),
                (AttackStyle.Stab, "CombatMelee", "Melee_1H_Attack_Stab", 0.6f),
                (AttackStyle.Spin, "CombatMelee", "Melee_2H_Attack_Spinning", 0.6f),
                (AttackStyle.JumpChop, "CombatMelee", "Melee_1H_Attack_Jump_Chop", 0.8f),
                (AttackStyle.Diagonal, "CombatMelee", "Melee_1H_Attack_Slice_Diagonal", 0.55f),
                (AttackStyle.MagicShoot, "CombatRanged", "Ranged_Magic_Shoot", 0.55f),
                (AttackStyle.MagicCast, "CombatRanged", "Ranged_Magic_Spellcasting", 0.6f),
                (AttackStyle.MagicRaise, "CombatRanged", "Ranged_Magic_Raise", 0.9f),
                (AttackStyle.Finisher, "CombatMelee", "Melee_2H_Attack_Chop", 0.75f),
                (AttackStyle.RangedShoot, "CombatRanged", "Ranged_2H_Shoot", 0.6f),
            };
            foreach (var attack in attacks)
            {
                var clip = Clip(attack.File, attack.Clip);
                var state = machine.AddState($"Golpe {attack.Style}: {attack.Clip}");
                state.motion = clip;
                state.speed = clip.length / attack.Seconds;
                var enter = machine.AddAnyStateTransition(state);
                enter.AddCondition(AnimatorConditionMode.If, 0, "Attack");
                enter.AddCondition(AnimatorConditionMode.Equals, attack.Style, "AttackKind");
                enter.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                Quick(enter, 0.06f);
                enter.canTransitionToSelf = true;
                Leave(state, locomotion, 0.85f);
            }

            // Golpe próprio de cada skill (SkillMoves): passos em sequência, no ritmo do AttackSpeed (Excellent acelera).
            for (var i = 0; i < SkillMoves.All.Count; i++)
            {
                var move = SkillMoves.All[i];
                var kind = SkillMoves.FirstKind + i;
                AnimatorState previous = null;
                for (var step = 0; step < move.Steps.Length; step++)
                {
                    var (file, clipName, seconds) = move.Steps[step];
                    var clip = Clip(file, clipName);
                    var state = machine.AddState($"Skill {kind}-{step + 1}: {move.Id}");
                    state.motion = clip;
                    state.speed = clip.length / seconds;
                    state.speedParameterActive = true;
                    state.speedParameter = "AttackSpeed";
                    if (previous == null)
                    {
                        var enter = machine.AddAnyStateTransition(state);
                        enter.AddCondition(AnimatorConditionMode.If, 0, "Attack");
                        enter.AddCondition(AnimatorConditionMode.Equals, kind, "AttackKind");
                        enter.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
                        Quick(enter, 0.05f);
                        enter.canTransitionToSelf = true;
                    }
                    else
                    {
                        var next = previous.AddTransition(state);
                        next.hasExitTime = true;
                        next.exitTime = 0.94f;
                        next.duration = 0.05f;
                        next.hasFixedDuration = true;
                    }
                    previous = state;
                }
                if (previous != null) Leave(previous, locomotion, 0.9f);
            }

            var hit = machine.AddState("Dano");
            hit.motion = Clip("General", "Hit_A");
            hit.speed = 1.3f;
            var toHit = machine.AddAnyStateTransition(hit);
            toHit.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            toHit.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            Quick(toHit, 0.05f);
            Leave(hit, locomotion, 0.8f);

            var dodge = machine.AddState("Esquiva");
            dodge.motion = Clip("MovementAdvanced", "Dodge_Forward");
            var toDodge = machine.AddAnyStateTransition(dodge);
            toDodge.AddCondition(AnimatorConditionMode.If, 0, "Dodge");
            toDodge.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            Quick(toDodge, 0.04f);
            Leave(dodge, locomotion, 0.9f);

            var death = machine.AddState("Morte");
            death.motion = dying;
            var toDeath = machine.AddAnyStateTransition(death);
            toDeath.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            toDeath.canTransitionToSelf = false;
            Quick(toDeath, 0.1f);
            var revive = death.AddTransition(locomotion);
            revive.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            Quick(revive, 0.2f);

            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void Quick(AnimatorStateTransition transition, float duration)
        {
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.hasFixedDuration = true;
        }

        private static void Leave(AnimatorState state, AnimatorState locomotion, float exitTime)
        {
            var back = state.AddTransition(locomotion);
            back.hasExitTime = true;
            back.exitTime = exitTime;
            back.duration = 0.15f;
            back.hasFixedDuration = true;
        }

        // ===== Prefabs =====

        private static void MakePrefab(string path, Look look, Dictionary<string, Material> materials, AnimatorController controller,
            bool weaponFx = false)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Characters}/{look.Model}.fbx");
            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            model.name = look.Model;
            if (!model.TryGetComponent<Animator>(out var animator)) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var name in look.Hide)
            {
                var part = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
                if (part != null) part.gameObject.SetActive(false);
            }

            Attach(model, "handslot.r", look.RightHand);
            Attach(model, "handslot.l", look.LeftHand);
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                var shared = renderer.sharedMaterials;
                var isEyes = renderer.sharedMaterial != null && renderer.sharedMaterial.name.ToLowerInvariant().Contains("glow");
                for (var i = 0; i < shared.Length; i++)
                    shared[i] = isEyes ? materials[look.Eyes] : look.Override.Length > 0 ? materials[look.Override] : MaterialFor(renderer, look.Model, materials);
                renderer.sharedMaterials = shared;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = false;
            }

            // Escala pela altura (pivô nos pés), igual para todos.
            var bounds = Bounds(model);
            var scale = bounds.size.y > 0.1f ? look.Height / bounds.size.y : 1f;
            model.transform.localScale = Vector3.one * scale;
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            // Classes: rastro na arma, energia no cajado e avanço nos golpes (WeaponFx).
            if (weaponFx) root.AddComponent<WeaponFx>().SetTrailMaterial(TrailMaterial());

            AssetDatabase.DeleteAsset(path);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
        }

        private static void Attach(GameObject model, string slot, string weapon)
        {
            if (string.IsNullOrEmpty(weapon)) return;
            var hand = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == slot);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>($"{Weapons}/{weapon}.fbx");
            if (hand == null || asset == null) throw new System.InvalidOperationException($"Sem {slot} ou {weapon}.");
            var item = (GameObject)PrefabUtility.InstantiatePrefab(asset, hand);
            item.name = weapon;
            item.transform.localPosition = Vector3.zero;
            item.transform.localRotation = Quaternion.identity;
            item.transform.localScale = Vector3.one;
        }

        /// <summary>O material do atlas certo: a arma usa a textura que o FBX dela pede; o corpo, a do personagem.</summary>
        private static Material MaterialFor(Renderer renderer, string model, Dictionary<string, Material> materials)
        {
            var original = renderer.sharedMaterial != null ? renderer.sharedMaterial.name.ToLowerInvariant() : "";
            if (original.Contains("glow")) return materials["glow"];
            foreach (var (name, material) in materials)
                if (original.StartsWith(name)) return material;
            var key = model.ToLowerInvariant().Split('_')[0];
            return materials.TryGetValue(key, out var own) ? own : materials.Values.First();
        }

        private static Bounds Bounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
