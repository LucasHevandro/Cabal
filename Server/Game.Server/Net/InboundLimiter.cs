namespace Game.Server.Net;

/// <summary>
/// Limite de mensagens recebidas por conexão (anti flood): balde de fichas que enche <see cref="PerSecond"/> por
/// segundo até <see cref="Burst"/>. Sem ficha, a mensagem é descartada; descartes demais em pouco tempo derrubam a
/// conexão. O cliente normal manda ~21 por segundo (20 de movimento e 1 ping) e fica bem abaixo do limite.
/// Usado só pela thread de leitura de uma conexão.
/// </summary>
public sealed class InboundLimiter
{
    public double PerSecond { get; }
    public double Burst { get; }

    /// <summary>Descartes dentro de <see cref="KickWindowSeconds"/> que derrubam a conexão.</summary>
    public int KickAfterDrops { get; }

    public const double KickWindowSeconds = 5;

    private double _tokens;
    private double _lastAt = double.NaN;
    private int _dropsInWindow;
    private double _windowStartedAt;

    public InboundLimiter(double perSecond = 40, double burst = 80, int kickAfterDrops = 100)
    {
        PerSecond = perSecond;
        Burst = burst;
        KickAfterDrops = kickAfterDrops;
        _tokens = burst;
    }

    /// <summary>Total de mensagens descartadas (para o log).</summary>
    public long Dropped { get; private set; }

    /// <summary>A conexão passou do limite de descartes e deve ser fechada.</summary>
    public bool ShouldKick { get; private set; }

    /// <summary>Verdadeiro se a mensagem chegada em <paramref name="now"/> (segundos) pode passar.</summary>
    public bool Allow(double now)
    {
        if (double.IsNaN(_lastAt)) _lastAt = now;
        _tokens = Math.Min(Burst, _tokens + (now - _lastAt) * PerSecond);
        _lastAt = now;
        if (_tokens >= 1)
        {
            _tokens -= 1;
            return true;
        }

        Dropped++;
        if (now - _windowStartedAt > KickWindowSeconds)
        {
            _windowStartedAt = now;
            _dropsInWindow = 0;
        }
        if (++_dropsInWindow >= KickAfterDrops) ShouldKick = true;
        return false;
    }
}
