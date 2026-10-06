using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using costats.Core.Pulse;
using costats.Infrastructure.Providers;

namespace costats.Infrastructure.Expense;

/// <summary>
/// Parses JSONL log files and extracts token consumption data.
/// </summary>
public static class LogDigestor
{
    private const int MaxLineLength = 512 * 1024;
    private const int FileReadBufferSize = 16 * 1024;
    private const int MaxDedupeKeyCount = 250_000;

    /// <summary>
    /// Digests Claude Code log files and produces consumption slices.
    /// </summary>
    public static Task<IReadOnlyList<ConsumptionSlice>> DigestClaudeLogsAsync(
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => DigestClaudeLogsCore(since, until, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Digests Claude Code log files from a specific directory and produces consumption slices.
    /// </summary>
    public static Task<IReadOnlyList<ConsumptionSlice>> DigestClaudeLogsAsync(
        string logDirectory,
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => DigestClaudeLogsCore(logDirectory, since, until, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// 계약: 폴더마다 주인(Owner)을 달아 읽는다 — Owner 가 있는 폴더는 그 계정에 연동된 프로그램의 줄만 센다.
    /// </summary>
    public static Task<IReadOnlyList<ConsumptionSlice>> DigestClaudeLogsAsync(
        IReadOnlyList<ClaudeLogRoot> roots,
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => DigestClaudeLogsCore(roots, since, until, cancellationToken), cancellationToken);
    }

    private static IReadOnlyList<ConsumptionSlice> DigestClaudeLogsCore(
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken) =>
        DigestClaudeLogsCore([new ClaudeLogRoot(GetClaudeLogDirectory(), null)], since, until, cancellationToken);

    private static IReadOnlyList<ConsumptionSlice> DigestClaudeLogsCore(
        string logDirectory,
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken) =>
        DigestClaudeLogsCore([new ClaudeLogRoot(logDirectory, null)], since, until, cancellationToken);

    private static IReadOnlyList<ConsumptionSlice> DigestClaudeLogsCore(
        IReadOnlyList<ClaudeLogRoot> roots,
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken)
    {
        var aggregates = new Dictionary<DateOnly, Dictionary<string, SliceAccumulator>>();
        var dedupeSet = new HashSet<MessageRequestKey>();
        var cutoff = since.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc) - TimeSpan.FromDays(1);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root.ProjectsDir))
                continue;

            // Scan all project directories recursively (includes subagents subdirectories)
            foreach (var projectDir in Directory.EnumerateDirectories(root.ProjectsDir))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ScanClaudeDirectoryRecursive(projectDir, root.Owner, since, until, cutoff, aggregates, dedupeSet, cancellationToken);
            }
        }

        return BuildAggregatedSlices(aggregates);
    }

    private static void ScanClaudeDirectoryRecursive(
        string directory,
        string? owner,
        DateOnly since,
        DateOnly until,
        DateTime cutoff,
        Dictionary<DateOnly, Dictionary<string, SliceAccumulator>> aggregates,
        HashSet<MessageRequestKey> dedupeSet,
        CancellationToken cancellationToken)
    {
        // Scan jsonl files in current directory
        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.GetLastWriteTimeUtc(file) < cutoff)
                continue;

            DigestClaudeFile(file, owner, since, until, aggregates, dedupeSet, cancellationToken);
        }

        // Recurse into subdirectories (e.g., subagents/)
        foreach (var subDir in Directory.EnumerateDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanClaudeDirectoryRecursive(subDir, owner, since, until, cutoff, aggregates, dedupeSet, cancellationToken);
        }
    }

    /// <summary>
    /// Digests Codex log files and produces consumption slices.
    /// </summary>
    public static Task<IReadOnlyList<ConsumptionSlice>> DigestCodexLogsAsync(
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken = default,
        string? codexHome = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => DigestCodexLogsCore(since, until, cancellationToken, codexHome), cancellationToken);
    }

    private static IReadOnlyList<ConsumptionSlice> DigestCodexLogsCore(
        DateOnly since,
        DateOnly until,
        CancellationToken cancellationToken,
        string? codexHome)
    {
        var logDir = GetCodexLogDirectory(codexHome);
        if (!Directory.Exists(logDir))
            return [];

        var aggregates = new Dictionary<DateOnly, Dictionary<string, SliceAccumulator>>();

        foreach (var file in EnumerateCodexSessionFiles(logDir, since, until))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Each file has its own cumulative totals - don't share across files
            DigestCodexFile(file, since, until, aggregates, cancellationToken);
        }

        return BuildAggregatedSlices(aggregates);
    }

    private static void DigestClaudeFile(
        string filePath,
        string? owner,
        DateOnly since,
        DateOnly until,
        Dictionary<DateOnly, Dictionary<string, SliceAccumulator>> aggregates,
        HashSet<MessageRequestKey> dedupeSet,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(filePath);
        if (owner is not null &&
            ProgramsByFile.TryGetValue(filePath, out var known) &&
            known.WriteTimeUtc == info.LastWriteTimeUtc && known.Length == info.Length &&
            !known.Programs.Any(program => IsOwner(program, owner)))
            return;

        var programsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in ReadLines(filePath, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Quick pre-filter before parsing JSON
                if (!line.Contains("\"type\":\"assistant\"") || !line.Contains("\"usage\""))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!TryGetString(root, "type", out var type) || type != "assistant")
                        continue;

                    string? program = null;
                    if (owner is not null)
                    {
                        program = ClaudeProgramRouter.SourceOf(
                            TryGetString(root, "entrypoint", out var ep) ? ep : null,
                            TryGetString(root, "sessionId", out var sid) ? sid : null,
                            TryGetString(root, "timestamp", out var at) && DateTimeOffset.TryParse(at, System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.AssumeUniversal, out var stamped) ? stamped : null);
                        programsSeen.Add(program);
                        if (!IsOwner(program, owner))
                            continue;
                    }

                    if (!TryGetString(root, "timestamp", out var timestamp))
                        continue;

                    var entryDate = ParseDateFromTimestamp(timestamp);
                    if (entryDate is null || entryDate < since || entryDate > until)
                        continue;

                    if (!root.TryGetProperty("message", out var message))
                        continue;

                    if (!TryGetString(message, "model", out var model))
                        continue;

                    if (!message.TryGetProperty("usage", out var usage))
                        continue;

                    // Deduplicate by message ID + request ID (streaming sends duplicates)
                    var messageId = TryGetString(message, "id", out var mid) ? mid : null;
                    var requestId = TryGetString(root, "requestId", out var rid) ? rid : null;
                    if (messageId is not null && requestId is not null)
                    {
                        if (!TryAddDedupeKey(dedupeSet, new MessageRequestKey(messageId, requestId)))
                            continue;
                    }

                    var ledger = ExtractClaudeLedger(usage);
                    if (ledger.TotalConsumed == 0)
                        continue;

                    var rate = TariffRegistry.FindClaudeRate(model);
                    var cost = rate.ComputeCost(ledger);

                    AddAggregate(aggregates, entryDate.Value, model, ledger, cost, program);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }

            if (owner is not null)
                ProgramsByFile[filePath] = new FilePrograms(info.LastWriteTimeUtc, info.Length, programsSeen.ToArray());
        }
        catch (IOException)
        {
            // File access error, skip
        }
    }

    private static bool IsOwner(string program, string owner) =>
        string.Equals(ClaudeProgramRouter.OwnerOf(program), owner, StringComparison.OrdinalIgnoreCase);

    private static void DigestCodexFile(
        string filePath,
        DateOnly since,
        DateOnly until,
        Dictionary<DateOnly, Dictionary<string, SliceAccumulator>> aggregates,
        CancellationToken cancellationToken)
    {
        string? currentModel = null;
        // Track cumulative totals per file (each file is a session)
        int prevInput = 0, prevCached = 0, prevOutput = 0;

        try
        {
            foreach (var line in ReadLines(filePath, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Quick pre-filter
                var hasEventMsg = line.Contains("\"type\":\"event_msg\"");
                var hasTurnContext = line.Contains("\"type\":\"turn_context\"");

                if (!hasEventMsg && !hasTurnContext)
                    continue;

                if (hasEventMsg && !line.Contains("\"token_count\""))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;

                    if (!TryGetString(root, "type", out var type))
                        continue;

                    // Extract model from turn context
                    if (type == "turn_context")
                    {
                        if (root.TryGetProperty("payload", out var turnPayload))
                        {
                            if (TryGetString(turnPayload, "model", out var m))
                                currentModel = m;
                            else if (turnPayload.TryGetProperty("info", out var turnInfo) && TryGetString(turnInfo, "model", out var m2))
                                currentModel = m2;
                        }
                        continue;
                    }

                    if (type != "event_msg")
                        continue;

                    if (!TryGetString(root, "timestamp", out var timestamp))
                        continue;

                    var entryDate = ParseDateFromTimestamp(timestamp);
                    if (entryDate is null || entryDate < since || entryDate > until)
                        continue;

                    if (!root.TryGetProperty("payload", out var eventPayload))
                        continue;

                    if (!TryGetString(eventPayload, "type", out var payloadType) || payloadType != "token_count")
                        continue;

                    // Get info object - may be nested under payload.info or directly in payload
                    JsonElement info = default;
                    if (eventPayload.TryGetProperty("info", out var infoEl) && infoEl.ValueKind == JsonValueKind.Object)
                        info = infoEl;
                    else
                        info = eventPayload;

                    var model = TryGetString(info, "model", out var mod) ? mod
                        : TryGetString(info, "model_name", out var mod2) ? mod2
                        : currentModel ?? "gpt-5";

                    // Extract token counts - prefer last_token_usage for incremental, fall back to delta from totals
                    int deltaInput = 0, deltaCached = 0, deltaOutput = 0;

                    if (info.TryGetProperty("last_token_usage", out var last) && last.ValueKind == JsonValueKind.Object)
                    {
                        // Use incremental values directly
                        deltaInput = Math.Max(0, GetIntOrZero(last, "input_tokens"));
                        deltaCached = GetIntOrZero(last, "cached_input_tokens");
                        if (deltaCached == 0) deltaCached = GetIntOrZero(last, "cache_read_input_tokens");
                        deltaOutput = Math.Max(0, GetIntOrZero(last, "output_tokens"));
                    }
                    else if (info.TryGetProperty("total_token_usage", out var totals) && totals.ValueKind == JsonValueKind.Object)
                    {
                        // Calculate delta from cumulative totals
                        var currInput = GetIntOrZero(totals, "input_tokens");
                        var currCached = GetIntOrZero(totals, "cached_input_tokens");
                        if (currCached == 0) currCached = GetIntOrZero(totals, "cache_read_input_tokens");
                        var currOutput = GetIntOrZero(totals, "output_tokens");

                        deltaInput = Math.Max(0, currInput - prevInput);
                        deltaCached = Math.Max(0, currCached - prevCached);
                        deltaOutput = Math.Max(0, currOutput - prevOutput);

                        prevInput = currInput;
                        prevCached = currCached;
                        prevOutput = currOutput;
                    }
                    else
                    {
                        continue;
                    }

                    if (deltaInput == 0 && deltaCached == 0 && deltaOutput == 0)
                        continue;

                    // Cached cannot exceed input
                    deltaCached = Math.Min(deltaCached, deltaInput);

                    var ledger = new TokenLedger
                    {
                        StandardInput = Math.Max(0, deltaInput - deltaCached),
                        CachedInput = Math.Max(0, deltaCached),
                        CacheWriteInput = 0,
                        GeneratedOutput = Math.Max(0, deltaOutput)
                    };

                    var rate = TariffRegistry.FindCodexRate(model);
                    var cost = rate.ComputeCost(ledger);

                    AddAggregate(aggregates, entryDate.Value, model, ledger, cost);
                }
                catch (JsonException)
                {
                    // Skip malformed lines
                }
            }
        }
        catch (IOException)
        {
            // File access error, skip
        }
    }

    private static TokenLedger ExtractClaudeLedger(JsonElement usage)
    {
        var input = GetIntOrZero(usage, "input_tokens");
        var cacheRead = GetIntOrZero(usage, "cache_read_input_tokens");
        var cacheCreate = GetIntOrZero(usage, "cache_creation_input_tokens");
        var output = GetIntOrZero(usage, "output_tokens");

        return new TokenLedger
        {
            StandardInput = Math.Max(0, input),
            CachedInput = Math.Max(0, cacheRead),
            CacheWriteInput = Math.Max(0, cacheCreate),
            GeneratedOutput = Math.Max(0, output)
        };
    }

    private static IReadOnlyList<ConsumptionSlice> BuildAggregatedSlices(
        Dictionary<DateOnly, Dictionary<string, SliceAccumulator>> aggregates)
    {
        var grouped = aggregates
            .SelectMany(day =>
                day.Value.Select(model =>
                {
                    var accumulator = model.Value;
                    return new ConsumptionSlice
                    {
                        Period = day.Key,
                        ModelIdentifier = accumulator.Model,
                        Tokens = accumulator.ToTokenLedger(),
                        ComputedCostUsd = accumulator.Cost,
                        Program = accumulator.Program
                    };
                }))
            .OrderByDescending(s => s.Period)
            .ThenBy(s => s.ModelIdentifier, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return grouped;
    }

    private static void AddAggregate(
        Dictionary<DateOnly, Dictionary<string, SliceAccumulator>> aggregates,
        DateOnly period,
        string modelIdentifier,
        TokenLedger ledger,
        decimal cost,
        string? program = null)
    {
        if (!aggregates.TryGetValue(period, out var byModel))
        {
            byModel = new Dictionary<string, SliceAccumulator>(StringComparer.OrdinalIgnoreCase);
            aggregates[period] = byModel;
        }

        var key = program is null ? modelIdentifier : modelIdentifier + "\u001F" + program;
        if (!byModel.TryGetValue(key, out var accumulator))
        {
            accumulator = new SliceAccumulator { Model = modelIdentifier, Program = program };
        }

        accumulator.StandardInput += ledger.StandardInput;
        accumulator.CachedInput += ledger.CachedInput;
        accumulator.CacheWriteInput += ledger.CacheWriteInput;
        accumulator.GeneratedOutput += ledger.GeneratedOutput;
        accumulator.Cost += cost;
        byModel[key] = accumulator;
    }

    private static TokenLedger ToTokenLedger(SliceAccumulator accumulator)
    {
        return new TokenLedger
        {
            StandardInput = accumulator.StandardInput,
            CachedInput = accumulator.CachedInput,
            CacheWriteInput = accumulator.CacheWriteInput,
            GeneratedOutput = accumulator.GeneratedOutput
        };
    }

    private static IEnumerable<string> ReadLines(string filePath, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(filePath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite,
            Options = FileOptions.SequentialScan,
            BufferSize = FileReadBufferSize
        });
        using var reader = new StreamReader(stream);
        var buffer = new StringBuilder();

        string? line;
        while ((line = ReadLineLimited(reader, buffer)) is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (line.Length == 0)
            {
                continue;
            }

            yield return line;
        }
    }

    private static string GetClaudeLogDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".claude", "projects");
    }

    private static string GetCodexLogDirectory(string? overrideHome)
    {
        var codexHome = overrideHome ?? Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
            return Path.Combine(codexHome.Trim(), "sessions");

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".codex", "sessions");
    }

    private static IEnumerable<string> EnumerateCodexSessionFiles(string baseDir, DateOnly since, DateOnly until)
    {
        if (!Directory.Exists(baseDir))
            yield break;

        // Codex stores sessions in nested date directories: sessions/YYYY/MM/DD/*.jsonl
        for (var date = since; date <= until; date = date.AddDays(1))
        {
            var dayPath = Path.Combine(
                baseDir,
                date.Year.ToString("D4"),
                date.Month.ToString("D2"),
                date.Day.ToString("D2"));

            if (!Directory.Exists(dayPath))
                continue;

            foreach (var file in Directory.EnumerateFiles(dayPath, "*.jsonl"))
            {
                yield return file;
            }
        }
    }

    // 함정: 로그 시각은 UTC("…Z")다 — 앞 10자를 날짜로 쓰면 한국 시간 00~09시 사용이 전날로 들어간다. 시각이 있으면 현지 날짜로 바꾼다
    private static DateOnly? ParseDateFromTimestamp(string timestamp)
    {
        if (timestamp.Length > 10 &&
            DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var stamped))
            return DateOnly.FromDateTime(stamped.LocalDateTime);

        // 날짜만 적힌 값
        if (timestamp.Length >= 10 &&
            timestamp[4] == '-' &&
            timestamp[7] == '-')
        {
            if (int.TryParse(timestamp.AsSpan(0, 4), out var year) &&
                int.TryParse(timestamp.AsSpan(5, 2), out var month) &&
                int.TryParse(timestamp.AsSpan(8, 2), out var day))
            {
                try
                {
                    return new DateOnly(year, month, day);
                }
                catch
                {
                    // Invalid date components
                }
            }
        }

        // Fallback to full parse
        if (DateTimeOffset.TryParse(timestamp, out var dto))
            return DateOnly.FromDateTime(dto.LocalDateTime);

        return null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? string.Empty;
            return !string.IsNullOrEmpty(value);
        }
        value = string.Empty;
        return false;
    }

    private static int GetIntOrZero(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetInt32();
        return 0;
    }

    private static string? ReadLineLimited(StreamReader reader, StringBuilder buffer)
    {
        buffer.Clear();
        var overflowed = false;
        int ch;

        while ((ch = reader.Read()) != -1)
        {
            if (ch == '\r')
            {
                if (reader.Peek() == '\n')
                    reader.Read();
                return overflowed ? string.Empty : buffer.ToString();
            }

            if (ch == '\n')
            {
                return overflowed ? string.Empty : buffer.ToString();
            }

            if (!overflowed)
            {
                if (buffer.Length < MaxLineLength)
                {
                    buffer.Append((char)ch);
                }
                else
                {
                    overflowed = true;
                }
            }
        }

        if (buffer.Length == 0 && !overflowed)
            return null;

        return overflowed ? string.Empty : buffer.ToString();
    }

    private static bool TryAddDedupeKey(HashSet<MessageRequestKey> dedupeSet, MessageRequestKey key)
    {
        // Bound memory for very large log histories.
        if (dedupeSet.Count >= MaxDedupeKeyCount)
        {
            dedupeSet.Clear();
        }

        return dedupeSet.Add(key);
    }

    private readonly record struct MessageRequestKey(string MessageId, string RequestId);

    // 왜: 기본 폴더는 계정마다 한 번씩 읽힌다 — 연동된 프로그램이 없는 파일은 바뀌지 않은 한 다시 파싱하지 않는다
    private static readonly ConcurrentDictionary<string, FilePrograms> ProgramsByFile = new(StringComparer.OrdinalIgnoreCase);

    private sealed record FilePrograms(DateTime WriteTimeUtc, long Length, string[] Programs);

    private record struct SliceAccumulator
    {
        public string Model { get; set; }
        public string? Program { get; set; }
        public long StandardInput { get; set; }
        public long CachedInput { get; set; }
        public long CacheWriteInput { get; set; }
        public long GeneratedOutput { get; set; }
        public decimal Cost { get; set; }

        public TokenLedger ToTokenLedger() => LogDigestor.ToTokenLedger(this);
    }
}
