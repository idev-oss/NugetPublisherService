# install_service.ps1
$serviceName = "NugetPublisherService"
$serviceDisplayName = "NuGet Publisher Service"
$exePath = "C:\CI_CD\NugetPublisherService\NugetPublisherService.exe"
$workingDir = "C:\CI_CD\NugetPublisherService"

Write-Host "→ Установка службы $serviceName..."

# Проверяем, существует ли уже служба
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    Write-Host "⚠️ Служба $serviceName уже существует. Останавливаем и удаляем..."
    Stop-Service -Name $serviceName -Force
    sc.exe delete $serviceName | Out-Null
    Start-Sleep -Seconds 2
}

# Создаём службу
sc.exe create $serviceName binPath= "`"$exePath`"" start= auto DisplayName= "`"$serviceDisplayName`""
sc.exe description $serviceName "Автоматическая публикация NuGet-пакетов через GitLab NuGet Registry"
Start-Sleep -Seconds 1

# Настройка автоматического восстановления при сбое
sc.exe failure $serviceName reset= 0 actions= restart/5000

# Стартуем службу
Start-Service -Name $serviceName

Write-Host "✅ Служба $serviceName установлена и запущена."
