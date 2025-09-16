# NugetPublisherService

<div align="center">
  <img src="NugetPublisherService/NugetPublisherService.ico" alt="NugetPublisherService Icon" width="64" height="64">
</div>


## Description

NugetPublisherService is an automated service for publishing NuGet packages to a private GitLab repository. The service runs in background mode, regularly scans specified directories for new packages and publishes them to the GitLab NuGet repository.

## Key Features

- **Automatic scanning**: Regular search for new NuGet packages in specified directories
- **Smart scheduling**: Different scanning intervals for business and non-business hours
- **Duplicate checking**: Prevention of repeated publications of already existing packages
- **Email notifications**: Automatic sending of publication reports via email
- **Dry Run mode**: Ability to test service operation without actual package publication

## How It Works

1. The service scans specified directories for .nupkg files
2. Checks if each package has already been published to the GitLab repository
3. Publishes new packages through NuGet API
4. Sends email report about publication results

## Configuration

The service is configured through the `appsettings.json` file with the following parameters:

```json
{
  "Scan": {
    "BasePath": "path/to/packages/directory",
    "PathPatternRegex": "regex_pattern_for_directory_search",
    "ScanIntervalMinutes": 20
  },
  "GitLab": {
    "BaseUrl": "https://gitlab.example.com/api/v4",
    "ProjectId": "project_identifier",
    "PrivateToken": "access_token"
  },
  "Smtp": {
    "Server": "smtp.example.com",
    "Port": 587,
    "UseSsl": true,
    "Username": "username",
    "Password": "password",
    "From": "sender@example.com",
    "To": [
      "recipient1@example.com",
      "recipient2@example.com"
    ]
  },
  "DryRun": false
}
```
## Installation

1. Clone the repository
2. Configure the `appsettings.json` file with your settings
3. Build and run the service

## Requirements

- .NET 8.0
- Access to GitLab repository with NuGet package registry
- SMTP server for email notifications (optional)

## License

This project is licensed under the terms specified in the LICENSE file.

<div align="center">
  <a href="Readme.ru.md">
    <img src="https://img.shields.io/badge/🇷🇺-Русская_версия-success?style=flat&color=4CAF50" alt="Русская версия">
  </a>
</div>