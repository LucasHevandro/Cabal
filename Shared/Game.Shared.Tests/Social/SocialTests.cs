using Game.Shared.Net;
using Game.Shared.Social;

namespace Game.Shared.Tests.Social;

public class PartyTests
{
    [Fact]
    public void Grupo_aceita_ate_quatro_e_passa_lideranca_ao_mais_antigo()
    {
        var party = new Party(1, 10);
        Assert.True(party.TryAdd(11));
        Assert.True(party.TryAdd(12));
        Assert.True(party.TryAdd(13));
        Assert.True(party.IsFull);
        Assert.False(party.TryAdd(14));
        Assert.False(party.TryAdd(11));

        Assert.True(party.Remove(10));
        Assert.Equal(11, party.LeaderId);
        Assert.True(party.TryPromote(13));
        Assert.Equal(13, party.LeaderId);
        Assert.False(party.TryPromote(99));

        party.Remove(11);
        party.Remove(12);
        Assert.True(party.ShouldDisband);
    }

    [Fact]
    public void Xp_de_grupo_tem_bonus_e_cada_um_ganha_menos_que_sozinho()
    {
        var solo = PartyRules.SharedXp(1000, 10, 10, 1);
        var four = PartyRules.SharedXp(1000, 10, 10, 4);
        Assert.Equal(1000, solo);
        Assert.Equal(362, four); // 1450 / 4 = 362,5
        Assert.True(four * 4 > solo);
        // Cada membro com o próprio ajuste de nível.
        Assert.True(PartyRules.SharedXp(1000, 10, 15, 2) < PartyRules.SharedXp(1000, 10, 10, 2));
    }

    [Fact]
    public void Alz_e_dividido_e_o_resto_fica_com_quem_matou()
    {
        Assert.Equal(new long[] { 34, 33, 33 }, PartyRules.SplitAlz(100, 3));
        Assert.Equal(new long[] { 7 }, PartyRules.SplitAlz(7, 1));
    }
}

public class ChatTests
{
    [Theory]
    [InlineData("que porra é essa", "que ***** é essa")]
    [InlineData("M3RD4!", "*****!")]
    [InlineData("merrrrda", "********")]
    [InlineData("Caralhudo demais", "********* demais")]
    [InlineData("o cubo do computador", "o cubo do computador")]
    [InlineData("Puta que pariu", "**** que pariu")]
    public void Filtro_troca_palavroes_por_asteriscos(string text, string expected) =>
        Assert.Equal(expected, ProfanityFilter.Clean(text));

    [Fact]
    public void Sanitizar_corta_tamanho_controle_e_espacos()
    {
        Assert.Equal("oi tudo bem", ChatRules.Sanitize("  oi \t tudo\n\nbem  "));
        Assert.Equal("", ChatRules.Sanitize("   "));
        Assert.Equal(ChatRules.MaxLength, ChatRules.Sanitize(new string('a', 500)).Length);
        Assert.Equal("ab", ChatRules.Sanitize("a\u0007b"));
    }

    [Fact]
    public void Limite_de_mensagens_bloqueia_flood_e_volta_com_o_tempo()
    {
        var limiter = new ChatRateLimiter();
        for (var i = 0; i < ChatRules.Burst; i++) Assert.True(limiter.TryConsume(10));
        Assert.False(limiter.TryConsume(10));
        Assert.False(limiter.TryConsume(10 + ChatRules.RefillSeconds * 0.5));
        Assert.True(limiter.TryConsume(10 + ChatRules.RefillSeconds * 1.1));
    }

