# install_service.ps1 - PowerShell script to install Windows Service
# Template for installing NugetPublisherService as Windows Service

# Service configuration - modify paths for your environment
$serviceName = "NugetPublisherService"
$serviceDisplayName = "NuGet Publisher Service"
$exePath = "C:\Services\NugetPublisherService\NugetPublisherService.exe"
$workingDir = "C:\Services\NugetPublisherService"
$serviceDescription = "Automated NuGet package publishing service for GitLab NuGet Registry"

Write-Host "→ Installing service: $serviceName..."

# Check if service already exists
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Write-Host "⚠️ Service $serviceName already exists. Stopping and removing..."
    Stop-Service -Name $serviceName -Force
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

# Create the service
Write-Host "Creating service..."
sc.exe create $serviceName binPath= "`"$exePath`"" start= auto DisplayName= "`"$serviceDisplayName`""
sc.exe description $serviceName "$serviceDescription"
Start-Sleep -Seconds 1

# Configure automatic recovery on failure
Write-Host "Configuring failure recovery..."
sc.exe failure $serviceName reset= 0 actions= restart/5000

# Start the service
Write-Host "Starting service..."
Start-Service -Name $serviceName

Write-Host "✅ Service $serviceName has been installed and started successfully."
