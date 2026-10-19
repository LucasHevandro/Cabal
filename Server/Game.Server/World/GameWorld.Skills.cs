using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;

namespace Game.Server.World;

/// <summary>
/// Mestre de skills: o jogador aprende as skills da classe pagando em Alz (o nível e a missão de rank só deixam
/// aprender), e arruma a barra de atalhos como quiser. Tudo salvo com o personagem.
/// </summary>
public sealed partial class GameWorld
{
    private void HandleLearnSkill(ServerPlayer player, LearnSkill message)
    {
        if (player.IsDead || !NearNpc(player, NpcRole.Trainer))
        {
            Notify(player, LT("trainer.go_to", Loc.Ref("npc." + NpcCatalog.Trainer)));
            return;
        }
        var index = -1;
        for (var i = 0; i < player.ClassSkills.Count; i++)
            if (player.ClassSkills[i].Skill.Id == message.SkillId) index = i;
        if (index < 0) return;

        var skill = player.ClassSkills[index];
        var refused = SkillTraining.CanLearn(skill, player.Progress.Level, player.QuestCompleted, player.Alz, player.SkillUnlocked(index));
        if (refused != null)
        {
            Notify(player, refused);
            return;
        }
        var price = SkillTraining.PriceFor(skill);
        player.Alz -= price;
        player.LearnedSkills.Add(skill.Skill.Id);
        SkillTraining.PlaceInFirstEmpty(player.Hotbar, (byte)index);
        Log(player, "skill_aprendida", null, 0, -price, skill.Skill.Id);
        Notify(player, LT("trainer.learned", Loc.Ref("skill." + skill.Skill.Id), price));
        player.SheetDirty = true;
        player.StatsDirty = true;
    }

    private static void HandleSetHotbar(ServerPlayer player, SetHotbar message)
    {
        player.Hotbar = SkillTraining.Sanitize(message.Slots, player.ClassSkills, player.SkillUnlockedIds());
        player.SheetDirty = true;
    }
}
