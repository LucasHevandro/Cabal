using System;
using Game.Client.Fx;
using Game.Client.UI;
using Game.Shared.Progression;
using UnityEngine;

namespace Game.Client.Player
{
    /// <summary>
    /// Nível e XP do jogador, usando as regras do Shared (<see cref="CharacterProgress"/>, <see cref="XpRules"/>).
    /// Os atributos do nível vêm de <see cref="CharacterStats.ForLevel"/>.
    /// </summary>
    public sealed class PlayerProgression : MonoBehaviour
    {
        [SerializeField] private int startingLevel = 1;

        private CharacterProgress _progress;

        // Criado sob demanda: outros componentes podem ler no Awake deles, antes do nosso.
        public CharacterProgress Progress =>
            _progress ??= new CharacterProgress(Mathf.Clamp(startingLevel, 1, LevelCurve.MaxLevel));

        public CharacterStats Stats => CharacterStats.ForLevel(Progress.Level);
        public int Level => Progress.Level;

        /// <summary>Disparado ao subir de nível (novo nível).</summary>
        public event Action<int> LeveledUp;

        /// <summary>Ganha XP de um monstro, ajustada pela diferença de nível. Retorna a XP recebida.</summary>
        public int GainMonsterXp(int baseXp, int monsterLevel)
        {
            var xp = XpRules.Adjusted(baseXp, Progress.Level, monsterLevel);
            DamageNumbers.Spawn(transform.position + Vector3.up * 2.6f, $"+{xp} XP", new Color(0.75f, 0.55f, 1f), 22f);

            var levels = Progress.AddXp(xp);
            if (levels > 0)
            {
                DamageNumbers.Spawn(transform.position + Vector3.up * 3.1f, $"NÍVEL {Progress.Level}!", new Color(1f, 0.9f, 0.3f), 40f);
                SoundPlayer.Play(SoundId.Route, 1f, 0f);
                if (CombatFx.Instance != null)
                    CombatFx.Instance.Sparks(transform.position + Vector3.up, new Color(1f, 0.9f, 0.3f), 60);
                LeveledUp?.Invoke(Progress.Level);
            }
            return xp;
        }
    }
}
