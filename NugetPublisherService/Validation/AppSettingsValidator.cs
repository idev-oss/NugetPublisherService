using Microsoft.Extensions.Options;

namespace NugetPublisherService.Validation
{
    /// <summary>
    /// Source-generated валидатор настроек. Атрибут [OptionsValidator] заставляет
    /// генератор кода рекурсивно применить DataAnnotations ко всем секциям
    /// (включая вложенные через [ValidateObjectMembers]).
    /// Существование сетевой папки BasePath здесь НЕ проверяется — шара может быть
    /// временно недоступна, и сервис не должен падать на старте; это проверяется в рантайме.
    /// </summary>
    [OptionsValidator]
    public sealed partial class AppSettingsValidator : IValidateOptions<AppSettings>
    {
    }
}
