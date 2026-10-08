using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace costats.Setup
{
    /// <summary>
    /// 웹 설치 관리자 화면 — 설치 위치를 정하고 GitHub 릴리스 목록에서 버전을 골라 받아 설치·업데이트·제거한다.
    /// 계약: --version 1.0.2 로 처음 고를 버전을, --silent 로 화면 없이 설치·실행을 지정한다.
    /// 계약: --state fresh|installed 는 개발 확인용 — 설치 전/후 화면을 강제로 보인다(실제 설치 여부와 무관, 설치·제거는 실제로 동작하니 누르지 않는다).
    /// 계약: 「사용 정보 수집 동의」 체크 전에는 「설치」가 눌리지 않고, 동의하면 앱과 같은 consent.txt 를 쓴다.
    /// 계약: 설치 위치는 Installer.DetectInstallDir 가 먼저 찾고(저장값 · 시작 프로그램 · 바로가기 · 기본), 「변경」이나 직접 입력으로 바꾼다 — exe 가 있는 폴더를 고르면 설치본으로 본다.
    /// 왜: 기본 WinForms 테두리·체크박스·콤보는 앱(WPF 다크)과 어긋난다 — 제목줄·체크박스·선택 상자·진행 막대를 직접 그린다.
    /// </summary>
    internal sealed class SetupForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(24, 20, 19);
        private static readonly Color Panel = Color.FromArgb(36, 31, 29);
        private static readonly Color PanelHover = Color.FromArgb(48, 42, 39);
        private static readonly Color Divider = Color.FromArgb(58, 51, 48);
        private static readonly Color Text1 = Color.FromArgb(240, 234, 230);
        private static readonly Color Text2 = Color.FromArgb(168, 158, 152);
        private static readonly Color Accent = Color.FromArgb(232, 69, 69);
        private static readonly Color AccentHover = Color.FromArgb(244, 92, 92);

        // 왜: PerMonitorV2 라 Windows 가 창을 키워 주지 않는다 — 모든 px 를 화면 배율로 직접 곱한다(AutoScale 은 Margin·직접 그린 도형을 못 키운다)
        private static readonly float S = SystemScale();

        // 왜: 2560×1440 100% 화면에서 448px 창은 너무 작았다 — 설계 수치는 그대로 두고 1.3배로 키워 그린다
        private const float Design = 1.3f;

        private static int Px(int v) => (int)Math.Round(v * S * Design);

        private static float SystemScale()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                return Math.Max(1f, g.DpiX / 96f);
            }
        }

        // 계약: 한 줄이다 — 넘치는 문구(개발용 --state 꼬리표)는 「…」 로 줄인다
        private readonly Label _sub = new Label { ForeColor = Text2, AutoSize = false, Width = Px(400), Height = Px(24), AutoEllipsis = true, Margin = new Padding(0, 0, 0, Px(12)) };
        private readonly TextBox _pathBox = new TextBox { Width = Px(304), BorderStyle = BorderStyle.FixedSingle, BackColor = Panel, ForeColor = Text1, TabStop = false };
        private readonly AccentButton _pathChange = new AccentButton { Text = "변경", Width = Px(88), Height = Px(30) };
        private readonly AccentDropDown _versions = new AccentDropDown { Width = Px(400), Height = Px(34) };
        private readonly AccentCheck _runAfter = new AccentCheck { Text = "바로 실행", Checked = true };
        private readonly AccentCheck _consent = new AccentCheck { Text = "정보 수집 동의 (필수)" };
        private readonly LinkLabel _consentInfo = new LinkLabel { Text = "자세히", AutoSize = true, LinkColor = Accent, ActiveLinkColor = AccentHover, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0) };
        private readonly SlimProgress _progress = new SlimProgress { Width = Px(400), Height = Px(6) };
        private readonly Label _status = new Label { AutoSize = false, Width = Px(400), Height = Px(22), ForeColor = Text2, AutoEllipsis = true };
        private readonly AccentButton _install = new AccentButton { Text = "설치", Width = Px(104), Height = Px(34), Enabled = false, Primary = true };
        private readonly AccentButton _remove = new AccentButton { Text = "제거", Width = Px(88), Height = Px(34), Visible = false };
        private readonly AccentButton _close = new AccentButton { Text = "닫기", Width = Px(88), Height = Px(34) };
        private readonly ReleaseClient _client = new ReleaseClient();
        private readonly string _wantedVersion;
        private readonly bool _silent;
        private readonly string _testState;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _releasesLoaded;
        private string _installed;
        private bool _devBuild;

        private static readonly string ConsentPath = System.IO.Path.Combine(Installer.DataDir, "consent.txt");
        private const string DetailsUrl = "https://github.com/HyungJIn-kr-97/JHJ_AI_USAGE#%EC%88%98%EC%A7%91%ED%95%98%EB%8A%94-%EC%82%AC%EC%9A%A9-%EC%A0%95%EB%B3%B4";

        public SetupForm(string wantedVersion, bool silent, string testState = null)
        {
            _wantedVersion = wantedVersion;
            _silent = silent;
            _testState = testState?.Trim().ToLowerInvariant();

            Text = "AI 통합 사용량 모니터 설치";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Text1;
            Font = new Font("Malgun Gothic", 10.5f);
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Px(448), Px(414));
            // 계약: 크기는 고정이다 — 배율을 곱한 값보다 작아지지 않는다
            MinimumSize = Size;
            MaximumSize = Size;
            DoubleBuffered = true;

            Controls.Add(BuildTitleBar());

            var body = new FlowLayoutPanel { Location = new Point(Px(24), Px(56)), Size = new Size(Px(400), Px(358)), FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Bg };
            var title = new Label { Text = "AI 통합 사용량 모니터", Font = new Font(Font.FontFamily, 17f, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, Px(2)) };

            // 설치 위치 — 직접 입력(Enter · 포커스 이탈)하거나 「변경」으로 고른다
            var pathLabel = new Label { Text = "설치 위치", ForeColor = Text2, AutoSize = true, Margin = new Padding(0, 0, 0, Px(6)) };
            var pathRow = new System.Windows.Forms.Panel { Width = Px(400), Height = Px(30), Margin = new Padding(0, 0, 0, Px(14)) };
            Installer.DetectInstallDir();
            _pathBox.Text = Installer.InstallDir;
            _pathBox.Location = new Point(0, (pathRow.Height - _pathBox.Height) / 2);
            _pathBox.Leave += (s, e) => ApplyPath(_pathBox.Text);
            _pathBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyPath(_pathBox.Text); } };
            _pathChange.Location = new Point(Px(312), 0);
            _pathChange.Click += (s, e) => ChoosePath();
            new ToolTip().SetToolTip(_pathChange, "설치할 폴더를 고르거나, 이미 설치된 폴더를 찾아 지정합니다.");
            pathRow.Controls.Add(_pathBox);
            pathRow.Controls.Add(_pathChange);

            var versionLabel = new Label { Text = "설치할 버전", ForeColor = Text2, AutoSize = true, Margin = new Padding(0, 0, 0, Px(6)) };
            _versions.Margin = new Padding(0, 0, 0, Px(14));
            new ToolTip().SetToolTip(_versions, "낮은 버전을 고르면 그 버전으로 되돌립니다.");
            _versions.SelectedChanged += (s, e) => UpdateInstallEnabled();

            // 체크 두 개를 한 줄에 — FlowLayoutPanel 은 자식마다 기본 여백 3px 을 더하므로 고정 패널에 직접 놓는다
            var checksRow = new System.Windows.Forms.Panel { Width = Px(400), Height = Px(24), Margin = new Padding(0, 0, 0, Px(14)) };
            _runAfter.Location = new Point(0, 0);
            _consent.Checked = System.IO.File.Exists(ConsentPath);
            _consent.CheckedChanged += (s, e) => UpdateInstallEnabled();
            _consentInfo.LinkClicked += (s, e) => System.Diagnostics.Process.Start(DetailsUrl);
            checksRow.Controls.Add(_runAfter);
            checksRow.Controls.Add(_consent);
            checksRow.Controls.Add(_consentInfo);
            EventHandler layoutChecks = (s, e) =>
            {
                _consent.Location = new Point(_runAfter.Right + Px(24), 0);
                _consentInfo.Location = new Point(_consent.Right + Px(10), (checksRow.Height - _consentInfo.Height) / 2);
            };
            _runAfter.SizeChanged += layoutChecks;
            _consent.SizeChanged += layoutChecks;
            layoutChecks(null, EventArgs.Empty);

            _status.Text = "GitHub 에서 버전 목록을 읽는 중입니다...";
            _status.Margin = new Padding(0, 0, 0, Px(8));
            _progress.Margin = new Padding(0, 0, 0, Px(14));

            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Width = Px(400), Height = Px(40), Margin = new Padding(0) };
            _close.Margin = new Padding(0);
            _remove.Margin = new Padding(0, 0, Px(8), 0);
            _install.Margin = new Padding(0, 0, Px(8), 0);
            buttons.Controls.Add(_close);
            buttons.Controls.Add(_remove);
            buttons.Controls.Add(_install);

            body.Controls.Add(title);
            body.Controls.Add(_sub);
            body.Controls.Add(pathLabel);
            body.Controls.Add(pathRow);
            body.Controls.Add(versionLabel);
            body.Controls.Add(_versions);
            body.Controls.Add(checksRow);
            body.Controls.Add(new HairLine { Width = Px(400), Margin = new Padding(0, 0, 0, Px(10)) });
            body.Controls.Add(_status);
            body.Controls.Add(_progress);
            body.Controls.Add(buttons);
            Controls.Add(body);

            _install.Click += async (s, e) => await InstallAsync();
            _remove.Click += (s, e) => Remove();
            _close.Click += (s, e) => Close();
            FormClosing += (s, e) => _cts.Cancel();
            Shown += async (s, e) => { ActiveControl = null; ShowPathStart(); await LoadAsync(); };
            HandleCreated += (s, e) => RoundCorners();
            RefreshInstalledState();
        }

        // 계약: 설치 위치에 exe 가 있으면 설치본이다 — 제목 밑 문구 · 「설치」/「업데이트」 · 「제거」 표시가 여기서 갈린다. --state 는 그것을 덮어쓴다
        private void RefreshInstalledState()
        {
            _installed = _testState == "fresh" ? null
                : _testState == "installed" ? (Installer.InstalledVersion() ?? "1.0.0.20261008")
                : Installer.InstalledVersion();
            _sub.Text = (_installed == null ? "이 위치에는 설치돼 있지 않습니다" : "설치된 버전 v" + _installed)
                        + (_testState != null ? "  [확인용 --state " + _testState + "]" : string.Empty);
            _install.Text = _installed == null ? "설치" : "업데이트";
            _remove.Visible = _installed != null;
            // 함정: 개발 PC 는 시작 프로그램 등록이 bin\ 의 개발 빌드를 가리킨다 — 거기에 업데이트하면 빌드 결과물이 지워지므로 막는다
            _devBuild = _installed != null && Installer.InstallDir.IndexOf("\\bin\\", StringComparison.OrdinalIgnoreCase) >= 0;
            if (_devBuild)
            {
                _sub.Text += "  · 개발 빌드 — 업데이트·제거 대상 아님";
                _remove.Visible = false;
            }
            UpdateInstallEnabled();
        }

        private void ApplyPath(string text)
        {
            var dir = (text ?? string.Empty).Trim().Trim('"');
            if (dir.Length == 0 || string.Equals(dir, Installer.InstallDir, StringComparison.OrdinalIgnoreCase))
            {
                _pathBox.Text = Installer.InstallDir;
                return;
            }

            Installer.InstallDir = dir;
            _pathBox.Text = dir;
            ShowPathStart();
            RefreshInstalledState();
        }

        // 함정: 긴 경로는 TextBox 가 끝부분부터 보인다 — Select(0,0) 만으로는 안 움직이고 ScrollToCaret 까지 불러야 앞머리가 보인다
        private void ShowPathStart()
        {
            _pathBox.Select(0, 0);
            _pathBox.ScrollToCaret();
        }

        private void ChoosePath()
        {
            var picked = ModernFolderPicker.Pick(this, "AI 통합 사용량 모니터를 설치할 폴더", Installer.InstallDir);
            if (picked == null)
            {
                return;
            }

            // 왜: 설치 폴더는 통째로 비우고 다시 푼다 — 남의 파일이 있는 폴더를 그대로 쓰면 지워진다
            if (System.IO.Directory.Exists(picked) && System.IO.Directory.EnumerateFileSystemEntries(picked).Any() && !System.IO.File.Exists(System.IO.Path.Combine(picked, Installer.ExeName)))
            {
                picked = System.IO.Path.Combine(picked, "AI-Usage-Monitor_JHJ");
            }

            ApplyPath(picked);
        }

        // 계약: 앱과 같은 어두운 제목줄 — 아이콘 · 제목 · ✕. 빈 자리를 끌면 창이 움직인다
        private Control BuildTitleBar()
        {
            var bar = new System.Windows.Forms.Panel { Dock = DockStyle.Top, Height = Px(44), BackColor = Bg };
            var icon = new PictureBox { Image = Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(Px(20), Px(20)), Location = new Point(Px(16), Px(12)) };
            var caption = new Label { Text = "AI 통합 사용량 모니터  ·  설치", ForeColor = Text2, AutoSize = true, Location = new Point(Px(44), Px(14)), Font = new Font(Font, FontStyle.Bold) };
            var close = new AccentButton { Text = "✕", Size = new Size(Px(36), Px(28)), Location = new Point(Px(404), Px(8)), Font = new Font("Segoe UI Symbol", 10f), TabStop = false };
            close.Click += (s, e) => Close();
            bar.Controls.Add(icon);
            bar.Controls.Add(caption);
            bar.Controls.Add(close);
            foreach (Control c in new Control[] { bar, icon, caption })
            {
                c.MouseDown += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        ReleaseCapture();
                        SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
                    }
                };
            }

            return bar;
        }

        // 왜: 테두리 없는 창은 Windows 11 에서도 각진다 — DWM 에 둥근 모서리를 청한다(지원 안 하면 무시된다)
        private void RoundCorners()
        {
            try
            {
                var preference = 2;
                DwmSetWindowAttribute(Handle, 33, ref preference, sizeof(int));
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Divider))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }

        private async Task LoadAsync()
        {
            try
            {
                var releases = await _client.GetReleasesAsync(_cts.Token);
                _versions.SetItems(releases.Cast<object>().ToArray());
                if (releases.Count == 0)
                {
                    _status.Text = "설치할 수 있는 릴리스가 없습니다.";
                    return;
                }

                // 계약: 고르지 않으면 정식(비 pre) 최신 버전 — --version 이 있으면 그 버전
                var wanted = releases.FirstOrDefault(r => _wantedVersion != null && (r.VersionText == _wantedVersion.TrimStart('v', 'V') || r.Version.ToString(3) == _wantedVersion.TrimStart('v', 'V')))
                             ?? releases.FirstOrDefault(r => !r.Prerelease) ?? releases[0];
                _versions.SelectedItem = wanted;
                _releasesLoaded = true;
                UpdateInstallEnabled();

                // 왜: --silent 는 앱 안의 「이 버전 설치」가 부른다 — 동의는 이미 앱이 받았다
                if (_silent)
                {
                    _consent.Checked = true;
                    await InstallAsync();
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _status.Text = "GitHub 에서 목록을 읽지 못했습니다: " + ex.Message;
            }
        }

        private void UpdateInstallEnabled()
        {
            _install.Enabled = _releasesLoaded && _consent.Checked && !_devBuild;
            if (_releasesLoaded)
            {
                _status.Text = _consent.Checked
                    ? $"{_versions.SelectedItem} 을(를) {_install.Text}합니다 — 맞으면 「{_install.Text}」를 누르십시오."
                    : "사용 정보 수집에 동의해야 설치할 수 있습니다.";
            }
        }

        private async Task InstallAsync()
        {
            if (!(_versions.SelectedItem is Release release) || !_consent.Checked)
            {
                return;
            }

            // 함정: --silent(앱 · winget)는 버튼을 거치지 않는다 — 개발 빌드 폴더가 대상이면 여기서도 막아야 bin\ 이 비워지지 않는다
            if (_devBuild)
            {
                _status.Text = "개발 빌드 폴더에는 설치하지 않습니다. 설치 위치를 바꿔 주십시오.";
                if (_silent)
                {
                    Environment.ExitCode = 2;
                    Close();
                }

                return;
            }

            _install.Enabled = false;
            _remove.Enabled = false;
            _pathChange.Enabled = false;
            try
            {
                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(ConsentPath));
                    System.IO.File.WriteAllText(ConsentPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " v" + release.Version.ToString(3) + " (setup)");
                }
                catch (System.IO.IOException) { }

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
                    return;
                }

                RefreshInstalledState();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                _status.Text = "설치하지 못했습니다: " + ex.Message;
                _install.Enabled = _consent.Checked;
                if (_silent)
                {
                    Environment.ExitCode = 1;
                }
            }
            finally
            {
                _remove.Enabled = true;
                _pathChange.Enabled = true;
            }
        }

        // 계약: 제거 — 앱 종료 · 설치 폴더 · 시작 메뉴 바로가기 · 시작 프로그램 등록을 지운다. 설정·이력은 물어보고 지운다
        private void Remove()
        {
            var answer = MessageBox.Show(this,
                "설치된 앱을 제거합니다.\n\n설정과 사용량 이력(%LOCALAPPDATA%\\AI-Usage-Monitor_JHJ)도 함께 지울까요?\n「예」= 모두 지움 · 「아니요」= 앱만 지우고 설정·이력은 남김",
                "제거", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Cancel)
            {
                return;
            }

            try
            {
                Installer.Uninstall(keepData: answer == DialogResult.No);
                _status.Text = answer == DialogResult.Yes ? "제거했습니다. 설정·이력도 지웠습니다." : "제거했습니다. 설정·이력은 남겨 두었습니다.";
                RefreshInstalledState();
            }
            catch (Exception ex)
            {
                _status.Text = "제거하지 못했습니다: " + ex.Message;
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

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>직접 그리는 체크박스 — 둥근 네모, 체크되면 강조색 채움 + 흰 체크.</summary>
        private sealed class AccentCheck : Control
        {
            private bool _checked;
            private bool _hover;

            public AccentCheck()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
                Height = Px(24);
                Width = Px(300);
                ForeColor = Text1;
            }

            public bool Checked
            {
                get => _checked;
                set
                {
                    if (_checked == value) return;
                    _checked = value;
                    Invalidate();
                    CheckedChanged?.Invoke(this, EventArgs.Empty);
                }
            }

            public event EventHandler CheckedChanged;

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                Checked = !Checked;
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            // 왜: 글꼴은 부모에 붙은 뒤에야 정해진다 — 글·글꼴·부모가 바뀔 때마다 폭을 다시 잰다
            protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); }

            protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); }

            protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); Fit(); }

            private void Fit()
            {
                Width = Px(36) + TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent?.BackColor ?? Bg);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var b = Px(18);
                var box = new Rectangle(1, (Height - b) / 2, b, b);
                using (var path = Rounded(box, Px(5)))
                {
                    if (_checked)
                    {
                        using (var fill = new SolidBrush(_hover ? AccentHover : Accent)) e.Graphics.FillPath(fill, path);
                        using (var pen = new Pen(Color.White, 2.2f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                        {
                            e.Graphics.DrawLines(pen, new[] { new PointF(box.X + 4.5f * S, box.Y + 9.5f * S), new PointF(box.X + 8f * S, box.Y + 13f * S), new PointF(box.X + 13.5f * S, box.Y + 5.5f * S) });
                        }
                    }
                    else
                    {
                        using (var fill = new SolidBrush(_hover ? PanelHover : Panel)) e.Graphics.FillPath(fill, path);
                        using (var pen = new Pen(_hover ? Text2 : Divider, 1.2f)) e.Graphics.DrawPath(pen, path);
                    }
                }

                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Px(28), 0, Width - Px(28), Height), ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }
        }

        /// <summary>평평한 버튼 — Primary 는 강조색 채움, 아니면 패널색. 비활성은 흐리게.</summary>
        private sealed class AccentButton : Button
        {
            private bool _hover;

            public bool Primary { get; set; }

            public AccentButton()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                Cursor = Cursors.Hand;
                ForeColor = Text1;
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent?.BackColor ?? Bg);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var fill = Primary ? (Enabled ? (_hover ? AccentHover : Accent) : Color.FromArgb(90, 48, 48)) : (_hover ? PanelHover : Panel);
                var text = Enabled ? Text1 : Text2;
                using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 6))
                using (var brush = new SolidBrush(fill))
                {
                    e.Graphics.FillPath(brush, path);
                }

                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        /// <summary>얇은 진행 막대 — 둥근 홈 위에 강조색 채움. 기본 ProgressBar 는 색을 못 바꾼다.</summary>
        private sealed class SlimProgress : Control
        {
            private int _value;

            public SlimProgress()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            public int Value
            {
                get => _value;
                set { _value = Math.Max(0, Math.Min(100, value)); Invalidate(); }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent?.BackColor ?? Bg);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var track = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2))
                using (var brush = new SolidBrush(Panel))
                {
                    e.Graphics.FillPath(brush, track);
                }

                var w = (int)Math.Round((Width - 1) * _value / 100.0);
                if (w > Height)
                {
                    using (var fill = Rounded(new Rectangle(0, 0, w, Height - 1), Height / 2))
                    using (var brush = new SolidBrush(Accent))
                    {
                        e.Graphics.FillPath(brush, fill);
                    }
                }
            }
        }

        private sealed class HairLine : Control
        {
            public HairLine() { Height = 1; }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (var brush = new SolidBrush(Divider)) e.Graphics.FillRectangle(brush, ClientRectangle);
            }
        }

        /// <summary>직접 그리는 선택 상자 — 둥근 패널 + 선택값 + 꺾쇠. 누르면 어두운 메뉴로 목록이 열린다. 기본 ComboBox 는 초점 반전·흰 화살표를 못 없앤다.</summary>
        private sealed class AccentDropDown : Control
        {
            private object[] _items = new object[0];
            private object _selected;
            private bool _hover;
            private readonly ContextMenuStrip _menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false, Renderer = new DarkRenderer(), BackColor = Panel, ForeColor = Text1 };

            public AccentDropDown()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                Cursor = Cursors.Hand;
                Height = Px(34);
                TabStop = false;
            }

            public object SelectedItem
            {
                get => _selected;
                set { _selected = value; Invalidate(); SelectedChanged?.Invoke(this, EventArgs.Empty); }
            }

            public event EventHandler SelectedChanged;

            public void SetItems(object[] items)
            {
                _items = items;
                _menu.Items.Clear();
                foreach (var item in items)
                {
                    var entry = new ToolStripMenuItem(item.ToString()) { Tag = item, ForeColor = Text1, Padding = new Padding(4, 4, 4, 4) };
                    entry.Click += (s, e) => SelectedItem = ((ToolStripMenuItem)s).Tag;
                    _menu.Items.Add(entry);
                }
            }

            protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); _menu.Font = Font; }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                if (Enabled && _items.Length > 0)
                {
                    _menu.MinimumSize = new Size(Width, 0);
                    _menu.Show(this, new Point(0, Height + 2));
                }
            }

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Parent?.BackColor ?? Bg);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 6))
                {
                    using (var fill = new SolidBrush(_hover && Enabled ? PanelHover : Panel)) e.Graphics.FillPath(fill, path);
                    using (var pen = new Pen(_hover && Enabled ? Text2 : Divider, 1f)) e.Graphics.DrawPath(pen, path);
                }

                TextRenderer.DrawText(e.Graphics, _selected?.ToString() ?? string.Empty, Font, new Rectangle(Px(12), 0, Width - Px(44), Height),
                    Enabled ? Text1 : Text2, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                using (var pen = new Pen(Enabled ? Text2 : Divider, 1.8f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    var cx = Width - 20f * S;
                    var cy = Height / 2f;
                    e.Graphics.DrawLines(pen, new[] { new PointF(cx - 5f * S, cy - 2.5f * S), new PointF(cx, cy + 2.5f * S), new PointF(cx + 5f * S, cy - 2.5f * S) });
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _menu.Dispose();
                base.Dispose(disposing);
            }
        }

        private sealed class DarkRenderer : ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        }

        private sealed class DarkColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected => PanelHover;
            public override Color MenuItemBorder => PanelHover;
            public override Color MenuItemSelectedGradientBegin => PanelHover;
            public override Color MenuItemSelectedGradientEnd => PanelHover;
            public override Color MenuItemPressedGradientBegin => PanelHover;
            public override Color MenuItemPressedGradientEnd => PanelHover;
            public override Color ToolStripDropDownBackground => Panel;
            public override Color ImageMarginGradientBegin => Panel;
            public override Color ImageMarginGradientMiddle => Panel;
            public override Color ImageMarginGradientEnd => Panel;
            public override Color MenuBorder => Divider;
            public override Color SeparatorDark => Divider;
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// 탐색기 모양의 폴더 선택창(IFileOpenDialog · 폴더 선택 모드) — 주소줄에 경로를 붙여 넣을 수 있다.
    /// 왜: WinForms 기본 FolderBrowserDialog 는 옛 트리 창이라 경로 입력이 안 된다. COM 이 막히면 그 창으로 물러선다.
    /// </summary>
    internal static class ModernFolderPicker
    {
        public static string Pick(IWin32Window owner, string title, string initialDir)
        {
            try
            {
                var dialog = (IFileDialog)new FileOpenDialogRCW();
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_NOCHANGEDIR);
                dialog.SetTitle(title);
                if (!string.IsNullOrEmpty(initialDir) && System.IO.Directory.Exists(initialDir))
                {
                    var iid = typeof(IShellItem).GUID;
                    SHCreateItemFromParsingName(initialDir, IntPtr.Zero, ref iid, out var folder);
                    dialog.SetFolder(folder);
                }

                if (dialog.Show(owner.Handle) != 0)
                {
                    return null;
                }

                dialog.GetResult(out var result);
                result.GetDisplayName(SIGDN_FILESYSPATH, out var path);
                return path;
            }
            catch (COMException)
            {
                using (var fallback = new FolderBrowserDialog { Description = title, SelectedPath = initialDir, ShowNewFolderButton = true })
                {
                    return fallback.ShowDialog(owner) == DialogResult.OK ? fallback.SelectedPath : null;
                }
            }
        }

        private const uint FOS_PICKFOLDERS = 0x20;
        private const uint FOS_FORCEFILESYSTEM = 0x40;
        private const uint FOS_NOCHANGEDIR = 0x8;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialogRCW { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr filterSpec);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
            void AddPlace(IShellItem item, int where);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem item);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
            void GetAttributes(uint mask, out uint attributes);
            void Compare(IShellItem other, uint hint, out int order);
        }
    }
}
