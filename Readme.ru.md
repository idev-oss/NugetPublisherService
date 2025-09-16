# NugetPublisherService

<div align="center">
  <img src="NugetPublisherService/NugetPublisherService.ico" alt="NugetPublisherService Icon" width="64" height="64">
</div>

## Описание

NugetPublisherService - это автоматизированный сервис для публикации NuGet-пакетов в приватном репозитории GitLab. Сервис работает в фоновом режиме, регулярно сканирует указанную директорию на наличие новых пакетов и выполняет их публикацию в репозиторий GitLab NuGet.

## Ключевые возможности

- **Автоматическое сканирование**: Регулярный поиск новых NuGet-пакетов в указанных директориях
- **Интеллектуальное расписание**: Разные интервалы сканирования в рабочее и нерабочее время
- **Проверка дубликатов**: Предотвращение повторных публикаций уже существующих пакетов
- **Email-уведомления**: Автоматическая отправка отчетов о публикации по электронной почте
- **Режим "Dry Run"**: Возможность тестировать работу сервиса без фактической публикации пакетов

## Принцип работы

1. Сервис просматривает указанные директории на наличие .nupkg файлов
2. Проверяет, был ли каждый пакет уже опубликован в репозитории GitLab
3. Публикует новые пакеты через NuGet API
4. Отправляет email-отчет о результатах публикации

## Конфигурация

Сервис настраивается через файл `appsettings.json` со следующими параметрами:

```json
{
  "Scan": {
    "BasePath": "путь/к/директории/с/пакетами",
    "PathPatternRegex": "регулярное_выражение_для_поиска_директорий",
    "ScanIntervalMinutes": 20
  },
  "GitLab": {
    "BaseUrl": "https://gitlab.example.com/api/v4",
    "ProjectId": "идентификатор_проекта",
    "PrivateToken": "токен_доступа"
  },
  "Smtp": {
    "Server": "smtp.example.com",
    "Port": 587,
    "UseSsl": true,
    "Username": "username",
    "Password": "password",
    "From": "sender@example.com",
    "To": ["recipient1@example.com", "recipient2@example.com"]
  },
  "DryRun": false
}
```

## Установка

1. Клонируйте репозиторий
2. Настройте файл `appsettings.json` согласно вашим параметрам
3. Соберите и запустите сервис

**Служба Windows:** Для продакшн развертывания приложение может быть установлено как служба Windows с использованием предоставленного скрипта [install_service.ps1](NugetPublisherService/install_service.ps1).

## Требования

- .NET 8.0
- Доступ к репозиторию GitLab с поддержкой NuGet package registry
- SMTP сервер для email-уведомлений (опционально)

## Лицензия

Этот проект лицензирован согласно условиям, указанным в файле LICENSE.

<div align="center">
  <a href="Readme.md">
    <img src="https://img.shields.io/badge/🇺🇸-English_version-success?style=flat&color=2196F3" alt="English version">
  </a>
</div>