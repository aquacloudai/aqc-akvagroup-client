# Database to S3 Daily Export Service

This .NET 9 service exports database tables (MySQL or PostgreSQL) to CSV files and uploads them to an S3 bucket with Keycloak authentication.

## Features

- Supports both MySQL and PostgreSQL databases
- Exports last 30 days of data from specified tables
- Filters by customer IDs if needed
- Authenticates via Keycloak to get temporary S3 credentials
- Handles large files with multipart upload
- Comprehensive logging and error handling
- Retry logic for network operations
- Cleanup of temporary files

## Requirements

- .NET 9.0 SDK or runtime
- MySQL 5.7+ or PostgreSQL 10+
- Access to S3-compatible storage
- Keycloak for authentication

## Setup Instructions

1. **Install .NET 9.0 SDK** if not already installed:
   ```bash
   # Windows (using winget)
   winget install Microsoft.DotNet.SDK.9

   # macOS
   brew install --cask dotnet-sdk

   # Linux (Ubuntu/Debian)
   wget https://dot.net/v1/dotnet-install.sh
   chmod +x dotnet-install.sh
   ./dotnet-install.sh --version 9.0.100
   ```

2. **Configure the service**:
   - Copy `appsettings.json` to your project directory
   - Update all configuration values for your environment

3. **Build the project**:
   ```bash
   dotnet build
   ```

4. **Run the service**:
   ```bash
   dotnet run
   ```

## Configuration Guide

### Database Connection
- `DatabaseType`: Either "MySQL" or "PostgreSQL"
- `ConnectionString`: Your database connection string
  - MySQL: `"Server=host;Database=db;Uid=user;Pwd=pass;"`
  - PostgreSQL: `"Host=host;Database=db;Username=user;Password=pass;"`

### Table Export Configuration
- `TableName`: Name of the table to export
- `Schema`: Database schema (PostgreSQL only, optional)
- `DateColumn`: Column used for date filtering (default: "created_at")
- `CustomerIdColumn`: Column containing customer IDs (optional)
- `CustomerIds`: List of customer IDs to filter (optional)
- `Columns`: Specific columns to export (null = all columns)

### Authentication
- `KeycloakUrl`: Your Keycloak server URL
- `Realm`: Keycloak realm name
- `ClientId`: Service account client ID
- `ClientSecret`: Service account client secret
- `CredentialEndpoint`: Your API endpoint that provides temporary S3 credentials

### S3 Configuration
- `BucketName`: Target S3 bucket
- `KeyPrefix`: Prefix for S3 keys (e.g., "akvagroup/fishtalk")
- `Region`: AWS region

## Example Configurations

### MySQL Configuration
```json
{
  "DatabaseType": "MySQL",
  "ConnectionString": "Server=mysql.example.com;Database=production;Uid=exporter;Pwd=SecurePass123!;",
   "Tables": [
        {
            "TableName": "status",
            "DateColumn": "date",
            "CustomerIdColumn": "owner_id",
            "CustomerIds": [
                "wen",
                "slx"
            ],
            "Columns": null,
            "Schema": "fishtalk"
        },
        {
            "TableName": "sensor",
            "DateColumn": "date",
            "CustomerIdColumn": "owner_id",
            "CustomerIds": [
                "wen",
                "slx"
            ],
            "Columns": null,
            "Schema": "fishtalk"
        },
        {
            "TableName": "mortality",
            "DateColumn": "date",
            "CustomerIdColumn": "owner_id",
            "CustomerIds": [
                "wen",
                "slx"
            ],
            "Columns": null,
            "Schema": "fishtalk"
        },
        {
            "TableName": "feeding",
            "DateColumn": "date",
            "CustomerIdColumn": "owner_id",
            "CustomerIds": [
                "wen",
                "slx"
            ],
            "Columns": null,
            "Schema": "fishtalk"
        },
        {
            "TableName": "culling",
            "DateColumn": "date",
            "CustomerIdColumn": "owner_id",
            "CustomerIds": [
                "wen",
                "slx"
            ],
            "Columns": null,
            "Schema": "fishtalk"
        },
        {
            "TableName": "lice",
            "DateColumn": "date",
            "CustomerIdColumn": "owner_id",
            "CustomerIds": [
                "wen",
                "slx"
            ],
            "Columns": null,
            "Schema": "fishtalk"
        },
        {
            "TableName": "treatment",
            "DateColumn": "date",
            "CustomerIdColumn": "owner_id",
            "CustomerIds": [
                "wen",
                "slx"
            ],
            "Columns": null,
            "Schema": "fishtalk"
        },
        {
            "TableName": "wrasse",
            "DateColumn": "date",
            "CustomerIdColumn": "owner_id",
            "CustomerIds": [
                "wen",
                "slx"
            ],
            "Columns": null,
            "Schema": "fishtalk"
        }
    ],
    "S3": {
        "BucketName": "aqc-supplier-upload",
        "KeyPrefix": "akvagroup/fishtalk/",
        "Region": "eu-north-1"
    },
    "DaysToExport": 30,
    "TempDirectory": "./temp",
    "Logging": {
        "LogLevel": {
            "Default": "Information",
            "Microsoft": "Warning"
        }
    }
}