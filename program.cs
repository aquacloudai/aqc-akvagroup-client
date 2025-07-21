using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using DatabaseToS3Exporter.Extensions;
using DatabaseToS3Exporter.Services;
using DatabaseToS3Exporter.Logging;
using Serilog;
using DotNetEnv;

namespace DatabaseToS3Exporter;

class Program
{
    static async Task<int> Main(string[] args)
    {
        // Load environment variables from .env file
        Env.Load();

        Console.WriteLine("=== Database to S3 Exporter Starting ===");

        // Build configuration first to get settings for logging
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // Read directly from environment variables (loaded by Env.Load())
        var credentialEndpoint = Environment.GetEnvironmentVariable("CREDENTIAL_ENDPOINT")
            ?? configuration["Authentication:CredentialEndpoint"];
        var clientId = Environment.GetEnvironmentVariable("KEYCLOAK_CLIENT_ID")
            ?? configuration["Authentication:ClientId"];
        var keycloakUrl = Environment.GetEnvironmentVariable("KEYCLOAK_URL")
            ?? configuration["Authentication:KeycloakUrl"];
        var realm = Environment.GetEnvironmentVariable("KEYCLOAK_REALM")
            ?? configuration["Authentication:Realm"];
        var clientSecret = Environment.GetEnvironmentVariable("KEYCLOAK_CLIENT_SECRET")
            ?? configuration["Authentication:ClientSecret"];

        // Configure Serilog
        var loggerConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/database-s3-export-.txt",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30);

        // Add remote log sink if configured
        if (!string.IsNullOrEmpty(credentialEndpoint))
        {
            var serverBaseUrl = new Uri(credentialEndpoint).GetLeftPart(UriPartial.Authority);
            var environment = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "production";

            if (!string.IsNullOrEmpty(keycloakUrl) && !string.IsNullOrEmpty(realm) && !string.IsNullOrEmpty(clientSecret))
            {
                Console.WriteLine($"Configuring remote log sink: {serverBaseUrl}/api/v1/logs");
                Console.WriteLine($"Client ID: {clientId}, Environment: {environment}");

                loggerConfig.WriteTo.SimpleRemoteLogServer(
                    serverBaseUrl,
                    clientId,
                    environment,
                    keycloakUrl,
                    realm,
                    clientSecret);
            }
            else
            {
                Console.WriteLine("Remote log sink not fully configured - missing Keycloak settings");
            }
        }
        else
        {
            Console.WriteLine("Remote log sink not configured - Authentication:CredentialEndpoint is empty");
        }

        Log.Logger = loggerConfig.CreateLogger();

        try
        {
            Log.Information("Starting Database to S3 Export Service");

            var host = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddExporterServices(context.Configuration);
                })
                .Build();

            var orchestrator = host.Services.GetRequiredService<ExportOrchestrationService>();
            await orchestrator.ExecuteExportAsync();

            Log.Information("Export completed successfully");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Export failed with error");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }
}