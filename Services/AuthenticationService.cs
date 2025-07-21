using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using DatabaseToS3Exporter.Models;
using Newtonsoft.Json;

namespace DatabaseToS3Exporter.Services;

public class AuthenticationService
{
    private readonly HttpClient _httpClient;
    private readonly ExportConfiguration _config;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        HttpClient httpClient,
        ExportConfiguration config,
        ILogger<AuthenticationService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<string> GetKeycloakTokenAsync()
    {
        _logger.LogInformation("Authenticating with Keycloak");

        var tokenUrl = $"{_config.Authentication.KeycloakUrl}/realms/{_config.Authentication.Realm}/protocol/openid-connect/token";

        var formData = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", _config.Authentication.ClientId),
            new KeyValuePair<string, string>("client_secret", _config.Authentication.ClientSecret)
        });

        var response = await _httpClient.PostAsync(tokenUrl, formData);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonConvert.DeserializeObject<AuthTokenResponse>(content)
            ?? throw new InvalidOperationException("Failed to deserialize token response");

        _logger.LogInformation("Successfully obtained Keycloak token");
        return tokenResponse.AccessToken;
    }

    public async Task<S3CredentialsResponse> GetS3CredentialsAsync(string keycloakToken)
    {
        _logger.LogInformation("Requesting temporary S3 credentials");

        var request = new HttpRequestMessage(HttpMethod.Post, _config.Authentication.CredentialEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", keycloakToken);

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync();
        var credentials = JsonConvert.DeserializeObject<S3CredentialsResponse>(content)
            ?? throw new InvalidOperationException("Failed to deserialize S3 credentials");

        _logger.LogInformation("Successfully obtained S3 credentials, expires at {Expiration}",
            credentials.Expiration);

        return credentials;
    }
}