using System.ComponentModel.DataAnnotations;

namespace DatabaseToS3Exporter.Models;

public class ExportConfiguration
{
    [Required]
    public DatabaseType DatabaseType { get; set; } = DatabaseType.MySQL;
    
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
    
    [Required]
    public List<TableExportConfig> Tables { get; set; } = new();
    
    [Required]
    public AuthConfig Authentication { get; set; } = new();
    
    [Required]
    public S3Config S3 { get; set; } = new();
    
    public int DaysToExport { get; set; } = 30;
    public string TempDirectory { get; set; } = "./temp";
}

public enum DatabaseType
{
    MySQL,
    PostgreSQL
}

public class TableExportConfig
{
    public string TableName { get; set; } = string.Empty;
    public string DateColumn { get; set; } = "created_at";
    public string? CustomerIdColumn { get; set; }
    public List<string>? CustomerIds { get; set; }
    public List<string>? Columns { get; set; } // null means all columns
    public string? Schema { get; set; } // For PostgreSQL schema support
}

public class AuthConfig
{
    public string KeycloakUrl { get; set; } = string.Empty;
    public string Realm { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string CredentialEndpoint { get; set; } = string.Empty;
}

public class S3Config
{
    public string BucketName { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = "database-exports/";
    public string Region { get; set; } = "us-east-1";
}

public class ExportResult
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime? MinDate { get; set; }
    public DateTime? MaxDate { get; set; }
    public int RowCount { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
}
