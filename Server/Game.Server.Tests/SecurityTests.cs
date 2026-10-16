using Game.Server.Accounts;

namespace Game.Server.Tests;

public class SecurityTests
{
    private static readonly PasswordHasher Fast = new(iterations: 1000);

    [Fact]
    public void Senha_certa_confere_e_errada_nao()
    {
        var stored = Fast.Hash("senha123");

        Assert.True(Fast.Verify("senha123", stored));
        Assert.False(Fast.Verify("senha124", stored));
        Assert.False(Fast.Verify("", stored));
    }

    [Fact]
    public void Mesmo_senha_gera_hashes_diferentes_e_nao_guarda_a_senha()
    {
        var a = Fast.Hash("senha123");
        var b = Fast.Hash("senha123");

        Assert.NotEqual(a, b); // sal aleatório
        Assert.DoesNotContain("senha123", a);
        Assert.StartsWith("pbkdf2-sha256$1000$", a);
    }

    [Theory]
    [InlineData("")]
    [InlineData("texto qualquer")]
    [InlineData("pbkdf2-sha256$abc$x$y")]
    [InlineData("pbkdf2-sha256$1000$não-é-base64$x")]
    public void Hash_estragado_nunca_confere(string stored) => Assert.False(Fast.Verify("senha123", stored));

    [Fact]
    public void Hash_com_menos_iteracoes_pede_refazer()
    {
        var old = Fast.Hash("senha123");
        var strong = new PasswordHasher(iterations: 2000);

        Assert.True(strong.NeedsRehash(old));
        Assert.True(strong.Verify("senha123", old)); // continua aceitando a senha antiga
        Assert.False(strong.NeedsRehash(strong.Hash("senha123")));
    }

    [Fact]
    public void Token_identifica_a_conta_ate_expirar()
    {
        var now = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
        var tokens = new SessionTokens(TimeSpan.FromHours(1), () => now);

        var token = tokens.Create(accountId: 42);

        Assert.Equal(42, tokens.Validate(token));
        now = now.AddMinutes(59);
        Assert.Equal(42, tokens.Validate(token));
        now = now.AddMinutes(2);
        Assert.Null(tokens.Validate(token));
    }

    [Fact]
    public void Token_desconhecido_ou_revogado_nao_vale()
    {
        var tokens = new SessionTokens();
        var token = tokens.Create(1);

        Assert.Null(tokens.Validate("inventado"));
        Assert.Null(tokens.Validate(null));
        tokens.Revoke(token);
        Assert.Null(tokens.Validate(token));
    }

    [Fact]
    public void Tokens_sao_unicos_e_seguros_para_url()
    {
        var tokens = new SessionTokens();
        var all = Enumerable.Range(0, 200).Select(_ => tokens.Create(1)).ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, t => Assert.Matches("^[A-Za-z0-9_-]{43}$", t));
    }
}
