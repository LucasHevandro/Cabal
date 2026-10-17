using System.Globalization;
using Game.Shared.Accounts;
using Game.Shared.Progression;

// Simula a subida do nível 1 ao 30 para as duas classes e imprime uma tabela (meta do GDD: 8 a 12 horas).
// Uso: dotnet run --project Tools/Game.Balance [-- --markdown]
Console.OutputEncoding = System.Text.Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
var markdown = args.Contains("--markdown");

foreach (var characterClass in new[] { CharacterClass.Blade, CharacterClass.Arcanist })
{
    var steps = LevelingSimulation.Run(characterClass);
    var total = LevelingSimulation.TotalHours(steps);
    Console.WriteLine(markdown ? $"### {AccountRules.ClassDisplayName(characterClass)}: {total:0.0} horas\n" : $"== {AccountRules.ClassDisplayName(characterClass)}: {total:0.0} horas ==");
    if (markdown)
    {
        Console.WriteLine("| Nível | Caça | s/abate | XP/abate | Abates | XP de missão | Minutos | Acumulado (h) |");
        Console.WriteLine("|---:|---|---:|---:|---:|---:|---:|---:|");
    }
    var accumulated = 0.0;
    foreach (var s in steps)
    {
        accumulated += s.Minutes / 60;
        Console.WriteLine(markdown
            ? $"| {s.Level} | {s.MonsterId} (Nv {s.MonsterLevel}) | {s.SecondsPerKill:0.0} | {s.XpPerKill} | {s.Kills} | {s.QuestXp} | {s.Minutes:0} | {accumulated:0.0} |"
            : $"Nv {s.Level,2}  {s.MonsterId,-16} Nv {s.MonsterLevel,2}  {s.SecondsPerKill,5:0.0} s/abate  {s.XpPerKill,5} XP  {s.Kills,4} abates  missão {s.QuestXp,6}  {s.Minutes,5:0} min  total {accumulated,5:0.0} h");
    }
    Console.WriteLine();
}

// Sensibilidade: o mesmo personagem jogado por alguém rápido e por alguém sem pressa.
var fast = new LevelingAssumptions { SecondsBetweenKills = 4, Downtime = 0.08, ComboBonus = 0.3 };
var slow = new LevelingAssumptions { SecondsBetweenKills = 10, Downtime = 0.3, ComboBonus = 0.05 };
foreach (var characterClass in new[] { CharacterClass.Blade, CharacterClass.Arcanist })
{
    Console.WriteLine($"{(markdown ? "- " : "")}{AccountRules.ClassDisplayName(characterClass)}: jogador rápido " +
        $"{LevelingSimulation.TotalHours(LevelingSimulation.Run(characterClass, fast)):0.0} h, sem pressa " +
        $"{LevelingSimulation.TotalHours(LevelingSimulation.Run(characterClass, slow)):0.0} h");
}

// Economia: Alz que entra (monstros e venda de drops) e que sai (melhoria, núcleos, poções, equipamento da loja).
Console.WriteLine();
Console.WriteLine(markdown ? "## Economia (1→30)\n" : "== Economia (1→30) ==");
foreach (var characterClass in new[] { CharacterClass.Blade, CharacterClass.Arcanist })
{
    var bands = Game.Shared.Economy.EconomySimulation.Run(characterClass);
    Console.WriteLine(markdown ? $"### {AccountRules.ClassDisplayName(characterClass)}\n" : $"-- {AccountRules.ClassDisplayName(characterClass)} --");
    if (markdown)
    {
        Console.WriteLine("| Faixa | Horas | Alz dos monstros | Venda de drops | Melhoria | Núcleos | Poções | Loja (equip.) | Saldo |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
    }
    long saved = 0;
    foreach (var b in bands)
    {
        saved += b.Balance;
        Console.WriteLine(markdown
            ? $"| {b.Name} | {b.Hours:0.0} | {b.AlzFromMonsters:N0} | {b.AlzFromSales:N0} | {b.SpentOnUpgrades:N0} | {b.SpentOnCores:N0} | {b.SpentOnPotions:N0} | {b.SpentOnGear:N0} | {b.Balance:N0} |"
            : $"{b.Name,-24} {b.Hours,4:0.0} h  entra {b.Income,9:N0} (monstros {b.AlzFromMonsters:N0}, vendas {b.AlzFromSales:N0})  " +
              $"sai {b.Spent,9:N0} (melhoria {b.SpentOnUpgrades:N0}, núcleos {b.SpentOnCores:N0}, poções {b.SpentOnPotions:N0}, loja {b.SpentOnGear:N0})  saldo {b.Balance,9:N0}");
    }
    var income = bands.Sum(b => b.Income);
    var spent = bands.Sum(b => b.Spent);
    Console.WriteLine($"{(markdown ? "\n" : "")}Total: entra {income:N0}, sai {spent:N0} ({spent * 100.0 / Math.Max(1, income):0}% gasto), sobra {saved:N0} Alz no nível 30");
    Console.WriteLine();
}
