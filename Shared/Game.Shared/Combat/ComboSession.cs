using System;
using System.Collections.Generic;

namespace Game.Shared.Combat
{
    /// <summary>
    /// Regras do combo por timing. Não conhece Unity nem rede: quem chama informa o tempo atual em segundos
    /// (no cliente, <c>Time.time</c>; no servidor, o relógio da simulação).
    ///
    /// Funcionamento:
    /// a cada ciclo um marcador percorre a barra de 0 a 1. Apertar dentro da janela-alvo continua a corrente
    /// e começa um novo ciclo; apertar fora, ou deixar o marcador passar da janela, encerra o combo.
    /// A cada acerto a janela encolhe e a barra acelera, até estabilizar.
    /// Se o tipo de cada skill for informado, sequências de tipos formam rotas (<see cref="ComboRoute"/>)
    /// que dão bônus de dano ao golpe que as completa.
    /// </summary>
    public sealed class ComboSession
    {
        private readonly ComboConfig _config;
        private readonly List<SkillKind> _chainKinds = new List<SkillKind>();
        private double _cycleStart;

        /// <summary>Estado atual.</summary>
        public ComboState State { get; private set; } = ComboState.Inactive;

        /// <summary>Acertos na corrente atual (ou na última, se já terminou).</summary>
        public int HitCount { get; private set; }

        /// <summary>Quantos dos acertos foram Excellent.</summary>
        public int ExcellentCount { get; private set; }

        /// <summary>Largura atual da janela-alvo (normalizada).</summary>
        public double WindowWidth { get; private set; }

        /// <summary>Duração atual de um ciclo da barra, em segundos.</summary>
        public double BarDurationSeconds { get; private set; }

        /// <summary>Bônus acumulado que o finalizador converte em dano (0 = nenhum).</summary>
        public double ChainBonus { get; private set; }

        /// <summary>Tipos das skills acertadas na corrente atual, do mais antigo para o mais recente.</summary>
        public IReadOnlyList<SkillKind> ChainKinds => _chainKinds;

        /// <summary>Quantas rotas foram completadas na corrente atual.</summary>
        public int RoutesCompleted { get; private set; }

        /// <summary>Motivo do fim do último combo.</summary>
        public ComboEndReason LastEndReason { get; private set; } = ComboEndReason.None;

        /// <summary>Configuração em uso.</summary>
        public ComboConfig Config => _config;

        /// <summary>Instante em que o ciclo atual da barra começou (o servidor envia ao cliente para desenhar).</summary>
        public double CycleStart => _cycleStart;

        /// <summary>Início da janela-alvo na barra.</summary>
        public double WindowStart => _config.WindowCenter - WindowWidth / 2;

        /// <summary>Fim da janela-alvo na barra.</summary>
        public double WindowEnd => _config.WindowCenter + WindowWidth / 2;

        /// <summary>Verdadeiro se o finalizador pode ser usado agora.</summary>
        public bool CanFinish => State == ComboState.Active && HitCount >= _config.MinHitsForFinisher;

        /// <summary>Cria a sessão. A configuração é validada aqui.</summary>
        public ComboSession(ComboConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _config.Validate();
            ResetChain();
        }

        /// <summary>
        /// Inicia um combo no instante <paramref name="now"/>. Não gasta SP: quem chama confere e desconta
        /// <see cref="ComboConfig.ActivationSpCost"/> antes. Retorna falso se já houver combo ativo.
        /// </summary>
        public bool TryStart(double now)
        {
            if (State == ComboState.Active) return false;
            ResetChain();
            State = ComboState.Active;
            _cycleStart = now;
            return true;
        }

        /// <summary>
        /// Posição do marcador na barra no instante <paramref name="now"/>: 0 no início do ciclo, 1 no fim.
        /// Pode passar de 1 se ninguém chamar <see cref="Tick"/>. Retorna 0 sem combo ativo.
        /// </summary>
        public double GetCursor(double now)
        {
            if (State != ComboState.Active) return 0;
            return Math.Max(0, (now - _cycleStart) / BarDurationSeconds);
        }

