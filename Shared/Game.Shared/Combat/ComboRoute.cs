using System;
using System.Collections.Generic;
using Game.Shared.Text;

namespace Game.Shared.Combat
{
    /// <summary>Tipo de dano de uma skill. A ordem dos tipos dentro do combo forma as rotas.</summary>
    public enum SkillKind
    {
        /// <summary>Golpe físico (arma).</summary>
        Physical = 0,

        /// <summary>Golpe mágico (Force).</summary>
        Magical = 1,
    }

    /// <summary>
    /// Rota de combo: uma sequência de tipos de skill que, completada dentro da corrente,
    /// dá bônus de dano ao golpe que a fecha. Ex.: físico → mágico.
    /// </summary>
    public sealed class ComboRoute
    {
        private readonly SkillKind[] _pattern;

        /// <summary>Identificador estável (vai pela rede).</summary>
        public string Id { get; }

        /// <summary>Nome no idioma do jogador (chave <c>route.&lt;id&gt;</c>).</summary>
        public string Name => Loc.T(Loc.Key("route.", Id));

        /// <summary>Sequência de tipos, do mais antigo para o mais recente.</summary>
        public IReadOnlyList<SkillKind> Pattern => _pattern;

        /// <summary>Bônus de dano do golpe que fecha a rota (0,10 = +10%).</summary>
        public double DamageBonus { get; }

        /// <summary>Cria uma rota. O padrão precisa de pelo menos 2 golpes.</summary>
        public ComboRoute(string id, double damageBonus, params SkillKind[] pattern)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A rota precisa de id.", nameof(id));
            if (pattern == null || pattern.Length < 2) throw new ArgumentException("A rota precisa de pelo menos 2 golpes.", nameof(pattern));
            if (damageBonus < 0) throw new ArgumentOutOfRangeException(nameof(damageBonus), "O bônus não pode ser negativo.");
            Id = id;
            DamageBonus = damageBonus;
            _pattern = (SkillKind[])pattern.Clone();
        }

        /// <summary>Verdadeiro se os últimos golpes de <paramref name="history"/> formam esta rota.</summary>
        public bool MatchesEndOf(IReadOnlyList<SkillKind> history)
        {
            if (history.Count < _pattern.Length) return false;
            var offset = history.Count - _pattern.Length;
            for (var i = 0; i < _pattern.Length; i++)
            {
                if (history[offset + i] != _pattern[i]) return false;
            }
            return true;
        }

        /// <summary>
        /// Rotas iniciais do protótipo. Rotas longas valem mais e têm prioridade sobre as curtas
        /// que terminam do mesmo jeito.
        /// </summary>
        public static List<ComboRoute> CreateDefaults() => new List<ComboRoute>
        {
            new ComboRoute("alternancia", 0.10, SkillKind.Physical, SkillKind.Magical),
            new ComboRoute("alternancia_inversa", 0.10, SkillKind.Magical, SkillKind.Physical),
            new ComboRoute("tempestade", 0.25, SkillKind.Physical, SkillKind.Physical, SkillKind.Magical),
            new ComboRoute("eco_arcano", 0.25, SkillKind.Magical, SkillKind.Magical, SkillKind.Physical),
        };
    }
}
