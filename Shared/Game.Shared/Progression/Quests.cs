using System;
using System.Collections.Generic;
using System.Linq;
using Game.Shared.Items;

namespace Game.Shared.Progression
{
    public enum NpcRole
    {
        QuestGiver = 0,
        Smith = 1,
        Warehouse = 2,
    }

    /// <summary>NPC da vila: posição fixa e o que ele faz.</summary>
    public sealed class NpcDefinition
    {
        public string Id = "";
        public string Name = "";
        public NpcRole Role;
        public float X;
        public float Z;
    }

    /// <summary>Os NPCs da Vila de Partida (em volta do ponto de nascimento).</summary>
    public static class NpcCatalog
    {
        /// <summary>Distância máxima para falar com um NPC.</summary>
        public const float InteractRange = 4f;

        public const string Elder = "anciao_doran";
        public const string Smith = "ferreira_ilsa";
        public const string Warehouse = "guarda_volumes_bento";

        public static readonly IReadOnlyList<NpcDefinition> All = new List<NpcDefinition>
        {
            new NpcDefinition { Id = Elder, Name = "Ancião Doran", Role = NpcRole.QuestGiver, X = 0, Z = 8 },
            new NpcDefinition { Id = Smith, Name = "Ferreira Ilsa", Role = NpcRole.Smith, X = 7, Z = 3 },
            new NpcDefinition { Id = Warehouse, Name = "Bento do Armazém", Role = NpcRole.Warehouse, X = -7, Z = 3 },
        };

        public static NpcDefinition? Find(string id) => All.FirstOrDefault(n => n.Id == id);

        public static bool InRange(NpcDefinition npc, float x, float z)
        {
            var dx = npc.X - x;
            var dz = npc.Z - z;
            return dx * dx + dz * dz <= InteractRange * InteractRange;
        }
    }

    public enum QuestObjectiveKind
    {
        /// <summary>Derrotar N monstros de um tipo.</summary>
        Kill = 0,

        /// <summary>Ter N itens na bolsa ao entregar (são consumidos).</summary>
        Collect = 1,

        /// <summary>Falar com um NPC.</summary>
        Talk = 2,
    }

    public sealed class QuestObjective
    {
        public QuestObjectiveKind Kind { get; }

        /// <summary>Id do monstro, do item ou do NPC.</summary>
        public string TargetId { get; }

        public int Count { get; }

        public QuestObjective(QuestObjectiveKind kind, string targetId, int count = 1)
        {
            Kind = kind;
            TargetId = targetId;
            Count = Math.Max(1, count);
        }
    }

    public sealed class QuestDefinition
    {
        public string Id = "";
        public string Name = "";
        public string Description = "";
        public int MinLevel = 1;

        /// <summary>Missão que precisa estar concluída antes (vazio = nenhuma).</summary>
        public string PreviousQuestId = "";

        public string GiverNpcId = NpcCatalog.Elder;
        public List<QuestObjective> Objectives = new List<QuestObjective>();
        public long RewardXp;
        public long RewardAlz;
        public List<(string ItemId, int Quantity)> RewardItems = new List<(string, int)>();

        /// <summary>Texto curto do que a missão libera, para mostrar na janela (vazio = nada).</summary>
        public string Unlocks = "";
    }

    /// <summary>
    /// Missões do MVP, do nível 1 ao 30 (GDD: matar N, coletar N, falar com NPC). Encadeadas: cada uma pede a anterior.
    /// Três delas são as missões de rank — liberam o combo e as skills de nível médio e alto.
    /// </summary>
    public static class QuestCatalog
    {
        public const int MaxActive = 5;

