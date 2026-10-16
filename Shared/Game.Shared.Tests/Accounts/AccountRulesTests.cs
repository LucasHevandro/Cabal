using Game.Shared.Accounts;
using Game.Shared.Net;

namespace Game.Shared.Tests.Accounts;

public class AccountRulesTests
{
    [Theory]
    [InlineData("lucas")]
    [InlineData("  Lucas_99 ")]
    public void Conta_valida(string account) => Assert.Null(AccountRules.ValidateAccount(account));

    [Theory]
    [InlineData("abc")]                 // curta
    [InlineData("conta com espaco")]
    [InlineData("ação")]                // acento
    [InlineData("um_nome_grande_demais_aqui")]
    public void Conta_invalida(string account) => Assert.NotNull(AccountRules.ValidateAccount(account));

    [Fact]
    public void Conta_eh_normalizada_em_minusculas() => Assert.Equal("lucas_99", AccountRules.NormalizeAccount("  Lucas_99 "));

    [Theory]
    [InlineData("senha123", true)]
    [InlineData("curta1", false)]
    [InlineData("somenteletras", false)]
    [InlineData("12345678", false)]
    public void Senha_precisa_de_tamanho_letras_e_numeros(string password, bool valid) =>
        Assert.Equal(valid, AccountRules.ValidatePassword(password) == null);

    [Theory]
    [InlineData("Guerreiro", true)]
    [InlineData("João", true)]
    [InlineData("Ana2", true)]
    [InlineData("2Ana", false)]
    [InlineData("Ana Maria", false)]
    [InlineData("Al", false)]
    [InlineData("Nome-Com-Hifen", false)]
    public void Nome_de_personagem(string name, bool valid) =>
        Assert.Equal(valid, AccountRules.ValidateCharacterName(name) == null);

    [Fact]
    public void Nomes_que_so_mudam_acento_ou_caixa_sao_o_mesmo()
    {
        Assert.Equal(AccountRules.NameKey("Ação"), AccountRules.NameKey("acao"));
        Assert.NotEqual(AccountRules.NameKey("Ana"), AccountRules.NameKey("Ana2"));
    }

    [Fact]
    public void Hello_de_versao_antiga_eh_lido_so_ate_a_versao()
    {
        // Cliente v1 mandava só versão + nome.
        var writer = new PacketWriter();
        writer.WriteInt(1);
        writer.WriteString("Antigo");

        var hello = Assert.IsType<Hello>(NetCodec.Decode(MessageType.Hello, writer.ToArray()));

        Assert.Equal(1, hello.ProtocolVersion);
    }

    [Fact]
    public void Hello_com_token_volta_igual()
    {
        var frame = NetCodec.Encode(new Hello { Name = "", Token = "abc.def", CharacterId = 7 });
        var buffer = new FrameBuffer();
        buffer.Append(frame, 0, frame.Length);
        buffer.TryRead(out var type, out var payload);

        var back = Assert.IsType<Hello>(NetCodec.Decode(type, payload));

        Assert.Equal(("abc.def", 7), (back.Token, back.CharacterId));
    }
}
