using Jhj.Core.Setup;

namespace costats.Setup
{
    /// <summary>
    /// 이 설치 관리자가 아는 앱 — 몸통(설치본 찾기 · 버전 판정 · 설치 · 제거 · 릴리스 읽기)은 JHJ_CS_CORE 의 Jhj.Core.Setup 이고 여기는 값뿐이다.
    /// 계약: Name · DisplayName · 옛 이름은 costats.App 의 JhjApp(App.xaml.cs)와 같다 — 어느 쪽으로 깔아도 같은 자리에 놓인다.
    /// 계약: PackagePrefix 는 800.Deploy/publish.ps1 이 만드는 zip 이름(AiUsageMonitor-&lt;rid&gt;-v&lt;버전&gt;.zip)이다 — 이미 깔린 버전이 이 이름으로만 다음 버전을 찾는다.
    /// TODO: 옛 이름 사본을 꾸러미에서 빼는 릴리스에서 LegacyExecutableNames 를 함께 비운다
    /// </summary>
    internal static class Setup
    {
        public static readonly SetupApp App = new SetupApp
        {
            Name = "JHJ_AI-Usage-Monitor",
            DisplayName = "JHJ AI 통합 사용량 모니터",
            LegacyExecutableNames = new[] { "AI-Usage-Monitor_JHJ.exe", "AiUsageMonitor.exe" },
            LegacyShortcutNames = new[] { "AI 통합 사용량 모니터.lnk" },
            Repository = "HyungJIn-kr-97/JHJ_AI_USAGE",
            PackagePrefix = "AiUsageMonitor",
            Publisher = "HyungJin Ju",
        };

        public static readonly AppInstaller Installer = new AppInstaller(App);
    }
}
