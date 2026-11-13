using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimsModManager.Core.Helper;

/// <summary>Где сейчас игра, по словам помощника.</summary>
public enum HelperState
{
    /// <summary>Помощник не отвечает: игра закрыта, скрипты выключены или помощник не установлен.</summary>
    Offline,
    Starting,
    /// <summary>Главное меню — сохранение не открыто.</summary>
    Menu,
    /// <summary>Сохранение открыто, можно выполнять команды.</summary>
    Live,
}

public sealed record HelperStatus(HelperState State, long? SlotId, string? SlotName, int Version, DateTime UpdatedAt)
{
    /// <summary>Окно «Вернуться к точке?» в игре: null, "shown", "accepted" или "cancelled".</summary>
    public string? Dialog { get; init; }


    public static readonly HelperStatus Offline = new(HelperState.Offline, null, null, 0, DateTime.MinValue);
}

public sealed record HelperResponse(bool Ok, JsonNode? Result, string? Error)
{
    public static HelperResponse Fail(string error) => new(false, null, error);
}

/// <summary>
/// Связь с помощником через файлы в папке bridge. Каждая команда ждёт ответа не дольше таймаута —
/// программа никогда не зависает из-за игры.
/// </summary>
public sealed class HelperBridge(string bridgeFolder)
{
    /// <summary>Помощник пишет status.json раз в секунду; старше этого — считаем, что игры нет.</summary>
    public static readonly TimeSpan AliveWindow = TimeSpan.FromSeconds(4);

    public string Folder { get; } = bridgeFolder;

    public HelperStatus ReadStatus(DateTime? nowUtc = null)
    {
        var path = Path.Combine(Folder, "status.json");
        try
        {
            if (!File.Exists(path)) return HelperStatus.Offline;
            var json = JsonNode.Parse(File.ReadAllText(path));
            if (json is null) return HelperStatus.Offline;

            var updated = DateTimeOffset.FromUnixTimeMilliseconds((long)(json["time"]!.GetValue<double>() * 1000)).UtcDateTime;
            if ((nowUtc ?? DateTime.UtcNow) - updated > AliveWindow) return HelperStatus.Offline;

            var state = (string?)json["state"] switch
            {
                "live" => HelperState.Live,
                "menu" => HelperState.Menu,
                _ => HelperState.Starting,
            };
            return new HelperStatus(
                state,
                json["slot_id"] is JsonValue id ? id.GetValue<long>() : null,
                (string?)json["slot_name"],
                (int?)json["version"] ?? 0,
                updated)
            {
                Dialog = (string?)json["dialog"],
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or InvalidOperationException or FormatException or NullReferenceException)
        {
            // Файл как раз перезаписывается — в следующий раз прочитаем
            return HelperStatus.Offline;
        }
    }

    /// <summary>Отправляет команду и ждёт ответа. Никогда не бросает исключений — ошибки приходят в ответе.</summary>
    public async Task<HelperResponse> SendAsync(string command, TimeSpan timeout,
        IReadOnlyDictionary<string, object?>? args = null, CancellationToken cancellation = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var cmdPath = Path.Combine(Folder, $"cmd-{id}.json");
        var resPath = Path.Combine(Folder, $"res-{id}.json");
        try
        {
            Directory.CreateDirectory(Folder);
            var payload = new JsonObject
            {
                ["id"] = id,
                ["command"] = command,
                ["time"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0,
                ["ttl"] = timeout.TotalSeconds,
            };
            if (args is not null)
                foreach (var (key, value) in args)
                    payload[key] = JsonSerializer.SerializeToNode(value);

            // Сначала во временный файл, потом переименование — помощник никогда не увидит половину команды
            var temp = cmdPath + ".tmp";
            await File.WriteAllTextAsync(temp, payload.ToJsonString(), cancellation);
            File.Move(temp, cmdPath, overwrite: true);

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(resPath))
                {
                    var response = TryReadResponse(resPath);
                    if (response is not null) return response;
                }
                await Task.Delay(50, cancellation);
            }
            return HelperResponse.Fail(L.T("Игра не ответила"));
        }
        catch (OperationCanceledException)
        {
            return HelperResponse.Fail(L.T("Отменено"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return HelperResponse.Fail(ex.Message);
        }
        finally
        {
            TryDelete(cmdPath);
            TryDelete(resPath);
        }
    }

    private static HelperResponse? TryReadResponse(string path)
    {
        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path));
            if (json is null) return null;
            return new HelperResponse(
                json["ok"]?.GetValue<bool>() ?? false,
                json["result"]?.DeepClone(),
                (string?)json["error"]);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
