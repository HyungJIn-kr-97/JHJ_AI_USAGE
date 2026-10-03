using System.Text.Json;
using costats.Application.Pulse;
using costats.Application.Settings;
using costats.Core.Pulse;
using costats.Infrastructure.Expense;
using static costats.Core.Pulse.UsageFormatter;

namespace costats.Infrastructure.Providers;

/// <summary>
/// Gemini CLI 사용량 — 한도는 GeminiQuotaFetcher, 일별 토큰·비용은 ~/.gemini/tmp/&lt;프로젝트&gt;/chats/session-* 에서 읽는다.
/// 계약: 화면의 두 막대는 Pro 계열 / Flash 계열의 일일 한도다(세션·주간이 아니다).
/// 함정: 세션 파일은 v0.38 까지 통짜 json, v0.39 부터 jsonl(같은 id 는 마지막 줄이 이긴다)이라 둘 다 읽는다.
/// 함정: CLI 가 30일 지난 세션을 스스로 지운다 — 그 이전 날짜는 앱의 history 폴더에 쌓인 것만 남는다.
/// 함정: 무료·Code Assist 사용분은 토큰당 과금이 아니다 — 비용은 API 단가로 환산한 추정이다.
/// </summary>
public sealed class GeminiLogSource : ISignalSource
{
    private const int WindowDays = 365;
    private const string DefaultModel = "gemini-2.5-pro";

    private readonly AppSettings _settings;
    private readonly GeminiQuotaFetcher _quotaFetcher = new();
    private readonly ExpenseAnalyzer _expenseAnalyzer = new();

    public GeminiLogSource(AppSettings settings)
    {
        _settings = settings;
    }

    public ProviderProfile Profile => ProviderCatalog.Gemini;

    public async Task<ProviderReading> ReadAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (!_settings.GeminiEnabled)
        {
            return Empty(now, "Gemini disabled in Settings");
        }

        var quotaTask = _quotaFetcher.FetchAsync(cancellationToken);
        var consumption = await SafeDigestAsync(cancellationToken).ConfigureAwait(false);
        var quota = await quotaTask.ConfigureAwait(false);

        if (quota is null && consumption.DailyBreakdown.Count == 0)
        {
            return Empty(now, "No Gemini usage data available");
        }

        var usage = new UsagePulse(
            ProviderId: Profile.ProviderId,
            CapturedAt: quota?.FetchedAt ?? now,
            SessionUsed: quota?.Pro is { } pro ? (long)Math.Round(pro.UsedPercent) : null,
            SessionLimit: quota?.Pro is null ? null : 100,
            WeekUsed: quota?.Flash is { } flash ? (long)Math.Round(flash.UsedPercent) : null,
            WeekLimit: quota?.Flash is null ? null : 100,
            SpendingBucket: null,
            Consumption: consumption,
            SessionWindow: new QuotaWindow(TimeSpan.FromDays(1), quota?.Pro?.ResetsAt),
            WeekWindow: new QuotaWindow(TimeSpan.FromDays(1), quota?.Flash?.ResetsAt));

