using System.Text;
using System.Threading.Tasks;
using Realmforge.Client.Core.Api;
using UnityEngine.Networking;

namespace Realmforge.Client.Unity.Net
{
    /// <summary>UnityWebRequest works on every platform including WebGL, unlike HttpClient.</summary>
    public sealed class UnityHttpTransport : IHttpTransport
    {
        private const int TimeoutSeconds = 15;
        private const string JsonContentType = "application/json";

        public Task<HttpResponse> Send(string method, string url, string? jsonBody, string? bearerToken)
        {
            var completion = new TaskCompletionSource<HttpResponse>();
            var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = TimeoutSeconds,
            };
            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)) { contentType = JsonContentType };
                request.SetRequestHeader("Content-Type", JsonContentType);
            }
            if (bearerToken != null)
            {
                request.SetRequestHeader("Authorization", "Bearer " + bearerToken);
            }
            request.SendWebRequest().completed += _ =>
            {
                int status = request.result == UnityWebRequest.Result.ConnectionError ? ApiException.NetworkFailureStatus : (int)request.responseCode;
                completion.SetResult(new HttpResponse(status, request.downloadHandler.text ?? string.Empty));
                request.Dispose();
            };
            return completion.Task;
        }
    }
}
