namespace Game.Server.World;

/// <summary>
/// Anti speed hack: o cliente informa a posição e o servidor confere se dava para chegar lá.
/// Usa um "saldo de distância" que enche com o tempo (velocidade máxima) e tem uma reserva para a esquiva.
/// </summary>
public sealed class MovementValidator
{
    /// <summary>Corrida do cliente (7,5 m/s) com folga de 35% para jitter de rede.</summary>
    public const float MaxSpeed = 7.5f * 1.35f;

    /// <summary>Reserva para a esquiva (4,5 m) mais folga.</summary>
    public const float BurstAllowance = 6f;

    private const float MaxBudget = MaxSpeed * 0.5f + BurstAllowance;

    private double _lastTime;
    private float _budget = MaxBudget;

    public float X { get; private set; }
    public float Z { get; private set; }

    public MovementValidator(float x, float z, double now)
    {
        X = x;
        Z = z;
        _lastTime = now;
    }

    /// <summary>Aceita a posição se couber no saldo; senão mantém a última posição válida e retorna falso.</summary>
    public bool TryMove(float x, float z, double now)
    {
        var elapsed = Math.Max(0, now - _lastTime);
        _lastTime = now;
        _budget = (float)Math.Min(MaxBudget, _budget + MaxSpeed * elapsed);

        var dx = x - X;
        var dz = z - Z;
        var distance = MathF.Sqrt(dx * dx + dz * dz);
        if (float.IsNaN(distance) || distance > _budget) return false;

        _budget -= distance;
        X = x;
        Z = z;
        return true;
    }

    /// <summary>Move sem conferir (renascer, teleporte do próprio servidor).</summary>
    public void Teleport(float x, float z, double now)
    {
        X = x;
        Z = z;
        _lastTime = now;
        _budget = MaxBudget;
    }
}
