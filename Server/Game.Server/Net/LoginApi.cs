using System.Text.Json;
using Game.Server.Accounts;
using Game.Shared.Accounts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Game.Server.Net;

/// <summary>
/// API HTTP de contas, no mesmo processo do jogo:
/// <code>
/// POST /api/register      { Account, Password }        → 200 | 400 | 409
/// POST /api/login         { Account, Password }        → { Token, ExpiresInSeconds } | 401 | 429
/// GET  /api/characters    Authorization: Bearer token  → { Characters: [...] }
/// POST /api/characters    { Name, ClassId }            → { Id, Name, ClassId, Level } | 400 | 409
/// </code>
/// Em desenvolvimento roda em HTTP; em produção deve ficar atrás de HTTPS (proxy reverso ou certificado no Kestrel).
/// </summary>
public sealed class LoginApi : IAsyncDisposable
{
    // Campos públicos com os nomes exatos (o JsonUtility do Unity lê assim).
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, PropertyNamingPolicy = null };

    private readonly WebApplication _app;

    public LoginApi(AuthService auth, int port, Action<string>? log = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 16 * 1024);
        _app = builder.Build();

        _app.MapPost("/api/register", async (HttpRequest request) =>
        {
            var body = await ReadAsync<Credentials>(request);
            if (body == null) return Error("Pedido inválido.", 400);
            var result = await auth.RegisterAsync(body);
            if (result.Ok) log?.Invoke($"Conta criada: {result.Value}");
            return result.Ok ? Ok(new ApiError()) : Error(result.Error, result.StatusCode);
        });

        _app.MapPost("/api/login", async (HttpRequest request) =>
        {
            var body = await ReadAsync<Credentials>(request);
            if (body == null) return Error("Pedido inválido.", 400);
            var result = await auth.LoginAsync(body);
            return result.Ok ? Ok(result.Value!) : Error(result.Error, result.StatusCode);
        });

        _app.MapGet("/api/characters", async (HttpRequest request) =>
        {
            var result = await auth.ListCharactersAsync(BearerToken(request));
            return result.Ok ? Ok(result.Value!) : Error(result.Error, result.StatusCode);
        });

        _app.MapPost("/api/characters", async (HttpRequest request) =>
        {
            var body = await ReadAsync<CreateCharacterRequest>(request);
            if (body == null) return Error("Pedido inválido.", 400);
            var result = await auth.CreateCharacterAsync(BearerToken(request), body);
            if (result.Ok) log?.Invoke($"Personagem criado: {result.Value!.Name} (id {result.Value.Id})");
            return result.Ok ? Ok(result.Value!) : Error(result.Error, result.StatusCode);
        });
    }

    public Task StartAsync() => _app.StartAsync();

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    /// <summary>Endereço em uso (com porta 0 nos testes, a porta escolhida pelo sistema).</summary>
    public string Address => _app.Urls.FirstOrDefault()?.Replace("0.0.0.0", "127.0.0.1") ?? "";

    private static async Task<T?> ReadAsync<T>(HttpRequest request) where T : class
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(request.Body, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? BearerToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header["Bearer ".Length..].Trim() : null;
    }

    private static IResult Ok<T>(T value) => Results.Json(value, Json);

    private static IResult Error(string message, int status) => Results.Json(new ApiError { Error = message }, Json, statusCode: status);
}
