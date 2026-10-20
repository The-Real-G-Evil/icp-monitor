using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace IcpMonitor;

/// <summary>
/// Autostart ueber die Windows-Aufgabenplanung: startet die App bei der Anmeldung mit hoechsten Rechten,
/// also ohne UAC-Abfrage und mit allen Sensoren. Anlegen/Loeschen der Aufgabe braucht Administratorrechte;
/// ohne diese startet sich die App kurz selbst erhoeht mit <see cref="Argument"/>.
/// </summary>
/// <remarks>
/// Sicherheit: Die Aufgabe wird direkt ueber die COM-Schnittstelle der Aufgabenplanung registriert (keine
/// Zwischendatei, die ein anderes Programm austauschen koennte), und nur fuer eine exe unter "Programme" -
/// eine exe an einem fuer normale Benutzer beschreibbaren Ort liesse sich sonst gegen Schadcode tauschen,
/// der dann bei jeder Anmeldung mit Adminrechten liefe.
/// </remarks>
internal static class Autostart
{
    public const string Argument = "--autostart";
    private const string TaskName = "ICP Monitor";
    private const int TaskCreateOrUpdate = 6;
    private const int TaskLogonInteractiveToken = 3;
    private const int ErrorFileNotFound = unchecked((int)0x80070002);

    /// <summary>Nur die installierte Version (unter "Programme", nur fuer Admins beschreibbar) darf in den Autostart.</summary>
    public static bool IsAllowedLocation
    {
        get
        {
            string exe = Path.GetFullPath(Environment.ProcessPath!);
            return new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 }
                .Select(Environment.GetFolderPath)
                .Where(dir => dir.Length > 0)
                .Any(dir => exe.StartsWith(Path.TrimEndingDirectorySeparator(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            RootFolder().GetTask(TaskName);
            return true;
        }
        // Die COM-Anbindung uebersetzt 0x80070002 in eine FileNotFoundException.
        catch (Exception ex) when (ex is FileNotFoundException || ex.HResult == ErrorFileNotFound)
        {
            return false;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            // Z. B. Zugriff verweigert: Die Aufgabe existiert, ist fuer diesen Prozess aber nicht lesbar.
            return true;
        }
    }

    /// <summary>Schaltet den Autostart um; fragt bei Bedarf per UAC nach Rechten. null = UAC-Abfrage abgebrochen.</summary>
    public static bool? Set(bool enabled)
    {
        if (Program.IsAdmin()) return Apply(enabled) == 0;
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"{Argument} {(enabled ? "on" : "off")}",
                UseShellExecute = true,
                Verb = "runas",
            });
            process?.WaitForExit(30_000);
            return process?.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return null;
        }
    }

    /// <summary>Wird mit Administratorrechten ausgefuehrt (<c>IcpMonitor.exe --autostart on|off</c>). Rueckgabe = Exit-Code.</summary>
    public static int Apply(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                if (!IsEnabled()) return 0;
                RootFolder().DeleteTask(TaskName, 0);
                Log.Info("Autostart entfernt.");
                return 0;
            }

            if (!IsAllowedLocation)
            {
                Log.Warn($"Autostart abgelehnt: {Environment.ProcessPath} liegt nicht unter \"Programme\".");
                return 2;
            }
            RootFolder().RegisterTask(TaskName, BuildTaskXml(Environment.ProcessPath!), TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);
            Log.Info($"Autostart eingerichtet fuer {Environment.ProcessPath}.");
            return 0;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            Log.Error("Autostart konnte nicht geaendert werden", ex);
            return 1;
        }
    }

    private static dynamic RootFolder()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
        service.Connect();
        return service.GetFolder("\\");
    }

    /// <summary>
    /// Aufgabe: bei Anmeldung dieses Benutzers, hoechste Rechte, ohne Zeitlimit, auch im Akkubetrieb,
    /// nur eine Instanz. Kurze Verzoegerung, damit USB-Geraete bereit sind.
    /// </summary>
    private static string BuildTaskXml(string exePath)
    {
        string user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        string exe = SecurityElement.Escape(exePath);
        string dir = SecurityElement.Escape(Path.GetDirectoryName(exePath)!);
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Startet ICP Monitor bei der Anmeldung.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT5S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
                <Enabled>true</Enabled>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{exe}</Command>
                  <WorkingDirectory>{dir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }
}
