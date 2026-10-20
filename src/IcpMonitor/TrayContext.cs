using System.ComponentModel;
using System.Diagnostics;
using IcpMonitor.Pages;
using IcpMonitor.Sensors;
using IcpMonitor.Ui;
using IcpMonitor.Update;

namespace IcpMonitor;

/// <summary>Taskleistensymbol mit Kontextmenue. Laeuft auf dem UI-Thread.</summary>
internal sealed class TrayContext : ApplicationContext
{
    private readonly MonitorApp app;
    private readonly bool isAdmin;
    private readonly NotifyIcon notifyIcon;
    private readonly Icon icon;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private ToolStripMenuItem statusItem = null!;
    private ToolStripMenuItem pagesItem = null!;
    private ToolStripMenuItem displayItem = null!;
    private ToolStripMenuItem testPageItem = null!;
    private ToolStripMenuItem autostartItem = null!;
    private ToolStripMenuItem pauseItem = null!;
    private ToolStripMenuItem updateItem = null!;
    private Language menuLanguage;
    private SettingsForm? settingsForm;

    // Update-Suche: kurz nach dem Start, danach alle paar Stunden.
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
    private readonly UpdateService updates = new();
    private readonly System.Windows.Forms.Timer updateTimer;
    private UpdateInfo? availableUpdate;
    private Version? notifiedVersion;
    private bool updateBusy;

