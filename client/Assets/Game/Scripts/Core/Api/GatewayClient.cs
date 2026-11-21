using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Realmforge.Rules.Characters;

namespace Realmforge.Client.Core.Api
{
    public sealed class HttpResponse
    {
        public HttpResponse(int status, string body)
        {
            Status = status;
            Body = body;
        }

        public int Status { get; }
        public string Body { get; }
        public bool IsSuccess => Status >= 200 && Status < 300;
    }

    /// <summary>Implemented with UnityWebRequest in the game (works on WebGL) and HttpClient in tests.</summary>
    public interface IHttpTransport
    {
        Task<HttpResponse> Send(string method, string url, string? jsonBody, string? bearerToken);
    }

    public sealed class ApiException : Exception
    {
        public const int NetworkFailureStatus = 0;

        public ApiException(int status, string code, string message)
            : base(message)
        {
            Status = status;
            Code = code;
        }

        public int Status { get; }
        public string Code { get; }
    }

    public sealed class SessionDto
    {
        public Guid AccountId { get; set; }
        public string Token { get; set; } = string.Empty;
    }

    public sealed class CharacterSummaryDto
    {
        public Guid CharacterId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string RaceId { get; set; } = string.Empty;
        public int Level { get; set; }
        public string ZoneId { get; set; } = string.Empty;
        public List<string> Classes { get; set; } = new List<string>();
    }

    public sealed class EnterWorldDto
    {
        public string ZoneId { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int UdpPort { get; set; }
        public int WebSocketPort { get; set; }
        public string Ticket { get; set; } = string.Empty;
    }

    internal sealed class ApiErrorDto
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>Typed access to the gateway HTTP API; keeps the session token after login.</summary>
    public sealed class GatewayClient
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
        };

        private readonly IHttpTransport _http;
        private readonly string _baseUrl;

        public GatewayClient(IHttpTransport http, string baseUrl)
        {
            _http = http;
            _baseUrl = baseUrl.TrimEnd('/');
        }

        public string? Token { get; private set; }
        public Guid AccountId { get; private set; }
        public bool IsLoggedIn => Token != null;

        public Task Register(string email, string password) => StartSession("/auth/register", email, password);

        public Task Login(string email, string password) => StartSession("/auth/login", email, password);

        public void Logout()
        {
            Token = null;
            AccountId = Guid.Empty;
        }

        public Task<List<CharacterSummaryDto>> ListCharacters() => Request<List<CharacterSummaryDto>>("GET", "/characters", null);

        public Task<CharacterSummaryDto> CreateCharacter(CreateCharacterRequest request) =>
            Request<CharacterSummaryDto>("POST", "/characters", request);

        public Task<EnterWorldDto> EnterWorld(Guid characterId) =>
            Request<EnterWorldDto>("POST", "/characters/" + characterId + "/enter", new object());

        private async Task StartSession(string path, string email, string password)
        {
            SessionDto session = await Request<SessionDto>("POST", path, new { email, password });
            Token = session.Token;
            AccountId = session.AccountId;
        }

        /// <summary>Escape hatch for endpoints without a typed wrapper yet (party, auction, housing...).</summary>
        public async Task<T> Request<T>(string method, string path, object? body)
        {
            string? json = body == null ? null : JsonConvert.SerializeObject(body, Json);
            HttpResponse response = await _http.Send(method, _baseUrl + path, json, Token);
            if (!response.IsSuccess)
            {
                throw ToException(response);
            }
            if (string.IsNullOrEmpty(response.Body))
            {
                return default!;
            }
            return JsonConvert.DeserializeObject<T>(response.Body, Json)!;
        }

        private static ApiException ToException(HttpResponse response)
        {
            ApiErrorDto? error = null;
            try
            {
                error = JsonConvert.DeserializeObject<ApiErrorDto>(response.Body, Json);
            }
            catch (JsonException)
            {
            }
            return new ApiException(response.Status, error?.Code ?? "http." + response.Status, error?.Message ?? "Сервер недоступен.");
        }
    }
}
