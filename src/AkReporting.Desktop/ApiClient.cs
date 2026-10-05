using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using AkReporting.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AkReporting.Desktop
{
    public sealed class ApiException : Exception
    {
        public HttpStatusCode Status { get; }
        public ApiException(HttpStatusCode status, string message) : base(message) { Status = status; }
    }
    public sealed class ApiClient : IDisposable
    {
        private readonly HttpClient client;
        private string? token;
        public string Role { get; private set; } = "";
        public bool LoggedIn => token != null;
        public ApiClient(string endpoint)
        {
            var uri = new Uri(endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/");
            if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
                throw new ArgumentException("Use HTTPS for LAN operation. HTTP is allowed only for a loopback development host.");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            client = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(90) };
        }
        public async Task Login(string username, string password)
        {
            var response = await Post<LoginResponse>("auth/login", new LoginRequest { Username = username, Password = password });
            token = response.Token; Role = response.Role;
        }
        public async Task Logout()
        {
            try { if (token != null) await Post<object?>("auth/logout", new { }); }
            finally { ClearSession(); }
        }
        public void ClearSession() { token = null; Role = ""; }
        public Task<T> Get<T>(string path) => Send<T>(HttpMethod.Get, path, null);
        public Task<T> Post<T>(string path, object value) => Send<T>(HttpMethod.Post, path, value);
        private async Task<T> Send<T>(HttpMethod method, string path, object? value)
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (value != null) request.Content = new StringContent(JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
                using (var response = await client.SendAsync(request))
                {
                    var json = await response.Content.ReadAsStringAsync();
                    Check(response, json);
                    return string.IsNullOrWhiteSpace(json) ? default! : JsonConvert.DeserializeObject<T>(json)!;
                }
            }
        }
        public async Task<byte[]> Download(Guid documentId)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, "documents/" + documentId))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using (var response = await client.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode) Check(response, await response.Content.ReadAsStringAsync());
                    return await response.Content.ReadAsByteArrayAsync();
                }
            }
        }
        private void Check(HttpResponseMessage response, string text)
        {
            if (response.IsSuccessStatusCode) return;
            if (response.StatusCode == HttpStatusCode.Unauthorized) ClearSession();
            string message;
            try { var problem = JObject.Parse(text); message = (string?)problem["title"] ?? "Request failed."; }
            catch (JsonException) { message = response.StatusCode == HttpStatusCode.Unauthorized ? "Session expired. Sign in again." : "The application host could not complete the request."; }
            throw new ApiException(response.StatusCode, message);
        }
        public void Dispose() => client.Dispose();
    }
}
