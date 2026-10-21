using System;
using System.Collections.Generic;
using Game.Shared.Core;
using Game.Shared.Text;
using UnityEngine;

namespace Game.Client.Combat
{
    /// <summary>
    /// Algo que pode ser selecionado e receber dano (monstro, boneco de treino).
    /// </summary>
    public sealed class Targetable : MonoBehaviour
    {
        private static readonly List<Targetable> ActiveTargets = new List<Targetable>();

        /// <summary>Todos os alvos ativos na cena.</summary>
        public static IReadOnlyList<Targetable> All => ActiveTargets;

        [SerializeField] private string displayName = "Alvo";
        [SerializeField] private int maxHp = 5000;
        [SerializeField] private int defense = 100;
        [SerializeField] private float height = 2f;
        [SerializeField] private float radius = 0.6f;
        [SerializeField] private GameObject selectionRing;

        private ResourcePool _hp;

        /// <summary>Disparado ao receber dano: (quantidade, foi crítico).</summary>
        public event Action<int, bool> Damaged;

        /// <summary>Disparado quando o HP chega a zero.</summary>
        public event Action<Targetable> Died;

        /// <summary>Nome mostrado; se o nome guardado for uma chave do arquivo de idioma (ex.: dummy.a), vem traduzido.</summary>
        public string DisplayName => Elite ? Loc.T("monster.elite_name", BaseName) : BaseName;

        private string BaseName => Loc.Has(displayName) ? Loc.T(displayName) : displayName;

        /// <summary>Monstro elite: nome com "(Elite)" e plaquinha dourada.</summary>
        public bool Elite { get; set; }
        public int Defense => defense;
        public ResourcePool Hp => _hp ??= new ResourcePool(maxHp);
        public bool IsAlive => Hp.Current > 0;

        /// <summary>Raio do corpo, somado ao alcance das skills.</summary>
        public float Radius => radius;

        /// <summary>Ponto acima da cabeça, para números de dano e barra de vida.</summary>
        public Vector3 HeadPosition => transform.position + Vector3.up * height;

        /// <summary>Ponto central do corpo.</summary>
        public Vector3 CenterPosition => transform.position + Vector3.up * (height * 0.5f);

        /// <summary>Nível mostrado na plaquinha (0 = não mostrar).</summary>
        public int Level { get; set; }

        private string _nameplate;
        private int _nameplateLevel = -1;
        private int _nameplateLanguage = -1;

        /// <summary>"Nome Nv X" da plaquinha, montado só quando o nível ou o idioma muda (não a cada quadro).</summary>
        public string NameplateText
        {
            get
            {
                if (_nameplate == null || _nameplateLevel != Level || _nameplateLanguage != Loc.Version)
                {
                    _nameplate = Loc.T("hud.target", DisplayName, Level);
                    _nameplateLevel = Level;
                    _nameplateLanguage = Loc.Version;
                }
                return _nameplate;
            }
        }

        public void Configure(string newName, int newMaxHp, float newHeight, float newRadius, GameObject ring, int newDefense = 100)
        {
            displayName = newName;
            maxHp = newMaxHp;
            height = newHeight;
            radius = newRadius;
            selectionRing = ring;
            defense = newDefense;
            _hp = new ResourcePool(maxHp);
            // O anel chega depois do OnEnable (que o esconderia); esconde aqui também.
            SetSelected(false);
        }

        /// <summary>Volta com a vida cheia (renascer, voltar para casa).</summary>
        public void RestoreFullHealth() => Hp.Add(Hp.Max);

        /// <summary>Falso para o que só mostra plaquinha mas não pode ser alvo (outros jogadores, sem PvP).</summary>
        public bool IsSelectable { get; set; } = true;

        /// <summary>Id da entidade no servidor (0 no modo offline).</summary>
        public int NetworkId { get; set; }

        /// <summary>Modo online: o servidor manda a vida; ajusta sem disparar eventos de dano.</summary>
        public void SetHealth(int current, int max)
        {
            if (Hp.Max != max) Hp.SetMax(max);
            Hp.Add(current - Hp.Current);
        }

        public void SetSelected(bool selected)
        {
            if (selectionRing != null) selectionRing.SetActive(selected);
        }

        /// <summary>Aplica dano. Retorna o dano efetivamente causado.</summary>
        public int ApplyDamage(int amount, bool critical)
        {
            if (!IsAlive || amount <= 0) return 0;
            var dealt = -Hp.Add(-amount);
            Damaged?.Invoke(dealt, critical);
            Fx.VisualAnimator.Trigger(this, "Hit");
            if (!IsAlive) Died?.Invoke(this);
            return dealt;
        }

        private void OnEnable()
        {
            ActiveTargets.Add(this);
            SetSelected(false);
        }

        private void OnDisable() => ActiveTargets.Remove(this);
    }
}
