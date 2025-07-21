using Microsoft.Extensions.Logging;
using DatabaseToS3Exporter.Models;
using Polly;
using Polly.Retry;

namespace DatabaseToS3Exporter.Services;

public class ExportOrchestrationService
{
    private readonly AuthenticationService _authService;
    private readonly DatabaseExportService _dbService;
    private readonly S3UploadService _s3Service;
    private readonly ExportConfiguration _config;
    private readonly ILogger<ExportOrchestrationService> _logger;
    private readonly AsyncRetryPolicy _retryPolicy;

    public ExportOrchestrationService(
        AuthenticationService authService,
        DatabaseExportService dbService,
        S3UploadService s3Service,
        ExportConfiguration config,
        ILogger<ExportOrchestrationService> logger)
    {
        _authService = authService;
        _dbService = dbService;
        _s3Service = s3Service;
        _config = config;
        _logger = logger;

        _retryPolicy = Policy
            .Handle<Exception>()
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                (exception, timeSpan, retryCount, context) =>
                {
                    _logger.LogWarning(exception,
                        "Retry {RetryCount} after {TimeSpan}s due to error",
                        retryCount, timeSpan.TotalSeconds);
                });
    }

    public async Task ExecuteExportAsync()
    {
        _logger.LogInformation("Starting export process for {TableCount} tables",
            _config.Tables.Count);

        // Get authentication
        var keycloakToken = await _retryPolicy.ExecuteAsync(
            () => _authService.GetKeycloakTokenAsync());

        var s3Credentials = await _retryPolicy.ExecuteAsync(
            () => _authService.GetS3CredentialsAsync(keycloakToken));

        _s3Service.Initialize(s3Credentials);

        var successCount = 0;
        var failureCount = 0;

        // Process each table
        foreach (var tableConfig in _config.Tables)
        {
            try
            {
                await ProcessTableExportAsync(tableConfig);
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export table {TableName}",
                    tableConfig.TableName);
                failureCount++;
            }
        }

        _logger.LogInformation(
            "Export process completed. Success: {SuccessCount}, Failures: {FailureCount}",
            successCount, failureCount);

        if (failureCount > 0)
        {
            throw new InvalidOperationException(
                $"Export completed with {failureCount} failures");
        }
    }

    private async Task ProcessTableExportAsync(TableExportConfig tableConfig)
    {
        // If no specific customers are configured, export all data in one file
        if (tableConfig.CustomerIds == null || !tableConfig.CustomerIds.Any())
        {
            await ProcessSingleTableExportAsync(tableConfig, null);
            return;
        }

        // Export each customer separately
        foreach (var customerId in tableConfig.CustomerIds)
        {
            await ProcessSingleTableExportAsync(tableConfig, customerId);
        }
    }

    private async Task ProcessSingleTableExportAsync(TableExportConfig tableConfig, string? customerId)
    {
        string? tempFilePath = null;

        try
        {
            // Create a config for single customer export
            var singleCustomerConfig = new TableExportConfig
            {
                TableName = tableConfig.TableName,
                DateColumn = tableConfig.DateColumn,
                CustomerIdColumn = tableConfig.CustomerIdColumn,
                CustomerIds = customerId != null ? new List<string> { customerId } : null,
                Columns = tableConfig.Columns,
                Schema = tableConfig.Schema
            };

            // Export to CSV
            var exportResult = await _dbService.ExportTableToCsvAsync(singleCustomerConfig);
            tempFilePath = exportResult.FilePath;

            // Check if export has data (more than just headers)
            if (exportResult.RowCount == 0)
            {
                _logger.LogWarning("Skipping upload for table {TableName} customer {CustomerId} - no data rows found",
                    tableConfig.TableName, customerId ?? "all");
                return;
            }

            // Create custom S3 key for customer/table organization with date range
            var s3Key = GenerateS3Key(exportResult);

            // Upload to S3
            await _retryPolicy.ExecuteAsync(
                () => _s3Service.UploadFileAsync(tempFilePath, s3Key));

            _logger.LogInformation("Successfully processed table {TableName} for customer {CustomerId}",
                tableConfig.TableName, customerId ?? "all");
        }
        finally
        {
            // Cleanup temp file
            if (!string.IsNullOrEmpty(tempFilePath))
            {
                _dbService.CleanupTempFile(tempFilePath);
            }
        }
    }

    private string GenerateS3Key(ExportResult exportResult)
    {
        // Generate date range filename
        string fileName;
        if (exportResult.MinDate.HasValue && exportResult.MaxDate.HasValue)
        {
            var minDateStr = exportResult.MinDate.Value.ToString("yyyyMMdd");
            var maxDateStr = exportResult.MaxDate.Value.ToString("yyyyMMdd");
            fileName = $"{minDateStr}-{maxDateStr}.csv";
        }
        else
        {
            // Fallback to timestamp if no date range available
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            fileName = $"{exportResult.TableName}_{timestamp}.csv";
        }

        if (exportResult.CustomerId != null)
        {
            // Structure: {prefix}{customerId}/{tableName}/{filename}
            return $"{_config.S3.KeyPrefix}{exportResult.CustomerId}/{exportResult.TableName}/{fileName}";
        }
        else
        {
            // Structure: {prefix}{tableName}/{filename}
            return $"{_config.S3.KeyPrefix}{exportResult.TableName}/{fileName}";
        }
    }
}