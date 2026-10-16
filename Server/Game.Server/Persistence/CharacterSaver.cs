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

    public void Enqueue(CharacterSnapshot snapshot, List<ItemLogEntry> logs) => _queue.Writer.TryWrite((snapshot, logs));

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
        await foreach (var (snapshot, logs) in _queue.Reader.ReadAllAsync())
        {
            var delay = TimeSpan.FromMilliseconds(250);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await _store.SaveCharacterAsync(snapshot, logs);
                    Interlocked.Increment(ref _saved);
                    break;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _failures);
                    _log($"Falha ao salvar personagem {snapshot.CharacterId} (tentativa {attempt}): {ex.GetBaseException().Message}");
                    if (attempt >= 8)
                    {
                        _log($"ERRO: desisti de salvar o personagem {snapshot.CharacterId} depois de {attempt} tentativas.");
                        break;
                    }
                    await Task.Delay(delay);
                    delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 10_000));
                }
            }
        }
    }
}
