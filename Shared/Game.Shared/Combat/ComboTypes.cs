namespace Game.Shared.Combat
{
    /// <summary>Nota de um aperto durante o combo.</summary>
    public enum ComboGrade
    {
        /// <summary>Fora da janela: o combo termina.</summary>
        Miss = 0,

        /// <summary>Dentro da janela, longe do centro.</summary>
        Good = 1,

        /// <summary>Dentro da janela, perto do centro.</summary>
        Excellent = 2,
    }

    /// <summary>Estado da sessão de combo.</summary>
    public enum ComboState
    {
        /// <summary>Nenhum combo em andamento.</summary>
        Inactive = 0,

        /// <summary>Combo em andamento, aguardando o próximo aperto.</summary>
        Active = 1,
    }

    /// <summary>Por que o último combo terminou.</summary>
    public enum ComboEndReason
    {
        /// <summary>O combo ainda não terminou nenhuma vez.</summary>
        None = 0,

        /// <summary>Apertou fora da janela.</summary>
        Missed = 1,

        /// <summary>A barra passou da janela sem aperto.</summary>
        TimedOut = 2,

        /// <summary>O jogador usou o finalizador.</summary>
        Finished = 3,

        /// <summary>Interrompido por fora (alvo morreu sem troca, jogador morreu, atordoamento).</summary>
        Cancelled = 4,
    }

    /// <summary>Resultado de um aperto durante o combo.</summary>
    public readonly struct ComboHitResult
    {
        /// <summary>Nota do aperto.</summary>
        public ComboGrade Grade { get; }

        /// <summary>Multiplicador de dano para a skill deste aperto (1 = sem bônus).</summary>
        public double DamageMultiplier { get; }

        /// <summary>Quantidade de acertos na corrente depois deste aperto.</summary>
        public int HitCount { get; }

        /// <summary>Verdadeiro se este aperto encerrou o combo.</summary>
        public bool ComboEnded { get; }

        /// <summary>Verdadeiro se havia combo ativo quando o aperto aconteceu.</summary>
        public bool WasActive { get; }

        /// <summary>Rota de combo fechada por este aperto, ou nulo se nenhuma.</summary>
        public ComboRoute? Route { get; }

        /// <summary>Cria um resultado. <paramref name="damageMultiplier"/> já inclui o bônus da rota.</summary>
        public ComboHitResult(ComboGrade grade, double damageMultiplier, int hitCount, bool comboEnded, bool wasActive,
            ComboRoute? route = null)
        {
            Grade = grade;
            DamageMultiplier = damageMultiplier;
            HitCount = hitCount;
            ComboEnded = comboEnded;
            WasActive = wasActive;
            Route = route;
        }

        /// <summary>Resultado para um aperto sem combo ativo.</summary>
        public static ComboHitResult NotActive => new ComboHitResult(ComboGrade.Miss, 1.0, 0, false, false);
    }
}
