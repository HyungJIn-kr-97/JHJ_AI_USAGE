using System.IO;
using System.Reflection;
using System.Windows;
using costats.App.Services;
using costats.App.Services.Updates;
using costats.App.ViewModels;
using costats.Application.Abstractions;
using costats.Application.Pulse;
using costats.Application.Security;
using costats.Application.Settings;
using costats.Infrastructure.Providers;
using costats.Infrastructure.Pulse;
using costats.Infrastructure.Security;
using costats.Infrastructure.Settings;
using costats.Infrastructure.Time;
using Jhj.Core.App;
using Jhj.Core.Settings;
using Jhj.Core.Wpf.Boot;
using Jhj.Core.Wpf.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;

namespace costats.App
{
    public partial class App : System.Windows.Application
    {
        private IHost? _host;
        private SingleInstance? _singleInstance;
        private GithubUpdateService? _updateCoordinator;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);

            // 계약: --export-icon <폴더> 는 기본 아이콘을 .ico 로 뽑고 끝낸다 — 800.Deploy\make-icons.ps1 이 쓴다
            // 왜: 설치 물음·데이터 이사보다 먼저 갈라야 한다 — 아이콘만 뽑는데 창이 뜨면 안 된다
            if (TryExportIcon(e.Args))
            {
                Shutdown(0);
                return;
            }

            // 계약: 이 앱만의 이사 규칙 — 계정 목록이 폴더를 절대경로로 들고 있고, 포크 원본 이름의 죽은 줄도 치운다
            LegacyMigration.PathHolders = ["", "accounts", "accounts-codex"];
            LegacyMigration.ExtraRetiredNames = ["costats"];
            SelfInstaller.LegacyShortcutNames = ["AI 통합 사용량 모니터.lnk"];

            // ①~③ 신원 → 옛 이름 이사 → 테마 브러시(창보다 먼저 — 키가 없으면 창 XAML 로딩이 실패한다)
            ThemeManager.Extend = ThemeExtras.Add;
            JhjBoot.Begin(new JhjApp
            {
                Name = "JHJ_AI-Usage-Monitor",
                DisplayName = "JHJ AI 통합 사용량 모니터",
                DefaultPalette = JhjPalettes.Bull,
                // 계약: 공개 좌표는 UpdateOptions 상수 한 곳에서 온다 — 「앱 공유하기」·바닥 링크는 Core AppLinks 가 여기서 주소를 짓는다(030 규격 §4-1)
                Repository = UpdateOptions.DefaultRepository,
                SetupAssetName = UpdateOptions.SetupAssetName,
                WingetId = UpdateOptions.WingetId,
                PackagePrefix = UpdateOptions.PackagePrefix,
                AuthorEmail = "gudwls9730@gmail.com",
                // 계약: 오래된 것부터 — 두 세대 전에 머문 PC 도 한 번에 지금 이름까지 온다
                LegacyNames = ["costats-jhj", "AiUsageMonitor", "AI-Usage-Monitor_JHJ"],
                LegacyExecutableNames = ["AI-Usage-Monitor_JHJ.exe", "AiUsageMonitor.exe"],
            });

            JhjBoot.BootstrapLogger();
            RegisterExceptionHandlers();

            Loc.Register(CoreStrings.Catalog);
            Loc.Register(costats.App.Localization.LocStrings.Catalog);

            if (SelfInstaller.TryInstallAndRelaunch(ConfirmInstall, WarnInstallFailed))
            {
                Shutdown(0);
                return;
            }

            if (JhjBoot.TryHandOffToRunningInstance(out _singleInstance))
            {
                Shutdown(0);
                return;
            }

