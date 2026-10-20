using System.Numerics;
using Game.Shared.Combat;
using Game.Shared.Monsters;

namespace Game.Shared.Tests.Monsters;

/// <summary>Os jeitos de lutar de cada monstro (fugir, investir, recuar, sumir, enfurecer).</summary>
public class MonsterBehaviorTests
{
    private static MonsterData Data(MonsterBehavior behavior) => new()
    {
        Id = "teste",
        AttackRange = 1.5f,
        AttackCooldownSeconds = 2f,
        AttackWindupSeconds = 0.5f,
        MoveSpeed = 4f,
        Aggressive = true,
        AggroRadius = 15f,
        LeashRadius = 40f,
        WanderRadius = 0,
        Behavior = behavior,
    };

    private static MonsterBrain Brain(MonsterBehavior behavior) => new(Data(behavior), Vector2.Zero, new SystemRandomSource(1));

    [Fact]
    public void Coelho_com_pouca_vida_foge_para_longe_e_depois_volta()
    {
        var brain = Brain(MonsterBehavior.Skittish);
        var player = new Vector2(1, 0);
        brain.Tick(0, Vector2.Zero, player);

        var flee = brain.Tick(0.1, Vector2.Zero, player, hpFraction: 0.2f);

        Assert.Equal(MonsterState.Flee, brain.State);
        Assert.Equal(MonsterActionKind.MoveTo, flee.Kind);
        Assert.True(flee.Point.X < 0, "foge para o lado oposto ao jogador");
        Assert.True(flee.SpeedFactor > 1f);

        brain.Tick(0.1 + MonsterBehaviors.FleeSeconds + 0.1, flee.Point, player, hpFraction: 0.2f);
        Assert.Equal(MonsterState.Chase, brain.State);
        // Foge uma vez só por luta.
        brain.Tick(5, Vector2.Zero, new Vector2(5, 0), hpFraction: 0.1f);
        Assert.NotEqual(MonsterState.Flee, brain.State);
    }

    [Fact]
    public void Touro_de_longe_prepara_e_dispara_a_investida_em_linha_reta()
    {
        var brain = Brain(MonsterBehavior.Charger);
        var player = new Vector2(8, 0);
        brain.Tick(0, Vector2.Zero, player); // começa a perseguir (a investida só fica pronta depois do respiro)

        var windup = brain.Tick(2.5, Vector2.Zero, player);
        Assert.Equal(MonsterState.ChargeWindUp, brain.State);
        Assert.Equal(MonsterActionKind.AttackStarted, windup.Kind);
        Assert.True(brain.IsWindingUp);

        var charge = brain.Tick(2.5 + MonsterBehaviors.ChargeWindupSeconds + 0.01, Vector2.Zero, player);
        Assert.Equal(MonsterState.Charge, brain.State);
        Assert.Equal(MonsterActionKind.MoveTo, charge.Kind);
        Assert.Equal(MonsterBehaviors.ChargeSpeed, charge.SpeedFactor);
        Assert.True(charge.Point.X > player.X, "passa do ponto do jogador");

        // Chegou no jogador no caminho: o golpe sai.
        var hit = brain.Tick(3.8, new Vector2(7, 0), player);
        Assert.Equal(MonsterActionKind.AttackLanded, hit.Kind);
        Assert.Equal(MonsterState.Chase, brain.State);
    }

    [Fact]
    public void Investida_que_erra_nao_da_golpe()
    {
        var brain = Brain(MonsterBehavior.Charger);
        brain.Tick(0, Vector2.Zero, new Vector2(8, 0));
        brain.Tick(2.5, Vector2.Zero, new Vector2(8, 0));
        brain.Tick(2.5 + MonsterBehaviors.ChargeWindupSeconds + 0.01, Vector2.Zero, new Vector2(8, 0));

        // O jogador saiu da frente; a investida acaba no tempo máximo.
        var end = brain.Tick(2.5 + MonsterBehaviors.ChargeWindupSeconds + MonsterBehaviors.ChargeMaxSeconds + 0.1,
            new Vector2(10, 0), new Vector2(8, 8));
        Assert.Equal(MonsterActionKind.None, end.Kind);
        Assert.Equal(MonsterState.Chase, brain.State);
    }

    [Fact]
    public void Aranha_bate_e_recua()
    {
        var brain = Brain(MonsterBehavior.HitAndRun);
        var player = new Vector2(1, 0);
        brain.Tick(0, Vector2.Zero, player); // prepara o golpe

        var landed = brain.Tick(0.6, Vector2.Zero, player);
        Assert.Equal(MonsterActionKind.AttackLanded, landed.Kind);
        Assert.Equal(MonsterState.Retreat, brain.State);

        var retreat = brain.Tick(0.7, Vector2.Zero, player);
        Assert.Equal(MonsterActionKind.MoveTo, retreat.Kind);
        Assert.True(retreat.Point.X < 0, "recua para longe do jogador");
    }

    [Fact]
    public void Espectro_some_e_reaparece_atras_de_quem_se_afastou()
    {
        var brain = Brain(MonsterBehavior.Blink);
        var player = new Vector2(10, 0);
        brain.Tick(0, Vector2.Zero, player);

        var blink = brain.Tick(3.5, Vector2.Zero, player);

        Assert.Equal(MonsterActionKind.Blink, blink.Kind);
        Assert.True(blink.Point.X > player.X, "aparece do outro lado do jogador");
        Assert.Equal(MonsterBehaviors.BlinkLandDistance, Vector2.Distance(blink.Point, player), 3);
        // Recarga: logo depois só persegue.
        Assert.Equal(MonsterActionKind.MoveTo, brain.Tick(4, Vector2.Zero, player).Kind);
    }

    [Fact]
    public void Cavaleiro_com_pouca_vida_fica_furioso_e_bate_mais_rapido()
    {
        var brain = Brain(MonsterBehavior.Berserk);
        var player = new Vector2(1, 0);
        brain.Tick(0, Vector2.Zero, player, hpFraction: 0.9f);
        Assert.False(brain.Enraged);

        brain.Tick(0.6, Vector2.Zero, player, hpFraction: 0.3f); // o golpe sai já furioso
        Assert.True(brain.Enraged);

        // Recarga normal de 2 s; furioso, 2 × 0,55 = 1,1 s.
        Assert.Equal(MonsterActionKind.AttackStarted, brain.Tick(0.6 + 1.2, Vector2.Zero, player, hpFraction: 0.3f).Kind);
    }

    [Fact]
    public void Voltar_para_casa_esquece_a_furia_e_a_fuga()
    {
        var brain = Brain(MonsterBehavior.Berserk);
        brain.Tick(0, Vector2.Zero, new Vector2(1, 0), hpFraction: 0.2f);
        Assert.True(brain.Enraged);

        brain.Tick(1, new Vector2(50, 0), new Vector2(51, 0), hpFraction: 0.2f); // o golpe sai
        brain.Tick(1.5, new Vector2(50, 0), new Vector2(51, 0), hpFraction: 0.2f); // longe demais: volta
        brain.Tick(2, Vector2.Zero, null, hpFraction: 1f); // chegou em casa

        Assert.False(brain.Enraged);
        Assert.Equal(MonsterState.Idle, brain.State);
    }
}
