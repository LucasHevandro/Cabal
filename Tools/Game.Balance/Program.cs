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
