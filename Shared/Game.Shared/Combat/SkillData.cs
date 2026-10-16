using System;

namespace Game.Shared.Combat
{
    /// <summary>
    /// Dados de uma skill. Campos públicos para o Unity e serializadores JSON lerem e gravarem direto;
    /// mais adiante estes dados vêm da planilha de design.
    /// </summary>
    [Serializable]
    public sealed class SkillData
    {
        /// <summary>Identificador único e estável (usado pela rede e pelo banco).</summary>
        public string Id = "";

        /// <summary>Nome mostrado ao jogador.</summary>
        public string Name = "";

        /// <summary>Físico ou mágico: define as rotas de combo e se gasta MP.</summary>
        public SkillKind Kind = SkillKind.Physical;

        /// <summary>Dano base antes de ataque, combo, defesa e crítico.</summary>
        public int BaseDamage = 100;

        /// <summary>Alcance em metros, medido até a borda do alvo.</summary>
        public float Range = 3f;

        /// <summary>Recarga em segundos.</summary>
        public float CooldownSeconds = 1f;

        /// <summary>0 = só o alvo; maior que 0 = atinge todos neste raio em volta do alvo.</summary>
        public float AreaRadius;

        /// <summary>Mana gasta por uso.</summary>
        public int MpCost;

        /// <summary>Tempo em que o personagem fica parado ao usar a skill.</summary>
        public float LockSeconds = 0.15f;

        /// <summary>Construtor vazio para serializadores.</summary>
        public SkillData() { }

        /// <summary>Cria uma skill.</summary>
        public SkillData(string id, string name, SkillKind kind, int baseDamage, float range, float cooldownSeconds,
            float areaRadius = 0f, int mpCost = 0, float lockSeconds = 0.15f)
        {
            Id = id;
            Name = name;
            Kind = kind;
            BaseDamage = baseDamage;
            Range = range;
            CooldownSeconds = cooldownSeconds;
            AreaRadius = areaRadius;
            MpCost = mpCost;
            LockSeconds = lockSeconds;
        }

        /// <summary>Lança <see cref="ArgumentException"/> se algum valor for inválido.</summary>
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("A skill precisa de Id.", nameof(Id));
            if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException($"A skill {Id} precisa de nome.", nameof(Name));
            if (BaseDamage < 0) throw new ArgumentException($"{Id}: dano base não pode ser negativo.", nameof(BaseDamage));
            if (Range <= 0) throw new ArgumentException($"{Id}: alcance deve ser maior que zero.", nameof(Range));
            if (CooldownSeconds < 0) throw new ArgumentException($"{Id}: recarga não pode ser negativa.", nameof(CooldownSeconds));
            if (AreaRadius < 0) throw new ArgumentException($"{Id}: raio não pode ser negativo.", nameof(AreaRadius));
            if (MpCost < 0) throw new ArgumentException($"{Id}: custo de MP não pode ser negativo.", nameof(MpCost));
            if (LockSeconds < 0) throw new ArgumentException($"{Id}: tempo parado não pode ser negativo.", nameof(LockSeconds));
        }
    }
}
