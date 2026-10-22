using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Game.Server.Persistence;

/// <summary>
/// Salva personagens fora do loop do jogo: o loop só tira a "foto" e põe na fila; uma tarefa de fundo grava no banco,
/// um salvamento por vez e na ordem em que chegaram (o último estado de cada personagem sempre vence).
/// Se o banco falhar, tenta de novo com espera crescente sem perder a foto nem os registros de itens.
/// </summary>
public sealed class CharacterSaver : IAsyncDisposable
{
    private readonly IGameStore _store;
    private readonly Action<string> _log;
    private readonly Channel<(CharacterSnapshot Snapshot, List<ItemLogEntry> Logs)> _queue =
        Channel.CreateUnbounded<(CharacterSnapshot, List<ItemLogEntry>)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _worker;

    /// <summary>Salvamentos na fila (ou gravando agora) de cada personagem.</summary>
    private readonly ConcurrentDictionary<int, int> _pending = new();

    /// <summary>
    /// Fotos que falharam em todas as tentativas de uma rodada (banco fora do ar). Não são jogadas fora: os registros
    /// que só vão uma vez ao banco (itens, mercado, denúncias e métricas) entram na próxima foto do mesmo personagem, e
    /// a foto é tentada de novo quando a fila esvazia. Sem isso, uma compra no mercado podia ficar com o item no
    /// comprador e a oferta ainda ativa no banco. Enquanto está aqui, o personagem conta como pendente (não entra de
    /// novo com o estado antigo do banco). Só a tarefa de fundo mexe neste dicionário.
    /// </summary>
    private readonly Dictionary<int, (CharacterSnapshot Snapshot, List<ItemLogEntry> Logs, int Rounds)> _unsaved = new();

    /// <summary>Tentativas seguidas em cada rodada de gravação de uma foto.</summary>
    public const int AttemptsPerRound = 8;

    /// <summary>Rodadas antes de desistir de vez de uma foto (um erro que nunca passa, não o banco fora do ar).</summary>
    public const int MaxRounds = 5;

