using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DatabaseToS3Exporter.Models;
using DatabaseToS3Exporter.Services;
using Polly;

namespace DatabaseToS3Exporter.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddExporterServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configuration - Override with environment variables
        var config = configuration.Get<ExportConfiguration>()
            ?? throw new InvalidOperationException("Failed to load configuration");
        
        // Override with environment variables if they exist
        if (Enum.TryParse<DatabaseType>(Environment.GetEnvironmentVariable("DATABASE_TYPE"), out var dbType))
            config.DatabaseType = dbType;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CONNECTION_STRING")))
            config.ConnectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING")!;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KEYCLOAK_URL")))
            config.Authentication.KeycloakUrl = Environment.GetEnvironmentVariable("KEYCLOAK_URL")!;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KEYCLOAK_REALM")))
            config.Authentication.Realm = Environment.GetEnvironmentVariable("KEYCLOAK_REALM")!;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KEYCLOAK_CLIENT_ID")))
            config.Authentication.ClientId = Environment.GetEnvironmentVariable("KEYCLOAK_CLIENT_ID")!;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KEYCLOAK_CLIENT_SECRET")))
            config.Authentication.ClientSecret = Environment.GetEnvironmentVariable("KEYCLOAK_CLIENT_SECRET")!;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CREDENTIAL_ENDPOINT")))
            config.Authentication.CredentialEndpoint = Environment.GetEnvironmentVariable("CREDENTIAL_ENDPOINT")!;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("S3_KEY_PREFIX")))
            config.S3.KeyPrefix = Environment.GetEnvironmentVariable("S3_KEY_PREFIX")!;
        
        if (int.TryParse(Environment.GetEnvironmentVariable("EXPORT_DAYS_TO_EXPORT"), out var daysToExport))
            config.DaysToExport = daysToExport;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("EXPORT_TEMP_DIRECTORY")))
            config.TempDirectory = Environment.GetEnvironmentVariable("EXPORT_TEMP_DIRECTORY")!;
        
        services.AddSingleton(config);

        // HTTP Client with retry policy
        services.AddHttpClient<AuthenticationService>()
            .AddTransientHttpErrorPolicy(policy =>
                policy.WaitAndRetryAsync(3, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

        // Services
        services.AddSingleton<AuthenticationService>();
        services.AddSingleton<DatabaseExportService>();
        services.AddSingleton<S3UploadService>();
        services.AddSingleton<ExportOrchestrationService>();

        return services;
    }
}