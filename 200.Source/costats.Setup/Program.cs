using System;
using System.Windows.Forms;
using Jhj.Core.Setup;

namespace costats.Setup
{
    internal static class Program
    {
        // 계약: JHJ_AI-Usage-Monitor-Setup.exe [--version 1.0.2] [--silent] [--state fresh|installed] | --uninstall [--silent] [--purge]
        //       --state 는 개발 확인용(설치 전/후 화면 강제). --uninstall 은 「프로그램 추가/제거」·winget uninstall 이 부르고, --purge 가 있어야 설정·이력까지 지운다
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var silent = SetupArgs.Has(args, "--silent");
            if (SetupArgs.Has(args, "--uninstall"))
            {
                Environment.ExitCode = Uninstall(silent, SetupArgs.Has(args, "--purge"));
                return;
            }

            Application.Run(new SetupForm(SetupArgs.Find(args, "--version"), silent, SetupArgs.Find(args, "--state")));
        }

        private static int Uninstall(bool silent, bool purge)
        {
            Setup.Installer.DetectInstallDir();
            var keepData = !purge;
            if (!silent)
            {
                var choice = SetupPrompts.ConfirmUninstall(null, Setup.Installer, "설정과 사용량 이력");
                if (choice == UninstallChoice.Cancel)
                {
                    return 0;
                }

                keepData = choice == UninstallChoice.KeepData;
            }

            try
            {
                Setup.Installer.Uninstall(keepData);
                if (!silent)
                {
                    SetupPrompts.UninstallDone(null, Setup.Installer, keepData, "설정과 사용량 이력");
                }

                return 0;
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    SetupPrompts.UninstallFailed(null, Setup.Installer, ex);
                }

                return 1;
            }
        }
    }
}
