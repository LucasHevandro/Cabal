using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Accounts;
using Game.Shared.Items;
using Game.Shared.Progression;
using Game.Shared.World;

namespace Game.Shared.Economy
{
    /// <summary>Premissas de gasto de um jogador típico (para a revisão da economia).</summary>
    public sealed class EconomyAssumptions
    {
        /// <summary>Poções usadas por hora de caça (pequena antes do nível 10, média depois).</summary>
        public double PotionsPerHour = 25;

        /// <summary>Compra o conjunto de Ferro na loja uma vez (couraça, luvas, botas e anel).</summary>
        public bool BuysIronSet = true;
    }

    /// <summary>Alz que entra e que sai numa faixa de níveis.</summary>
    public sealed class EconomyBand
    {
        public string Name = "";
        public double Hours;
        public long AlzFromMonsters;
        public long AlzFromSales;
        public long AlzFromQuests;
        public long SpentOnUpgrades;
        public long SpentOnCores;
        public long SpentOnPotions;
        public long SpentOnGear;

        /// <summary>Skills aprendidas no mestre nesta faixa.</summary>
        public long SpentOnSkills;

        public long Income => AlzFromMonsters + AlzFromSales + AlzFromQuests;
        public long Spent => SpentOnUpgrades + SpentOnCores + SpentOnPotions + SpentOnGear + SpentOnSkills;
        public long Balance => Income - Spent;
    }

    /// <summary>
    /// Revisão da economia 1→30: usa a simulação de subida de nível (onde e quanto caçou) para estimar o Alz que entra
    /// (Alz dos monstros, das missões e venda dos drops na loja, exceto núcleos e pedras) e o que sai (melhorias até o nível médio de
    /// cada grau, núcleos comprados que faltaram, poções, o conjunto de Ferro e as skills do mestre).
    /// </summary>
    public static class EconomySimulation
    {
        public static List<EconomyBand> Run(CharacterClass characterClass, LevelingAssumptions? leveling = null, EconomyAssumptions? economy = null)
        {
            leveling ??= new LevelingAssumptions();
            economy ??= new EconomyAssumptions();
            var monsters = WorldContent.Spawns().Select(s => s.Data).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
            var steps = LevelingSimulation.Run(characterClass, leveling);
            var bands = new List<EconomyBand>();
            var coresDropped = new Dictionary<string, double>();

            for (var grade = 1; grade <= 3; grade++)
            {
                var from = grade == 1 ? 1 : grade == 2 ? 10 : 20;
                var to = grade == 3 ? LevelCurve.MaxLevel - 1 : from + 9 - (grade == 1 ? 1 : 0);
                var band = new EconomyBand { Name = $"Nv {from}–{to + 1} ({ItemCatalog.GradeName(grade)})" };
                foreach (var step in steps.Where(s => s.Level >= from && s.Level <= to))
                {
                    var monster = monsters[step.MonsterId];
                    band.Hours += step.Minutes / 60;
                    band.AlzFromMonsters += (long)(step.Kills * (monster.Drops.AlzMin + monster.Drops.AlzMax) / 2.0);
                    band.AlzFromQuests += step.QuestAlz;
                    foreach (var (itemId, quantity) in step.QuestItems)
                    {
                        var reward = ItemCatalog.Find(itemId);
                        if (reward != null && reward.UpgradeCoreGrade > 0) coresDropped[itemId] = coresDropped.GetValueOrDefault(itemId) + quantity;
                    }
                    foreach (var entry in monster.Drops.Entries)
                    {
                        var amount = step.Kills * entry.Chance * (entry.MinQuantity + entry.MaxQuantity) / 2.0;
                        var definition = ItemCatalog.Find(entry.ItemId);
                        if (definition == null) continue;
                        if (definition.UpgradeCoreGrade > 0 || definition.IsOptionStone)
                        {
                            coresDropped[entry.ItemId] = coresDropped.GetValueOrDefault(entry.ItemId) + amount;
                            continue;
                        }
                        band.AlzFromSales += (long)(amount * EconomyRules.SellPrice(definition));
                    }
                }

                // Melhoria: 5 peças do grau até o nível médio, tentativas esperadas = 1 / chance (sem queda até +5).
                var target = leveling.UpgradeByGrade[grade - 1];
                var coreId = ItemCatalog.CoreForGrade(grade);
                var coresNeeded = 0.0;
                var sample = ItemCatalog.Get("armadura_" + (grade == 3 ? "astralita" : grade == 2 ? "aco_negro" : "ferro"));
                for (var level = 0; level < target; level++)
                {
                    var cost = UpgradeRules.CostFor(sample, level);
                    var attempts = 1 / UpgradeRules.SuccessChance(level);
                    band.SpentOnUpgrades += (long)(5 * attempts * cost.Alz);
                    coresNeeded += 5 * attempts * cost.Cores;
                }
                var missing = Math.Max(0, coresNeeded - coresDropped.GetValueOrDefault(coreId));
                coresDropped[coreId] = Math.Max(0, coresDropped.GetValueOrDefault(coreId) - coresNeeded);
                var shopCore = ShopCatalog.Find(coreId);
                if (shopCore != null) band.SpentOnCores += (long)(missing * shopCore.Price);

                // Skills do mestre: aprende cada uma quando o nível deixa.
                foreach (var skill in ClassCatalog.SkillsFor(characterClass))
                    if (skill.RequiredLevel >= from && skill.RequiredLevel <= to) band.SpentOnSkills += SkillTraining.PriceFor(skill);

                var potion = ShopCatalog.Find(grade == 1 ? "pocao_hp_p" : "pocao_hp_m")!;
                band.SpentOnPotions += (long)(band.Hours * economy.PotionsPerHour * potion.Price);
                if (grade == 1 && economy.BuysIronSet)
                    band.SpentOnGear += new[] { "armadura_ferro", "luvas_ferro", "botas_ferro", "anel_ferro" }.Sum(id => ShopCatalog.Find(id)!.Price);
                bands.Add(band);
            }
            return bands;
        }
    }
}
