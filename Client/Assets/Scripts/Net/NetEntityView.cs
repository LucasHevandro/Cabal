using System.Collections.Generic;
using Game.Client.Combat;
using Game.Shared.Monsters;
using Game.Shared.Net;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>
    /// Uma entidade do servidor vista pelo cliente (monstro ou outro jogador).
    /// A posição é interpolada entre snapshots, desenhada um pouco no passado (<see cref="InterpolationDelay"/>)
    /// para sempre haver dois pontos entre os quais andar suavemente.
    /// </summary>
    public sealed class NetEntityView : MonoBehaviour
    {
        /// <summary>Atraso da interpolação: dois ticks do servidor (20/s) mais folga para jitter.</summary>
        public const double InterpolationDelay = 0.12;

        private const int MaxSamples = 20;

        private struct Sample
        {
            public double ServerTime;
            public Vector3 Position;
            public float Yaw;
        }

        private readonly List<Sample> _samples = new List<Sample>();
        private GameObject _body;
        private GameObject _ring;
        private Transform _telegraph;
        private float _telegraphStartedAt;
        private bool _windUp;

        public int Id { get; private set; }
        public EntityKind Kind { get; private set; }
        public string TypeId { get; private set; } = "";
        public Targetable Target { get; private set; }

        /// <summary>Dados do monstro conhecidos pelo cliente (do WorldContent), ou nulo.</summary>
        public MonsterData MonsterData { get; private set; }

        public bool IsDead { get; private set; }
        public bool IsEnraged { get; private set; }

        public void Initialize(EntitySpawned spawned, GameObject body, GameObject ring, Transform telegraph,
            Targetable target, MonsterData monsterData)
        {
            Id = spawned.Id;
            Kind = spawned.Kind;
            TypeId = spawned.TypeId;
            _body = body;
            _ring = ring;
            _telegraph = telegraph;
            Target = target;
            MonsterData = monsterData;
            transform.position = new Vector3(spawned.X, 0, spawned.Z);
        }

        /// <summary>Recebe o estado do snapshot.</summary>
        public void AddSample(double serverTime, in EntityState state)
        {
            if (_samples.Count > 0 && serverTime <= _samples[_samples.Count - 1].ServerTime) return;
            _samples.Add(new Sample { ServerTime = serverTime, Position = new Vector3(state.X, 0, state.Z), Yaw = state.Yaw });
            if (_samples.Count > MaxSamples) _samples.RemoveAt(0);

            if (Target != null) Target.SetHealth(state.Hp, Target.Hp.Max);

            var dead = (state.Flags & EntityState.FlagDead) != 0;
            if (dead != IsDead)
            {
                IsDead = dead;
                if (_body != null) _body.SetActive(!dead);
                if (dead && _ring != null) _ring.SetActive(false);
            }

            var windUp = (state.Flags & EntityState.FlagWindUp) != 0;
            if (windUp && !_windUp) _telegraphStartedAt = Time.time;
            _windUp = windUp;
            IsEnraged = (state.Flags & EntityState.FlagEnraged) != 0;
        }

        /// <summary>Teleporta sem interpolar (ex.: renasceu longe).</summary>
        public void SnapTo(Vector3 position)
        {
            _samples.Clear();
            transform.position = position;
        }

        private void Update()
        {
            var session = NetworkSession.Instance;
            if (session == null || _samples.Count == 0) return;

            var renderTime = session.ServerNow - InterpolationDelay;
            Sample from = _samples[0], to = _samples[0];
            for (var i = 0; i < _samples.Count; i++)
            {
                if (_samples[i].ServerTime <= renderTime) from = _samples[i];
                if (_samples[i].ServerTime >= renderTime)
                {
                    to = _samples[i];
                    break;
                }
                to = _samples[i];
            }

            var span = to.ServerTime - from.ServerTime;
            var t = span > 0 ? Mathf.Clamp01((float)((renderTime - from.ServerTime) / span)) : 1f;
            transform.position = Vector3.Lerp(from.Position, to.Position, t);
            transform.rotation = Quaternion.Euler(0, Mathf.LerpAngle(from.Yaw, to.Yaw, t), 0);

            UpdateTelegraph();
        }

        private void UpdateTelegraph()
        {
            if (_telegraph == null) return;
            var show = _windUp && !IsDead && MonsterData != null;
            if (_telegraph.gameObject.activeSelf != show) _telegraph.gameObject.SetActive(show);
            if (!show) return;

            var progress = MonsterData.AttackWindupSeconds <= 0 ? 1f
                : Mathf.Clamp01((Time.time - _telegraphStartedAt) / MonsterData.AttackWindupSeconds);
            var size = (MonsterData.AttackRange + MonsterData.BodyRadius) * 2f * Mathf.Lerp(0.3f, 1f, progress);
            _telegraph.localScale = new Vector3(size, 0.01f, size);
        }
    }
}
