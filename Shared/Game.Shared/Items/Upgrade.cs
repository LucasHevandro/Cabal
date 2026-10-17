using System;
using Game.Shared.Combat;
using Game.Shared.Text;

namespace Game.Shared.Items
{
    public enum UpgradeOutcome
    {
        /// <summary>Não tentou (falta material, Alz ou item inválido).</summary>
        Refused = 0,

        Success = 1,

        /// <summary>Falhou e manteve o nível.</summary>
        FailedKept = 2,

        /// <summary>Falhou e perdeu um nível.</summary>
        FailedDowngraded = 3,
    }

    /// <summary>Custo de uma tentativa de melhoria.</summary>
    public readonly struct UpgradeCost
    {
        public string CoreId { get; }
        public int Cores { get; }
        public long Alz { get; }

        public UpgradeCost(string coreId, int cores, long alz)
        {
            CoreId = coreId;
            Cores = cores;
            Alz = alz;
        }
    }

    /// <summary>
    /// Melhoria até +10 (GDD): chance sempre visível e igual para todos; falhar em +5 ou mais perde um nível, abaixo
    /// disso mantém; o item nunca quebra. Proteção contra azar, com memória:
    /// <list type="bullet">
    /// <item>cada falha tentando passar de +n conta para a barra de +n; cheia, a próxima tentativa em +n é garantida;</item>
    /// <item>quem cai de nível tem a recuperação garantida e não perde as falhas já contadas em +n.</item>
    /// </list>
    /// Assim o pior caso é limitado (de +0 a +10, no máximo 44 tentativas), sem tirar o risco de cada tentativa.
    /// </summary>
    public static class UpgradeRules
    {
        public const int MaxLevel = 10;

        /// <summary>Chance (0–1) de ir de +n para +(n+1), por n.</summary>
        private static readonly double[] Chances = { 1.00, 1.00, 0.95, 0.85, 0.75, 0.65, 0.55, 0.45, 0.38, 0.30 };

        /// <summary>A partir deste nível, falhar perde um nível.</summary>
        public const int DowngradeFrom = 5;

        public static double SuccessChance(int currentLevel) =>
            currentLevel < 0 || currentLevel >= MaxLevel ? 0 : Chances[currentLevel];

        /// <summary>
        /// Falhas seguidas que garantem o próximo sucesso: o número esperado de tentativas pela chance (30% → 4 falhas,
        /// a 5ª é garantida). Mostrado ao jogador como a barra de proteção.
        /// </summary>
        public static int PityLimit(int currentLevel)
        {
            var chance = SuccessChance(currentLevel);
            return chance >= 1 ? 0 : (int)Math.Ceiling(1 / chance);
        }

        public static UpgradeCost CostFor(ItemDefinition item, int currentLevel)
        {
            var target = currentLevel + 1;
            var cores = 1 + target / 4;                              // +1..+3: 1, +4..+7: 2, +8..+10: 3
            var alz = (long)(100 * item.Grade * target * target);   // +10 de Ferro: 10.000 Alz
            return new UpgradeCost(ItemCatalog.CoreForGrade(item.Grade), cores, alz);
        }

        /// <summary>
        /// Quanto da melhoria vale nos atributos: cada nível soma 1 passo, e de +7 em diante cada nível soma 2
        /// (os últimos níveis são os mais disputados).
        /// </summary>
        public static int EffectiveSteps(int upgrade)
        {
            var level = Math.Max(0, Math.Min(MaxLevel, upgrade));
            return level + Math.Max(0, level - 6);
        }

        /// <summary>Nulo se dá para tentar; senão o motivo.</summary>
        public static LocalText? CanTry(ItemInstance item, ItemContainer bag, long alz)
        {
            var definition = ItemCatalog.Find(item.ItemId);
            if (definition == null || definition.Kind != ItemKind.Equipment) return new LocalText("upgrade.not_gear");
            if (item.Upgrade >= MaxLevel) return new LocalText("upgrade.max", MaxLevel);
            var cost = CostFor(definition, item.Upgrade);
            if (bag.Count(cost.CoreId) < cost.Cores)
                return new LocalText("upgrade.no_cores", cost.Cores, Loc.Ref("item." + cost.CoreId));
            if (alz < cost.Alz) return new LocalText("upgrade.no_alz", cost.Alz);
            return null;
        }

        /// <summary>
        /// Tenta melhorar: consome núcleos e Alz e sorteia (ou usa a garantia). Retorna o resultado e o Alz gasto.
        /// O item, a bolsa e o Alz só mudam se a tentativa for feita.
        /// </summary>
        public static UpgradeOutcome Try(ItemInstance item, ItemContainer bag, ref long alz, IRandomSource random)
        {
            if (CanTry(item, bag, alz) != null) return UpgradeOutcome.Refused;
            var definition = ItemCatalog.Get(item.ItemId);
            var cost = CostFor(definition, item.Upgrade);
            bag.TryRemove(cost.CoreId, cost.Cores);
            alz -= cost.Alz;

            if (IsGuaranteed(item) || random.NextDouble() < SuccessChance(item.Upgrade))
            {
                if (item.RegainGuaranteed) item.RegainGuaranteed = false; // recuperou: as falhas do nível de cima continuam valendo
                else item.PityFails = 0;
                item.Upgrade++;
                return UpgradeOutcome.Success;
            }

            if (item.PityLevel != item.Upgrade)
            {
                item.PityLevel = item.Upgrade;
                item.PityFails = 0;
            }
            item.PityFails++;
            if (item.Upgrade < DowngradeFrom) return UpgradeOutcome.FailedKept;
            item.Upgrade--;
            item.RegainGuaranteed = true;
            return UpgradeOutcome.FailedDowngraded;
        }

        /// <summary>Verdadeiro se a próxima tentativa é sucesso garantido (recuperação ou barra de proteção cheia).</summary>
        public static bool IsGuaranteed(ItemInstance item) =>
            item.RegainGuaranteed || (item.PityLevel == item.Upgrade && item.PityFails >= PityLimit(item.Upgrade));

        /// <summary>Falhas já contadas na barra do nível atual (para mostrar ao jogador).</summary>
        public static int PityProgress(ItemInstance item) => item.PityLevel == item.Upgrade ? item.PityFails : 0;

        /// <summary>
        /// Coloca uma pedra de opção num item sem opção (a pedra é consumida). Nulo se deu certo; senão o motivo.
        /// </summary>
        public static LocalText? ApplyOption(ItemInstance item, ItemContainer bag, string stoneId)
        {
            var definition = ItemCatalog.Find(item.ItemId);
            var stone = ItemCatalog.Find(stoneId);
            if (definition == null || definition.Kind != ItemKind.Equipment) return new LocalText("option.not_gear");
            if (stone == null || !stone.IsOptionStone) return new LocalText("option.not_stone");
            if (!string.IsNullOrEmpty(item.OptionId)) return new LocalText("option.has_option");
            if (!bag.TryRemove(stoneId, 1)) return new LocalText("option.no_stone");
            item.OptionId = stoneId;
            return null;
        }
    }
}
