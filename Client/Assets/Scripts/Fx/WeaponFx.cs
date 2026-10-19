using System.Collections;
using Game.Shared.Combat;
using UnityEngine;

namespace Game.Client.Fx
{
    /// <summary>
    /// A arma do personagem durante as skills (<see cref="SkillMove"/>): rastro na lâmina na janela do golpe (cor da
    /// skill; dourado e mais largo no Excellent), energia juntando na ponta do cajado antes da magia sair, brasas na
    /// Lâmina Flamejante e o corpo avançando um pouco nos golpes de investida (só o modelo; a posição é a do jogo).
    /// Fica no modelo da classe (o KayKitBuilder põe no prefab); a ponta é achada pela malha da arma na mão direita.
    /// </summary>
    public sealed class WeaponFx : MonoBehaviour
    {
        private static readonly Color Gold = new Color(1f, 0.85f, 0.3f);

        [SerializeField] private Material trailMaterial;

        private Transform _tip;
        private TrailRenderer _trail;
        private Transform _hand;
        private string _weaponId = "";
        private int _upgrade;
        private MeshRenderer _glow;
        private MaterialPropertyBlock _glowBlock;
        private Vector3 _restPosition;
        private Coroutine _playing;
        private float _lunge;

        /// <summary>Toca o golpe da skill no modelo de <paramref name="owner"/>, se ele tiver arma.</summary>
        public static void Play(Component owner, SkillData skill, ComboGrade? grade)
        {
            if (owner == null || skill == null) return;
            var weapon = owner.GetComponentInChildren<WeaponFx>();
            if (weapon != null) weapon.Play(skill, grade);
        }

        /// <summary>
        /// Arma equipada e nível de melhoria de <paramref name="owner"/>: troca o modelo na mão (Visuals/weapon/&lt;id&gt;)
        /// e acende o brilho da melhoria, como no CABAL (+4 azul, +7 ciano com partículas, +9 roxo, +10 dourado pulsando).
        /// </summary>
        public static void SetLook(Component owner, string weaponId, int upgrade)
        {
            if (owner == null) return;
            var weapon = owner.GetComponentInChildren<WeaponFx>();
            if (weapon != null) weapon.SetLook(weaponId, upgrade);
        }

        /// <summary>Cor e força do brilho por nível de melhoria (alfa 0 = sem brilho).</summary>
        public static Color GlowFor(int upgrade)
        {
            if (upgrade >= 10) return new Color(1f, 0.72f, 0.2f, 1f);
            if (upgrade >= 9) return new Color(0.75f, 0.35f, 1f, 0.85f);
            if (upgrade >= 7) return new Color(0.2f, 1f, 0.9f, 0.7f);
            if (upgrade >= 4) return new Color(0.3f, 0.55f, 1f, 0.35f + (upgrade - 4) * 0.08f);
            return Color.clear;
        }

        /// <summary>Ponta da arma de <paramref name="owner"/> (de onde saem as magias), ou nula sem modelo.</summary>
        public static Vector3? TipOf(Component owner)
        {
            if (owner == null) return null;
            var weapon = owner.GetComponentInChildren<WeaponFx>();
            return weapon != null && weapon._tip != null ? weapon._tip.position : (Vector3?)null;
        }

        public void SetTrailMaterial(Material material) => trailMaterial = material;

        private void Awake()
        {
            _restPosition = transform.localPosition;
            _hand = FindDeep(transform, "handslot.r");
            var weapon = _hand != null ? _hand.GetComponentInChildren<MeshRenderer>() : null;
            if (weapon != null) Bind(weapon);
        }

        private void SetLook(string weaponId, int upgrade)
        {
            weaponId ??= "";
            if (weaponId.Length > 0 && weaponId != _weaponId && _hand != null)
            {
                var prefab = Resources.Load<GameObject>("Visuals/weapon/" + weaponId);
                if (prefab != null)
                {
                    for (var i = _hand.childCount - 1; i >= 0; i--) Destroy(_hand.GetChild(i).gameObject);
                    var model = Instantiate(prefab, _hand, false);
                    model.name = weaponId;
                    var renderer = model.GetComponentInChildren<MeshRenderer>();
                    if (renderer != null) Bind(renderer);
                }
            }
            _weaponId = weaponId;
            _upgrade = upgrade;
            UpdateGlowShell();
        }

        /// <summary>Casco de brilho: a mesma malha da arma, um pouco maior, aditiva (só a partir do +4).</summary>
        private void UpdateGlowShell()
        {
            var weapon = _tip != null ? _tip.parent : null;
            if (GlowFor(_upgrade).a <= 0f || weapon == null || trailMaterial == null)
            {
                if (_glow != null) Destroy(_glow.gameObject);
                _glow = null;
                return;
            }
            if (_glow == null || _glow.transform.parent != weapon)
            {
                if (_glow != null) Destroy(_glow.gameObject);
                var mesh = weapon.GetComponent<MeshFilter>().sharedMesh;
                var shell = new GameObject("Brilho da melhoria");
                shell.transform.SetParent(weapon, false);
                const float grow = 1.08f;
                shell.transform.localScale = Vector3.one * grow;
                shell.transform.localPosition = -mesh.bounds.center * (grow - 1f);
                shell.AddComponent<MeshFilter>().sharedMesh = mesh;
                _glow = shell.AddComponent<MeshRenderer>();
                _glow.sharedMaterial = trailMaterial;
                _glow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _glow.receiveShadows = false;
                _glowBlock ??= new MaterialPropertyBlock();
            }
        }

