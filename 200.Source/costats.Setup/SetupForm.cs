using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace costats.Setup
{
    /// <summary>
    /// 웹 설치 관리자 화면 — GitHub 릴리스 목록에서 버전을 골라 받아 설치하고 실행한다.
    /// 계약: --version 1.0.2 로 처음 고를 버전을, --silent 로 화면 없이 설치·실행을 지정한다.
    /// </summary>
    internal sealed class SetupForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(24, 20, 19);
        private static readonly Color Panel = Color.FromArgb(36, 31, 29);
        private static readonly Color Text1 = Color.FromArgb(240, 234, 230);
        private static readonly Color Text2 = Color.FromArgb(168, 158, 152);
        private static readonly Color Accent = Color.FromArgb(232, 69, 69);

        private readonly ComboBox _versions = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
        private readonly CheckBox _runAfter = new CheckBox { Text = "설치 후 바로 실행", Checked = true, AutoSize = true };
        private readonly ProgressBar _progress = new ProgressBar { Width = 400, Height = 6, Style = ProgressBarStyle.Continuous };
        private readonly Label _status = new Label { AutoSize = false, Width = 400, Height = 40 };
        private readonly Button _install = new Button { Text = "설치", Width = 96, Height = 32, Enabled = false };
        private readonly Button _close = new Button { Text = "닫기", Width = 80, Height = 32 };
        private readonly ReleaseClient _client = new ReleaseClient();
        private readonly string _wantedVersion;
        private readonly bool _silent;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public SetupForm(string wantedVersion, bool silent)
        {
            _wantedVersion = wantedVersion;
            _silent = silent;

            Text = "AI 통합 사용량 모니터 설치";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Text1;
            Font = new Font("Malgun Gothic", 9.5f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(448, 300);
            Padding = new Padding(24);

            var title = new Label { Text = "AI 통합 사용량 모니터", Font = new Font(Font.FontFamily, 14f, FontStyle.Bold), AutoSize = true };
            var installed = Installer.InstalledVersion();
            var sub = new Label
            {
                Text = (installed == null ? "아직 설치되지 않았습니다" : "설치된 버전 v" + installed) + "  ·  " + Installer.InstallDir,
                ForeColor = Text2, AutoSize = false, Width = 400, Height = 34
            };
            var versionLabel = new Label { Text = "설치할 버전", ForeColor = Text2, AutoSize = true };
            _status.ForeColor = Text2;
            _status.Text = "GitHub 에서 버전 목록을 읽는 중입니다...";

            foreach (var button in new[] { _install, _close })
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Panel;
                button.BackColor = Panel;
                button.ForeColor = Text1;
            }

            _install.BackColor = Accent;
            _install.FlatAppearance.BorderColor = Accent;
            _versions.BackColor = Panel;
            _versions.ForeColor = Text1;
            _versions.FlatStyle = FlatStyle.Flat;

            var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            layout.Controls.Add(title);
            layout.Controls.Add(sub);
            layout.Controls.Add(versionLabel);
            layout.Controls.Add(_versions);
            layout.Controls.Add(_runAfter);
            layout.Controls.Add(_progress);
            layout.Controls.Add(_status);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = 400, Height = 40 };
            buttons.Controls.Add(_close);
            buttons.Controls.Add(_install);
            layout.Controls.Add(buttons);
            Controls.Add(layout);

            foreach (Control c in layout.Controls)
            {
                c.Margin = new Padding(0, 0, 0, 8);
            }

            _install.Click += async (s, e) => await InstallAsync();
            _close.Click += (s, e) => Close();
            FormClosing += (s, e) => _cts.Cancel();
            Shown += async (s, e) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                var releases = await _client.GetReleasesAsync(_cts.Token);
                _versions.Items.AddRange(releases.Cast<object>().ToArray());
                if (releases.Count == 0)
                {
                    _status.Text = "설치할 수 있는 릴리스가 없습니다.";
                    return;
                }

                // 계약: 고르지 않으면 정식(비 pre) 최신 버전 — --version 이 있으면 그 버전
                var wanted = releases.FirstOrDefault(r => _wantedVersion != null && r.Version.ToString(3) == _wantedVersion.TrimStart('v', 'V'))
                             ?? releases.FirstOrDefault(r => !r.Prerelease) ?? releases[0];
                _versions.SelectedItem = wanted;
                _install.Enabled = true;
                _status.Text = "버전을 고른 뒤 「설치」를 누르십시오. 낮은 버전을 고르면 그 버전으로 되돌립니다.";

                if (_silent)
                {
                    await InstallAsync();
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _status.Text = "GitHub 에서 목록을 읽지 못했습니다: " + ex.Message;
            }
        }

        private async Task InstallAsync()
        {
            if (!(_versions.SelectedItem is Release release))
            {
                return;
            }

            _install.Enabled = false;
            _versions.Enabled = false;
            try
            {
                _status.Text = $"v{release.Version.ToString(3)} 을(를) 받는 중입니다...";
                var progress = new Progress<int>(p => _progress.Value = Math.Max(0, Math.Min(100, p)));
                var zip = await _client.DownloadAsync(release, progress, _cts.Token);

                _status.Text = "설치하는 중입니다...";
                await Task.Run(() => Installer.Install(zip));
                try { System.IO.File.Delete(zip); } catch (System.IO.IOException) { }

                _status.Text = $"v{release.Version.ToString(3)} 설치를 마쳤습니다.";
                if (_runAfter.Checked)
                {
                    Installer.Launch();
                    Close();
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _status.Text = "설치하지 못했습니다: " + ex.Message;
                _install.Enabled = true;
                _versions.Enabled = true;
                if (_silent)
                {
                    Environment.ExitCode = 1;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _client.Dispose();
                _cts.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
