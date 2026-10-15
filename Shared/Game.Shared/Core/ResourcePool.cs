using System;

namespace Game.Shared.Core
{
    /// <summary>
    /// Recurso com valor atual e máximo (HP, MP, SP). O valor fica sempre entre 0 e o máximo.
    /// </summary>
    public sealed class ResourcePool
    {
        /// <summary>Valor atual.</summary>
        public int Current { get; private set; }

        /// <summary>Valor máximo.</summary>
        public int Max { get; private set; }

        /// <summary>Cria o recurso. Se <paramref name="current"/> for omitido, começa cheio.</summary>
        public ResourcePool(int max, int? current = null)
        {
            if (max < 0) throw new ArgumentOutOfRangeException(nameof(max), "O máximo não pode ser negativo.");
            Max = max;
            Current = Clamp(current ?? max);
        }

        /// <summary>Verdadeiro se há pelo menos <paramref name="amount"/> disponível.</summary>
        public bool Has(int amount) => amount >= 0 && Current >= amount;

        /// <summary>Gasta <paramref name="amount"/> se houver o suficiente; caso contrário não altera nada.</summary>
        public bool TrySpend(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount), "A quantidade não pode ser negativa.");
            if (Current < amount) return false;
            Current -= amount;
            return true;
        }

        /// <summary>Soma (ou subtrai, se negativo) respeitando os limites. Retorna quanto realmente mudou.</summary>
        public int Add(int amount)
        {
            var before = Current;
            Current = Clamp(Current + amount);
            return Current - before;
        }

        /// <summary>Muda o máximo e ajusta o valor atual se passar do novo limite.</summary>
        public void SetMax(int max)
        {
            if (max < 0) throw new ArgumentOutOfRangeException(nameof(max), "O máximo não pode ser negativo.");
            Max = max;
            Current = Clamp(Current);
        }

        private int Clamp(int value) => Math.Max(0, Math.Min(Max, value));
    }
}
