using System.Text.RegularExpressions;
using Game.Shared.Accounts;
using Game.Shared.Combat;
using Game.Shared.Dungeon;
using Game.Shared.Items;
using Game.Shared.Net;
using Game.Shared.Progression;
using Game.Shared.Text;
using Game.Shared.World;

namespace Game.Shared.Tests.Text;

public class LocTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    private static string LocalePath => Path.Combine(RepoRoot, "Shared", "Game.Shared", "Resources", "Locales", "pt-BR.txt");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Game.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada");
    }

    [Fact]
    public void Arquivo_de_idioma_nao_tem_linhas_invalidas_nem_chaves_repetidas()
    {
        var texts = Loc.Parse(File.ReadAllText(LocalePath), out var problems);
        Assert.Empty(problems);
        Assert.True(texts.Count > 300, $"só {texts.Count} textos");
    }

    [Fact]
    public void Todo_texto_aceita_os_argumentos_que_pede()
    {
        var texts = Loc.Parse(File.ReadAllText(LocalePath), out _);
        var placeholder = new Regex(@"(?<!\{)\{(\d+)(?::[^}]*)?\}");
        foreach (var (key, template) in texts)
        {
            var count = placeholder.Matches(template).Select(m => int.Parse(m.Groups[1].Value) + 1).DefaultIfEmpty(0).Max();
            if (count == 0) continue;
            var args = Enumerable.Repeat<object?>(1, count).ToArray();
            var exception = Record.Exception(() => string.Format(template, args));
            Assert.True(exception == null, $"{key}: {exception?.Message}");
        }
    }

    [Fact]
    public void Todo_conteudo_dos_catalogos_tem_nome_traduzido()
    {
        var keys = new List<string>();
        keys.AddRange(ItemCatalog.All.Select(i => "item." + i.Id));
        keys.AddRange(WorldContent.Spawns().Select(s => "monster." + s.Data.Id));
        keys.AddRange(WorldContent.Zones().Select(z => "zone." + z.Id));
        var crypt = DungeonCatalog.Crypt();
        keys.Add("dungeon." + crypt.Id);
        keys.AddRange(crypt.Rooms.Select(r => "room." + r.Id));
        keys.AddRange(crypt.Rooms.SelectMany(r => r.Spawns).Select(s => "monster." + s.Data.Id));
        keys.AddRange(NpcCatalog.All.Select(n => "npc." + n.Id));
        keys.AddRange(QuestCatalog.All.SelectMany(q => new[] { "quest." + q.Id, "quest." + q.Id + ".desc" }));
        foreach (var characterClass in new[] { CharacterClass.Blade, CharacterClass.Arcanist })
        {
            keys.AddRange(ClassCatalog.SkillsFor(characterClass).Select(s => "skill." + s.Skill.Id));
            keys.AddRange(ClassCatalog.ComboConfigFor(characterClass).Routes.Select(r => "route." + r.Id));
        }
        keys.AddRange(PlayerRules.StarterSkills().Select(s => "skill." + s.Id));

        var missing = keys.Distinct().Where(k => !Loc.Has(k)).ToList();
        Assert.True(missing.Count == 0, "Faltam no pt-BR.txt: " + string.Join(", ", missing));
    }

    [Fact]
    public void Toda_chave_escrita_no_codigo_existe_no_arquivo()
    {
        // Chamadas com a chave literal: Loc.T("x"), Loc.Optional("x"), Loc.Ref("x"), new LocalText("x"), Tr("x").
        var call = new Regex(@"(?:Loc\.(?:T|Optional|Ref)|new LocalText|\bTr|\bLT)\(\s*""([a-z][a-z0-9_.]*)""");
        var folders = new[] { "Shared/Game.Shared", "Server/Game.Server", "Client/Assets/Scripts", "Tools" };
        var missing = new SortedSet<string>();
        var used = 0;
        foreach (var folder in folders)
        {
            var path = Path.Combine(RepoRoot, folder);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)) continue;
                foreach (Match match in call.Matches(File.ReadAllText(file)))
                {
                    used++;
                    var key = match.Groups[1].Value;
                    // Prefixos que o código completa (ex.: "item." + id) são conferidos pelo teste dos catálogos.
                    if (key.EndsWith(".")) continue;
                    if (!Loc.Has(key)) missing.Add($"{key} ({Path.GetFileName(file)})");
                }
            }
        }
        Assert.True(used > 100, $"só {used} usos achados: o padrão de busca quebrou?");
        Assert.True(missing.Count == 0, "Chaves usadas que faltam no pt-BR.txt:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void Argumentos_viram_texto_e_voltam_no_formato_do_idioma()
    {
        var text = new LocalText("upgrade.no_alz", 12500L);
        Assert.Equal(new[] { "12500" }, text.Args);
        Assert.Equal("Faltam Alz: precisa de 12.500.", text.ToString());

        // Referência a outra chave é traduzida no destino.
        var cores = new LocalText("upgrade.no_cores", 2, Loc.Ref("item.nucleo_medio"));
        Assert.Equal("Faltam núcleos: precisa de 2x Núcleo de melhoria (médio).", cores.ToString());

        // Texto do jogador passa como está, e chave que falta aparece como a própria chave.
        Assert.Equal("Só para Bia.", new LocalText("equip.class", "Bia").ToString());
        Assert.Equal("nao.existe", Loc.T("nao.existe"));
        Assert.Equal("", Loc.Optional("nao.existe"));
    }

    [Fact]
    public void Texto_traduzido_vai_e_volta_pela_rede()
    {
        var frame = NetCodec.Encode(new Notice { Text = new LocalText("quest.previous", Loc.Ref("quest.pele_e_presa")) });
        var buffer = new FrameBuffer();
        buffer.Append(frame, 0, frame.Length);
        Assert.True(buffer.TryRead(out var type, out var payload));
        var notice = Assert.IsType<Notice>(NetCodec.Decode(type, payload));
        Assert.Equal("Conclua antes: Pele e presa.", notice.Text.ToString());
    }
}

/// <summary>Troca o idioma global: roda sozinho, sem outros testes em paralelo.</summary>
[CollectionDefinition("Idioma", DisableParallelization = true)]
public class IdiomaCollection
{
}

[Collection("Idioma")]
public class LocLanguageTests
{
    [Fact]
    public void Outro_idioma_substitui_os_textos()
    {
        var original = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Locales", "pt-BR.txt"));
        try
        {
            Loc.Load("en-US", "item.pocao_hp_p = Health potion (S)\nupgrade.no_alz = Not enough Alz: {0:N0} needed.");
            Assert.Equal("Health potion (S)", ItemCatalog.Get("pocao_hp_p").Name);
            Assert.Equal("Not enough Alz: 12,500 needed.", new LocalText("upgrade.no_alz", 12500).ToString());
            Assert.Equal("item.espada_ferro", ItemCatalog.Get("espada_ferro").Name);
        }
        finally
        {
            Loc.Load(Loc.DefaultLanguage, original);
        }
    }
}
