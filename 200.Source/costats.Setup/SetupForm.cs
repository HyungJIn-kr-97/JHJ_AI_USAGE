using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jhj.Core.Setup;
using Jhj.Core.Setup.Ui;

namespace costats.Setup
{
    /// <summary>
    /// 웹 설치 관리자 화면 — GitHub 릴리스 목록에서 버전을 골라 받아 설치·업데이트·제거한다. 화면 틀은 Core 의 SetupWindow(040 §6), 몸통은 AppInstaller·GithubReleaseClient 다.
    /// 계약: --version 1.0.2 로 처음 고를 버전을, --silent 로 화면 없이 설치·실행을, --state fresh|installed 로 개발 확인용 화면을 지정한다.
    /// 계약: 「사용 정보 수집 동의」 체크 전에는 「설치」가 눌리지 않고, 동의하면 앱과 같은 consent.txt 를 쓴다.
    /// </summary>
    internal sealed class SetupForm : SetupWindow
    {
        private static readonly string ConsentPath = System.IO.Path.Combine(Setup.Installer.DataDir, "consent.txt");
        private const string DetailsUrl = "https://github.com/HyungJIn-kr-97/JHJ_AI_USAGE#%EC%88%98%EC%A7%91%ED%95%98%EB%8A%94-%EC%82%AC%EC%9A%A9-%EC%A0%95%EB%B3%B4";

        private readonly GithubReleaseClient _client = new GithubReleaseClient(Setup.App);
        private readonly string _wantedVersion;
        private string _loadState = "GitHub 에서 버전 목록을 읽는 중입니다...";

        public SetupForm(string wantedVersion, bool silent, string testState = null)
            : base(Setup.Installer, new SetupWindowOptions
            {
                VersionPicker = true,
                ConsentLabel = "정보 수집 동의 (필수)",
                ConsentUrl = DetailsUrl,
                ConsentFile = ConsentPath,
                DataName = "설정과 사용량 이력",
                Silent = silent,
                TestState = testState,
            })
        {
            _wantedVersion = wantedVersion;
        }

        private GithubRelease Release => SelectedVersion as GithubRelease;

        protected override string TargetVersion => Release?.VersionText;

        protected override string TargetText => Release?.ToString();

        // 계약: 목록을 읽는 동안 · 못 읽었을 때 · 릴리스가 없을 때는 그 말이 상태 줄이고 「설치」는 눌리지 않는다
        protected override string InstallBlockReason() => _loadState;

        protected override async Task OnShownAsync()
        {
            try
            {
                var releases = await _client.GetReleasesAsync(CancelToken);
                if (releases.Count == 0)
                {
                    _loadState = "설치할 수 있는 릴리스가 없습니다.";
                    UpdateInstallEnabled();
                    return;
                }

                // 계약: 고르지 않으면 정식(비 pre) 최신 버전 — --version 이 있으면 그 버전
                var wanted = releases.FirstOrDefault(r => _wantedVersion != null && (r.VersionText == _wantedVersion.TrimStart('v', 'V') || r.Version.ToString(3) == _wantedVersion.TrimStart('v', 'V')))
                             ?? releases.FirstOrDefault(r => !r.Prerelease) ?? releases[0];
                _loadState = null;
                SetVersions(releases.Cast<object>().ToArray(), wanted);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _loadState = "GitHub 에서 목록을 읽지 못했습니다: " + ex.Message;
                UpdateInstallEnabled();
            }
        }

        protected override async Task<string> InstallCoreAsync(InstallOptions options, IProgress<int> progress, CancellationToken ct)
        {
            var release = Release;
            Status = $"{release} 을(를) 받는 중입니다...";
            var zip = await _client.DownloadAsync(release, progress, ct);
            Status = "설치하는 중입니다...";
            await Task.Run(() => Installer.Install(zip, options));
            try { System.IO.File.Delete(zip); } catch (System.IO.IOException) { }
            return release.Version.ToString(3);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _client.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
