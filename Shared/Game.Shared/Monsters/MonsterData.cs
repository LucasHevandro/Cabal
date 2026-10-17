using System;
using Game.Shared.Loot;
using Game.Shared.Text;

namespace Game.Shared.Monsters
{
    /// <summary>Dados de um tipo de monstro. Campos públicos para Unity e JSON.</summary>
    [Serializable]
    public sealed class MonsterData
    {
        public string Id = "";

        /// <summary>Nome no idioma do jogador (chave <c>monster.&lt;id&gt;</c>).</summary>
        public string Name => Loc.T(Loc.Key("monster.", Id));

        public int Level = 1;
        public int MaxHp = 500;

        /// <summary>Dano base do ataque (entra na <see cref="Combat.DamageFormula"/> como o "dano da skill").</summary>
        public int AttackDamage = 40;

        public int AttackPower = 10;
        public int Defense = 30;

        /// <summary>Alcance do ataque, em metros até a borda do alvo.</summary>
        public float AttackRange = 1.5f;

        public float AttackCooldownSeconds = 2f;

        /// <summary>Tempo de preparo do golpe: a janela em que o jogador vê o aviso e pode esquivar.</summary>
        public float AttackWindupSeconds = 0.6f;

        public float MoveSpeed = 3f;

        /// <summary>Raio do corpo: o alcance das skills é medido até esta borda.</summary>
        public float BodyRadius = 0.5f;

        /// <summary>Agressivo ataca quem chegar perto; passivo só depois de apanhar.</summary>
        public bool Aggressive = true;

        public float AggroRadius = 7f;

        /// <summary>Distância máxima do ponto de origem antes de desistir e voltar.</summary>
        public float LeashRadius = 22f;

        /// <summary>Raio de passeio quando parado (0 = fica parado).</summary>
        public float WanderRadius = 4f;

        public int XpReward = 20;
        public float RespawnSeconds = 8f;
        public DropTable Drops = new DropTable();

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Monstro sem Id.", nameof(Id));
            if (Level < 1) throw new ArgumentException($"{Id}: nível inválido.", nameof(Level));
            if (MaxHp <= 0) throw new ArgumentException($"{Id}: HP deve ser maior que zero.", nameof(MaxHp));
            if (AttackRange <= 0 || AttackCooldownSeconds <= 0 || AttackWindupSeconds < 0)
                throw new ArgumentException($"{Id}: ataque inválido.", nameof(AttackRange));
            if (MoveSpeed <= 0) throw new ArgumentException($"{Id}: velocidade inválida.", nameof(MoveSpeed));
            if (AggroRadius < 0 || LeashRadius <= AttackRange || WanderRadius < 0)
                throw new ArgumentException($"{Id}: raios inválidos.", nameof(LeashRadius));
            Drops.Validate();
        }
    }
}