        public static readonly IReadOnlyList<QuestDefinition> All = new List<QuestDefinition>
        {
            new QuestDefinition
            {
                Id = "primeiros_passos", Name = "Primeiros passos", MinLevel = 1,
                Description = "Apresente-se à Ferreira Ilsa: ela cuida das armas e da melhoria dos equipamentos.",
                Objectives = { new QuestObjective(QuestObjectiveKind.Talk, NpcCatalog.Smith) },
                RewardXp = 300, RewardAlz = 500, RewardItems = { ("pocao_hp_p", 5) },
            },
            new QuestDefinition
            {
                Id = ClassCatalog.ComboQuestId, Name = "A arte do combo", MinLevel = 3, PreviousQuestId = "primeiros_passos",
                Description = "Derrote coelhos nos Campos do Vento para treinar o ritmo dos golpes.",
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "coelho_saltador", 10) },
                RewardXp = 1200, RewardAlz = 1500, RewardItems = { (ItemCatalog.CoreLow, 2) },
                Unlocks = "Libera o combo.",
            },
            new QuestDefinition
            {
                Id = "pele_e_presa", Name = "Pele e presa", MinLevel = 6, PreviousQuestId = ClassCatalog.ComboQuestId,
                Description = "Os lobos cinzentos rondam a vila. Traga peles para reforçar as armaduras.",
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "lobo_cinzento", 12),
                    new QuestObjective(QuestObjectiveKind.Collect, "pele_lobo", 6),
                },
                RewardXp = 4000, RewardAlz = 4000, RewardItems = { (ItemCatalog.CoreLow, 3), ("pedra_vigor", 1) },
            },
            new QuestDefinition
            {
                Id = ClassCatalog.RankTwoQuestId, Name = "Ameaça na mata", MinLevel = 10, PreviousQuestId = "pele_e_presa",
                Description = "Javalis e aranhas tomaram a Mata Sombria. Abra caminho.",
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "javali_furioso", 15),
                    new QuestObjective(QuestObjectiveKind.Kill, "aranha_da_mata", 15),
                },
                RewardXp = 10000, RewardAlz = 12000, RewardItems = { (ItemCatalog.CoreMedium, 2) },
                Unlocks = "Libera as skills de rank 2.",
            },
            new QuestDefinition
            {
                Id = "veneno_raro", Name = "Veneno raro", MinLevel = 15, PreviousQuestId = ClassCatalog.RankTwoQuestId,
                Description = "A Ferreira Ilsa precisa de glândulas de veneno para temperar o aço.",
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Collect, "veneno", 8),
                    new QuestObjective(QuestObjectiveKind.Talk, NpcCatalog.Smith),
                },
                RewardXp = 18000, RewardAlz = 20000, RewardItems = { (ItemCatalog.CoreMedium, 3), ("pedra_furia", 1) },
            },
            new QuestDefinition
            {
                Id = ClassCatalog.RankThreeQuestId, Name = "As ruínas", MinLevel = 20, PreviousQuestId = "veneno_raro",
                Description = "Golens de pedra guardam a entrada das Ruínas Esquecidas.",
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "golem_de_pedra", 20),
                    new QuestObjective(QuestObjectiveKind.Collect, "fragmento_pedra", 10),
                },
                RewardXp = 28000, RewardAlz = 45000, RewardItems = { (ItemCatalog.CoreHigh, 2) },
                Unlocks = "Libera as skills de rank 3.",
            },
            new QuestDefinition
            {
                Id = "ecos_espectrais", Name = "Ecos espectrais", MinLevel = 25, PreviousQuestId = ClassCatalog.RankThreeQuestId,
                Description = "Espectros vagam pelas ruínas. Reúna a essência deles.",
                Objectives =
                {
                    new QuestObjective(QuestObjectiveKind.Kill, "espectro", 25),
                    new QuestObjective(QuestObjectiveKind.Collect, "essencia", 10),
                },
                RewardXp = 40000, RewardAlz = 80000, RewardItems = { (ItemCatalog.CoreHigh, 3), ("pedra_precisao", 1) },
            },
            new QuestDefinition
            {
                Id = "o_guardiao", Name = "O Guardião", MinLevel = 30, PreviousQuestId = "ecos_espectrais",
                Description = "Derrote o Guardião das Ruínas na arena ao norte.",
                Objectives = { new QuestObjective(QuestObjectiveKind.Kill, "guardiao_das_ruinas") },
                RewardXp = 0, RewardAlz = 200000, RewardItems = { (ItemCatalog.CoreHigh, 5) },
            },
        };

        public static QuestDefinition? Find(string id) => All.FirstOrDefault(q => q.Id == id);

        public static QuestDefinition Get(string id) =>
            Find(id) ?? throw new ArgumentException($"Missão desconhecida: {id}", nameof(id));
    }

    /// <summary>Andamento de uma missão aceita: um contador por objetivo.</summary>
    public sealed class QuestProgress
    {
        public string QuestId { get; }
        public int[] Counts { get; }

        public QuestProgress(string questId, int[]? counts = null)
        {
            QuestId = questId;
            var objectives = QuestCatalog.Find(questId)?.Objectives.Count ?? 0;
            Counts = new int[objectives];
            if (counts != null) Array.Copy(counts, Counts, Math.Min(counts.Length, Counts.Length));
        }
    }

    /// <summary>Resultado da entrega de uma missão.</summary>
    public readonly struct QuestReward
    {
        public long Xp { get; }
        public long Alz { get; }

        public QuestReward(long xp, long alz)
        {
            Xp = xp;
            Alz = alz;
        }
    }

    /// <summary>
    /// Diário de missões de um personagem. Matar e falar contam aqui; coletar é conferido na bolsa na hora de entregar
    /// (assim o jogador pode vender ou guardar no meio do caminho sem dessincronizar o contador).
    /// </summary>
    public sealed class QuestLog
    {
        private readonly Dictionary<string, QuestProgress> _active = new Dictionary<string, QuestProgress>();
        private readonly HashSet<string> _completed = new HashSet<string>();

        public IEnumerable<QuestProgress> Active => _active.Values;
        public IEnumerable<string> Completed => _completed;

        public bool IsCompleted(string questId) => _completed.Contains(questId);
        public bool IsActive(string questId) => _active.ContainsKey(questId);
        public QuestProgress? ProgressOf(string questId) => _active.TryGetValue(questId, out var p) ? p : null;

        /// <summary>Nulo se pode aceitar; senão o motivo.</summary>
        public string? CanAccept(QuestDefinition quest, int level)
        {
            if (_completed.Contains(quest.Id)) return "Missão já concluída.";
            if (_active.ContainsKey(quest.Id)) return "Missão já aceita.";
            if (level < quest.MinLevel) return $"Precisa do nível {quest.MinLevel}.";
            if (!string.IsNullOrEmpty(quest.PreviousQuestId) && !_completed.Contains(quest.PreviousQuestId))
                return $"Conclua antes: {QuestCatalog.Find(quest.PreviousQuestId)?.Name}.";
            if (_active.Count >= QuestCatalog.MaxActive) return $"No máximo {QuestCatalog.MaxActive} missões ao mesmo tempo.";
            return null;
        }

        public string? Accept(string questId, int level)
        {
            var quest = QuestCatalog.Find(questId);
            if (quest == null) return "Missão desconhecida.";
            var reason = CanAccept(quest, level);
            if (reason != null) return reason;
            _active[questId] = new QuestProgress(questId);
            return null;
        }

        public bool Abandon(string questId) => _active.Remove(questId);

        /// <summary>Missões que o NPC pode oferecer agora.</summary>
        public IEnumerable<QuestDefinition> AvailableFrom(string npcId, int level) =>
            QuestCatalog.All.Where(q => q.GiverNpcId == npcId && CanAccept(q, level) == null);

        /// <summary>Conta um monstro derrotado. Retorna as missões que mudaram.</summary>
        public List<string> OnKill(string monsterId) => Advance(QuestObjectiveKind.Kill, monsterId);

        /// <summary>Conta uma conversa com o NPC. Retorna as missões que mudaram.</summary>
        public List<string> OnTalk(string npcId) => Advance(QuestObjectiveKind.Talk, npcId);

        private List<string> Advance(QuestObjectiveKind kind, string targetId)
        {
            var changed = new List<string>();
            foreach (var progress in _active.Values)
            {
                var objectives = QuestCatalog.Get(progress.QuestId).Objectives;
                for (var i = 0; i < objectives.Count; i++)
                {
                    var objective = objectives[i];
                    if (objective.Kind != kind || objective.TargetId != targetId || progress.Counts[i] >= objective.Count) continue;
                    progress.Counts[i]++;
                    if (!changed.Contains(progress.QuestId)) changed.Add(progress.QuestId);
                }
            }
            return changed;
        }

        /// <summary>Quanto já foi feito do objetivo (coletar olha a bolsa).</summary>
        public static int Done(QuestProgress progress, int index, ItemContainer bag)
        {
            var objective = QuestCatalog.Get(progress.QuestId).Objectives[index];
            var done = objective.Kind == QuestObjectiveKind.Collect ? bag.Count(objective.TargetId) : progress.Counts[index];
            return Math.Min(done, objective.Count);
        }

        public static bool IsReady(QuestProgress progress, ItemContainer bag)
        {
            var objectives = QuestCatalog.Get(progress.QuestId).Objectives;
            for (var i = 0; i < objectives.Count; i++)
                if (Done(progress, i, bag) < objectives[i].Count) return false;
            return true;
        }

        /// <summary>
        /// Entrega a missão no NPC que a deu: consome os itens de coleta e põe os itens da recompensa na bolsa. Tudo ou
        /// nada — se faltar espaço, nada muda. XP e Alz ficam para quem chama aplicar.
        /// </summary>
        public string? TurnIn(string questId, ItemContainer bag, Func<long> newUid, out QuestReward reward)
        {
            reward = default;
            if (!_active.TryGetValue(questId, out var progress)) return "Missão não aceita.";
            if (!IsReady(progress, bag)) return "Os objetivos ainda não foram cumpridos.";
            var quest = QuestCatalog.Get(questId);

            // Confere o espaço numa cópia antes de mexer na bolsa de verdade.
            var trial = bag.Clone();
            foreach (var objective in quest.Objectives)
                if (objective.Kind == QuestObjectiveKind.Collect) trial.TryRemove(objective.TargetId, objective.Count);
            long trialUid = 0;
            foreach (var (itemId, quantity) in quest.RewardItems)
                if (!trial.TryAdd(itemId, quantity, () => --trialUid)) return "Bolsa cheia: libere espaço para a recompensa.";

            foreach (var objective in quest.Objectives)
                if (objective.Kind == QuestObjectiveKind.Collect) bag.TryRemove(objective.TargetId, objective.Count);
            foreach (var (itemId, quantity) in quest.RewardItems) bag.TryAdd(itemId, quantity, newUid);

            _active.Remove(questId);
            _completed.Add(questId);
            reward = new QuestReward(quest.RewardXp, quest.RewardAlz);
            return null;
        }

        /// <summary>Restaura do banco.</summary>
        public void Restore(IEnumerable<string> completed, IEnumerable<QuestProgress> active)
        {
            _completed.Clear();
            _active.Clear();
            foreach (var id in completed) if (QuestCatalog.Find(id) != null) _completed.Add(id);
            foreach (var progress in active)
                if (QuestCatalog.Find(progress.QuestId) != null && !_completed.Contains(progress.QuestId))
                    _active[progress.QuestId] = progress;
        }
    }
}
