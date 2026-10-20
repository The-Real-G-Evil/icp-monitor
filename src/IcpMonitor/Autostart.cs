using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Text;

namespace IcpMonitor;

/// <summary>
/// Autostart ueber die Windows-Aufgabenplanung: startet die App bei der Anmeldung mit hoechsten Rechten,
/// also ohne UAC-Abfrage und mit allen Sensoren. Anlegen/Loeschen der Aufgabe braucht Administratorrechte;
/// ohne diese startet sich die App kurz selbst erhoeht mit <see cref="Argument"/>.
/// </summary>
internal static class Autostart
{
    public const string Argument = "--autostart";
    private const string TaskName = "ICP Monitor";

    public static bool IsEnabled() => Schtasks($"/Query /TN \"{TaskName}\"") == 0;

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
        if (!enabled)
        {
            // Nicht vorhanden gilt auch als Erfolg.
            if (!IsEnabled()) return 0;
            int deleted = Schtasks($"/Delete /TN \"{TaskName}\" /F");
            Log.Info($"Autostart entfernt (schtasks {deleted}).");
            return deleted;
        }

        string xmlPath = Path.Combine(Path.GetTempPath(), "IcpMonitor-Autostart.xml");
        try
        {
            File.WriteAllText(xmlPath, BuildTaskXml(Environment.ProcessPath!), Encoding.Unicode);
            int created = Schtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
            Log.Info($"Autostart eingerichtet fuer {Environment.ProcessPath} (schtasks {created}).");
            return created;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
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

    private static int Schtasks(string arguments)
    {
        using Process process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 && !arguments.StartsWith("/Query")) Log.Warn($"schtasks {arguments}: {error.Trim()}");
        return process.ExitCode;
    }
}
