using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace IcpMonitor.Security;

/// <summary>
/// Startet Programme, Links und Dateien mit den normalen Rechten des angemeldeten Benutzers, auch wenn
/// ICP Monitor selbst mit Administratorrechten laeuft. Sonst wuerden z. B. der Browser oder Befehle aus
/// der (vom Benutzer beschreibbaren) config.json mit Adminrechten laufen.
/// Verfahren: Token des Explorer-Prozesses (der Shell) kopieren und damit starten.
/// </summary>
internal static class Unelevated
{
    /// <summary>Oeffnet eine URL oder Datei mit dem Standardprogramm.</summary>
    public static void Open(string target)
    {
        if (!Program.IsAdmin())
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
            return;
        }
        // explorer.exe oeffnet URLs und Dateien ueber die Dateizuordnung - als nicht erhoehter Benutzer.
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        StartWithShellToken($"\"{explorer}\" \"{target.Replace("\"", "")}\"", null);
    }

    /// <summary>Fuehrt einen Befehl aus (Programm + Argumente).</summary>
    public static void Run(string command, string arguments, string workingDirectory)
    {
        if (!Program.IsAdmin())
        {
            Process.Start(new ProcessStartInfo(command, arguments) { UseShellExecute = true, WorkingDirectory = workingDirectory })?.Dispose();
            return;
        }
        string quoted = command.Contains(' ') && !command.StartsWith('"') ? $"\"{command}\"" : command;
        StartWithShellToken(string.IsNullOrWhiteSpace(arguments) ? quoted : $"{quoted} {arguments}", workingDirectory);
    }

    private static void StartWithShellToken(string commandLine, string? workingDirectory)
    {
        IntPtr shellProcess = IntPtr.Zero, shellToken = IntPtr.Zero, token = IntPtr.Zero;
        try
        {
            IntPtr shellWindow = GetShellWindow();
            if (shellWindow == IntPtr.Zero) throw new InvalidOperationException("Kein Explorer (Shell) gefunden.");
            GetWindowThreadProcessId(shellWindow, out uint shellPid);

            shellProcess = OpenProcess(ProcessQueryLimitedInformation, false, shellPid);
            if (shellProcess == IntPtr.Zero) throw new Win32Exception();
            if (!OpenProcessToken(shellProcess, TokenDuplicate, out shellToken)) throw new Win32Exception();
            if (!DuplicateTokenEx(shellToken, TokenAccessForCreateProcess, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out token)) throw new Win32Exception();

            var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>() };
            if (!CreateProcessWithTokenW(token, 0, null, new StringBuilder(commandLine), 0, IntPtr.Zero, workingDirectory, ref startup, out ProcessInformation info))
            {
                throw new Win32Exception();
            }
            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
            Log.Info($"Ohne Adminrechte gestartet (PID {info.dwProcessId}): {commandLine}");
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (shellToken != IntPtr.Zero) CloseHandle(shellToken);
            if (shellProcess != IntPtr.Zero) CloseHandle(shellProcess);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenDuplicate = 0x0002;
    // TOKEN_QUERY | TOKEN_DUPLICATE | TOKEN_ASSIGN_PRIMARY | TOKEN_ADJUST_DEFAULT | TOKEN_ADJUST_SESSIONID
    private const uint TokenAccessForCreateProcess = 0x0008 | 0x0002 | 0x0001 | 0x0080 | 0x0100;
    private const int SecurityImpersonation = 2;
    private const int TokenPrimary = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string? applicationName, StringBuilder commandLine,
        int creationFlags, IntPtr environment, string? currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
}
