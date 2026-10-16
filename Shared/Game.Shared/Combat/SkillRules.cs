using System;
using Game.Shared.Core;

namespace Game.Shared.Combat
{
    /// <summary>Resultado da checagem de uso de uma skill.</summary>
    public enum SkillUseCheck
    {
        /// <summary>Pode usar.</summary>
        Ok = 0,

        /// <summary>Nenhum alvo vivo selecionado.</summary>
        NoTarget = 1,

        /// <summary>A skill ainda está em recarga.</summary>
        OnCooldown = 2,

        /// <summary>Alvo longe demais.</summary>
        OutOfRange = 3,

        /// <summary>Mana insuficiente.</summary>
        NotEnoughMp = 4,
    }

    /// <summary>
    /// Recargas das skills de um personagem, por posição na barra. O tempo é informado por quem chama.
    /// </summary>
    public sealed class SkillCooldowns
    {
        private readonly double[] _readyAt;

        /// <summary>Cria para <paramref name="slots"/> skills, todas prontas.</summary>
        public SkillCooldowns(int slots)
        {
            if (slots < 0) throw new ArgumentOutOfRangeException(nameof(slots));
            _readyAt = new double[slots];
            for (var i = 0; i < slots; i++) _readyAt[i] = double.NegativeInfinity;
        }

        /// <summary>Quantidade de posições.</summary>
        public int Count => _readyAt.Length;

        /// <summary>Verdadeiro se a skill da posição está pronta em <paramref name="now"/>.</summary>
        public bool IsReady(int slot, double now) => now >= _readyAt[slot];

        /// <summary>Segundos até ficar pronta (0 se já está).</summary>
        public double Remaining(int slot, double now) => Math.Max(0, _readyAt[slot] - now);

        /// <summary>Começa a recarga da posição.</summary>
        public void Start(int slot, double now, double cooldownSeconds) => _readyAt[slot] = now + cooldownSeconds;
    }

    /// <summary>
    /// Regras de uso de skill compartilhadas: o cliente usa para dar resposta imediata,
    /// o servidor usa para validar o pedido do cliente.
    /// </summary>
    public static class SkillRules
    {
        /// <summary>
        /// Confere se a skill pode ser usada. <paramref name="distanceToTargetEdge"/> é a distância do atacante
        /// até a borda do alvo (centro a centro menos o raio do alvo), ou nulo se não há alvo vivo.
        /// A ordem das checagens define qual mensagem o jogador vê quando há mais de um problema.
        /// </summary>
        public static SkillUseCheck Check(SkillData skill, int slot, SkillCooldowns cooldowns, ResourcePool mp,
            double? distanceToTargetEdge, double now)
        {
            if (skill == null) throw new ArgumentNullException(nameof(skill));
            if (cooldowns == null) throw new ArgumentNullException(nameof(cooldowns));
            if (mp == null) throw new ArgumentNullException(nameof(mp));

            if (!distanceToTargetEdge.HasValue) return SkillUseCheck.NoTarget;
            if (!cooldowns.IsReady(slot, now)) return SkillUseCheck.OnCooldown;
            if (distanceToTargetEdge.Value > skill.Range) return SkillUseCheck.OutOfRange;
            if (!mp.Has(skill.MpCost)) return SkillUseCheck.NotEnoughMp;
            return SkillUseCheck.Ok;
        }

        /// <summary>
        /// Confere e, se puder, consome a mana e inicia a recarga. Retorna o resultado da checagem.
        /// </summary>
        public static SkillUseCheck TryUse(SkillData skill, int slot, SkillCooldowns cooldowns, ResourcePool mp,
            double? distanceToTargetEdge, double now)
        {
            var check = Check(skill, slot, cooldowns, mp, distanceToTargetEdge, now);
            if (check != SkillUseCheck.Ok) return check;
            mp.TrySpend(skill.MpCost);
            cooldowns.Start(slot, now, skill.CooldownSeconds);
            return SkillUseCheck.Ok;
        }
    }
}