            _ = InitializeAsync();
        }

        protected override async void OnExit(System.Windows.ExitEventArgs e)
        {
            Log.Information("Application exiting (ExitCode={ExitCode})", e.ApplicationExitCode);
            try
            {
                if (_host is not null)
                {
                    await _host.StopAsync();
                    _host.Dispose();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during host shutdown");
            }

            _singleInstance?.Dispose();
            Log.CloseAndFlush();
            base.OnExit(e);
        }

        private async Task InitializeAsync()
        {
            try
            {
                var startupConfiguration = BuildStartupConfiguration();
                _updateCoordinator = new GithubUpdateService(UpdateOptions.FromConfiguration(startupConfiguration));
                var settingsStore = new JsonSettingsStore();
                // 왜: 설정을 못 읽는 결함도 업데이트로 고칠 수 있어야 한다 — 읽기에 실패하면 자동 업데이트를 켠 것으로 보고 적용부터 한다
                AppSettings? earlySettings = null;
                try
                {
                    earlySettings = await settingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Settings load failed before update check");
                }

                var autoUpdate = earlySettings?.AutoUpdateEnabled ?? true;
                if (autoUpdate && await _updateCoordinator.TryApplyPendingUpdateAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    Log.Information("Pending update is being applied, shutting down for update");
                    await Dispatcher.InvokeAsync(() => Shutdown(0));
                    return;
                }

                var settings = earlySettings ?? await settingsStore.LoadAsync(CancellationToken.None).ConfigureAwait(false);
                if (settings.MoveLegacyTrayIconDefault())
                {
                    await settingsStore.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false);
                }

                // 계약: 작업 관리자 「시작 앱」과 양방향으로 맞춘다 — 거기서 껐으면 설정을 끄고, 거기서 켰으면 설정을 켠다. 그 밖에는 설정대로 낡은 경로를 바로잡는다
                var registeredNow = costats.App.ViewModels.SettingsViewModel.GetStartupRegistryValue();
                if (settings.StartAtLogin && costats.App.ViewModels.SettingsViewModel.IsStartupDisabledByTaskManager())
                {
                    settings.StartAtLogin = false;
                    await settingsStore.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false);
                }
                else if (!settings.StartAtLogin && registeredNow)
                {
                    settings.StartAtLogin = true;
                    await settingsStore.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false);
                }
                else
                {
                    // 왜: 꺼진 상태에서도 「시작 앱」에 줄이 보여야 거기서 켤 수 있다 — 경로도 함께 바로잡는다
                    costats.App.ViewModels.SettingsViewModel.SetStartupRegistryValue(settings.StartAtLogin);
                }

                // 계약: 실행 파일 이름이 바뀐 업데이트 뒤 시작 메뉴 바로가기를 지금 exe 로 다시 쓴다
                SelfInstaller.RefreshShortcutIfStale();

                await Dispatcher.InvokeAsync(() =>
                {
                    ThemeManager.ApplyPreference(settings.Palette, settings.Theme);
                    // 계약: 명암이 "system" 인 동안 Windows 의 밝게/어둡게 전환을 따라간다
                    ThemeManager.FollowSystem();
                    Loc.SetLanguage(settings.Language);
                    var tray = InitializeHost(settingsStore, settings);
                    LogFireAndForget(StartListenerAsync(tray), "SingleInstanceListener");
                    tray.ShowWidget();
                });

                if (_updateCoordinator is not null && settings.AutoUpdateEnabled)
                {
                    // Use a timeout so a stalled download never holds the semaphore forever
                    var backgroundCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    LogFireAndForget(
                        Task.Run(() => _updateCoordinator.CheckAndStageUpdateAsync(backgroundCts.Token)),
                        "UpdateCheck");
                }

            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Startup failed");
                System.Windows.MessageBox.Show(
                    $"Startup error: {ex.Message}\n\n{ex.StackTrace}",
                    "JHJ AI Usage Monitor",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        private static IConfiguration BuildStartupConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
                .Build();
        }

        /// <summary>
        /// Bootstraps Serilog before the host is built so that startup and
        /// exception-handler logs reach the file sink even if host init fails.
        /// The host builder replaces this logger with the fully-configured one.
        /// </summary>
        // 계약: 창이 만들어지기 전에 한 번은 불러야 한다 — 테마 키가 없으면 창 XAML 로딩이 실패한다
        /// <summary>--export-icon &lt;폴더&gt; 를 받았으면 기본 아이콘을 .ico 로 뽑고 true.</summary>
        // 계약: 뽑는 파일 이름은 리소스와 같다 — tray-icon.ico(앱) · setup-icon.ico(설치 관리자)
        private static bool TryExportIcon(string[] args)
        {
            var at = Array.FindIndex(args, a => string.Equals(a, "--export-icon", StringComparison.OrdinalIgnoreCase));
            if (at < 0 || at + 1 >= args.Length)
            {
                return false;
            }

            var dir = args[at + 1];
            foreach (var name in new[] { "tray-icon.ico", "setup-icon.ico" })
            {
                TrayIconRenderer.SaveIcoFile(System.IO.Path.Combine(dir, name));
            }

            return true;
        }

        // 계약: 묻고 알리는 것은 앱 몫이다 — Core 의 SelfInstaller 는 WPF 를 모른다
        private static bool ConfirmInstall(string installDir) =>
            System.Windows.MessageBox.Show(
                $"{Loc.T("Install to this PC and run?")}\n\n{installDir}\n\n{Loc.T("Choosing No runs it from here without installing.")}",
                JhjApp.Current.DisplayName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;

        private static void WarnInstallFailed(string message) =>
            System.Windows.MessageBox.Show(
                $"{Loc.T("Could not install. Running from this location instead.")}\n\n{message}",
                JhjApp.Current.DisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

        private void RegisterExceptionHandlers()
        {
            DispatcherUnhandledException += (_, args) =>
            {
                Log.Error(args.Exception, "Unhandled UI exception");
                args.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Log.Error(args.Exception, "Unobserved task exception");
                args.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    Log.Fatal(ex, "Unhandled domain exception (IsTerminating={IsTerminating})", args.IsTerminating);
                    Log.CloseAndFlush();
                }
            };

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                Log.CloseAndFlush();
            };
        }

        /// <summary>
        /// Observes a fire-and-forget task so exceptions are logged instead
        /// of silently swallowed or deferred to the finalizer.
        /// </summary>
        private static void LogFireAndForget(Task task, string operationName)
        {
            task.ContinueWith(
                t => Log.Error(t.Exception!.GetBaseException(), "Background task {Operation} faulted", operationName),
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private TrayHost InitializeHost(ISettingsStore settingsStore, AppSettings settings)
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration(config =>
                {
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);
                })
                .UseSerilog((context, services, loggerConfig) =>
                {
                    loggerConfig
                        .ReadFrom.Configuration(context.Configuration)
                        .Enrich.FromLogContext();
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ISettingsStore>(settingsStore);
                    services.AddSingleton(settings);
                    // 계약: 예약 업데이트는 같은 AppSettings 를 계약 모양으로 본다
                    services.AddSingleton<IScheduledUpdateSettings>(settings);

                    if (_updateCoordinator is not null)
                    {
                        services.AddSingleton(_updateCoordinator);
                        services.AddSingleton<ScheduledUpdateService>();
                    }

                    services.AddOptions<PulseOptions>()
                        .Configure<AppSettings>((options, appSettings) =>
                        {
                            var minutes = Math.Max(1, appSettings.RefreshMinutes);
                            options.RefreshInterval = TimeSpan.FromMinutes(minutes);
                        });

                    services.AddSingleton<IClock, SystemClock>();

                    services.AddSingleton<PulseBroadcaster>();
                    services.AddSingleton<ISourceSelector, SourceSelector>();
                    services.AddSingleton<CopilotUsageFetcher>();
                    services.AddSingleton<ISignalSource, CodexLogSource>();
                    foreach (var codexAccount in CodexAccountStore.List())
                    {
                        services.AddSingleton<ISignalSource>(new CodexLogSource(codexAccount, CodexAccountStore.DirOf(codexAccount)));
                    }
                    services.AddSingleton<ISignalSource, CopilotPersonalSource>();
                    services.AddSingleton<ISignalSource, GeminiLogSource>();
                    // Multicc integration: conditionally register per-profile or default Claude source
                    services.AddSingleton<MulticcConfigReader>();

                    var tempReader = new MulticcConfigReader(
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<MulticcConfigReader>.Instance);
                    var discovery = new MulticcDiscoveryService(tempReader, settings.MulticcConfigPath ?? AccountProfileStore.RootDir);

                    services.AddSingleton<IMulticcDiscovery>(discovery);

                    if (settings.MulticcEnabled && discovery.IsDetected && discovery.Profiles.Count > 0)
                    {
                        ClaudeProgramRouter.Configure(
                            "claude:" + AccountProfileStore.DefaultName,
                            settings.ProgramAccounts,
                            discovery.Profiles,
                            settings.ProgramAccountPins);
                        if (settings.MulticcSelectedProfile is not null)
                        {
                            // Single-profile mode: register one source for the selected profile
                            var selected = discovery.Profiles
                                .FirstOrDefault(p => p.Name.Equals(settings.MulticcSelectedProfile, StringComparison.OrdinalIgnoreCase));

                            if (selected is not null)
                            {
                                services.AddSingleton<ISignalSource>(new MulticcClaudeLogSource(selected));
                            }
                            else
                            {
                                // Fallback to default Claude source if selected profile not found
                                services.AddSingleton<ISignalSource, ClaudeLogSource>();
                            }
                        }
                        else
                        {
                            // Stacked mode: register one source per profile
                            foreach (var profile in discovery.Profiles)
                            {
                                services.AddSingleton<ISignalSource>(new MulticcClaudeLogSource(profile));
                            }
                        }
                    }
                    else
                    {
                        // No multicc or disabled: use default Claude source
                        services.AddSingleton<ISignalSource, ClaudeLogSource>();
                    }
                    services.AddSingleton<IPulseSnapshotWriter, JsonPulseSnapshotWriter>();
                    services.AddSingleton<IPulseOrchestrator, PulseOrchestrator>();
                    services.AddHostedService(sp => (PulseOrchestrator)sp.GetRequiredService<IPulseOrchestrator>());

                    services.AddSingleton<ICredentialVault, CredentialVault>();
                    services.AddSingleton<IGlassBackdropService, GlassBackdropService>();

                    services.AddSingleton<PulseViewModel>();
                    services.AddSingleton<SettingsViewModel>();
                    services.AddSingleton<GlassWidgetWindow>();
                    services.AddSingleton<TaskbarPositionService>();
                    services.AddSingleton<TrayHost>();
                    services.AddSingleton<HotkeyService>();
                })
                .Build();

            _host.Start();

            var lifetime = _host.Services.GetRequiredService<IHostApplicationLifetime>();
            lifetime.ApplicationStopping.Register(() => Log.Warning("Host is stopping"));

            _ = _host.Services.GetRequiredService<HotkeyService>();
            var tray = _host.Services.GetRequiredService<TrayHost>();
            if (_host.Services.GetService<ScheduledUpdateService>() is { } scheduledUpdate)
            {
                scheduledUpdate.Notify = tray.ShowBalloon;
            }

            return tray;
        }

        private async Task StartListenerAsync(TrayHost tray)
        {
            if (_singleInstance is null)
            {
                return;
            }

            await _singleInstance.StartListenerAsync(
                _ => Dispatcher.InvokeAsync(() => tray.ShowWidget()).Task,
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
