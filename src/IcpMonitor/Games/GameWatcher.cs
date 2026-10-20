using System.Diagnostics;

namespace IcpMonitor.Games;

/// <summary>
/// Prueft regelmaessig, ob ein Programm aus der Vorrang-Liste laeuft.
/// Erst wenn es einige Sekunden weg ist, gilt es als beendet - so loest ein kurzer Neustart keinen Wechsel aus.
/// </summary>
internal sealed class GameWatcher(Func<PauseConfig> getConfig) : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReleaseDelay = TimeSpan.FromSeconds(3);

    private readonly CancellationTokenSource cts = new();
    private Thread? thread;
    private DateTime lastSeen;

    /// <summary>Name des laufenden Programms oder null. Wird im Waechter-Thread ausgeloest.</summary>
    public event Action<string?>? ActiveChanged;

    public string? Active { get; private set; }

    public void Start()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Spiele-Waechter", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    private void Run()
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                Check();
            }
            catch (Exception ex)
            {
                Log.Warn($"Spiele-Erkennung: {ex.Message}");
            }
            cts.Token.WaitHandle.WaitOne(Interval);
        }
    }

    private void Check()
    {
        PauseConfig config = getConfig();
        string? found = null;
        if (config.Enabled)
        {
            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in Process.GetProcesses())
            {
                running.Add(process.ProcessName);
                process.Dispose();
            }
            found = GameCatalog.Watched(config).FirstOrDefault(g => g.Processes.Any(running.Contains))?.Name;
        }

        if (found != null)
        {
            lastSeen = DateTime.Now;
            if (found != Active) SetActive(found);
        }
        else if (Active != null && (!config.Enabled || DateTime.Now - lastSeen > ReleaseDelay))
        {
            SetActive(null);
        }
    }

    private void SetActive(string? name)
    {
        Active = name;
        Log.Info(name != null ? $"Vorrang-Programm erkannt: {name}" : "Vorrang-Programm beendet.");
        ActiveChanged?.Invoke(name);
    }

    public void Dispose()
    {
        cts.Cancel();
        thread?.Join(TimeSpan.FromSeconds(3));
        cts.Dispose();
    }
}