    /// <summary>Espera, com a fila vazia, antes de tentar de novo as fotos que não foram gravadas.</summary>
    public TimeSpan RetryUnsavedAfter { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Espera antes da segunda tentativa (dobra a cada falha, até 10 s).</summary>
    public TimeSpan FirstRetryDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    private long _saved;
    private long _failures;

    public CharacterSaver(IGameStore store, Action<string>? log = null)
    {
        _store = store;
        _log = log ?? (_ => { });
        _worker = Task.Run(WorkAsync);
    }

    /// <summary>Salvamentos concluídos e falhas (cada nova tentativa conta).</summary>
    public long Saved => Interlocked.Read(ref _saved);
    public long Failures => Interlocked.Read(ref _failures);

    /// <summary>Quantas fotos esperam para ser gravadas.</summary>
    public int Pending => _queue.Reader.Count;

    /// <summary>Quantas fotos do personagem já entraram na fila desde que o servidor ligou.</summary>
    private readonly ConcurrentDictionary<int, long> _versions = new();

    /// <summary>
    /// Muda a cada foto do personagem posta na fila. Quem carrega o personagem do banco anota antes e confere depois:
    /// se mudou no meio, o que foi lido pode ser anterior à última saída.
    /// </summary>
    public long SaveVersion(int characterId) => _versions.TryGetValue(characterId, out var version) ? version : 0;

    public void Enqueue(CharacterSnapshot snapshot, List<ItemLogEntry> logs)
    {
        _versions.AddOrUpdate(snapshot.CharacterId, 1, (_, version) => version + 1);
        _pending.AddOrUpdate(snapshot.CharacterId, 1, (_, count) => count + 1);
        if (!_queue.Writer.TryWrite((snapshot, logs))) Done(snapshot.CharacterId);
    }

    /// <summary>O personagem tem salvamento na fila ou gravando agora?</summary>
    public bool IsPending(int characterId) => _pending.TryGetValue(characterId, out var count) && count > 0;

    /// <summary>
    /// Espera gravar tudo o que está na fila deste personagem (ou o tempo acabar). Usado antes de carregá-lo do banco:
    /// quem sai e entra de novo logo em seguida (trocar de personagem) não volta com o estado de antes da saída.
    /// </summary>
    public async Task<bool> WaitForCharacterAsync(int characterId, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (IsPending(characterId))
        {
            if (DateTime.UtcNow >= until) return false;
            await Task.Delay(25);
        }
        return true;
    }

    private void Done(int characterId) => _pending.AddOrUpdate(characterId, 0, (_, count) => Math.Max(0, count - 1));

    /// <summary>Para de aceitar e espera gravar tudo o que já está na fila (desligamento do servidor).</summary>
    public async Task FlushAndStopAsync(TimeSpan timeout)
    {
        _queue.Writer.TryComplete();
        var finished = await Task.WhenAny(_worker, Task.Delay(timeout));
        if (finished != _worker) _log($"ATENÇÃO: desligando com {Pending} salvamentos pendentes (tempo esgotado).");
    }

    public async ValueTask DisposeAsync() => await FlushAndStopAsync(TimeSpan.FromSeconds(10));

    private async Task WorkAsync()
    {
        var reader = _queue.Reader;
        while (true)
        {
            if (reader.TryRead(out var next))
            {
                await SaveQueuedAsync(next.Snapshot, next.Logs);
                continue;
            }
            if (_unsaved.Count == 0)
            {
                if (!await reader.WaitToReadAsync()) break;
                continue;
            }
            // Fila vazia com fotos que não foram gravadas: espera um pouco (o banco pode voltar) e tenta de novo.
            using var wait = new CancellationTokenSource(RetryUnsavedAfter);
            try
            {
                if (await reader.WaitToReadAsync(wait.Token)) continue;
                await RetryUnsavedAsync(); // desligando: última chance
                break;
            }
            catch (OperationCanceledException)
            {
                await RetryUnsavedAsync();
            }
        }
    }

    /// <summary>Grava uma foto da fila, junto com o que sobrou de uma foto anterior do mesmo personagem.</summary>
    private async Task SaveQueuedAsync(CharacterSnapshot snapshot, List<ItemLogEntry> logs)
    {
        var rounds = 0;
        if (_unsaved.Remove(snapshot.CharacterId, out var old))
        {
            snapshot = snapshot with
            {
                Reports = old.Snapshot.Reports.Concat(snapshot.Reports).ToList(),
                Metrics = old.Snapshot.Metrics.Concat(snapshot.Metrics).ToList(),
                Market = old.Snapshot.Market.Concat(snapshot.Market).ToList(),
            };
            logs = old.Logs.Concat(logs).ToList();
            rounds = old.Rounds;
            Done(snapshot.CharacterId); // a foto antiga deixa de contar: ela vai dentro desta
        }
        if (!await TrySaveAsync(snapshot, logs)) KeepUnsaved(snapshot, logs, rounds + 1);
        Done(snapshot.CharacterId);
    }

    private async Task RetryUnsavedAsync()
    {
        foreach (var (characterId, (snapshot, logs, rounds)) in _unsaved.ToList())
        {
            _unsaved.Remove(characterId);
            if (await TrySaveAsync(snapshot, logs)) Done(characterId);
            else
            {
                Done(characterId);
                KeepUnsaved(snapshot, logs, rounds + 1);
            }
        }
    }

    /// <summary>Guarda a foto para a próxima rodada, ou desiste de vez depois de <see cref="MaxRounds"/>.</summary>
    private void KeepUnsaved(CharacterSnapshot snapshot, List<ItemLogEntry> logs, int rounds)
    {
        if (rounds >= MaxRounds)
        {
            _log($"ERRO: desisti de salvar o personagem {snapshot.CharacterId} depois de {rounds} rodadas de {AttemptsPerRound} tentativas.");
            return;
        }
        _log($"Personagem {snapshot.CharacterId} não foi salvo (rodada {rounds}); tento de novo junto com o próximo salvamento.");
        _unsaved[snapshot.CharacterId] = (snapshot, logs, rounds);
        _pending.AddOrUpdate(snapshot.CharacterId, 1, (_, count) => count + 1);
    }

    /// <summary>Até <see cref="AttemptsPerRound"/> tentativas com espera crescente; falso se todas falharem.</summary>
    private async Task<bool> TrySaveAsync(CharacterSnapshot snapshot, List<ItemLogEntry> logs)
    {
        var delay = FirstRetryDelay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _store.SaveCharacterAsync(snapshot, logs);
                Interlocked.Increment(ref _saved);
                return true;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failures);
                _log($"Falha ao salvar personagem {snapshot.CharacterId} (tentativa {attempt}): {ex.GetBaseException().Message}");
                if (attempt >= AttemptsPerRound) return false;
                await Task.Delay(delay);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 10_000));
            }
        }
    }
}