    [Fact]
    public void Parser_entende_canais_e_comandos()
    {
        var plain = ChatParser.Parse("olá pessoal", ChatChannel.Party);
        Assert.Equal(ChatInputKind.Message, plain.Kind);
        Assert.Equal(ChatChannel.Party, plain.Channel);

        var map = ChatParser.Parse("/m vendo espada", ChatChannel.Party);
        Assert.Equal((ChatChannel.Map, "vendo espada"), (map.Channel, map.Text));

        var whisper = ChatParser.Parse("/s Bia   oi, tudo bem?", ChatChannel.Map);
        Assert.Equal((ChatChannel.Whisper, "Bia", "oi, tudo bem?"), (whisper.Channel, whisper.Target, whisper.Text));

        var reply = ChatParser.Parse("/r valeu", ChatChannel.Map, "Caio");
        Assert.Equal(("Caio", "valeu"), (reply.Target, reply.Text));
        Assert.Equal(ChatInputKind.Invalid, ChatParser.Parse("/r valeu", ChatChannel.Map).Kind);

        Assert.Equal((ChatInputKind.PartyInvite, "Bia"), Pair(ChatParser.Parse("/convidar Bia", ChatChannel.Map)));
        Assert.Equal(ChatInputKind.PartyLeave, ChatParser.Parse("/sair", ChatChannel.Map).Kind);
        Assert.Equal((ChatInputKind.PartyKick, "Bia"), Pair(ChatParser.Parse("/expulsar Bia", ChatChannel.Map)));
        Assert.Equal((ChatInputKind.PartyPromote, "Bia"), Pair(ChatParser.Parse("/LIDER Bia", ChatChannel.Map)));
        Assert.Equal(ChatInputKind.Invalid, ChatParser.Parse("/convidar", ChatChannel.Map).Kind);
        Assert.Equal(ChatInputKind.Invalid, ChatParser.Parse("/dançar", ChatChannel.Map).Kind);
        Assert.Equal(ChatInputKind.None, ChatParser.Parse("/g   ", ChatChannel.Map).Kind);
        Assert.Equal(ChatInputKind.Help, ChatParser.Parse("/ajuda", ChatChannel.Map).Kind);

        Assert.Equal((ChatInputKind.FriendAdd, "Bia"), Pair(ChatParser.Parse("/amigo Bia", ChatChannel.Map)));
        Assert.Equal((ChatInputKind.FriendRemove, "Bia"), Pair(ChatParser.Parse("/remover Bia", ChatChannel.Map)));
        Assert.Equal((ChatInputKind.Block, "Bia"), Pair(ChatParser.Parse("/bloquear Bia", ChatChannel.Map)));
        Assert.Equal((ChatInputKind.Unblock, "Bia"), Pair(ChatParser.Parse("/desbloquear Bia", ChatChannel.Map)));
        var report = ChatParser.Parse("/denunciar Caio xingando no mapa", ChatChannel.Map);
        Assert.Equal((ChatInputKind.Report, "Caio", "xingando no mapa"), (report.Kind, report.Target, report.Text));
        Assert.Equal(ChatInputKind.Invalid, ChatParser.Parse("/denunciar Caio", ChatChannel.Map).Kind);
        Assert.Equal(ChatInputKind.FriendsWindow, ChatParser.Parse("/amigos", ChatChannel.Map).Kind);
    }

    private static (ChatInputKind, string) Pair(ChatInput input) => (input.Kind, input.Target);

    [Fact]
    public void Mensagens_sociais_ida_e_volta()
    {
        static T RoundTrip<T>(T message) where T : INetMessage
        {
            var frame = NetCodec.Encode(message);
            var buffer = new FrameBuffer();
            buffer.Append(frame, 0, frame.Length);
            Assert.True(buffer.TryRead(out var type, out var payload));
            return Assert.IsType<T>(NetCodec.Decode(type, payload));
        }

        var command = RoundTrip(new PartyCommand { Action = PartyActionKind.Kick, TargetId = 4, TargetName = "Bia" });
        Assert.Equal((PartyActionKind.Kick, 4, "Bia"), (command.Action, command.TargetId, command.TargetName));

        var send = RoundTrip(new ChatSend { Channel = ChatChannel.Whisper, Target = "Bia", Text = "oi ⚔" });
        Assert.Equal((ChatChannel.Whisper, "Bia", "oi ⚔"), (send.Channel, send.Target, send.Text));

        var state = RoundTrip(new PartyState
        {
            LeaderId = 2,
            Members =
            {
                new PartyMemberInfo { Id = 2, Name = "Ana", Level = 7, Class = 1, Hp = 50, MaxHp = 90, Mp = 3, MaxMp = 40, Near = true },
                new PartyMemberInfo { Id = 5, Name = "Bia", Level = 9, Hp = 0, MaxHp = 120, Dead = true },
            },
        });
        Assert.Equal(2, state.LeaderId);
        Assert.Equal(2, state.Members.Count);
        Assert.Equal(("Ana", 7, (byte)1, 50, 90, true), (state.Members[0].Name, state.Members[0].Level, state.Members[0].Class,
            state.Members[0].Hp, state.Members[0].MaxHp, state.Members[0].Near));
        Assert.True(state.Members[1].Dead);

        var invite = RoundTrip(new PartyInvite { InviterId = 3, InviterName = "Caio", Seconds = 30 });
        Assert.Equal((3, "Caio", 30f), (invite.InviterId, invite.InviterName, invite.Seconds));

        var social = RoundTrip(new SocialCommand { Action = RelationActionKind.Report, TargetName = "Caio", Text = "spam" });
        Assert.Equal((RelationActionKind.Report, "Caio", "spam"), (social.Action, social.TargetName, social.Text));

        var lists = RoundTrip(new SocialLists
        {
            Friends = { new FriendInfo { Name = "Bia", Online = true, Level = 12, Class = 1 }, new FriendInfo { Name = "Davi" } },
            Blocked = { "Caio" },
        });
        Assert.Equal(("Bia", true, 12, (byte)1), (lists.Friends[0].Name, lists.Friends[0].Online, lists.Friends[0].Level, lists.Friends[0].Class));
        Assert.False(lists.Friends[1].Online);
        Assert.Equal(new[] { "Caio" }, lists.Blocked);

        var chat = RoundTrip(new ChatMessage { Channel = ChatChannel.Party, FromId = 3, FromName = "Caio", ToName = "x", Text = "bora" });
        Assert.Equal((ChatChannel.Party, 3, "Caio", "x", "bora"), (chat.Channel, chat.FromId, chat.FromName, chat.ToName, chat.Text));
    }
}
