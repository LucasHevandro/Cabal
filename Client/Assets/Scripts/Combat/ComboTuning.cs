using System;
using Game.Shared.Combat;
using UnityEngine;

namespace Game.Client.Combat
{
    /// <summary>
    /// Parâmetros do combo editáveis no Inspector, para ajustar a sensação nos testes.
    /// Convertidos em <see cref="ComboConfig"/> do Shared ao iniciar.
    /// </summary>
    [Serializable]
    public sealed class ComboTuning
    {
        public int activationSpCost = 500;

        [Header("Barra")]
        public float initialBarDurationSeconds = 1.2f;
        [Range(0.5f, 1f)] public float barSpeedupFactor = 0.95f;
        public float minBarDurationSeconds = 0.6f;

        [Header("Janela")]
        [Range(0f, 1f)] public float windowCenter = 0.75f;
        [Range(0.02f, 1f)] public float initialWindowWidth = 0.30f;
        [Range(0.5f, 1f)] public float windowShrinkFactor = 0.88f;
        [Range(0.02f, 1f)] public float minWindowWidth = 0.08f;
        public int stabilizeAfterHits = 11;
        [Range(0f, 1f)] public float excellentZoneRatio = 0.35f;

        [Header("Recompensa")]
        public float goodDamageMultiplier = 1.10f;
        public float excellentDamageMultiplier = 1.20f;
        public float goodChainBonus = 0.03f;
        public float excellentChainBonus = 0.06f;
        public float maxChainBonus = 1.5f;
        public int minHitsForFinisher = 3;

        public ComboConfig ToConfig() => new ComboConfig
        {
            ActivationSpCost = activationSpCost,
            InitialBarDurationSeconds = initialBarDurationSeconds,
            BarSpeedupFactor = barSpeedupFactor,
            MinBarDurationSeconds = minBarDurationSeconds,
            WindowCenter = windowCenter,
            InitialWindowWidth = initialWindowWidth,
            WindowShrinkFactor = windowShrinkFactor,
            MinWindowWidth = minWindowWidth,
            StabilizeAfterHits = stabilizeAfterHits,
            ExcellentZoneRatio = excellentZoneRatio,
            GoodDamageMultiplier = goodDamageMultiplier,
            ExcellentDamageMultiplier = excellentDamageMultiplier,
            GoodChainBonus = goodChainBonus,
            ExcellentChainBonus = excellentChainBonus,
            MaxChainBonus = maxChainBonus,
            MinHitsForFinisher = minHitsForFinisher,
        };
    }
}
