using System;
using System.IO;
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

namespace NetGuardian.App;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
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
        ApplyConfiguredTheme(_services.GetRequiredService<NetGuardian.Core.Settings.SettingsStore>().Load().Theme);
        var mainWindow = _services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>Applies the configured theme mode: "auto" follows the OS, otherwise explicit light/dark.</summary>
    public static void ApplyConfiguredTheme(string themeMode)
    {
        if (Current?.MainWindow is not null)
        {
            SystemThemeWatcher.UnWatch(Current.MainWindow);
        }

        switch (themeMode)
        {
            case "light":
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case "dark":
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme();
                if (Current?.MainWindow is not null)
                {
                    SystemThemeWatcher.Watch(Current.MainWindow);
                }

                break;
        }
    }

    /// <summary>Keeps the custom semantic palette in sync when the WPF-UI theme flips.</summary>
    private static void OnApplicationThemeChanged(ApplicationTheme currentTheme, System.Windows.Media.Color systemAccent)
    {
        var palette = currentTheme == ApplicationTheme.Light ? "Palette.Light.xaml" : "Palette.Dark.xaml";
        var uri = new Uri($"Themes/{palette}", UriKind.Relative);
        var dictionaries = Current.Resources.MergedDictionaries;
        for (var index = 0; index < dictionaries.Count; index++)
        {
            var source = dictionaries[index].Source?.OriginalString;
            if (source is not null && source.Contains("Palette.", StringComparison.OrdinalIgnoreCase))
            {
                dictionaries[index] = new ResourceDictionary { Source = uri };
                return;
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = uri });
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
