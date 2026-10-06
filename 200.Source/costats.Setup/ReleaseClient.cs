using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace costats.Setup
{
    /// <summary>GitHub 릴리스 하나. Zip·Checksum 은 이 PC(RID) 용 자산의 내려받기 주소다.</summary>
    internal sealed class Release
    {
        public Version Version { get; set; }
        public DateTime? PublishedAt { get; set; }
        public bool Prerelease { get; set; }
        public string ZipName { get; set; }
        public string ZipUrl { get; set; }
        public string ChecksumUrl { get; set; }

        // 계약: 릴리스 버전은 「배포 버전.빌드 날짜」(1.0.0.20261006) — 옛 세 자리 릴리스는 Revision 이 -1 이다
        public string VersionText => Version.Revision >= 0 ? Version.ToString(4) : Version.ToString(3);

        public override string ToString() =>
            "v" + VersionText + (Version.Revision < 0 && PublishedAt is DateTime at ? " · " + at.ToLocalTime().ToString("yyyy-MM-dd") : string.Empty) +
            (Prerelease ? " (pre)" : string.Empty);
    }

    /// <summary>
    /// 앱과 같은 공개 저장소의 Releases 를 토큰 없이 읽는다.
    /// 계약: 자산 이름 규칙은 800.Deploy/publish.ps1 이 만드는 AiUsageMonitor-&lt;rid&gt;-v&lt;버전&gt;.zip(+ .sha256)이다.
    /// </summary>
    internal sealed class ReleaseClient : IDisposable
    {
        public const string Repository = "HyungJIn-kr-97/JHJ_AI_USAGE";

        private readonly HttpClient _http;

        public ReleaseClient()
        {
            // 함정: .NET Framework 4.8 은 OS 설정에 따라 TLS 1.0 으로 붙어 GitHub 가 거절한다 — 1.2 를 명시한다
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("AiUsageMonitor-Setup/1.0");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        // 왜: 릴리스는 win-x64 하나만 만든다 — ARM64 Windows 도 x64 에뮬레이션으로 돈다
        public static string Rid => "win-x64";

        public async Task<List<Release>> GetReleasesAsync(CancellationToken ct)
        {
            var json = await (await _http.GetAsync($"https://api.github.com/repos/{Repository}/releases?per_page=30", ct)).EnsureSuccessStatusCode()
                .Content.ReadAsStringAsync();
            var items = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<Dictionary<string, object>>>(json);
            var list = new List<Release>();
            foreach (var item in items)
            {
                if (item.TryGetValue("draft", out var draft) && draft is bool d && d)
                {
                    continue;
                }

                var tag = (item.TryGetValue("tag_name", out var t) ? t as string : null) ?? string.Empty;
                if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
                {
                    continue;
                }

                var release = new Release
                {
                    Version = version,
                    Prerelease = item.TryGetValue("prerelease", out var pre) && pre is bool p && p,
                    PublishedAt = item.TryGetValue("published_at", out var pub) && pub is string s &&
                                  DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var at)
                        ? at
                        : (DateTime?)null
                };

                var zipName = $"AiUsageMonitor-{Rid}-v{release.VersionText}.zip";
                // 함정: JavaScriptSerializer 는 JSON 배열을 object[] 가 아니라 ArrayList 로 준다
                if (item.TryGetValue("assets", out var assets) && assets is System.Collections.IEnumerable array)
                {
                    foreach (var asset in array.OfType<Dictionary<string, object>>())
                    {
                        var name = asset.TryGetValue("name", out var n) ? n as string : null;
                        var url = asset.TryGetValue("browser_download_url", out var u) ? u as string : null;
                        if (string.Equals(name, zipName, StringComparison.OrdinalIgnoreCase))
                        {
                            release.ZipName = name;
                            release.ZipUrl = url;
                        }
                        else if (string.Equals(name, zipName + ".sha256", StringComparison.OrdinalIgnoreCase))
                        {
                            release.ChecksumUrl = url;
                        }
                    }
                }

                if (release.ZipUrl != null)
                {
                    list.Add(release);
                }
            }

            // 계약: 같은 배포 버전(세 자리)에서는 날짜가 가장 늦은 릴리스 하나만 보인다
            return list
                .GroupBy(r => new Version(r.Version.Major, r.Version.Minor, r.Version.Build))
                .Select(g => g.OrderByDescending(r => r.Version).First())
                .OrderByDescending(r => r.Version)
                .ToList();
        }

        /// <returns>받은 zip 의 경로 — 체크섬이 있으면 맞는지 확인한 뒤다</returns>
        public async Task<string> DownloadAsync(Release release, IProgress<int> progress, CancellationToken ct)
        {
            var path = Path.Combine(Path.GetTempPath(), release.ZipName);
            using (var response = await _http.GetAsync(release.ZipUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                using (var source = await response.Content.ReadAsStreamAsync())
                using (var target = File.Create(path))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                    {
                        await target.WriteAsync(buffer, 0, read, ct);
                        done += read;
                        if (total > 0)
                        {
                            progress.Report((int)(done * 100 / total));
                        }
                    }
                }
            }

            if (release.ChecksumUrl != null)
            {
                var expected = (await _http.GetStringAsync(release.ChecksumUrl)).Trim().Split(' ', '\t')[0];
                string actual;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(path))
                {
                    actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                }

                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(path);
                    throw new InvalidDataException("내려받은 파일의 체크섬이 릴리스와 다릅니다.");
                }
            }

            return path;
        }

        public void Dispose() => _http.Dispose();
    }
}
