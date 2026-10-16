using Game.Shared.Combat;

namespace Game.Shared.Tests.Combat;

// Todos os apertos acontecem no centro da janela (Excellent, multiplicador 1,20 na configuração padrão).
public class ComboRouteTests
{
    private const double Precision = 1e-9;
    private const SkillKind P = SkillKind.Physical;
    private const SkillKind M = SkillKind.Magical;

    private sealed class Driver
    {
        public ComboSession Session { get; }
        private double _now = 10;

        public Driver(ComboConfig? config = null)
        {
            Session = new ComboSession(config ?? ComboConfig.Default);
            Assert.True(Session.TryStart(_now));
        }

        public ComboHitResult Hit(SkillKind? kind)
        {
            _now += Session.Config.WindowCenter * Session.BarDurationSeconds;
            return Session.Press(_now, kind);
        }
    }

    [Fact]
    public void Primeiro_golpe_nao_fecha_rota()
    {
        var d = new Driver();

        var result = d.Hit(P);

        Assert.Null(result.Route);
        Assert.Equal(1.20, result.DamageMultiplier, Precision);
    }

    [Fact]
    public void Fisico_depois_magico_fecha_Alternancia()
    {
        var d = new Driver();
        d.Hit(P);

        var result = d.Hit(M);

        Assert.NotNull(result.Route);
        Assert.Equal("Alternância", result.Route!.Name);
        Assert.Equal(1.20 * 1.10, result.DamageMultiplier, Precision);
        Assert.Equal(1, d.Session.RoutesCompleted);
    }

    [Fact]
    public void Dois_iguais_seguidos_nao_fecham_rota()
    {
        var d = new Driver();
        d.Hit(P);

        var result = d.Hit(P);

        Assert.Null(result.Route);
    }

    [Fact]
    public void Rota_mais_longa_tem_prioridade()
    {
        var d = new Driver();
        d.Hit(P);
        d.Hit(P);

        // P, P, M casa com "Tempestade" (3 golpes) e também com o fim "Alternância" (P, M).
        var result = d.Hit(M);

        Assert.Equal("Tempestade", result.Route!.Name);
        Assert.Equal(1.20 * 1.25, result.DamageMultiplier, Precision);
    }

    [Fact]
    public void Alternar_sempre_fecha_rota_a_cada_golpe()
    {
        var d = new Driver();
        d.Hit(P);
        d.Hit(M);
        d.Hit(P);
        d.Hit(M);

        Assert.Equal(3, d.Session.RoutesCompleted);
        Assert.Equal(new[] { P, M, P, M }, d.Session.ChainKinds);
    }

    [Fact]
    public void Golpe_sem_tipo_recomeca_a_sequencia()
    {
        var d = new Driver();
        d.Hit(P);
        d.Hit(null);

        var result = d.Hit(M);

        Assert.Null(result.Route);
        Assert.Equal(new[] { M }, d.Session.ChainKinds);
    }

    [Fact]
    public void Novo_combo_esquece_a_sequencia_anterior()
    {
        var d = new Driver();
        d.Hit(P);
        d.Hit(M);
        d.Session.Cancel();
        Assert.True(d.Session.TryStart(1000));

        Assert.Empty(d.Session.ChainKinds);
        Assert.Equal(0, d.Session.RoutesCompleted);
    }

    [Fact]
    public void Sem_rotas_configuradas_nao_ha_bonus()
    {
        var d = new Driver(new ComboConfig { Routes = new List<ComboRoute>() });
        d.Hit(P);

        var result = d.Hit(M);

        Assert.Null(result.Route);
        Assert.Equal(1.20, result.DamageMultiplier, Precision);
    }

    [Fact]
    public void Rota_invalida_eh_rejeitada()
    {
        Assert.Throws<ArgumentException>(() => new ComboRoute("Curta", 0.1, P));
        Assert.Throws<ArgumentException>(() => new ComboRoute(" ", 0.1, P, M));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ComboRoute("Negativa", -0.1, P, M));
    }
}
