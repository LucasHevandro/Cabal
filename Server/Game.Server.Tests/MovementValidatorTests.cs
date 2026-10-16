using Game.Server.World;

namespace Game.Server.Tests;

public class MovementValidatorTests
{
    [Fact]
    public void Correr_em_velocidade_normal_eh_aceito_por_muito_tempo()
    {
        var validator = new MovementValidator(0, 0, 0);
        var x = 0f;

        // 10 s correndo a 7,5 m/s, posição enviada 20 vezes por segundo.
        for (var i = 1; i <= 200; i++)
        {
            x += 7.5f * 0.05f;
            Assert.True(validator.TryMove(x, 0, i * 0.05), $"recusou no passo {i}");
        }
    }

    [Fact]
    public void Esquiva_curta_cabe_na_reserva()
    {
        var validator = new MovementValidator(0, 0, 0);

        Assert.True(validator.TryMove(4.5f, 0, 0.22));
    }

    [Fact]
    public void Velocidade_dobrada_eh_recusada_logo()
    {
        var validator = new MovementValidator(0, 0, 0);
        var x = 0f;
        var refused = false;

        for (var i = 1; i <= 40 && !refused; i++)
        {
            x += 20f * 0.05f;
            refused = !validator.TryMove(x, 0, i * 0.05);
        }

        Assert.True(refused);
    }

    [Fact]
    public void Posicao_invalida_mantem_a_ultima_valida()
    {
        var validator = new MovementValidator(5, 5, 0);

        Assert.False(validator.TryMove(500, 5, 0.1));
        Assert.False(validator.TryMove(float.NaN, 5, 0.2));
        Assert.Equal((5f, 5f), (validator.X, validator.Z));
    }

    [Fact]
    public void Teleporte_do_servidor_nao_e_conferido()
    {
        var validator = new MovementValidator(0, 0, 0);

        validator.Teleport(100, 100, 1);

        Assert.Equal((100f, 100f), (validator.X, validator.Z));
        Assert.True(validator.TryMove(101, 100, 1.2));
    }
}
