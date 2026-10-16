using Game.Shared.World;

namespace Game.Shared.Tests.World;

public class WorldContentTests
{
    [Fact]
    public void Todos_os_monstros_e_skills_tem_dados_validos()
    {
        foreach (var spawn in WorldContent.Spawns()) spawn.Data.Validate();
        foreach (var skill in PlayerRules.StarterSkills()) skill.Validate();
    }

    [Fact]
    public void Ids_de_monstro_nao_se_repetem()
    {
        var ids = WorldContent.Spawns().Select(s => s.Data.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void Existe_exatamente_um_chefe()
    {
        Assert.Single(WorldContent.Spawns(), s => s.IsBoss);
    }

    [Fact]
    public void Cada_chamada_cria_dados_novos()
    {
        var a = WorldContent.Spawns()[0].Data;
        var b = WorldContent.Spawns()[0].Data;

        a.AttackCooldownSeconds = 99;

        Assert.NotEqual(99, b.AttackCooldownSeconds);
    }

    [Fact]
    public void Pontos_de_nascimento_sao_repetiveis_e_ficam_no_raio()
    {
        foreach (var spawn in WorldContent.Spawns())
        {
            var first = spawn.SpawnPoints(seed: 7);
            var second = spawn.SpawnPoints(seed: 7);

            Assert.Equal(first, second);
            Assert.Equal(spawn.Count, first.Count);
            foreach (var (x, z) in first)
            {
                var distance = MathF.Sqrt((x - spawn.CenterX) * (x - spawn.CenterX) + (z - spawn.CenterZ) * (z - spawn.CenterZ));
                Assert.True(distance <= spawn.Radius + 0.001f, $"{spawn.Data.Id} nasceu fora do raio");
            }
        }
    }

    [Fact]
    public void Cada_grupo_de_monstros_fica_dentro_de_uma_zona_do_nivel_certo()
    {
        var zones = WorldContent.Zones();
        foreach (var spawn in WorldContent.Spawns())
        {
            var zone = zones.FirstOrDefault(z =>
                MathF.Sqrt((spawn.CenterX - z.CenterX) * (spawn.CenterX - z.CenterX) + (spawn.CenterZ - z.CenterZ) * (spawn.CenterZ - z.CenterZ)) <= z.Radius
                && z.Name != "Vila de Partida");
            Assert.NotNull(zone);
            Assert.InRange(spawn.Data.Level, zone!.MinLevel, zone.MaxLevel);
        }
    }
}
