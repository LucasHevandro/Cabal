using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Accounts;
using Game.Shared.Text;

namespace Game.Shared.Progression
{
    /// <summary>
    /// Skills aprendidas com o mestre de skills da vila (como no CABAL): o nível e a missão de rank só deixam aprender;
    /// a skill entra quando o jogador paga ao mestre. A barra de atalhos (8 casas) é montada pelo jogador arrastando.
    /// </summary>
    public static class SkillTraining
    {
        /// <summary>Casas da barra: duas fileiras de 10 (1 a 0 e Alt+1 a Alt+0), como no CABAL.</summary>
        public const int HotbarSize = 20;

        /// <summary>Casas por fileira.</summary>
        public const int HotbarRow = 10;

        /// <summary>Casa vazia na barra.</summary>
        public const byte Empty = 255;

        /// <summary>Preço no mestre (as skills de nível 1 já vêm com o personagem). Números provisórios até a planilha.</summary>
        public static long PriceFor(ClassSkill skill) =>
            skill.RequiredLevel <= 1 ? 0 : 40L * skill.RequiredLevel * skill.RequiredLevel;

        /// <summary>Skills que o personagem já sabe ao ser criado.</summary>
        public static IEnumerable<string> Starting(CharacterClass characterClass) =>
            ClassCatalog.SkillsFor(characterClass).Where(s => PriceFor(s) == 0 && string.IsNullOrEmpty(s.RequiredQuestId)).Select(s => s.Skill.Id);

        /// <summary>
        /// Personagens de antes do mestre: ficam com tudo o que o nível e as missões já liberavam (ninguém perde skill).
        /// </summary>
        public static IEnumerable<string> FromOldRules(CharacterClass characterClass, int level, Func<string, bool> questCompleted) =>
            ClassCatalog.SkillsFor(characterClass).Where(s => s.IsUnlocked(level, questCompleted)).Select(s => s.Skill.Id);

        /// <summary>Por que não pode aprender (nulo = pode).</summary>
        public static LocalText? CanLearn(ClassSkill skill, int level, Func<string, bool> questCompleted, long alz, bool known)
        {
            if (known) return new LocalText("trainer.already_known", Loc.Ref("skill." + skill.Skill.Id));
            if (level < skill.RequiredLevel) return new LocalText("trainer.need_level", skill.RequiredLevel);
            if (!string.IsNullOrEmpty(skill.RequiredQuestId) && !questCompleted(skill.RequiredQuestId))
                return new LocalText("trainer.need_quest", Loc.Ref("quest." + skill.RequiredQuestId));
            var price = PriceFor(skill);
            if (alz < price) return new LocalText("trainer.no_alz", price);
            return null;
        }

        /// <summary>Barra padrão: as skills aprendidas na ordem do catálogo, o resto vazio.</summary>
        public static byte[] DefaultHotbar(IReadOnlyList<ClassSkill> skills, ICollection<string> learned)
        {
            var bar = Enumerable.Repeat(Empty, HotbarSize).ToArray();
            var next = 0;
            for (var i = 0; i < skills.Count && next < HotbarSize; i++)
                if (learned.Contains(skills[i].Skill.Id)) bar[next++] = (byte)i;
            return bar;
        }

        /// <summary>
        /// Arruma uma barra vinda de fora (cliente ou banco): tamanho certo, só skills aprendidas, sem repetir.
        /// </summary>
        public static byte[] Sanitize(IReadOnlyList<byte> slots, IReadOnlyList<ClassSkill> skills, ICollection<string> learned)
        {
            var bar = Enumerable.Repeat(Empty, HotbarSize).ToArray();
            var used = new HashSet<byte>();
            for (var i = 0; i < HotbarSize && i < slots.Count; i++)
            {
                var index = slots[i];
                if (index == Empty || index >= skills.Count || !learned.Contains(skills[index].Skill.Id) || !used.Add(index)) continue;
                bar[i] = index;
            }
            return bar;
        }

        /// <summary>Põe a skill na primeira casa vazia (se ainda não está na barra).</summary>
        public static void PlaceInFirstEmpty(byte[] bar, byte index)
        {
            if (Array.IndexOf(bar, index) >= 0) return;
            var free = Array.IndexOf(bar, Empty);
            if (free >= 0) bar[free] = index;
        }

        /// <summary>Barra para o banco: o id de cada casa (vazio = casa vazia), separados por vírgula.</summary>
        public static IReadOnlyList<string> ToIds(byte[] bar, IReadOnlyList<ClassSkill> skills) =>
            bar.Select(i => i == Empty || i >= skills.Count ? "" : skills[i].Skill.Id).ToList();

        /// <summary>Barra do banco (ids) de volta para índices da classe.</summary>
        public static byte[] FromIds(IReadOnlyList<string> ids, IReadOnlyList<ClassSkill> skills)
        {
            var bar = Enumerable.Repeat(Empty, HotbarSize).ToArray();
            for (var i = 0; i < HotbarSize && i < ids.Count; i++)
            {
                for (var s = 0; s < skills.Count; s++)
                    if (skills[s].Skill.Id == ids[i]) bar[i] = (byte)s;
            }
            return bar;
        }
    }
}
