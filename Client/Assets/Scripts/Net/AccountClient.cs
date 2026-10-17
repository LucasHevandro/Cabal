using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Game.Shared.Accounts;
using Game.Shared.Text;
using UnityEngine;

namespace Game.Client.Net
{
    /// <summary>Resposta da API de login: o valor ou a mensagem de erro para mostrar ao jogador.</summary>
    public readonly struct ApiResult<T>
    {
        public bool Ok { get; }
        public T Value { get; }
        public string Error { get; }

        public ApiResult(bool ok, T value, string error)
        {
            Ok = ok;
            Value = value;
            Error = error;
        }
    }

    /// <summary>
    /// Cliente da API HTTP de contas do servidor (porta do jogo + 1). Usa HttpClient (funciona no Unity sem a opção de
    /// "HTTP inseguro"); o JSON é o mesmo do servidor (DTOs do Shared com JsonUtility).
    /// Em desenvolvimento a API é HTTP; em produção ficará atrás de HTTPS.
    /// </summary>
    public sealed class AccountClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        private readonly string _baseUrl;

        public string Token { get; private set; } = "";

        public AccountClient(string host, int gamePort)
        {
            _baseUrl = $"http://{host}:{gamePort + 1}/api";
        }

        public Task<ApiResult<ApiError>> RegisterAsync(string account, string password) =>
            SendAsync<ApiError>(HttpMethod.Post, "/register", new Credentials { Account = account, Password = password });

        public async Task<ApiResult<LoginResponse>> LoginAsync(string account, string password)
        {
            var result = await SendAsync<LoginResponse>(HttpMethod.Post, "/login", new Credentials { Account = account, Password = password });
            if (result.Ok) Token = result.Value.Token;
            return result;
        }

        public Task<ApiResult<CharacterList>> ListCharactersAsync() => SendAsync<CharacterList>(HttpMethod.Get, "/characters", null);

        public Task<ApiResult<CharacterSummary>> CreateCharacterAsync(string name, CharacterClass characterClass) =>
            SendAsync<CharacterSummary>(HttpMethod.Post, "/characters", new CreateCharacterRequest { Name = name, ClassId = (int)characterClass });

        private async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string path, object body)
        {
            try
            {
                using var request = new HttpRequestMessage(method, _baseUrl + path);
                if (body != null) request.Content = new StringContent(JsonUtility.ToJson(body), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(Token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);

                using var response = await Http.SendAsync(request);
                var text = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode) return new ApiResult<T>(true, JsonUtility.FromJson<T>(text), "");

                var error = string.IsNullOrEmpty(text) ? null : JsonUtility.FromJson<ApiError>(text);
                return new ApiResult<T>(false, default, error == null || (string.IsNullOrEmpty(error.Error) && string.IsNullOrEmpty(error.Key)) ? Loc.T("net.http_error", (int)response.StatusCode) : error.Display);
            }
            catch (TaskCanceledException)
            {
                return new ApiResult<T>(false, default, Loc.T("net.api_timeout"));
            }
            catch (HttpRequestException)
            {
                return new ApiResult<T>(false, default, Loc.T("net.api_unreachable"));
            }
            catch (ArgumentException)
            {
                return new ApiResult<T>(false, default, Loc.T("net.api_bad_response"));
            }
        }
    }
}
