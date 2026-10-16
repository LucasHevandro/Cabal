using System;
using System.Collections.Generic;
using Game.Shared.Core;
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

        public string DisplayName => displayName;
        public int Defense => defense;
        public ResourcePool Hp => _hp ??= new ResourcePool(maxHp);
        public bool IsAlive => Hp.Current > 0;

        /// <summary>Raio do corpo, somado ao alcance das skills.</summary>
        public float Radius => radius;

        /// <summary>Ponto acima da cabeça, para números de dano e barra de vida.</summary>
        public Vector3 HeadPosition => transform.position + Vector3.up * height;

        /// <summary>Ponto central do corpo.</summary>
        public Vector3 CenterPosition => transform.position + Vector3.up * (height * 0.5f);

        public void Configure(string newName, int newMaxHp, float newHeight, float newRadius, GameObject ring)
        {
            displayName = newName;
            maxHp = newMaxHp;
            height = newHeight;
            radius = newRadius;
            selectionRing = ring;
            _hp = new ResourcePool(maxHp);
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
