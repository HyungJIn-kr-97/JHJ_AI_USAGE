using System.Text.Json;

namespace costats.Infrastructure.Providers;

/// <summary>
/// 「프로그램 연동」의 시간 이력 — 프로그램(entrypoint)마다 어느 계정에 붙어 있었는지를 바뀐 시각과 함께 쌓는다.
/// 왜: 한 PC 에서 계정을 갈아 가며 쓰므로, 표 한 칸을 바꾸면 지난 기록까지 새 주인을 따라가 집계가 흔들렸다 — 줄의 시각으로 그때의 주인을 찾는다.
/// 계약: 값은 계정 UUID 다 — 자리 이름(providerId)이 바뀌어도 같은 계정이 돌아오면 이력이 이어진다.
/// 계약: 저장은 %LOCALAPPDATA%\JHJ_AI-Usage-Monitor\program-links.json — 지우지 않고 쌓기만 한다.
/// 함정: 이력이 시작되기 전 줄은 여기에 답이 없다 — 그때는 옛 「프로그램 연동」 표(AppSettings.ProgramAccounts)를 따른다.
/// 계약: 주인 판정 순서·형식의 정본은 300.Docs/프로그램-연동-규칙.md 다.
/// </summary>
public static class ClaudeProgramLinks
{
    /// <param name="From">이 계정으로 바뀐 시각(UTC) — 다음 줄이 나올 때까지 유효하다.</param>
    /// <param name="DeviceId">이 줄을 남긴 장비의 ID — 장비 정보가 생기기 전의 옛 줄은 null.</param>
    /// <param name="DeviceName">그때의 장비 명칭(사용자 지정, 없으면 PC 이름).</param>
    public sealed record Entry(string Program, string Account, DateTimeOffset From, string? DeviceId = null, string? DeviceName = null);

    // 계약: 앱 층이 시작할 때 꽂는다 — 비어 있으면 줄에 장비를 남기지 않는다
    public static string? DeviceId { get; set; }

    public static string? DeviceName { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JHJ_AI-Usage-Monitor", "program-links.json");

    // 왜: 한 번 바꿀 때마다 한 줄이 는다 — 몇 년을 써도 넘지 않을 선에서만 잘라 파일이 무한히 자라지 않게 한다
    private const int MaxEntries = 4000;

    private static readonly object Gate = new();
    private static List<Entry>? _entries;

    // 왜: 로그 줄마다 불리는 길이다 — 쓰는 쪽만 잠그고 읽는 쪽은 통째로 바꿔 끼운 표를 본다
    private static IReadOnlyDictionary<string, Entry[]> _index = new Dictionary<string, Entry[]>(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    /// <summary>그 시각에 이 프로그램이 붙어 있던 계정 UUID. at 이 null 이면 지금 값. 이력 시작 전이면 null.</summary>
    public static string? AccountAt(string program, DateTimeOffset? at)
    {
        EnsureLoaded();
        if (!Volatile.Read(ref _index).TryGetValue(program, out var entries))
        {
            return null;
        }

        for (var i = entries.Length - 1; i >= 0; i--)
        {
            if (at is null || entries[i].From <= at)
            {
                return entries[i].Account;
            }
        }

        return null;
    }

    /// <summary>지금 이 프로그램이 붙어 있는 계정 UUID.</summary>
    public static string? CurrentAccount(string program) => AccountAt(program, null);

    /// <summary>
    /// 지금의 연동을 한 줄 남긴다 — 직전과 같으면 쓰지 않는다.
    /// 계약: account 가 null(로그아웃·읽기 실패)이면 아무것도 남기지 않는다 — 같은 계정으로 다시 로그인하면 그대로 이어진다.
    /// </summary>
    public static void Record(string program, string? account, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return;
        }

        lock (Gate)
        {
            EnsureLoadedLocked();
            if (CurrentAccount(program) is { } last && last.Equals(account, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _entries!.Add(new Entry(program, account, at.ToUniversalTime(),
                string.IsNullOrEmpty(DeviceId) ? null : DeviceId, string.IsNullOrEmpty(DeviceName) ? null : DeviceName));
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }

            Reindex();
            Save(_entries);
        }
    }

    /// <summary>진단 보고서용 — 쌓인 줄 전부. UUID 가림은 호출자가 한다.</summary>
    public static IReadOnlyList<Entry> All()
    {
        EnsureLoaded();
        lock (Gate)
        {
            return _entries!.ToList();
        }
    }

    private static void EnsureLoaded()
    {
        if (Volatile.Read(ref _loaded))
        {
            return;
        }

        lock (Gate)
        {
            EnsureLoadedLocked();
        }
    }

    private static void EnsureLoadedLocked()
    {
        if (_loaded)
        {
            return;
        }

        _entries = Load();
        Reindex();
        Volatile.Write(ref _loaded, true);
    }

    // 계약: 프로그램마다 시각 오름차순 배열 — AccountAt 이 뒤에서부터 훑어 그 시각에 유효한 줄을 고른다
    private static void Reindex() => Volatile.Write(ref _index, _entries!
        .GroupBy(e => e.Program, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.OrderBy(e => e.From).ToArray(), StringComparer.OrdinalIgnoreCase));

    private static List<Entry> Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(FilePath)) is { } loaded)
            {
                return loaded
                    .Where(e => !string.IsNullOrWhiteSpace(e.Program) && !string.IsNullOrWhiteSpace(e.Account))
                    .OrderBy(e => e.From).ToList();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // 깨진 파일은 빈 이력으로 시작한다 — 지난 줄은 옛 「프로그램 연동」 표를 따른다
        }

        return [];
    }

    private static void Save(List<Entry> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 저장 실패는 집계를 막지 않는다 — 다음 갱신에 다시 남긴다
        }
    }
}
