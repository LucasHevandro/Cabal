using Game.Shared.Combat;

namespace Game.Shared.Tests.Combat;

// Com a configuração padrão: barra de 1,2 s, janela centrada em 0,75 com largura 0,30 (de 0,60 a 0,90)
// e zona Excellent de ±0,0525 em volta do centro (0,6975 a 0,8025).
public class ComboSessionTests
{
    private const double Precision = 1e-9;

    /// <summary>Controla o relógio do teste e aperta numa posição exata da barra.</summary>
    private sealed class Driver
    {
        public ComboSession Session { get; }
        public double Now { get; private set; }

        public Driver(ComboConfig? config = null)
        {
            Session = new ComboSession(config ?? ComboConfig.Default);
            Now = 100.0;
            Assert.True(Session.TryStart(Now));
        }

        /// <summary>Instante em que o marcador estará na posição <paramref name="cursor"/> do ciclo atual.</summary>
        public double TimeAt(double cursor) => Now + cursor * Session.BarDurationSeconds;

        public ComboHitResult PressAt(double cursor)
        {
            var t = TimeAt(cursor);
            var result = Session.Press(t);
            Now = t;
            return result;
        }

        public bool TickAt(double cursor) => Session.Tick(TimeAt(cursor));
    }

    [Fact]
    public void Aperto_no_centro_da_janela_eh_Excellent()
    {
        var d = new Driver();

        var result = d.PressAt(0.75);

        Assert.Equal(ComboGrade.Excellent, result.Grade);
        Assert.Equal(1.20, result.DamageMultiplier, Precision);
        Assert.Equal(1, result.HitCount);
        Assert.False(result.ComboEnded);
        Assert.Equal(ComboState.Active, d.Session.State);
    }

    [Fact]
    public void Aperto_dentro_da_janela_mas_longe_do_centro_eh_Good()
    {
        var d = new Driver();

        var result = d.PressAt(0.65);

        Assert.Equal(ComboGrade.Good, result.Grade);
        Assert.Equal(1.10, result.DamageMultiplier, Precision);
        Assert.Equal(ComboState.Active, d.Session.State);
    }

    [Theory]
    [InlineData(0.50)]
    [InlineData(0.59)]
    [InlineData(0.91)]
    public void Aperto_fora_da_janela_eh_Miss_e_encerra_o_combo(double cursor)
    {
        var d = new Driver();

        var result = d.PressAt(cursor);

        Assert.Equal(ComboGrade.Miss, result.Grade);
        Assert.True(result.ComboEnded);
        Assert.Equal(1.0, result.DamageMultiplier, Precision);
        Assert.Equal(ComboState.Inactive, d.Session.State);
        Assert.Equal(ComboEndReason.Missed, d.Session.LastEndReason);
    }

    [Fact]
    public void Tick_nao_encerra_enquanto_o_marcador_esta_na_janela()
    {
        var d = new Driver();

        Assert.False(d.TickAt(0.85));
        Assert.Equal(ComboState.Active, d.Session.State);
    }

    [Fact]
    public void Tick_encerra_quando_o_marcador_passa_da_janela()
    {
        var d = new Driver();

        Assert.True(d.TickAt(0.95));
        Assert.Equal(ComboState.Inactive, d.Session.State);
        Assert.Equal(ComboEndReason.TimedOut, d.Session.LastEndReason);
    }

    [Fact]
    public void Cada_acerto_encolhe_a_janela_e_acelera_a_barra()
    {
        var d = new Driver();

        d.PressAt(0.75);

        Assert.Equal(0.30 * 0.88, d.Session.WindowWidth, Precision);
        Assert.Equal(1.2 * 0.95, d.Session.BarDurationSeconds, Precision);
    }

    [Fact]
    public void Janela_e_barra_param_de_mudar_depois_de_estabilizar()
    {
        var config = ComboConfig.Default;
        var d = new Driver(config);

        for (var i = 0; i < config.StabilizeAfterHits; i++) d.PressAt(0.75);
        var width = d.Session.WindowWidth;
        var duration = d.Session.BarDurationSeconds;

        for (var i = 0; i < 5; i++) d.PressAt(0.75);

        Assert.Equal(config.StabilizeAfterHits + 5, d.Session.HitCount);
        Assert.Equal(width, d.Session.WindowWidth, Precision);
        Assert.Equal(duration, d.Session.BarDurationSeconds, Precision);
    }

