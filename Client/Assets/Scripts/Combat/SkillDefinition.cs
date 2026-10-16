using System;
using UnityEngine;

namespace Game.Client.Combat
{
    /// <summary>Tipo de dano da skill (usado depois pelas rotas de combo).</summary>
    public enum SkillKind
    {
        Fisico,
        Magico,
    }

    /// <summary>
    /// Definição de uma skill do protótipo. Mais adiante estes dados vêm da planilha de design via JSON.
    /// </summary>
    [Serializable]
    public sealed class SkillDefinition
    {
        public string name = "Skill";
        public SkillKind kind = SkillKind.Fisico;

        [Tooltip("Dano base antes de bônus de combo e crítico.")]
        public int baseDamage = 100;

        [Tooltip("Alcance em metros, medido até a borda do alvo.")]
        public float range = 3f;

        [Tooltip("Recarga em segundos.")]
        public float cooldown = 1f;

        [Tooltip("0 = só o alvo; maior que 0 = atinge todos neste raio em volta do alvo.")]
        public float areaRadius;

        [Tooltip("Tempo em que o personagem fica parado ao usar a skill.")]
        public float lockSeconds = 0.15f;

        public SkillDefinition() { }

        public SkillDefinition(string name, SkillKind kind, int baseDamage, float range, float cooldown, float areaRadius = 0f)
        {
            this.name = name;
            this.kind = kind;
            this.baseDamage = baseDamage;
            this.range = range;
            this.cooldown = cooldown;
            this.areaRadius = areaRadius;
        }
    }
}
