using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace DatabaseToS3Exporter.Logging
{
    public class SimpleRemoteLogSink : ILogEventSink
    {
        private readonly string _serverUrl;
        private readonly string _clientId;
        private readonly string _environment;
        private readonly string _keycloakUrl;
        private readonly string _realm;
        private readonly string _clientSecret;
        private readonly HttpClient _httpClient;

        public SimpleRemoteLogSink(
            string serverUrl,
            string clientId,
            string environment,
            string keycloakUrl,
            string realm,
            string clientSecret)
        {
            _serverUrl = serverUrl.TrimEnd('/') + "/api/v1/logs";
            _clientId = clientId;
            _environment = environment;
            _keycloakUrl = keycloakUrl;
            _realm = realm;
            _clientSecret = clientSecret;
            _httpClient = new HttpClient();
        }

        public void Emit(LogEvent logEvent)
        {
            // Don't send logs below Information level to remote
            if (logEvent.Level < LogEventLevel.Information)
                return;

            // Fire and forget - we don't want to block logging
            Task.Run(async () =>
            {
                try
                {
                    await SendLogToServerAsync(logEvent);
                }
                catch (Exception ex)
                {
                    // Silently ignore errors to avoid blocking logging
                }
            });
        }

        private async Task SendLogToServerAsync(LogEvent logEvent)
        {
            // Get token first
            string token;
            try
            {
                token = await GetKeycloakTokenAsync();
            }
            catch (Exception ex)
            {
                // Silently ignore authentication errors
                return;
            }

            // Prepare log batch
            var logBatch = new
            {
                logs = new[]
                {
                    new
                    {
                        timestamp = logEvent.Timestamp.UtcDateTime,
                        level = logEvent.Level.ToString(),
                        message = logEvent.RenderMessage(),
                        properties = logEvent.Properties.Count > 0 
                            ? logEvent.Properties.ToDictionary(p => p.Key, p => p.Value.ToString())
                            : null,
                        exception = logEvent.Exception?.ToString(),
                        source_context = logEvent.Properties.ContainsKey("SourceContext") 
                            ? logEvent.Properties["SourceContext"].ToString().Trim('"')
                            : null
                    }
                },
                client_id = _clientId,
                environment = _environment
            };

            var json = JsonConvert.SerializeObject(logBatch);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, _serverUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = content;

            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                // Log forwarding failed, but we don't want to disrupt the main application
                // Consider implementing a retry mechanism or dead letter queue in the future
            }
        }

        private async Task<string> GetKeycloakTokenAsync()
        {
            var tokenUrl = $"{_keycloakUrl}/realms/{_realm}/protocol/openid-connect/token";

            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret)
            });

            var response = await _httpClient.PostAsync(tokenUrl, formData);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            dynamic tokenResponse = JsonConvert.DeserializeObject(content);
            return tokenResponse.access_token;
        }
    }

    public static class SimpleRemoteLogSinkExtensions
    {
        public static LoggerConfiguration SimpleRemoteLogServer(
            this LoggerSinkConfiguration sinkConfiguration,
            string serverUrl,
            string clientId,
            string environment,
            string keycloakUrl,
            string realm,
            string clientSecret)
        {
            return sinkConfiguration.Sink(
                new SimpleRemoteLogSink(serverUrl, clientId, environment, keycloakUrl, realm, clientSecret));
        }
    }
}