# Deployment Guide

## Windows Binary Installation

### Download Binary

1. Go to the [Releases page](https://github.com/aquacloudai/aqc-akvagroup/releases)
2. Download the latest `DatabaseToS3Exporter-windows-x64.zip`
3. Extract and install:

```powershell
# Create application directory
New-Item -ItemType Directory -Force -Path "C:\Program Files\DatabaseS3Exporter"

# Extract downloaded zip to the directory
Expand-Archive -Path "DatabaseToS3Exporter-windows-x64.zip" -DestinationPath "C:\Program Files\DatabaseS3Exporter"

# Copy configuration
Copy-Item "appsettings.json" -Destination "C:\Program Files\DatabaseS3Exporter\"
```

### Configuration

Configure your settings:

```powershell
notepad "C:\Program Files\DatabaseS3Exporter\appsettings.json"
```

### Add to PATH (Optional)

To run from anywhere:

```powershell
# Add to system PATH (requires admin)
$env:PATH += ";C:\Program Files\DatabaseS3Exporter"
```

## Windows Automated Daily Execution

### Option 1: Task Scheduler (Recommended)

Create a scheduled task using PowerShell (run as Administrator):

```powershell
$Action = New-ScheduledTaskAction -Execute "C:\Program Files\DatabaseS3Exporter\DatabaseToS3Exporter.exe"
$Trigger = New-ScheduledTaskTrigger -Daily -At "02:00"
$Principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName "DatabaseS3Export" -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings -Description "Daily database export to S3"
```

Or create manually via Task Scheduler GUI:

1. Open Task Scheduler (`taskschd.msc`)
2. Create Basic Task
3. Name: "DatabaseS3Export"
4. Trigger: Daily at 2:00 AM
5. Action: Start a program
6. Program: `C:\Program Files\DatabaseS3Exporter\DatabaseToS3Exporter.exe`
7. Start in: `C:\Program Files\DatabaseS3Exporter`

### Option 2: Windows Service

Install as a Windows service using `sc` command (run as Administrator):

```cmd
sc create DatabaseS3Export binPath= "C:\Program Files\DatabaseS3Exporter\DatabaseToS3Exporter.exe" start= auto
sc description DatabaseS3Export "Daily database export to S3 service"
```

Then create a scheduled task to start the service daily:

```powershell
$Action = New-ScheduledTaskAction -Execute "sc.exe" -Argument "start DatabaseS3Export"
$Trigger = New-ScheduledTaskTrigger -Daily -At "02:00"
Register-ScheduledTask -TaskName "DatabaseS3ExportTrigger" -Action $Action -Trigger $Trigger -Description "Trigger daily database export service"
```

## Windows Monitoring

### Check Task Scheduler Logs

```powershell
# View scheduled task history
Get-WinEvent -LogName "Microsoft-Windows-TaskScheduler/Operational" | Where-Object {$_.TaskDisplayName -eq "DatabaseS3Export"}

# View recent logs
Get-EventLog -LogName Application -Source "DatabaseS3Export" -Newest 10
```

### Check Service Status

```powershell
# Check if service is running
Get-Service -Name "DatabaseS3Export"

# Start service manually
Start-Service -Name "DatabaseS3Export"
```

### Manual Test Run

```powershell
# Run directly
& "C:\Program Files\DatabaseS3Exporter\DatabaseToS3Exporter.exe"

# Or run scheduled task manually
Start-ScheduledTask -TaskName "DatabaseS3Export"
```

## Windows Updating

To update to a new version:

```powershell
# Stop scheduled task
Disable-ScheduledTask -TaskName "DatabaseS3Export"

# Download and extract new version
Expand-Archive -Path "DatabaseToS3Exporter-windows-x64.zip" -DestinationPath "C:\Program Files\DatabaseS3Exporter" -Force

# Re-enable task
Enable-ScheduledTask -TaskName "DatabaseS3Export"
```

## Linux Binary Installation

### Download Binary

1. Go to the [Releases page](https://github.com/aquacloudai/aqc-akvagroup/releases)
2. Download the latest `DatabaseToS3Exporter-linux-x64.tar.gz`
3. Extract and install:

```bash
# Download and extract
wget https://github.com/aquacloudai/aqc-akvagroup/releases/latest/download/DatabaseToS3Exporter-linux-x64.tar.gz
tar -xzf DatabaseToS3Exporter-linux-x64.tar.gz

# Create application directory
sudo mkdir -p /opt/database-s3-exporter
sudo cp DatabaseToS3Exporter /opt/database-s3-exporter/
sudo cp appsettings.json /opt/database-s3-exporter/
sudo chmod +x /opt/database-s3-exporter/DatabaseToS3Exporter

# Create symlink for easy access
sudo ln -s /opt/database-s3-exporter/DatabaseToS3Exporter /usr/local/bin/database-s3-exporter
```

### Configuration

Copy and configure your settings:

```bash
sudo cp appsettings.json /opt/database-s3-exporter/
sudo nano /opt/database-s3-exporter/appsettings.json
```

## Automated Daily Execution

### Option 1: Systemd Service + Timer (Recommended)

Create a systemd service:

```bash
sudo tee /etc/systemd/system/database-s3-export.service > /dev/null <<EOF
[Unit]
Description=Database to S3 Export Service
After=network.target

[Service]
Type=oneshot
User=nobody
WorkingDirectory=/opt/database-s3-exporter
ExecStart=/opt/database-s3-exporter/DatabaseToS3Exporter
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
EOF
```

Create a systemd timer for daily execution:

```bash
sudo tee /etc/systemd/system/database-s3-export.timer > /dev/null <<EOF
[Unit]
Description=Run Database S3 Export Daily
Requires=database-s3-export.service

[Timer]
OnCalendar=daily
Persistent=true
RandomizedDelaySec=300

[Install]
WantedBy=timers.target
EOF
```

Enable and start the timer:

```bash
sudo systemctl daemon-reload
sudo systemctl enable database-s3-export.timer
sudo systemctl start database-s3-export.timer

# Check timer status
sudo systemctl status database-s3-export.timer
sudo systemctl list-timers database-s3-export.timer
```

### Option 2: Cron Job

Add to crontab for daily execution at 2 AM:

```bash
sudo crontab -e
```

Add this line:

```bash
0 2 * * * /opt/database-s3-exporter/DatabaseToS3Exporter >> /var/log/database-s3-export.log 2>&1
```

Or for a specific user:

```bash
crontab -e
```

Add:

```bash
0 2 * * * /opt/database-s3-exporter/DatabaseToS3Exporter >> $HOME/database-s3-export.log 2>&1
```

## Monitoring

### Check Service Logs (systemd)

```bash
# View recent logs
sudo journalctl -u database-s3-export.service -n 50

# Follow logs in real-time
sudo journalctl -u database-s3-export.service -f

# View timer logs
sudo journalctl -u database-s3-export.timer
```

### Check Cron Logs

```bash
# View cron logs
sudo tail -f /var/log/cron
grep database-s3-export /var/log/syslog
```

### Manual Test Run

```bash
# Test the service manually
sudo systemctl start database-s3-export.service

# Or run directly
/opt/database-s3-exporter/DatabaseToS3Exporter
```

## Updating

To update to a new version:

```bash
# Stop the timer/service
sudo systemctl stop database-s3-export.timer

# Download new binary
wget https://github.com/aquacloudai/aqc-akvagroup/releases/latest/download/DatabaseToS3Exporter-linux-x64.tar.gz
tar -xzf DatabaseToS3Exporter-linux-x64.tar.gz

# Replace binary
sudo cp DatabaseToS3Exporter /opt/database-s3-exporter/
sudo chmod +x /opt/database-s3-exporter/DatabaseToS3Exporter

# Restart timer
sudo systemctl start database-s3-export.timer
```

## Troubleshooting

### Common Issues

1. **Permission denied**: 
   - Linux: Ensure the binary has execute permissions (`chmod +x`)
   - Windows: Run PowerShell/Command Prompt as Administrator
2. **Configuration not found**: Check that `appsettings.json` is in the working directory
3. **Database connection issues**: Verify network connectivity and credentials
4. **S3 upload failures**: Check AWS credentials and bucket permissions
5. **Windows service issues**: Ensure the binary supports running as a service

### Log Locations

**Linux:**
- Systemd: `journalctl -u database-s3-export.service`
- Application logs: `/opt/database-s3-exporter/logs/`
- Cron logs: `/var/log/cron` or user-specific log file

**Windows:**
- Task Scheduler logs: Event Viewer → Windows Logs → System
- Application logs: `C:\Program Files\DatabaseS3Exporter\logs\`
- Windows Event Logs: Event Viewer → Windows Logs → Application