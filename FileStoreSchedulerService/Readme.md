# What is this

Run at Windows Service.

This application periodically scans files under the specified EntryDirectory (e.g., \*.ts) and moves them to the DestDirectory.

1. Preserves the relative structure of subdirectories.
1. Allows setting a pause period during which processing is skipped.
1. Runs from the Windows service version. Can also be used as a terminal.
1. Configuration is in JSON format (e.g., AppConfig.json).

---

## How to use

### Install .NET 10

[Download .NET](https://dotnet.microsoft.com/download)

### Console

Run the exe.

### Windows Service

Run this command in administrator terminal.

Install this app.

```bash
sc create FileStoreSchedulerService binPath= "C:\path\to\FileStoreSchedulerService.exe" start= auto
```

Service start.

```bash
sc start FileStoreSchedulerService
```

Uninstall.

```bash
sc delete FileStoreSchedulerService
```

### Configure service hosting account

At first, configured to "Local System" account.
If you are using a network drive, access permissions may be restricted.
In that case, adjust the service execution user.

---

## Configuration

Example.

### AppConfig.json

```json
{
  "AppDefinition": {
    "EntryDirectory": "D:\\Entry",
    "DestDirectory": "\\\\NAS\\Destination",
    "Patterns": ["*.ts"], // Multiple settings are possible
    "IntervalSeconds": 60, // Detection interval (seconds)
    "Recursive": true, // Include subdirectories
    "MoveRetryCount": 1,
    "MoveRetryDelayMs": 2000,
    "PausePeriods": [
      {
        // During the Pause
        "Start": "00:00",
        "End": "07:00"
      }
    ]
  }
}
```

---

© Ricewin 2026
