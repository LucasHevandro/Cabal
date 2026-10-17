using Game.Shared.Combat;
using Game.Shared.Core;

namespace Game.Shared.Tests.Combat;

public class SkillRulesTests
{
    private static readonly SkillData Corte = new("corte", SkillKind.Physical, 120, 2.5f, 0.5f);
    private static readonly SkillData Lanca = new("lanca", SkillKind.Magical, 160, 12f, 1.5f, mpCost: 40);

    [Fact]
    public void Pode_usar_com_alvo_no_alcance_e_skill_pronta()
    {
        var check = SkillRules.Check(Corte, 0, new SkillCooldowns(4), new ResourcePool(100), 1.0, now: 10);

        Assert.Equal(SkillUseCheck.Ok, check);
    }

    [Fact]
    public void Sem_alvo_nao_pode()
    {
        var check = SkillRules.Check(Corte, 0, new SkillCooldowns(4), new ResourcePool(100), null, now: 10);

        Assert.Equal(SkillUseCheck.NoTarget, check);
    }

    [Fact]
    public void Fora_do_alcance_nao_pode()
    {
        var check = SkillRules.Check(Corte, 0, new SkillCooldowns(4), new ResourcePool(100), 2.6, now: 10);

        Assert.Equal(SkillUseCheck.OutOfRange, check);
    }

    [Fact]
    public void Sem_mana_nao_pode()
    {
        var check = SkillRules.Check(Lanca, 2, new SkillCooldowns(4), new ResourcePool(100, 39), 5.0, now: 10);

        Assert.Equal(SkillUseCheck.NotEnoughMp, check);
    }

    [Fact]
    public void Usar_gasta_mana_e_inicia_a_recarga()
    {
        var cooldowns = new SkillCooldowns(4);
        var mp = new ResourcePool(100);

        Assert.Equal(SkillUseCheck.Ok, SkillRules.TryUse(Lanca, 2, cooldowns, mp, 5.0, now: 10));

        Assert.Equal(60, mp.Current);
        Assert.False(cooldowns.IsReady(2, 11.0));
        Assert.Equal(0.5, cooldowns.Remaining(2, 11.0), 6);
        Assert.True(cooldowns.IsReady(2, 11.5));
        Assert.True(cooldowns.IsReady(0, 11.0));
    }

    [Fact]
    public void Em_recarga_nao_pode_e_nao_gasta_mana()
    {
        var cooldowns = new SkillCooldowns(4);
        var mp = new ResourcePool(100);
        SkillRules.TryUse(Lanca, 2, cooldowns, mp, 5.0, now: 10);

        var check = SkillRules.TryUse(Lanca, 2, cooldowns, mp, 5.0, now: 10.5);

        Assert.Equal(SkillUseCheck.OnCooldown, check);
        Assert.Equal(60, mp.Current);
    }

    [Fact]
    public void Recarga_tem_prioridade_sobre_alcance()
    {
        // O jogador vê "em recarga" (que quebra o combo) mesmo se também estiver longe.
        var cooldowns = new SkillCooldowns(1);
        cooldowns.Start(0, 10, 5);

        var check = SkillRules.Check(Corte, 0, cooldowns, new ResourcePool(100), 50.0, now: 11);

        Assert.Equal(SkillUseCheck.OnCooldown, check);
    }

    [Fact]
    public void Dados_invalidos_sao_rejeitados()
    {
        Assert.Throws<ArgumentException>(() => new SkillData("", SkillKind.Physical, 10, 1, 1).Validate());
        Assert.Throws<ArgumentException>(() => new SkillData("x", SkillKind.Physical, 10, 0, 1).Validate());
        Assert.Throws<ArgumentException>(() => new SkillData("x", SkillKind.Magical, 10, 1, 1, mpCost: -1).Validate());
        new SkillData("ok", SkillKind.Physical, 10, 1, 1).Validate();
    }
}
