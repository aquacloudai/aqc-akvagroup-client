using System.Data;
using System.Data.Common;
using System.Globalization;
using CsvHelper;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Npgsql;
using DatabaseToS3Exporter.Models;

namespace DatabaseToS3Exporter.Services;

// Database abstraction interface
public interface IDatabaseConnection : IDisposable
{
    Task<DbConnection> CreateConnectionAsync();
    string BuildQuery(TableExportConfig config, int daysToExport);
    string EscapeIdentifier(string identifier);
}

// MySQL implementation
public class MySqlDatabaseConnection : IDatabaseConnection
{
    private readonly string _connectionString;

    public MySqlDatabaseConnection(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<DbConnection> CreateConnectionAsync()
    {
        var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public string BuildQuery(TableExportConfig config, int daysToExport)
    {
        var columns = config.Columns?.Any() == true
            ? string.Join(", ", config.Columns.Select(c => EscapeIdentifier(c)))
            : "*";

        var tableName = EscapeIdentifier(config.TableName);
        var dateColumn = EscapeIdentifier(config.DateColumn);

        var query = $"SELECT {columns} FROM {tableName} WHERE 1=1";
        query += $" AND {dateColumn} >= DATE_SUB(CURDATE(), INTERVAL {daysToExport} DAY)";

        if (!string.IsNullOrEmpty(config.CustomerIdColumn) && config.CustomerIds?.Any() == true)
        {
            var customerColumn = EscapeIdentifier(config.CustomerIdColumn);
            var customerList = string.Join(",", config.CustomerIds.Select(id => $"'{id.Replace("'", "''")}'"));
            query += $" AND {customerColumn} IN ({customerList})";
        }

        query += $" ORDER BY {dateColumn} DESC";
        return query;
    }

    public string EscapeIdentifier(string identifier) => $"`{identifier}`";

    public void Dispose() { }
}

// PostgreSQL implementation
public class PostgreSqlDatabaseConnection : IDatabaseConnection
{
    private readonly string _connectionString;

    public PostgreSqlDatabaseConnection(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<DbConnection> CreateConnectionAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    public string BuildQuery(TableExportConfig config, int daysToExport)
    {
        var columns = config.Columns?.Any() == true
            ? string.Join(", ", config.Columns.Select(c => EscapeIdentifier(c)))
            : "*";

        var tableName = !string.IsNullOrEmpty(config.Schema)
            ? $"{EscapeIdentifier(config.Schema)}.{EscapeIdentifier(config.TableName)}"
            : EscapeIdentifier(config.TableName);

        var dateColumn = EscapeIdentifier(config.DateColumn);

        var query = $"SELECT {columns} FROM {tableName} WHERE 1=1";
        query += $" AND {dateColumn}::date >= CURRENT_DATE - INTERVAL '{daysToExport} days'";

        if (!string.IsNullOrEmpty(config.CustomerIdColumn) && config.CustomerIds?.Any() == true)
        {
            var customerColumn = EscapeIdentifier(config.CustomerIdColumn);
            var customerList = string.Join(",", config.CustomerIds.Select(id => $"'{id.Replace("'", "''")}'"));
            query += $" AND {customerColumn} IN ({customerList})";
        }

        query += $" ORDER BY {dateColumn} DESC";
        return query;
    }

    public string EscapeIdentifier(string identifier) => $"\"{identifier}\"";

    public void Dispose() { }
}

// Main export service
public class DatabaseExportService : IDisposable
{
    private readonly ExportConfiguration _config;
    private readonly ILogger<DatabaseExportService> _logger;
    private readonly IDatabaseConnection _databaseConnection;

    public DatabaseExportService(
        ExportConfiguration config,
        ILogger<DatabaseExportService> logger)
    {
        _config = config;
        _logger = logger;

        // Create appropriate database connection based on config
        _databaseConnection = config.DatabaseType switch
        {
            DatabaseType.MySQL => new MySqlDatabaseConnection(config.ConnectionString),
            DatabaseType.PostgreSQL => new PostgreSqlDatabaseConnection(config.ConnectionString),
            _ => throw new NotSupportedException($"Database type {config.DatabaseType} is not supported")
        };
    }

    public async Task<ExportResult> ExportTableToCsvAsync(TableExportConfig tableConfig)
    {
        _logger.LogInformation("Exporting table {TableName} from {DatabaseType} database",
            tableConfig.TableName, _config.DatabaseType);

        Directory.CreateDirectory(_config.TempDirectory);

        var fileName = GenerateFileName(tableConfig);
        var filePath = Path.Combine(_config.TempDirectory, fileName);

        using var connection = await _databaseConnection.CreateConnectionAsync();
        var query = _databaseConnection.BuildQuery(tableConfig, _config.DaysToExport);

        _logger.LogDebug("Generated query: {Query}", query);

        using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = 300; // 5 minutes timeout

        using var reader = await command.ExecuteReaderAsync();
        using var fileWriter = new StreamWriter(filePath);
        using var csvWriter = new CsvWriter(fileWriter, CultureInfo.InvariantCulture);

        // Find the date column index
        var dateColumnIndex = -1;
        for (int i = 0; i < reader.FieldCount; i++)
        {
            if (reader.GetName(i).Equals(tableConfig.DateColumn, StringComparison.OrdinalIgnoreCase))
            {
                dateColumnIndex = i;
                break;
            }
        }

        // Write headers
        for (int i = 0; i < reader.FieldCount; i++)
        {
            csvWriter.WriteField(reader.GetName(i));
        }
        await csvWriter.NextRecordAsync();

        // Track date range
        DateTime? minDate = null;
        DateTime? maxDate = null;
        var rowCount = 0;

        // Write data
        while (await reader.ReadAsync())
        {
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
                csvWriter.WriteField(value);
            }
            await csvWriter.NextRecordAsync();
            rowCount++;

            // Track date range if date column found
            if (dateColumnIndex >= 0 && !reader.IsDBNull(dateColumnIndex))
            {
                var dateString = reader.GetValue(dateColumnIndex)?.ToString();
                if (!string.IsNullOrEmpty(dateString))
                {
                    // Parse date strings in format: yyyy-mm-dd or yyyy-mm-dd hh:mm:ss+nn
                    if (DateTime.TryParse(dateString, out var dateValue))
                    {
                        if (minDate == null || dateValue.Date < minDate) minDate = dateValue.Date;
                        if (maxDate == null || dateValue.Date > maxDate) maxDate = dateValue.Date;
                    }
                }
            }

            if (rowCount % 10000 == 0)
            {
                _logger.LogInformation("Exported {RowCount} rows from {TableName}",
                    rowCount, tableConfig.TableName);
            }
        }

        await csvWriter.FlushAsync();
        _logger.LogInformation("Completed export of {TableName} with {RowCount} rows to {FilePath}",
            tableConfig.TableName, rowCount, fileName);

        return new ExportResult
        {
            FilePath = filePath,
            MinDate = minDate,
            MaxDate = maxDate,
            RowCount = rowCount,
            TableName = tableConfig.TableName,
            CustomerId = tableConfig.CustomerIds?.FirstOrDefault()
        };
    }

    private string GenerateFileName(TableExportConfig config)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var customerPart = config.CustomerIds?.Any() == true
            ? $"_{config.CustomerIds.First()}"
            : "";

        return $"{config.TableName}{customerPart}_{timestamp}.csv";
    }

    public void CleanupTempFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                _logger.LogDebug("Deleted temp file: {FilePath}", filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp file: {FilePath}", filePath);
        }
    }

    public void Dispose()
    {
        _databaseConnection?.Dispose();
    }
}
