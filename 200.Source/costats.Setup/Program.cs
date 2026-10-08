using System;
using System.Linq;
using System.Windows.Forms;

namespace costats.Setup
{
    internal static class Program
    {
        // 계약: AI-Usage-Monitor_JHJ-Setup.exe [--version 1.0.2] [--silent] [--state fresh|installed] | --uninstall [--silent] [--purge]
        //       --state 는 개발 확인용(설치 전/후 화면 강제). --uninstall 은 「프로그램 추가/제거」·winget uninstall 이 부르고, --purge 가 있어야 설정·이력까지 지운다
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var silent = Has(args, "--silent");
            if (Has(args, "--uninstall"))
            {
                Environment.ExitCode = Uninstall(silent, Has(args, "--purge"));
                return;
            }

            Application.Run(new SetupForm(Installer.FindArg(args, "--version"), silent, Installer.FindArg(args, "--state")));
        }

        private const string Title = "AI 통합 사용량 모니터";

        private static int Uninstall(bool silent, bool purge)
        {
            Installer.DetectInstallDir();
            var keepData = !purge;
            if (!silent)
            {
                var answer = MessageBox.Show(
                    "설치된 앱을 제거합니다.\n\n설정과 사용량 이력(%LOCALAPPDATA%\\AI-Usage-Monitor_JHJ)도 함께 지울까요?\n「예」= 모두 지움 · 「아니요」= 앱만 지우고 설정·이력은 남김",
                    Title + " 제거", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (answer == DialogResult.Cancel)
                {
                    return 0;
                }

                keepData = answer == DialogResult.No;
            }

            try
            {
                Installer.Uninstall(keepData);
                if (!silent)
                {
                    MessageBox.Show(keepData ? "제거했습니다. 설정·이력은 남겨 두었습니다." : "제거했습니다. 설정·이력도 지웠습니다.", Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show("제거하지 못했습니다: " + ex.Message, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                return 1;
            }
        }

        private static bool Has(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }
}
