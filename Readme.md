# NugetPublisherService

<div align="center">
  <img src="NugetPublisherService/NugetPublisherService.ico" alt="NugetPublisherService Icon" width="64" height="64">
</div>


## Description

NugetPublisherService is an automated service for publishing NuGet packages to a private GitLab repository. The service runs in background mode, regularly scans specified directories for new packages and publishes them to the GitLab NuGet repository.

## Key Features

- **Fast targeted scanning**: Direct traversal of the known folder structure (`year → date → leaf`) instead of recursing the whole share; only a window of recent days is processed
- **Local SQLite cache**: Already-processed packages are skipped without reading the `.nupkg` or calling GitLab; dedup survives service restarts
- **Smart scheduling**: Different, configurable scanning intervals for business and non-business hours
- **Duplicate checking**: Prevention of repeated publications of already existing packages
- **Email notifications**: Reports with three statuses (Published / Failed / Skipped in DryRun)
- **Spam-free error alerts**: A publish-failure email is sent once when `FailureAlertThreshold` is reached; administrators (`Smtp.Admin`) get the details, regular recipients are asked to contact the administrator
- **Authorization failure handling**: On HTTP 401/403 from GitLab the service does not retry uselessly — it notifies only the administrator and shuts down gracefully (restart the service after fixing the token)
- **Dry Run mode**: Ability to test service operation without actual package publication

## How It Works

1. The service builds paths directly from the `BasePath\{year}\{date}\{leaf}` structure and scans only date folders within the `LookbackDays` window
2. Each `.nupkg` is checked against the local SQLite cache; already published ones are skipped
3. Packages unknown to the cache are checked once against GitLab (cache seeding); new ones are published through the NuGet API
4. Sends an email report about publication results

## Configuration

The service is configured through `appsettings.json`. A documented template is available at [appsettings.example.json](NugetPublisherService/appsettings.example.json).

```jsonc
{
  "Scan": {
    "BasePath": "\\\\nas\\app\\update",   // root folder (network share)
    "YearFolderFormat": "_yyyy",            // year folder name: _2026
    "DateFolderFormat": "yyyy-MM-dd",       // date folder name: 2026-05-18
    "LeafRelativePath": "Refactor\\NugetSource", // subpath to the .nupkg files
    "LookbackDays": 60,                     // scan window (previous + current month)
    "IncludePreviousYearFolder": true,      // include previous year at year boundary (January)
    "ScanIntervalMinutes": 10,              // interval during working hours
    "WorkingHourStart": 9,                  // working hours start
    "WorkingHourEnd": 21,                   // working hours end
    "OffHoursIntervalHours": 2              // interval off-hours and on weekends
  },
  "GitLab": {
    "BaseUrl": "http://git.example.local/api/v4",
    "ProjectId": 11,
    "PrivateToken": "access_token"
  },
  "Smtp": {
    "Server": "smtp.example.com",
    "Port": 465,
    "UseSsl": true,
    "Username": "username",
    "Password": "password",
    "From": "sender@example.com",
    "To": ["recipient1@example.com", "recipient2@example.com"],
    "Admin": ["admin@example.com"]          // recipients of error details
  },
  "State": {
    "DatabasePath": "state.db"              // local SQLite cache file (next to .exe)
  },
  "DryRun": false,
  "FailureAlertThreshold": 5                 // send error email after N failed cycles (once)
}
```

> Resulting scan path: `BasePath\_2026\2026-05-18\Refactor\NugetSource\*.nupkg`.
> The folder structure is configurable via `YearFolderFormat` / `DateFolderFormat` / `LeafRelativePath`.

Configuration is validated at startup (fail-fast): with invalid values the service won't start and
prints a clear error. The existence of the network `BasePath` is checked at runtime so a temporarily
unavailable share does not crash the service.
## Installation

1. Clone the repository
2. Configure the `appsettings.json` file with your settings
3. Build and run the service

**Windows Service:** For production deployment, the application can be installed as a Windows Service using the provided [install_service.ps1](NugetPublisherService/install_service.ps1) script.

## Requirements

- .NET 10.0
- Access to GitLab repository with NuGet package registry
- SMTP server for email notifications (optional)

## License

This project is licensed under the terms specified in the LICENSE file.

<div align="center">
  <a href="Readme.ru.md">
    <img src="https://img.shields.io/badge/🇷🇺-Русская_версия-success?style=flat&color=4CAF50" alt="Русская версия">
  </a>
</div>