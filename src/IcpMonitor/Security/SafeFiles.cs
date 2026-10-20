using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace IcpMonitor.Security;

/// <summary>
/// Schutz fuer Schreibzugriffe mit Administratorrechten in Ordner, die auch normale Programme beschreiben
/// koennen (%AppData%). Ersetzt dort jemand eine Datei oder einen Ordner durch eine Verknuepfung auf eine
/// Systemdatei, wuerde die App sonst mit Adminrechten die falsche Datei ueberschreiben.
/// </summary>
internal static class SafeFiles
{
    /// <summary>
    /// Windows-11-Schutz: Der Prozess folgt keinen Verzeichnis-Verknuepfungen (Junctions), die von Nicht-Admins
    /// angelegt wurden. Auf aelteren Windows-Versionen wirkungslos, dann greifen die Pruefungen unten.
    /// </summary>
    public static void EnableRedirectionTrust()
    {
        var policy = new RedirectionTrustPolicy { Flags = 1 }; // EnforceRedirectionTrust
        if (!SetProcessMitigationPolicy(ProcessRedirectionTrustPolicy, ref policy, Marshal.SizeOf<RedirectionTrustPolicy>()))
        {
            Log.Info($"Schutz gegen umgeleitete Pfade nicht verfuegbar (Fehler {Marshal.GetLastWin32Error()}).");
        }
    }

    /// <summary>
    /// true, wenn in die Datei gefahrlos geschrieben werden kann: weder sie noch ihr Ordner ist eine
    /// Verknuepfung (Symlink/Junction), und die Datei hat keine weiteren harten Links.
    /// </summary>
    public static bool IsSafeTarget(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (dir == null || IsReparsePoint(dir)) return false;
            if (!File.Exists(path)) return true;
            if (IsReparsePoint(path)) return false;
            using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return GetFileInformationByHandle(handle, out FileInformation info) && info.NumberOfLinks <= 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private const int ProcessRedirectionTrustPolicy = 16;

    [StructLayout(LayoutKind.Sequential)]
    private struct RedirectionTrustPolicy
    {
        public uint Flags;
    }

    // BY_HANDLE_FILE_INFORMATION: FILETIME besteht aus zwei DWORDs, daher 4-Byte-Ausrichtung.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct FileInformation
    {
        public uint FileAttributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessMitigationPolicy(int policy, ref RedirectionTrustPolicy buffer, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}
