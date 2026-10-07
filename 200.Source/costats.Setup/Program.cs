using System;
using System.Linq;
using System.Windows.Forms;

namespace costats.Setup
{
    internal static class Program
    {
        // 계약: AiUsageMonitor-Setup.exe [--version 1.0.2] [--silent] [--state fresh|installed]  — --state 는 개발 확인용(설치 전/후 화면 강제)
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var silent = args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));
            Application.Run(new SetupForm(Installer.FindArg(args, "--version"), silent, Installer.FindArg(args, "--state")));
        }
    }
}
