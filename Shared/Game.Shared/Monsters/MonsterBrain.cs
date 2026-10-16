using System;
using System.Numerics;
using Game.Shared.Combat;

namespace Game.Shared.Monsters
{
    /// <summary>Estado da IA do monstro.</summary>
    public enum MonsterState
    {
        Idle,
        Wander,
        Chase,
        WindUp,
        Return,
        Dead,
    }

    /// <summary>O que a IA pede ao corpo do monstro neste tick.</summary>
    public enum MonsterActionKind
    {
        /// <summary>Ficar parado.</summary>
        None,

        /// <summary>Andar até <see cref="MonsterAction.Point"/>.</summary>
        MoveTo,

        /// <summary>Parar e virar para <see cref="MonsterAction.Point"/>.</summary>
        Face,

        /// <summary>Começou a preparar o golpe: mostrar o aviso.</summary>
        AttackStarted,

        /// <summary>O golpe saiu: quem controla o corpo confere alcance/esquiva e aplica o dano.</summary>
        AttackLanded,

        /// <summary>Voltou para casa: recuperar a vida.</summary>
        ResetHealth,
    }

    /// <summary>Pedido da IA para este tick.</summary>
    public readonly struct MonsterAction
    {
        public MonsterActionKind Kind { get; }
        public Vector2 Point { get; }

        public MonsterAction(MonsterActionKind kind, Vector2 point = default)
        {
            Kind = kind;
            Point = point;
        }

        public static MonsterAction None => new MonsterAction(MonsterActionKind.None);
    }

    /// <summary>
    /// IA do monstro, sem motor gráfico: recebe posições no plano do chão (X, Z do mundo como X, Y)
    /// e devolve o que o corpo deve fazer. O cliente usa no modo offline; o servidor vai usar a mesma IA.
    ///
    /// Parado/passeando → (alvo perto e agressivo, ou provocado) → persegue → no alcance prepara o golpe →
    /// o golpe sai → persegue de novo. Longe demais de casa → volta, recupera a vida e esquece a provocação.
    /// </summary>
    public sealed class MonsterBrain
    {
        private const float ArriveDistance = 0.4f;

        private readonly MonsterData _data;
        private readonly IRandomSource _random;
        private bool _provoked;
        private double _nextAttackAt = double.NegativeInfinity;
        private double _windupEndsAt;
        private double _nextWanderAt;
        private Vector2 _wanderPoint;

        public MonsterState State { get; private set; } = MonsterState.Idle;
        public Vector2 Home { get; private set; }
        public MonsterData Data => _data;

        /// <summary>Quando o golpe em preparo vai sair (válido em <see cref="MonsterState.WindUp"/>).</summary>
        public double WindupEndsAt => _windupEndsAt;

        public MonsterBrain(MonsterData data, Vector2 home, IRandomSource random)
        {
            _data = data ?? throw new ArgumentNullException(nameof(data));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            Home = home;
        }

        /// <summary>Chamar quando o monstro apanha: passa a perseguir mesmo se for passivo.</summary>
        public void Provoke()
        {
            if (State != MonsterState.Dead && State != MonsterState.Return) _provoked = true;
        }

        public void Kill() => State = MonsterState.Dead;

        /// <summary>Renasce parado em casa.</summary>
        public void Revive(double now)
        {
            State = MonsterState.Idle;
            _provoked = false;
            _nextAttackAt = double.NegativeInfinity;
            _nextWanderAt = now + 2;
        }

        /// <summary>
        /// Um passo da IA. <paramref name="target"/> é a posição do alvo (jogador) ou nulo se não há alvo válido.
        /// </summary>
        public MonsterAction Tick(double now, Vector2 self, Vector2? target)
        {
            switch (State)
            {
                case MonsterState.Dead:
                    return MonsterAction.None;

                case MonsterState.Return:
                    if (Vector2.Distance(self, Home) <= ArriveDistance)
                    {
                        State = MonsterState.Idle;
                        _nextWanderAt = now + 2;
                        return new MonsterAction(MonsterActionKind.ResetHealth);
                    }
                    return new MonsterAction(MonsterActionKind.MoveTo, Home);

                case MonsterState.Idle:
                case MonsterState.Wander:
                    if (target.HasValue && WantsToFight(self, target.Value))
                    {
                        State = MonsterState.Chase;
                        return Chase(now, self, target.Value);
                    }
                    return Wander(now, self);

                case MonsterState.Chase:
                    if (!target.HasValue) return GoHome();
                    return Chase(now, self, target.Value);

                case MonsterState.WindUp:
                    if (now < _windupEndsAt)
                        return target.HasValue ? new MonsterAction(MonsterActionKind.Face, target.Value) : MonsterAction.None;
                    _nextAttackAt = now + _data.AttackCooldownSeconds;
                    State = MonsterState.Chase;
                    return new MonsterAction(MonsterActionKind.AttackLanded, target ?? self);

                default:
                    return MonsterAction.None;
            }
        }

        private bool WantsToFight(Vector2 self, Vector2 target) =>
            _provoked || (_data.Aggressive && Vector2.Distance(self, target) <= _data.AggroRadius);

        private MonsterAction Chase(double now, Vector2 self, Vector2 target)
        {
            if (Vector2.Distance(self, Home) > _data.LeashRadius) return GoHome();

            if (Vector2.Distance(self, target) <= _data.AttackRange)
            {
                if (now < _nextAttackAt) return new MonsterAction(MonsterActionKind.Face, target);
                State = MonsterState.WindUp;
                _windupEndsAt = now + _data.AttackWindupSeconds;
                return new MonsterAction(MonsterActionKind.AttackStarted, target);
            }
            return new MonsterAction(MonsterActionKind.MoveTo, target);
        }

        private MonsterAction GoHome()
        {
            State = MonsterState.Return;
            _provoked = false;
            return new MonsterAction(MonsterActionKind.MoveTo, Home);
        }

        private MonsterAction Wander(double now, Vector2 self)
        {
            if (_data.WanderRadius <= 0) return MonsterAction.None;

            if (State == MonsterState.Wander)
            {
                if (Vector2.Distance(self, _wanderPoint) > ArriveDistance)
                    return new MonsterAction(MonsterActionKind.MoveTo, _wanderPoint);
                State = MonsterState.Idle;
                _nextWanderAt = now + 3 + _random.NextDouble() * 4;
                return MonsterAction.None;
            }

            if (now < _nextWanderAt) return MonsterAction.None;
            var angle = _random.NextDouble() * Math.PI * 2;
            var distance = _data.WanderRadius * Math.Sqrt(_random.NextDouble());
            _wanderPoint = Home + new Vector2((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance));
            State = MonsterState.Wander;
            return new MonsterAction(MonsterActionKind.MoveTo, _wanderPoint);
        }
    }
}
