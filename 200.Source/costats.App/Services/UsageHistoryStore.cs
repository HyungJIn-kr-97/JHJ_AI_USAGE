using System.IO;
using System.Text.Json;

namespace costats.App.Services;

/// <summary>
/// 일×모델 단위 사용량 한 건.
/// </summary>
public sealed record UsageHistoryEntry(DateOnly Day, string Model, decimal Cost, long Tokens);

/// <summary>
/// 일별 사용량을 계정마다 파일로 쌓아 둔다 — %LOCALAPPDATA%\AiUsageMonitor\history\.
/// 왜: Claude Code 가 오래된 대화 기록을 스스로 지우므로, 로그만 읽어서는 90일·1년 범위를 채울 수 없다.
/// 계약: 같은 (날짜, 모델)은 비용이 더 큰 쪽을 남긴다 — 로그가 일부 지워진 뒤의 작은 값이 옛 값을 덮지 못한다.
/// </summary>
public static class UsageHistoryStore
{
    private const int KeepDays = 400;

    private static readonly string RootDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageMonitor", "history");

    private static readonly Dictionary<string, Dictionary<(DateOnly, string), UsageHistoryEntry>> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<UsageHistoryEntry> Merge(string providerId, IEnumerable<UsageHistoryEntry> current)
    {
        var key = FileKey(providerId);
        if (!Cache.TryGetValue(key, out var known))
        {
            known = Load(key);
            Cache[key] = known;
        }

        var changed = false;
        foreach (var entry in current)
        {
            var id = (entry.Day, entry.Model);
            if (!known.TryGetValue(id, out var old) || entry.Cost > old.Cost || entry.Tokens > old.Tokens)
            {
                known[id] = entry;
                changed = true;
            }
        }

        var oldest = DateOnly.FromDateTime(DateTime.Now).AddDays(-KeepDays);
        foreach (var stale in known.Keys.Where(k => k.Item1 < oldest).ToList())
        {
            known.Remove(stale);
            changed = true;
        }

        if (changed)
        {
            Save(key, known.Values);
        }

        return known.Values.ToList();
    }

    // 왜: 계정 목록이 생기면 기본 계정의 ID 가 "claude" 에서 "claude:default" 로 바뀐다 — 같은 파일을 쓰게 맞춘다
    private static string FileKey(string providerId)
    {
        var id = providerId.Equals("claude:" + AccountProfileStore.DefaultName, StringComparison.OrdinalIgnoreCase)
            ? "claude"
            : providerId;
        return string.Concat(id.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
    }

    private static Dictionary<(DateOnly, string), UsageHistoryEntry> Load(string key)
    {
        var result = new Dictionary<(DateOnly, string), UsageHistoryEntry>();
        var path = Path.Combine(RootDir, key + ".json");
        try
        {
            if (File.Exists(path) &&
                JsonSerializer.Deserialize<List<UsageHistoryEntry>>(File.ReadAllText(path)) is { } list)
            {
                foreach (var entry in list)
                {
                    result[(entry.Day, entry.Model)] = entry;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // 깨진 이력은 버리고 지금 로그에서 다시 쌓는다
        }

        return result;
    }

    private static void Save(string key, IEnumerable<UsageHistoryEntry> entries)
    {
        try
        {
            Directory.CreateDirectory(RootDir);
            var ordered = entries.OrderBy(e => e.Day).ThenBy(e => e.Model, StringComparer.Ordinal).ToList();
            File.WriteAllText(Path.Combine(RootDir, key + ".json"), JsonSerializer.Serialize(ordered));
        }
        catch (IOException)
        {
            // 저장 실패는 화면 갱신을 막지 않는다 — 다음 갱신 때 다시 시도된다
        }
    }
}