        return new ProviderReading(
            Usage: usage,
            Identity: new IdentityCard(Profile.ProviderId, Profile.DisplayName, null, null, PlanText(quota?.Tier), "OAuth"),
            StatusSummary: $"Updated {FormatRelativeTime(usage.CapturedAt, now)}",
            CapturedAt: usage.CapturedAt,
            Confidence: quota is not null ? ReadingConfidence.High : ReadingConfidence.Medium,
            Source: quota is not null ? ReadingSource.Api : ReadingSource.LocalLog);
    }

    private ProviderReading Empty(DateTimeOffset now, string summary) => new(
        Usage: null,
        Identity: new IdentityCard(Profile.ProviderId, Profile.DisplayName, null, null, "Gemini", "OAuth"),
        StatusSummary: summary,
        CapturedAt: now,
        Confidence: ReadingConfidence.Low,
        Source: ReadingSource.LocalLog);

    private static string PlanText(string? tier) => tier switch
    {
        null or "" => "Logs",
        "free-tier" => "Free",
        "legacy-tier" => "Legacy",
        "standard-tier" => "Standard",
        _ => char.ToUpperInvariant(tier[0]) + tier[1..].Replace("-tier", string.Empty)
    };

    private async Task<ConsumptionDigest> SafeDigestAsync(CancellationToken cancellationToken)
    {
        try
        {
            var slices = await Task.Run(() => DigestSessions(cancellationToken), cancellationToken).ConfigureAwait(false);
            return _expenseAnalyzer.FromSlices(slices);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return ConsumptionDigest.None;
        }
    }

    private static IReadOnlyList<ConsumptionSlice> DigestSessions(CancellationToken cancellationToken)
    {
        var tmp = Path.Combine(GeminiQuotaFetcher.GeminiHome, "tmp");
        if (!Directory.Exists(tmp))
        {
            return [];
        }

        var since = DateOnly.FromDateTime(DateTime.Now).AddDays(-(WindowDays - 1));
        var totals = new Dictionary<(DateOnly Day, string Model), (long Input, long Cached, long Output)>();

        foreach (var chats in Directory.EnumerateDirectories(tmp).Select(dir => Path.Combine(dir, "chats")).Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(chats, "session-*.json*"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    foreach (var message in ReadMessages(file))
                    {
                        Accumulate(message, since, totals);
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    // 쓰는 중이거나 깨진 세션 파일 하나가 전체 집계를 막지 않게 건너뛴다
                }
            }
        }

        return totals
            .Select(pair =>
            {
                var ledger = new TokenLedger
                {
                    StandardInput = Clamp(pair.Value.Input),
                    CachedInput = Clamp(pair.Value.Cached),
                    GeneratedOutput = Clamp(pair.Value.Output)
                };
                return new ConsumptionSlice
                {
                    Period = pair.Key.Day,
                    ModelIdentifier = pair.Key.Model,
                    Tokens = ledger,
                    ComputedCostUsd = TariffRegistry.FindGeminiRate(pair.Key.Model).ComputeCost(ledger)
                };
            })
            .OrderByDescending(slice => slice.Period)
            .ThenBy(slice => slice.ModelIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<JsonElement> ReadMessages(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        if (!file.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            return MessagesOf(doc.RootElement).Select(message => message.Clone()).ToList();
        }

        // 왜: jsonl 은 덧붙이기만 하므로 같은 id 가 여러 번 나온다 — 마지막 것이 그 메시지의 최종 토큰 수다
        var byId = new Dictionary<string, JsonElement>();
        var anonymous = new List<JsonElement>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            var candidates = root.TryGetProperty("$set", out var patch) && patch.ValueKind == JsonValueKind.Object
                ? MessagesOf(patch)
                : root.TryGetProperty("tokens", out _) ? [root] : MessagesOf(root);

            foreach (var message in candidates)
            {
                if (message.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                {
                    byId[id.GetString()!] = message.Clone();
                }
                else
                {
                    anonymous.Add(message.Clone());
                }
            }
        }

        return byId.Values.Concat(anonymous).ToList();
    }

    private static IEnumerable<JsonElement> MessagesOf(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array
            ? messages.EnumerateArray()
            : [];

    private static void Accumulate(
        JsonElement message,
        DateOnly since,
        Dictionary<(DateOnly Day, string Model), (long Input, long Cached, long Output)> totals)
    {
        if (!message.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("timestamp", out var stamp) ||
            !DateTimeOffset.TryParse(stamp.GetString(), out var at))
        {
            return;
        }

        var day = DateOnly.FromDateTime(at.LocalDateTime);
        if (day < since)
        {
            return;
        }

        long prompt = Number(tokens, "input");
        long cached = Math.Min(Number(tokens, "cached"), prompt);

        // 계약: input 은 캐시분을 포함한 프롬프트 전체다 — 캐시분을 빼야 두 번 세지 않는다. 생각(thoughts) 토큰은 출력 단가로 과금된다
        var input = prompt - cached + Number(tokens, "tool");
        var output = Number(tokens, "output") + Number(tokens, "thoughts");

        var model = message.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(m.GetString())
            ? m.GetString()!
            : DefaultModel;

        var key = (day, model);
        totals.TryGetValue(key, out var sum);
        totals[key] = (sum.Input + input, sum.Cached + cached, sum.Output + output);
    }

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n)
            ? Math.Max(0, n)
            : 0;

    private static int Clamp(long value) => value > int.MaxValue ? int.MaxValue : (int)value;
}
