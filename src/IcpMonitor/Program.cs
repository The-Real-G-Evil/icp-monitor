using System.Security.Principal;

namespace IcpMonitor;

internal static class Program
{
    /// <summary>Wird beim Neustart als Administrator uebergeben: auf das Ende der alten Instanz warten.</summary>
    public const string RestartArgument = "--restarted";

    /// <summary>Oeffnet beim Start direkt das Einstellungsfenster.</summary>
    public const string SettingsArgument = "--settings";

    private const string MutexName = @"Local\IcpMonitor.SingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        // "--autostart on|off": nur Autostart einrichten/entfernen und beenden (vom Tray-Menue erhoeht gestartet und vom Installer).
        int autostartIndex = Array.IndexOf(args, Autostart.Argument);
        if (autostartIndex >= 0)
        {
            bool enable = autostartIndex + 1 < args.Length && args[autostartIndex + 1].Equals("on", StringComparison.OrdinalIgnoreCase);
            return Autostart.Apply(enable);
        }

        AppConfig config = AppConfig.Load();
        L.SetLanguage(config.Language);

        using var mutex = new Mutex(false, MutexName);
        if (!TryAcquire(mutex, args.Contains(RestartArgument) ? TimeSpan.FromSeconds(10) : TimeSpan.Zero))
        {
            MessageBox.Show(L.AlreadyRunning, "ICP Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }

        try
        {
            Application.ThreadException += (_, e) => Log.Error("Unbehandelter Fehler (UI)", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unbehandelter Fehler", e.ExceptionObject as Exception);
            ApplicationConfiguration.Initialize();

            bool isAdmin = IsAdmin();
            Log.Info($"ICP Monitor {Application.ProductVersion} startet (Administrator: {isAdmin}).");

            using var app = new MonitorApp(config);
            app.Start();
            using var tray = new TrayContext(app, isAdmin);
            if (args.Contains(SettingsArgument)) tray.ShowSettings();
            Application.Run(tray);

            Log.Info("ICP Monitor beendet.");
            return 0;
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static bool TryAcquire(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true; // Vorgaenger ist abgestuerzt, Mutex gehoert jetzt uns.
        }
    }

    public static bool IsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
