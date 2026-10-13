namespace SandboxieMcp.Core;

public sealed class SandboxieException(string code, string message, object? details = null) : Exception(message)
{
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

public static class Messages
{
    private static readonly IReadOnlyDictionary<string, string> Russian = new Dictionary<string, string>
    {
        ["access_denied"] = "Операция запрещена политикой доступа.",
        ["not_found"] = "Объект не найден.",
        ["already_exists"] = "Объект с таким именем уже существует.",
        ["sandbox_busy"] = "Сначала завершите процессы в песочнице.",
        ["invalid_argument"] = "Некорректные параметры запроса.",
        ["native_error"] = "Sandboxie вернул ошибку; см. код состояния.",
        ["feature_unavailable"] = "Необходимая функция недоступна в текущей установке Sandboxie.",
        ["configuration_conflict"] = "Конфигурация изменилась. Повторно прочитайте её перед записью.",
        ["verification_failed"] = "Не удалось подтвердить результат операции.",
        ["unsafe_path"] = "Путь выходит за разрешённую область или содержит ссылку.",
        ["snapshot_in_use"] = "Снимок используется или имеет дочерние снимки.",
        ["timeout"] = "Время ожидания истекло; проверьте состояние операции.",
        ["io_error"] = "Ошибка доступа к файлу или компоненту Windows.",
        ["partial_failure"] = "Операция выполнена частично; см. завершённые шаги.",
        ["not_empty"] = "Песочница содержит данные; очистите её отдельной операцией.",
        ["not_initialized"] = "Запустите программу в песочнице перед созданием снимка.",
        ["cancelled"] = "Ожидание отменено; проверьте состояние уже начатых действий."
    };

    public static string Localize(string code, string english, string language) =>
        language == "ru" && Russian.TryGetValue(code, out var text) ? text : english;
}