        /// <summary>
        /// Registra o aperto de uma skill no instante <paramref name="now"/>. Informe <paramref name="kind"/>
        /// para que a skill conte nas rotas de combo; sem ele, a sequência de rotas recomeça.
        /// </summary>
        public ComboHitResult Press(double now, SkillKind? kind = null)
        {
            if (State != ComboState.Active) return ComboHitResult.NotActive;

            var cursor = GetCursor(now);
            var grace = _config.GraceSeconds / BarDurationSeconds;

            if (cursor < WindowStart - grace || cursor > WindowEnd + grace)
            {
                End(ComboEndReason.Missed);
                return new ComboHitResult(ComboGrade.Miss, 1.0, HitCount, comboEnded: true, wasActive: true);
            }

            var halfWidth = WindowWidth / 2;
            var offset = Math.Abs(cursor - _config.WindowCenter);
            var grade = offset <= halfWidth * _config.ExcellentZoneRatio ? ComboGrade.Excellent : ComboGrade.Good;

            HitCount++;
            double multiplier;
            if (grade == ComboGrade.Excellent)
            {
                ExcellentCount++;
                multiplier = _config.ExcellentDamageMultiplier;
                ChainBonus += _config.ExcellentChainBonus;
            }
            else
            {
                multiplier = _config.GoodDamageMultiplier;
                ChainBonus += _config.GoodChainBonus;
            }
            ChainBonus = Math.Min(ChainBonus, _config.MaxChainBonus);

            var route = RegisterKind(kind);
            if (route != null)
            {
                RoutesCompleted++;
                multiplier *= 1 + route.DamageBonus;
            }

            if (HitCount <= _config.StabilizeAfterHits)
            {
                WindowWidth = Math.Max(_config.MinWindowWidth, WindowWidth * _config.WindowShrinkFactor);
                BarDurationSeconds = Math.Max(_config.MinBarDurationSeconds, BarDurationSeconds * _config.BarSpeedupFactor);
            }

            _cycleStart = now;
            return new ComboHitResult(grade, multiplier, HitCount, comboEnded: false, wasActive: true, route);
        }

        /// <summary>
        /// Chamar a cada frame (ou tick do servidor). Encerra o combo se o marcador já passou da janela.
        /// Retorna verdadeiro se o combo terminou nesta chamada.
        /// </summary>
        public bool Tick(double now)
        {
            if (State != ComboState.Active) return false;
            var grace = _config.GraceSeconds / BarDurationSeconds;
            if (GetCursor(now) <= WindowEnd + grace) return false;
            End(ComboEndReason.TimedOut);
            return true;
        }

        /// <summary>
        /// Usa o finalizador: encerra o combo e devolve o multiplicador de dano do golpe final
        /// (1 + bônus acumulado). Retorna falso se ainda não for possível finalizar.
        /// </summary>
        public bool TryFinish(out double damageMultiplier)
        {
            if (!CanFinish)
            {
                damageMultiplier = 1.0;
                return false;
            }
            damageMultiplier = 1.0 + ChainBonus;
            End(ComboEndReason.Finished);
            return true;
        }

        /// <summary>Interrompe o combo por motivo externo (alvo morreu sem troca, jogador atordoado, etc.).</summary>
        public void Cancel()
        {
            if (State == ComboState.Active) End(ComboEndReason.Cancelled);
        }

        private void End(ComboEndReason reason)
        {
            State = ComboState.Inactive;
            LastEndReason = reason;
        }

        /// <summary>
        /// Guarda o tipo do golpe e devolve a rota completada por ele: a mais longa que casar com o fim
        /// da corrente; empate fica com o maior bônus.
        /// </summary>
        private ComboRoute? RegisterKind(SkillKind? kind)
        {
            if (!kind.HasValue)
            {
                _chainKinds.Clear();
                return null;
            }

            _chainKinds.Add(kind.Value);
            ComboRoute? best = null;
            foreach (var route in _config.Routes)
            {
                if (!route.MatchesEndOf(_chainKinds)) continue;
                if (best == null || route.Pattern.Count > best.Pattern.Count ||
                    (route.Pattern.Count == best.Pattern.Count && route.DamageBonus > best.DamageBonus))
                    best = route;
            }
            return best;
        }

        private void ResetChain()
        {
            HitCount = 0;
            RoutesCompleted = 0;
            _chainKinds.Clear();
            ExcellentCount = 0;
            ChainBonus = 0;
            WindowWidth = _config.InitialWindowWidth;
            BarDurationSeconds = _config.InitialBarDurationSeconds;
        }
    }
}
