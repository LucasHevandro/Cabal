using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Game.Server.Accounts;

/// <summary>
/// Hash de senha com PBKDF2-SHA256 e sal aleatório (recomendação da OWASP: 600.000 iterações).
/// Formato guardado no banco: <c>pbkdf2-sha256$iterações$sal$hash</c> (base64), para poder aumentar as
/// iterações no futuro sem invalidar as senhas antigas.
/// </summary>
public sealed class PasswordHasher
{
    public const int DefaultIterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Prefix = "pbkdf2-sha256";

    private readonly int _iterations;

    /// <summary>Menos iterações só nos testes (deixa-os rápidos).</summary>
    public PasswordHasher(int iterations = DefaultIterations)
    {
        if (iterations < 1000) throw new ArgumentOutOfRangeException(nameof(iterations));
        _iterations = iterations;
    }

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, _iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    /// <summary>Confere a senha em tempo constante. Formato desconhecido é tratado como senha errada.</summary>
    public bool Verify(string password, string stored)
    {
        var parts = (stored ?? "").Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations) || iterations < 1000)
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password ?? "", salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Verdadeiro se o hash foi feito com menos iterações que o padrão atual (refazer no próximo login).</summary>
    public bool NeedsRehash(string stored)
    {
        var parts = (stored ?? "").Split('$');
        return parts.Length != 4 || !int.TryParse(parts[1], out var iterations) || iterations < _iterations;
    }
}

/// <summary>
/// Tokens de sessão: entregues pela API de login e usados no Hello do jogo. Ficam só na memória do servidor
/// (o login e o jogo rodam no mesmo processo); reiniciar o servidor obriga a logar de novo.
/// </summary>
public sealed class SessionTokens
{
    private readonly ConcurrentDictionary<string, (int AccountId, DateTimeOffset ExpiresAt)> _tokens = new();
    private readonly Func<DateTimeOffset> _now;

    public TimeSpan Lifetime { get; }

    public SessionTokens(TimeSpan? lifetime = null, Func<DateTimeOffset>? now = null)
    {
        Lifetime = lifetime ?? TimeSpan.FromHours(12);
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Cria um token aleatório (256 bits) para a conta.</summary>
    public string Create(int accountId)
    {
        PurgeExpired();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        _tokens[token] = (accountId, _now() + Lifetime);
        return token;
    }

    /// <summary>Id da conta dona do token, ou nulo se não existe ou expirou.</summary>
    public int? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || !_tokens.TryGetValue(token, out var entry)) return null;
        if (_now() >= entry.ExpiresAt)
        {
            _tokens.TryRemove(token, out _);
            return null;
        }
        return entry.AccountId;
    }

    public void Revoke(string token) => _tokens.TryRemove(token, out _);

    private void PurgeExpired()
    {
        var now = _now();
        foreach (var (token, entry) in _tokens)
        {
            if (now >= entry.ExpiresAt) _tokens.TryRemove(token, out _);
        }
    }
}
