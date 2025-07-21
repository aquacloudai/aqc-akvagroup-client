using Newtonsoft.Json;

namespace DatabaseToS3Exporter.Models;

public class AuthTokenResponse
{
    [JsonProperty("access_token")]
    public string AccessToken { get; set; } = string.Empty;
    
    [JsonProperty("expires_in")]
    public int ExpiresIn { get; set; }
    
    [JsonProperty("token_type")]
    public string TokenType { get; set; } = string.Empty;
}

public class S3CredentialsResponse
{
    [JsonProperty("access_key_id")]
    public string AccessKeyId { get; set; } = string.Empty;
    
    [JsonProperty("secret_access_key")]
    public string SecretAccessKey { get; set; } = string.Empty;
    
    [JsonProperty("session_token")]
    public string SessionToken { get; set; } = string.Empty;
    
    [JsonProperty("expiration")]
    public DateTime Expiration { get; set; }
    
    [JsonProperty("region")]
    public string Region { get; set; } = string.Empty;
}