        private void Update()
        {
            if (_glow == null) return;
            var color = GlowFor(_upgrade);
            // Pulsa; no +10 pulsa mais forte e mais rápido.
            var pulse = 0.75f + 0.25f * Mathf.Sin(Time.time * (_upgrade >= 10 ? 6f : 3f));
            var strength = color.a * pulse;
            _glowBlock.SetColor("_BaseColor", new Color(color.r * strength, color.g * strength, color.b * strength, strength));
            _glow.SetPropertyBlock(_glowBlock);
            // A partir do +7: faíscas subindo da lâmina.
            var fx = CombatFx.Instance;
            if (_upgrade >= 7 && fx != null && _tip != null && Random.value < (_upgrade >= 10 ? 0.6f : 0.25f))
            {
                var along = Vector3.Lerp(_tip.parent.position, _tip.position, Random.value);
                fx.Glow(along, Vector3.up * 0.4f + Random.insideUnitSphere * 0.2f, color, 0.1f, 0.5f);
            }
        }

        /// <summary>Acha a ponta da arma e põe nela o rastro (de novo a cada troca de arma).</summary>
        private void Bind(MeshRenderer weapon)
        {
            // Ponta: a extremidade do eixo mais comprido da arma, a mais longe da mão.
            var mesh = weapon.GetComponent<MeshFilter>().sharedMesh;
            var bounds = mesh.bounds;
            var axis = bounds.size.x > bounds.size.y && bounds.size.x > bounds.size.z ? 0 : bounds.size.y > bounds.size.z ? 1 : 2;
            var plus = bounds.center;
            var minus = bounds.center;
            plus[axis] = bounds.max[axis];
            minus[axis] = bounds.min[axis];
            var tipLocal = plus.sqrMagnitude >= minus.sqrMagnitude ? plus : minus;
            if (_tip != null) Destroy(_tip.gameObject);
            _tip = new GameObject("Ponta da arma").transform;
            _tip.SetParent(weapon.transform, false);
            _tip.localPosition = tipLocal * 0.92f;

            _trail = _tip.gameObject.AddComponent<TrailRenderer>();
            _trail.time = 0.28f;
            _trail.minVertexDistance = 0.04f;
            _trail.emitting = false;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _trail.receiveShadows = false;
            _trail.alignment = LineAlignment.View;
            _trail.numCapVertices = 2;
            _trail.textureMode = LineTextureMode.Stretch;
            var material = trailMaterial != null ? trailMaterial : Resources.Load<Material>("Visuals/fx/particula_suave");
            if (material != null) _trail.sharedMaterial = material;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void Play(SkillData skill, ComboGrade? grade)
        {
            var move = SkillMoves.For(skill.Id);
            if (move == null) return;
            if (_playing != null) StopCoroutine(_playing);
            _playing = StartCoroutine(Run(skill, move, grade));
        }

        private IEnumerator Run(SkillData skill, SkillMove move, ComboGrade? grade)
        {
            var excellent = grade == ComboGrade.Excellent;
            var color = SkillFx.ColorFor(skill, grade, false);
            var fire = skill.Id == "lamina_flamejante";
            var start = Time.time;
            var end = Mathf.Max(move.TrailTo, move.FirstImpact) + 0.35f;
            if (_trail != null)
            {
                var width = (skill.Kind == SkillKind.Physical ? 0.5f : 0.25f) * (excellent ? 1.4f : 1f);
                _trail.startWidth = width;
                _trail.endWidth = 0f;
                var head = excellent ? Color.Lerp(color, Gold, 0.5f) : color;
                head.a = 0.85f;
                _trail.startColor = head;
                _trail.endColor = new Color(color.r, color.g, color.b, 0f);
                _trail.Clear();
            }
            while (true)
            {
                var t = Time.time - start;
                if (_trail != null) _trail.emitting = skill.Kind == SkillKind.Physical && t >= move.TrailFrom && t <= move.TrailTo;

                // Avanço: vai até a distância no LungeAt e volta devagar.
                if (move.Lunge > 0f)
                {
                    var forward = t < move.LungeAt ? Mathf.SmoothStep(0f, 1f, t / move.LungeAt)
                        : 1f - Mathf.SmoothStep(0f, 1f, (t - move.LungeAt) / 0.35f);
                    _lunge = move.Lunge * Mathf.Clamp01(forward);
                }

                var fx = CombatFx.Instance;
                if (fx != null && _tip != null)
                {
                    // Magia carregando: partículas vindo de fora para a ponta do cajado até soltar.
                    if (move.Charge && t < move.FirstImpact)
                    {
                        var from = _tip.position + Random.onUnitSphere * 0.7f;
                        fx.Glow(from, (_tip.position - from) * 3.2f, color, 0.12f, 0.3f);
                        if (Random.value < 0.4f) fx.Glow(_tip.position, Vector3.zero, color, 0.3f, 0.12f);
                    }
                    if (fire && _trail != null && _trail.emitting)
                        fx.Emit(_tip.position, Random.insideUnitSphere * 0.6f + Vector3.up, new Color(1f, 0.5f + Random.value * 0.3f, 0.1f), 0.14f, 0.35f);
                    // Excellent: um brilho dourado na arma no momento do acerto.
                    if (excellent && Mathf.Abs(t - move.FirstImpact) < Time.deltaTime) fx.Glow(_tip.position, Vector3.zero, Gold, 0.9f, 0.15f);
                }
                if (t > end) break;
                yield return null;
            }
            if (_trail != null) _trail.emitting = false;
            _lunge = 0f;
            _playing = null;
        }

        private void LateUpdate()
        {
            transform.localPosition = _restPosition + Vector3.forward * _lunge;
        }
    }
}
