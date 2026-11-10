using System;
using System.IO;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Appearance;
using NetGuardian.Core.Connectivity;
using NetGuardian.Core.Localization;
using NetGuardian.Core.Logging;
using NetGuardian.Core.Network;
using NetGuardian.Core.Settings;
using NetGuardian.Core.SystemCommands;
using NetGuardian.Core.Wifi;
using NetGuardian.App.Presentation;
using NetGuardian.App.Updates;

namespace NetGuardian.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private ServiceProvider? _services;
    private static string _configuredTheme = "auto";

    protected override void OnStartup(StartupEventArgs e)
    {
        using var maintenance = UpdateMaintenanceGate.TryEnter();
        if (maintenance is null)
        {
            Shutdown();
            return;
        }
        _singleInstanceMutex = new Mutex(true, @"Global\NetGuardian.UI", out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        _ownsSingleInstanceMutex = true;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        base.OnStartup(e);

        _services = BuildServices();
        ApplicationThemeManager.Changed += OnApplicationThemeChanged;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        ApplyConfiguredTheme(_services.GetRequiredService<NetGuardian.Core.Settings.SettingsStore>().Load().Theme);
        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        // 主窗口拥有 HWND 后，自动主题才能绑定系统消息监听。
        ApplyConfiguredTheme(_services.GetRequiredService<SettingsStore>().Load().Theme);
    }

    /// <summary>优先遵循系统高对比，其余情况保留用户选择。</summary>
    public static void ApplyConfiguredTheme(string themeMode)
    {
        _configuredTheme = themeMode;
        if (Current?.MainWindow is { IsLoaded: true })
        {
            SystemThemeWatcher.UnWatch(Current.MainWindow);
        }

        // 高对比明确传给库，避免依赖自定义系统主题文件名识别。
        AppThemePolicy.Apply(themeMode, SystemParameters.HighContrast,
            theme => ApplicationThemeManager.Apply(theme), () => ApplicationThemeManager.ApplySystemTheme());
        if (!SystemParameters.HighContrast && themeMode is not ("light" or "dark") && Current?.MainWindow is { IsLoaded: true })
            SystemThemeWatcher.Watch(Current.MainWindow);
    }

    private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (null or "" or nameof(SystemParameters.HighContrast))) return;
        var application = Current;
        if (application is null || application.Dispatcher.HasShutdownStarted) return;
        // 系统消息处理完成后再读取状态；显式浅色/深色也必须响应高对比切换。
        application.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!application.Dispatcher.HasShutdownStarted) ApplyConfiguredTheme(_configuredTheme);
        }));
    }

    /// <summary>主题切换时同步自定义语义资源。</summary>
    private static void OnApplicationThemeChanged(ApplicationTheme currentTheme, System.Windows.Media.Color systemAccent)
    {
        var palette = AppThemePolicy.CreatePalette(currentTheme);
        var dictionaries = Current.Resources.MergedDictionaries;
        for (var index = 0; index < dictionaries.Count; index++)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source is not null && source.Contains("Palette.", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries[index] = palette;
                return;
            }
        }

        dictionaries.Add(palette);
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<Localizer>();
        // 备用客户端参数与注册接口同型，必须显式构造以避免容器注入自身。
        services.AddSingleton<IWifiClient>(_ => new WifiClient());
        services.AddSingleton<IConnectivityTester, ConnectivityTesterService>();
        services.AddSingleton<ISystemCommandRunner, ProcessCommandRunner>();
        services.AddSingleton<NetworkConfigurationService>();
        services.AddSingleton<WebsiteTestService>();
        services.AddSingleton<PublicIpInfoService>();
        services.AddSingleton<NetworkSpeedTestService>();
        services.AddSingleton(provider => new FileLogSink(
            retentionDays: provider.GetRequiredService<SettingsStore>().Load().LogRetentionDays));
        services.AddSingleton<ILogSink>(provider => provider.GetRequiredService<FileLogSink>());
        services.AddSingleton<EventJournal>();
        services.AddSingleton<Wpf.Ui.ISnackbarService, Wpf.Ui.SnackbarService>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        ApplicationThemeManager.Changed -= OnApplicationThemeChanged;
        _services?.Dispose();
        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        NotifyUserOfCrash();
        e.Handled = true;
    }

    /// <summary>Surfaces an unhandled UI-thread exception to the user instead of failing silently.</summary>
    private static void NotifyUserOfCrash()
    {
        try
        {
            if (Current?.MainWindow is MainWindow window)
            {
                window.NotifyUnhandledError();
                return;
            }
        }
        catch
        {
            // fall through to a plain message box
        }

        try
        {
            System.Windows.MessageBox.Show(
                "NetGuardian ran into a problem. Details were written to the log.",
                "NetGuardian",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch
        {
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog(e.Exception);
        e.SetObserved();
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog(exception);
        }
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            var directory = Path.Combine(
                NetGuardian.Core.Runtime.GuardianPaths.DefaultDirectory,
                "logs");
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "crash.log"),
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
