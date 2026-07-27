using System.Windows;
using Velopack;

namespace WPlayer;

public class App : System.Windows.Application
{
    private const string MutexName = "Local\\WPlayer.SingleInstance";
    private const string ActivationEventName = "Local\\WPlayer.ShowSettings";
    private Mutex? _mutex;
    private EventWaitHandle? _activationEvent;
    private RegisteredWaitHandle? _activationRegistration;

    [STAThread]
    private static void Main()
    {
        if (!PackageIdentity.IsPackaged)
        {
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ =>
                {
                    StartupRegistration.SetEnabled(false);
                    AppConfig.DeleteLocalData();
                })
                .Run();
        }

        var app = new App();
        app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) => AppLog.Error("Unhandled UI exception", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog.Error("Unhandled process exception", args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
        TaskScheduler.UnobservedTaskException += (_, args) => AppLog.Error("Unobserved task exception", args.Exception);

        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName);
        _mutex = new Mutex(true, MutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            _activationEvent.Set();
            Shutdown();
            return;
        }

        var config = AppConfig.Load();
        StartupRegistration.SetEnabled(config.LaunchAtStartup);

        base.OnStartup(e);

        var mainWindow = new MainWindow(config);
        mainWindow.Show();

        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            (_, _) => _ = Dispatcher.InvokeAsync(mainWindow.ShowSettings),
            null,
            Timeout.Infinite,
            false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationRegistration?.Unregister(null);
        _activationEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
