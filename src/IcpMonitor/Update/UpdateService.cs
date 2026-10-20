using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace IcpMonitor.Update;

internal enum UpdateStatus { UpToDate, Available, NoReleases, Failed }

/// <param name="ChecksumUrl">Datei "&lt;Setup&gt;.sha256" im selben Release; ohne sie wird nicht installiert.</param>
internal sealed record UpdateInfo(Version Version, string PageUrl, string Notes, string? SetupName, string? SetupUrl, string? ChecksumUrl);

internal sealed record UpdateCheckResult(UpdateStatus Status, UpdateInfo? Info = null, string? Error = null);

/// <summary>
/// Sucht im GitHub-Repository nach einem neueren Release, laedt dessen Installer herunter,
/// prueft ihn per SHA-256 und startet ihn ohne Rueckfragen.
/// </summary>
internal sealed class UpdateService : IDisposable
{
    public const string DefaultRepository = "The-Real-G-Evil/icp-monitor";
    private const string SetupPrefix = "ICP-Monitor-Setup-";

    private readonly HttpClient http;
    private readonly string repository;

    public UpdateService(string repository = DefaultRepository)
    {
        this.repository = repository;
        http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("IcpMonitor", CurrentVersion.ToString()));
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public static Version CurrentVersion
    {
        get
        {
            Version v = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken token = default)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync($"https://api.github.com/repos/{repository}/releases/latest", token);
            // 404: noch kein Release - oder das Repository ist nicht oeffentlich.
            if (response.StatusCode == HttpStatusCode.NotFound) return new UpdateCheckResult(UpdateStatus.NoReleases);
            response.EnsureSuccessStatusCode();

            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            JsonElement root = json.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? version))
            {
                return new UpdateCheckResult(UpdateStatus.Failed, Error: $"Unbekanntes Versionsformat: {tag}");
            }

            string? setupName = null, setupUrl = null, checksumUrl = null;
            foreach (JsonElement asset in root.GetProperty("assets").EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (!name.StartsWith(SetupPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) (setupName, setupUrl) = (name, url);
                else if (name.EndsWith(".exe.sha256", StringComparison.OrdinalIgnoreCase)) checksumUrl = url;
            }

            var info = new UpdateInfo(
                version,
                root.GetProperty("html_url").GetString() ?? $"https://github.com/{repository}/releases",
                root.TryGetProperty("body", out JsonElement body) ? body.GetString() ?? "" : "",
                setupName, setupUrl, checksumUrl);
            return new UpdateCheckResult(version > CurrentVersion ? UpdateStatus.Available : UpdateStatus.UpToDate, info);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            Log.Warn($"Update-Suche fehlgeschlagen: {ex.Message}");
            return new UpdateCheckResult(UpdateStatus.Failed, Error: ex.Message);
        }
    }

    /// <summary>Laedt den Installer herunter und prueft die SHA-256-Summe. Gibt den Pfad zurueck.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<int> progress, CancellationToken token)
    {
        if (info.SetupUrl == null || info.SetupName == null) throw new InvalidOperationException(L.UpdateNoInstaller);
        if (info.ChecksumUrl == null) throw new InvalidOperationException(L.UpdateNoChecksum);

        string expected = (await http.GetStringAsync(info.ChecksumUrl, token)).Trim().Split(' ', '\t', '\r', '\n')[0];

        string dir = Path.Combine(Path.GetTempPath(), "IcpMonitor-Update");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, info.SetupName);

        using (HttpResponseMessage response = await http.GetAsync(info.SetupUrl, HttpCompletionOption.ResponseHeadersRead, token))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using Stream source = await response.Content.ReadAsStreamAsync(token);
            await using FileStream target = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), token);
                done += read;
                if (total > 0) progress.Report((int)(done * 100 / total.Value));
            }
        }

        string actual;
        await using (FileStream file = File.OpenRead(path))
        {
            actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token));
        }
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException(L.UpdateChecksumMismatch);
        }
        Log.Info($"Update {info.Version} heruntergeladen und geprueft: {path}");
        return path;
    }

    /// <summary>
    /// Startet den Installer ohne Rueckfragen. Er beendet die laufende App und startet danach die neue Version.
    /// Laeuft die App mit Administratorrechten, erscheint keine UAC-Abfrage.
    /// </summary>
    public static void LaunchInstaller(string path)
    {
        Process.Start(new ProcessStartInfo(path, "/SILENT /SUPPRESSMSGBOXES /NORESTART")
        {
            UseShellExecute = true,
            Verb = Program.IsAdmin() ? "" : "runas",
        })?.Dispose();
        Log.Info("Update-Installer gestartet.");
    }

    public void Dispose() => http.Dispose();
}
