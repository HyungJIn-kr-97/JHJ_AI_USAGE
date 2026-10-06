using System.IO;
using System.Text.Json;

namespace costats.App.Services;

/// <summary>
/// 일×모델 단위 사용량 한 건.
/// </summary>
/// 계약: Input~CacheWrite 는 토큰 유형별 몫이다 — 이 칸이 생기기 전에 쌓인 날은 전부 0 이고, 그 차이(Tokens − 합)가 "유형 미상"이다.
public sealed record UsageHistoryEntry(
    DateOnly Day, string Model, decimal Cost, long Tokens,
    long Input = 0, long Output = 0, long CacheRead = 0, long CacheWrite = 0)
{
    public long TypedTokens => Input + Output + CacheRead + CacheWrite;
}

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

    public static IReadOnlyList<UsageHistoryEntry> Merge(string providerId, IEnumerable<UsageHistoryEntry> current) =>
        MergeKey(FileKey(providerId), current);

    // 계약: Claude 기본 폴더의 기록은 계정이 아니라 프로그램(entrypoint)마다 쌓는다 — 연동을 바꾸면 옛 이력이 통째로 새 주인을 따라간다
    public static IReadOnlyList<UsageHistoryEntry> MergeProgram(string program, IEnumerable<UsageHistoryEntry> current) =>
        MergeKey(ProgramKey(program), current);

    // 계약: 지금까지 쌓인 프로그램 이름 전부 — 이번 갱신에 로그가 없던 프로그램도 들어 있다
    public static IReadOnlyList<string> Programs()
    {
        try
        {
            var dir = Path.Combine(RootDir, ProgramDir);
            return Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.json").Select(path => Path.GetFileNameWithoutExtension(path)).ToList()
                : [];
        }
        catch (IOException)
        {
            return [];
        }
    }

    private const string ProgramDir = "claude-program";

    // 함정: '@' 를 남긴다 — "claude-desktop@<계정UUID>" 가 파일 이름에서 바뀌면 Programs() 로 되읽을 때 주인을 못 찾는다
    private static string ProgramKey(string program) =>
        Path.Combine(ProgramDir, string.Concat(program.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '@' ? ch : '_')));

    private static IReadOnlyList<UsageHistoryEntry> MergeKey(string key, IEnumerable<UsageHistoryEntry> current)
    {
        if (!Cache.TryGetValue(key, out var known))
        {
            known = Load(key);
            Cache[key] = known;
        }

        var changed = false;
        var list = current as IReadOnlyCollection<UsageHistoryEntry> ?? current.ToList();
        // 왜: 날짜 기준이 UTC → 현지로 바뀌었다 — 옛 UTC 날짜 값이 「큰 값 남기기」로 남지 않게, 키마다 한 번 로그가 있는 날부터를 새 값으로 갈아 낀다
        if (list.Count > 0 && Rebased.Add(key))
        {
            var from = list.Min(e => e.Day).AddDays(-1);
            foreach (var stale in known.Keys.Where(k => k.Item1 >= from).ToList())
            {
                known.Remove(stale);
            }

            changed = true;
            SaveRebased();
        }

        foreach (var entry in list)
        {
            var id = (entry.Day, entry.Model);
            // 왜: 유형 칸이 없던 옛 기록은 같은 값의 새 기록으로 갈아야 유형 통계가 채워진다 — 값이 줄어든 기록으로는 갈지 않는다
            var fillsTypes = known.TryGetValue(id, out var old) && old.TypedTokens == 0 && entry.TypedTokens > 0 &&
                             entry.Cost >= old.Cost && entry.Tokens >= old.Tokens;
            if (old is null || entry.Cost > old.Cost || entry.Tokens > old.Tokens || fillsTypes)
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

    // 계약: 현지 날짜 기준으로 갈아 낀 이력 키 목록 — history\local-days.txt
    private static readonly string RebasedPath = Path.Combine(RootDir, "local-days.txt");

    private static readonly HashSet<string> Rebased = LoadRebased();

    private static HashSet<string> LoadRebased()
    {
        try
        {
            return File.Exists(RebasedPath)
                ? new HashSet<string>(File.ReadAllLines(RebasedPath), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveRebased()
    {
        try
        {
            Directory.CreateDirectory(RootDir);
            File.WriteAllLines(RebasedPath, Rebased.OrderBy(k => k, StringComparer.Ordinal));
        }
        catch (IOException)
        {
            // 저장 실패면 다음 실행에 한 번 더 갈아 낀다 — 같은 로그로 다시 채우므로 값은 같다
        }
    }

    // 왜: 계정 목록이 생기면 기본 계정의 ID 가 "claude" 에서 "claude:default" 로 바뀐다 — 같은 파일을 쓰게 맞춘다
    private static string FileKey(string providerId)
    {
        var id = providerId.Equals("claude:" + AccountProfileStore.DefaultName, StringComparison.OrdinalIgnoreCase)
            ? "claude"
            : providerId;
        return Sanitize(id);
    }

    private static string Sanitize(string id) =>
        string.Concat(id.Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

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
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(RootDir, key))!);
            var ordered = entries.OrderBy(e => e.Day).ThenBy(e => e.Model, StringComparer.Ordinal).ToList();
            File.WriteAllText(Path.Combine(RootDir, key + ".json"), JsonSerializer.Serialize(ordered));
        }
        catch (IOException)
        {
            // 저장 실패는 화면 갱신을 막지 않는다 — 다음 갱신 때 다시 시도된다
        }
    }
}
