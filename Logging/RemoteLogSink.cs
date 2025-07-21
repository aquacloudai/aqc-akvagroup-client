using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;

namespace DatabaseToS3Exporter.Logging
{
    public class RemoteLogSink : ILogEventSink, IDisposable
    {
        private readonly string _serverUrl;
        private readonly string _clientId;
        private readonly string _environment;
        private readonly IServiceProvider _serviceProvider;
        private readonly Queue<LogEvent> _logQueue;
        private readonly SemaphoreSlim _semaphore;
        private readonly Timer _flushTimer;
        private readonly int _batchSize;
        private readonly TimeSpan _flushInterval;
        private readonly object _queueLock = new object();

        public RemoteLogSink(
            string serverUrl,
            string clientId,
            string environment,
            IServiceProvider serviceProvider,
            int batchSize = 100,
            int flushIntervalSeconds = 30)
        {
            _serverUrl = serverUrl.TrimEnd('/') + "/api/v1/logs";
            _clientId = clientId;
            _environment = environment;
            _serviceProvider = serviceProvider;
            _logQueue = new Queue<LogEvent>();
            _semaphore = new SemaphoreSlim(1, 1);
            _batchSize = batchSize;
            _flushInterval = TimeSpan.FromSeconds(flushIntervalSeconds);
            
            // Start periodic flush timer
            _flushTimer = new Timer(async _ => await FlushLogsAsync(), null, _flushInterval, _flushInterval);
        }

        public void Emit(LogEvent logEvent)
        {
            // Don't send logs below Information level to remote
            if (logEvent.Level < LogEventLevel.Information)
                return;

            lock (_queueLock)
            {
                _logQueue.Enqueue(logEvent);
                
                // Trigger flush if we've reached batch size
                if (_logQueue.Count >= _batchSize)
                {
                    Task.Run(async () => await FlushLogsAsync());
                }
            }
        }

        private async Task FlushLogsAsync()
        {
            if (!await _semaphore.WaitAsync(0))
                return; // Already flushing

            try
            {
                List<LogEvent> logsToSend;
                lock (_queueLock)
                {
                    if (_logQueue.Count == 0)
                        return;

                    logsToSend = _logQueue.ToList();
                    _logQueue.Clear();
                }

                await SendLogsToServerAsync(logsToSend);
            }
            catch (Exception ex)
            {
                // Silently ignore errors to avoid infinite loop
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task SendLogsToServerAsync(List<LogEvent> logEvents)
        {
            using var scope = _serviceProvider.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<Services.AuthenticationService>();
            
            // Get authentication token
            string token;
            try
            {
                token = await authService.GetKeycloakTokenAsync();
            }
            catch (Exception ex)
            {
                // Silently ignore authentication errors
                return;
            }

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", token);

            var logBatch = new
            {
                logs = logEvents.Select(le => new
                {
                    timestamp = le.Timestamp.UtcDateTime,
                    level = le.Level.ToString(),
                    message = le.RenderMessage(),
                    properties = le.Properties.Count > 0 
                        ? le.Properties.ToDictionary(p => p.Key, p => p.Value.ToString())
                        : null,
                    exception = le.Exception?.ToString(),
                    source_context = le.Properties.ContainsKey("SourceContext") 
                        ? le.Properties["SourceContext"].ToString().Trim('"')
                        : null
                }),
                client_id = _clientId,
                environment = _environment
            };

            var json = JsonConvert.SerializeObject(logBatch);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await httpClient.PostAsync(_serverUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    // Log forwarding failed, but we don't want to disrupt the main application
                    // Consider implementing a retry mechanism or dead letter queue in the future
                }
            }
            catch (HttpRequestException ex)
            {
                // Silently ignore HTTP errors
            }
        }

        public void Dispose()
        {
            // Flush any remaining logs
            _flushTimer?.Dispose();
            FlushLogsAsync().GetAwaiter().GetResult();
            _semaphore?.Dispose();
        }
    }

    public static class RemoteLogSinkExtensions
    {
        public static LoggerConfiguration RemoteLogServer(
            this LoggerSinkConfiguration sinkConfiguration,
            string serverUrl,
            string clientId,
            string environment,
            IServiceProvider serviceProvider,
            int batchSize = 100,
            int flushIntervalSeconds = 30)
        {
            return sinkConfiguration.Sink(
                new RemoteLogSink(serverUrl, clientId, environment, serviceProvider, batchSize, flushIntervalSeconds));
        }
    }
}