    public TrayContext(MonitorApp app, bool isAdmin)
    {
        this.app = app;
        this.isAdmin = isAdmin;
        icon = CreateIcon();

        notifyIcon = new NotifyIcon
        {
            Icon = icon,
            Text = "ICP Monitor",
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
        notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) app.StepSlot(+1);
        };
        notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowSettings();
        };

        refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        refreshTimer.Tick += (_, _) =>
        {
            // Nach einem Sprachwechsel das Menue neu aufbauen.
            if (menuLanguage != L.Current)
            {
                ContextMenuStrip? old = notifyIcon.ContextMenuStrip;
                notifyIcon.ContextMenuStrip = BuildMenu();
                old?.Dispose();
            }
            RefreshTooltip();
        };
        refreshTimer.Start();
        RefreshTooltip();

        notifyIcon.BalloonTipClicked += (_, _) => { if (availableUpdate != null) InstallUpdate(availableUpdate); };
        updateTimer = new System.Windows.Forms.Timer { Interval = (int)FirstUpdateCheck.TotalMilliseconds };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = (int)UpdateCheckInterval.TotalMilliseconds;
            if (app.UpdateCheckEnabled) await CheckForUpdatesAsync(interactive: false);
        };
        updateTimer.Start();
    }

    /// <summary>
    /// Sucht nach einer neuen Version. Automatisch: nur ein Hinweis am Tray-Symbol (einmal je Version).
    /// Von Hand: Ergebnis immer per Meldung, bei neuer Version direkt die Installationsfrage.
    /// </summary>
    public async Task CheckForUpdatesAsync(bool interactive)
    {
        if (updateBusy) return;
        updateBusy = true;
        try
        {
            UpdateCheckResult result = await updates.CheckAsync();
            availableUpdate = result.Status == UpdateStatus.Available ? result.Info : null;
            Log.Info($"Update-Suche: {result.Status} {result.Info?.Version}");

            if (availableUpdate != null && !interactive && notifiedVersion != availableUpdate.Version)
            {
                notifiedVersion = availableUpdate.Version;
                notifyIcon.ShowBalloonTip(15000, L.UpdateTitle, L.UpdateAvailableBalloon(availableUpdate.Version), ToolTipIcon.Info);
            }
            if (!interactive) return;

            switch (result.Status)
            {
                case UpdateStatus.Available:
                    InstallUpdate(availableUpdate!);
                    break;
                case UpdateStatus.UpToDate:
                    MessageBox.Show(L.UpdateUpToDate(UpdateService.CurrentVersion), L.UpdateTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                case UpdateStatus.NoReleases:
                    MessageBox.Show(L.UpdateNoReleases, L.UpdateTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                default:
                    MessageBox.Show(L.UpdateFailed(result.Error ?? ""), L.UpdateTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
            }
        }
        finally
        {
            updateBusy = false;
        }
    }

    /// <summary>Nachfragen, herunterladen, pruefen, Installer starten und die App beenden.</summary>
    private void InstallUpdate(UpdateInfo info)
    {
        string notes = info.Notes.Length > 600 ? info.Notes[..600] + " ..." : info.Notes;
        if (MessageBox.Show(L.UpdateConfirm(UpdateService.CurrentVersion, info.Version, notes), L.UpdateTitle,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        using var dialog = new UpdateDialog(updates, info);
        if (dialog.ShowDialog() != DialogResult.OK || dialog.InstallerPath == null) return;
        try
        {
            UpdateService.LaunchInstaller(dialog.InstallerPath);
            ExitThread();
        }
        catch (Exception ex)
        {
            // Abgebrochene UAC-Abfrage o. ae.
            Log.Error("Update-Installer konnte nicht gestartet werden", ex);
            MessageBox.Show(L.UpdateInstallFailed(ex.Message), L.UpdateTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        menuLanguage = L.Current;
        statusItem = new ToolStripMenuItem(L.TrayConnecting) { Enabled = false };
        pagesItem = new ToolStripMenuItem(L.TrayShowPage);
        displayItem = new ToolStripMenuItem(L.TrayDisplayOn, null, (_, _) => app.SetDisplayOn(!app.DisplayOn));
        testPageItem = new ToolStripMenuItem(L.TrayTestPage, null, (_, _) => app.ToggleTestPage());
        autostartItem = new ToolStripMenuItem(L.TrayAutostart, null, (_, _) => ToggleAutostart());
        pauseItem = new ToolStripMenuItem(L.TrayPause, null, (_, _) => app.SetManualPause(!app.ManualPause));
        updateItem = new ToolStripMenuItem("", null, (_, _) => { if (availableUpdate != null) InstallUpdate(availableUpdate); })
        {
            Font = new Font(SystemFonts.MenuFont ?? Control.DefaultFont, FontStyle.Bold),
            Visible = false,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(L.TraySettings, null, (_, _) => ShowSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(updateItem);
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(pagesItem);
        menu.Items.Add(displayItem);
        menu.Items.Add(pauseItem);
        menu.Items.Add(testPageItem);
        menu.Items.Add(new ToolStripSeparator());
        if (!isAdmin)
        {
            menu.Items.Add(new ToolStripMenuItem(L.TrayRestartAdmin, null, (_, _) => RestartAsAdmin()));
        }
        menu.Items.Add(new ToolStripMenuItem(L.TrayOpenSensors, null, (_, _) => OpenFile(SensorService.DumpPath)));
        menu.Items.Add(new ToolStripMenuItem(L.TrayOpenConfig, null, (_, _) => OpenFile(AppConfig.FilePath)));
        menu.Items.Add(new ToolStripMenuItem(L.TrayOpenLog, null, (_, _) => OpenFile(Log.FilePath)));
        menu.Items.Add(new ToolStripMenuItem(L.TrayCheckUpdates, null, async (_, _) => await CheckForUpdatesAsync(interactive: true)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(L.TrayExit, null, (_, _) => ExitThread()));
        menu.Opening += (_, _) => RefreshMenu();
        return menu;
    }

    public void ShowSettings()
    {
        if (settingsForm == null || settingsForm.IsDisposed)
        {
            settingsForm = new SettingsForm(app, ReopenSettings, () => CheckForUpdatesAsync(interactive: true));
            settingsForm.Show();
        }
        if (settingsForm.WindowState == FormWindowState.Minimized) settingsForm.WindowState = FormWindowState.Normal;
        settingsForm.Activate();
    }

    /// <summary>Nach einem Sprachwechsel das Fenster in der neuen Sprache neu oeffnen.</summary>
    private void ReopenSettings()
    {
        settingsForm?.Close();
        settingsForm = null;
        ShowSettings();
    }

    private void RefreshMenu()
    {
        statusItem.Text = app.DeviceConnected ? L.TrayConnected : L.TrayDisconnected;
        displayItem.Checked = app.DisplayOn;
        testPageItem.Checked = app.TestPageActive;
        autostartItem.Checked = Autostart.IsEnabled();
        pauseItem.Checked = app.ManualPause;
        updateItem.Visible = availableUpdate != null;
        if (availableUpdate != null) updateItem.Text = L.TrayInstallUpdate(availableUpdate.Version);

        // Die Seiten spiegeln die sechs ICP-Tasten wider.
        pagesItem.DropDownItems.Clear();
        var slots = app.Slots;
        for (int i = 0; i < slots.Count; i++)
        {
            int index = i;
            Slot slot = slots[i];
            var item = new ToolStripMenuItem($"{slot.Label}:  {PageCatalog.Get(slot.Config.Page).DisplayName}", null, (_, _) => app.SelectSlot(index))
            {
                Checked = i == app.SlotIndex && !app.TestPageActive,
                Enabled = slot.Page != null,
            };
            pagesItem.DropDownItems.Add(item);
        }
    }

    private void RefreshTooltip()
    {
        SystemSnapshot s = app.Sensors;
        Slot slot = app.Slots[app.SlotIndex];
        string Value(float? v, string unit) => v.HasValue ? $"{v.Value:0}{unit}" : "--";
        string state = !app.DeviceConnected ? L.TrayNotConnectedTip
            : app.IsPaused ? L.TrayPausedTip(app.ActiveGame)
            : $"{slot.Label}: {PageCatalog.Get(slot.Config.Page).DisplayName}";
        string text =
            $"ICP Monitor - {state}\n" +
            $"CPU {Value(s.CpuLoad, "%")} {Value(s.CpuTemp, "°C")}\n" +
            $"GPU {Value(s.GpuLoad, "%")} {Value(s.GpuTemp, "°C")}";
        notifyIcon.Text = text.Length > 127 ? text[..127] : text;
    }

    private void ToggleAutostart()
    {
        bool target = !Autostart.IsEnabled();
        if (Autostart.Set(target) == false)
        {
            MessageBox.Show(L.AutostartFailed, "ICP Monitor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = Program.RestartArgument,
                UseShellExecute = true,
                Verb = "runas",
            })?.Dispose();
            ExitThread();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Benutzer hat die UAC-Abfrage abgebrochen - einfach weiterlaufen.
        }
        catch (Exception ex)
        {
            Log.Error("Neustart als Administrator fehlgeschlagen", ex);
        }
    }

    private static void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(L.FileMissing(path), "ICP Monitor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Programmsymbol (Assets/app.ico) in der passenden Groesse, Standard: Infobereich.</summary>
    internal static Icon CreateIcon(Size? size = null)
    {
        using Stream stream = typeof(TrayContext).Assembly.GetManifestResourceStream("IcpMonitor.app.ico")
            ?? throw new InvalidOperationException("Ressource app.ico fehlt.");
        return new Icon(stream, size ?? SystemInformation.SmallIconSize);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            settingsForm?.Dispose();
            refreshTimer.Dispose();
            updateTimer.Dispose();
            updates.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.ContextMenuStrip?.Dispose();
            notifyIcon.Dispose();
            icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