    [Fact]
    public void Janela_e_barra_respeitam_os_minimos()
    {
        var config = new ComboConfig { WindowShrinkFactor = 0.5, BarSpeedupFactor = 0.5, StabilizeAfterHits = 50 };
        var d = new Driver(config);

        for (var i = 0; i < 10; i++) d.PressAt(0.75);

        Assert.Equal(config.MinWindowWidth, d.Session.WindowWidth, Precision);
        Assert.Equal(config.MinBarDurationSeconds, d.Session.BarDurationSeconds, Precision);
    }

    [Fact]
    public void Finalizador_exige_acertos_minimos()
    {
        var d = new Driver();
        d.PressAt(0.75);
        d.PressAt(0.75);

        Assert.False(d.Session.CanFinish);
        Assert.False(d.Session.TryFinish(out var multiplier));
        Assert.Equal(1.0, multiplier, Precision);
        Assert.Equal(ComboState.Active, d.Session.State);
    }

    [Fact]
    public void Finalizador_converte_o_bonus_acumulado_e_encerra()
    {
        var d = new Driver();
        d.PressAt(0.75); // Excellent: +0,06
        d.PressAt(0.75); // Excellent: +0,06
        d.PressAt(0.75 - d.Session.WindowWidth / 2 + 0.001); // Good: +0,03

        Assert.True(d.Session.TryFinish(out var multiplier));
        Assert.Equal(1.15, multiplier, Precision);
        Assert.Equal(ComboState.Inactive, d.Session.State);
        Assert.Equal(ComboEndReason.Finished, d.Session.LastEndReason);
        Assert.Equal(2, d.Session.ExcellentCount);
    }

    [Fact]
    public void Bonus_acumulado_respeita_o_teto()
    {
        var config = new ComboConfig { MaxChainBonus = 0.10 };
        var d = new Driver(config);

        for (var i = 0; i < 5; i++) d.PressAt(0.75);

        Assert.Equal(0.10, d.Session.ChainBonus, Precision);
    }

    [Fact]
    public void Tolerancia_aceita_aperto_logo_depois_da_janela()
    {
        // 0,06 s de tolerância numa barra de 1,2 s = 0,05 da barra.
        var d = new Driver(new ComboConfig { GraceSeconds = 0.06 });

        Assert.False(d.TickAt(0.93));
        var result = d.PressAt(0.93);

        Assert.Equal(ComboGrade.Good, result.Grade);
        Assert.False(result.ComboEnded);
    }

    [Fact]
    public void Nao_inicia_dois_combos_ao_mesmo_tempo()
    {
        var d = new Driver();

        Assert.False(d.Session.TryStart(d.Now + 0.1));
    }

    [Fact]
    public void Aperto_sem_combo_ativo_nao_tem_efeito()
    {
        var session = new ComboSession(ComboConfig.Default);

        var result = session.Press(5.0);

        Assert.False(result.WasActive);
        Assert.Equal(ComboState.Inactive, session.State);
        Assert.Equal(ComboEndReason.None, session.LastEndReason);
    }

    [Fact]
    public void Novo_combo_comeca_do_zero()
    {
        var d = new Driver();
        d.PressAt(0.75);
        d.PressAt(0.75);
        d.Session.Cancel();

        Assert.Equal(ComboEndReason.Cancelled, d.Session.LastEndReason);
        Assert.True(d.Session.TryStart(d.Now + 1));
        Assert.Equal(0, d.Session.HitCount);
        Assert.Equal(0, d.Session.ChainBonus, Precision);
        Assert.Equal(ComboConfig.Default.InitialWindowWidth, d.Session.WindowWidth, Precision);
    }

    [Fact]
    public void Configuracao_invalida_eh_rejeitada()
    {
        Assert.Throws<ArgumentException>(() => new ComboSession(new ComboConfig { InitialWindowWidth = 0 }));
        Assert.Throws<ArgumentException>(() => new ComboSession(new ComboConfig { WindowCenter = 0.95 }));
        Assert.Throws<ArgumentException>(() => new ComboSession(new ComboConfig { ExcellentDamageMultiplier = 1.0 }));
        Assert.Throws<ArgumentNullException>(() => new ComboSession(null!));
    }
}
