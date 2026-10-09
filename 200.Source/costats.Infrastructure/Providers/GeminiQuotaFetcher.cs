using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace costats.Infrastructure.Providers;

/// <summary>
/// Gemini CLI 가 쓰는 Code Assist 내부 API 에서 모델별 남은 한도를 읽는다.
/// 계약: ~/.gemini/oauth_creds.json 의 토큰을 읽기만 한다 — 갱신한 토큰도 메모리에만 두고 파일에 쓰지 않는다(실행 중인 CLI 와 경합).
/// 함정: v1internal 은 비공개 API 이고, 개인 Google 계정 로그인은 2026-06-18 에 막혔다 — 실패는 전부 null 로 돌려 로그 집계만 보이게 한다.
/// 함정: 토큰 갱신에는 CLI 의 OAuth 클라이언트 값이 필요하다 — 소스에 박지 않고 설치된 gemini-cli 번들에서 읽는다.
/// </summary>
public sealed class GeminiQuotaFetcher : IDisposable
{
    private const string ApiBase = "https://cloudcode-pa.googleapis.com/v1internal:";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiry;
    private (string Id, string Secret)? _oauthClient;
    private bool _oauthClientLooked;

    public GeminiQuotaFetcher()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "JHJ_AI-Usage-Monitor");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static string GeminiHome => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini");

    public async Task<GeminiQuotaResult?> FetchAsync(CancellationToken cancellationToken)
    {
        try
        {
            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (token is null)
            {
                return null;
            }

            using var load = await PostAsync("loadCodeAssist", token,
                """{"metadata":{"ideType":"GEMINI_CLI","pluginType":"GEMINI","platform":"WINDOWS_AMD64"}}""",
                cancellationToken).ConfigureAwait(false);
            if (load is null)
            {
                return null;
            }

            var project = ReadProject(load.RootElement);
            var tier = ReadTier(load.RootElement);

            var body = project is null ? "{}" : JsonSerializer.Serialize(new { project });
            using var quota = await PostAsync("retrieveUserQuota", token, body, cancellationToken).ConfigureAwait(false);
            if (quota is null || !quota.RootElement.TryGetProperty("buckets", out var buckets) ||
                buckets.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            GeminiQuotaBucket? pro = null;
            GeminiQuotaBucket? flash = null;
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (!bucket.TryGetProperty("remainingFraction", out var fraction) ||
                    fraction.ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                var model = bucket.TryGetProperty("modelId", out var m) ? m.GetString() ?? string.Empty : string.Empty;
                var resetsAt = bucket.TryGetProperty("resetTime", out var r) &&
                               DateTimeOffset.TryParse(r.GetString(), out var parsed) ? parsed : (DateTimeOffset?)null;
                var used = Math.Clamp((1 - fraction.GetDouble()) * 100, 0, 100);
                var candidate = new GeminiQuotaBucket(model, used, resetsAt);

                // 왜: 화면 막대는 둘뿐이라 Pro 계열과 그 밖(Flash 계열)으로 접고, 각 묶음에서 가장 많이 쓴 것을 보인다
                if (model.Contains("pro", StringComparison.OrdinalIgnoreCase))
                {
                    pro = pro is null || used > pro.UsedPercent ? candidate : pro;
                }
                else
                {
                    flash = flash is null || used > flash.UsedPercent ? candidate : flash;
                }
            }

            return pro is null && flash is null
                ? null
                : new GeminiQuotaResult(tier, pro, flash, DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<JsonDocument?> PostAsync(string method, string token, string body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + method)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
    }

    private static string? ReadProject(JsonElement root)
    {
        if (!root.TryGetProperty("cloudaicompanionProject", out var project))
        {
            return null;
        }

        return project.ValueKind switch
        {
            JsonValueKind.String => project.GetString(),
            JsonValueKind.Object when project.TryGetProperty("id", out var id) => id.GetString(),
            _ => null
        };
    }

    private static string? ReadTier(JsonElement root)
    {
        foreach (var name in new[] { "paidTier", "currentTier" })
        {
            if (root.TryGetProperty(name, out var tier) && tier.ValueKind == JsonValueKind.Object &&
                tier.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }

        return null;
    }

    private async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _accessTokenExpiry)
        {
            return _accessToken;
        }

        var path = Path.Combine(GeminiHome, "oauth_creds.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        var root = doc.RootElement;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        var expiry = root.TryGetProperty("expiry_date", out var e) && e.TryGetInt64(out var ms)
            ? DateTimeOffset.FromUnixTimeMilliseconds(ms)
            : DateTimeOffset.MinValue;

        if (access is not null && DateTimeOffset.UtcNow < expiry.AddMinutes(-1))
        {
            _accessToken = access;
            _accessTokenExpiry = expiry.AddMinutes(-1);
            return access;
        }

        return refresh is null ? null : await RefreshAsync(refresh, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var client = FindOAuthClient();
        if (client is null)
        {
            return null;
        }

        using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = client.Value.Id,
            ["client_secret"] = client.Value.Secret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var access = doc.RootElement.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        var seconds = doc.RootElement.TryGetProperty("expires_in", out var s) && s.TryGetInt32(out var n) ? n : 3000;
        if (access is null)
        {
            return null;
        }

        _accessToken = access;
        _accessTokenExpiry = DateTimeOffset.UtcNow.AddSeconds(seconds - 60);
        return access;
    }

    private (string Id, string Secret)? FindOAuthClient()
    {
        if (_oauthClientLooked)
        {
            return _oauthClient;
        }

        _oauthClientLooked = true;
        var bundle = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "npm", "node_modules", "@google", "gemini-cli", "bundle");
        if (!Directory.Exists(bundle))
        {
            return null;
        }

        string? id = null;
        string? secret = null;
        foreach (var file in Directory.EnumerateFiles(bundle, "*.js"))
        {
            var text = File.ReadAllText(file);
            id ??= Match(text, @"OAUTH_CLIENT_ID\s*=\s*[""']([^""']+)[""']");
            secret ??= Match(text, @"OAUTH_CLIENT_SECRET\s*=\s*[""']([^""']+)[""']");
            if (id is not null && secret is not null)
            {
                _oauthClient = (id, secret);
                break;
            }
        }

        return _oauthClient;
    }

    private static string? Match(string text, string pattern)
    {
        var match = Regex.Match(text, pattern);
        return match.Success ? match.Groups[1].Value : null;
    }

    public void Dispose() => _httpClient.Dispose();
}

/// <summary>한 모델 묶음의 일일 한도. UsedPercent 는 0~100.</summary>
public sealed record GeminiQuotaBucket(string ModelId, double UsedPercent, DateTimeOffset? ResetsAt);

public sealed record GeminiQuotaResult(string? Tier, GeminiQuotaBucket? Pro, GeminiQuotaBucket? Flash, DateTimeOffset FetchedAt);
