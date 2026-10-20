using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using IcpMonitor.Pages;
using IcpMonitor.Sensors;
using IcpMonitor.Ui;

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
    private Language menuLanguage;
    private SettingsForm? settingsForm;

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
    }

    private ContextMenuStrip BuildMenu()
    {
        menuLanguage = L.Current;
        statusItem = new ToolStripMenuItem(L.TrayConnecting) { Enabled = false };
        pagesItem = new ToolStripMenuItem(L.TrayShowPage);
        displayItem = new ToolStripMenuItem(L.TrayDisplayOn, null, (_, _) => app.SetDisplayOn(!app.DisplayOn));
        testPageItem = new ToolStripMenuItem(L.TrayTestPage, null, (_, _) => app.ToggleTestPage());

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(L.TraySettings, null, (_, _) => ShowSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(pagesItem);
        menu.Items.Add(displayItem);
        menu.Items.Add(testPageItem);
        menu.Items.Add(new ToolStripSeparator());
        if (!isAdmin)
        {
            menu.Items.Add(new ToolStripMenuItem(L.TrayRestartAdmin, null, (_, _) => RestartAsAdmin()));
        }
        menu.Items.Add(new ToolStripMenuItem(L.TrayOpenSensors, null, (_, _) => OpenFile(SensorService.DumpPath)));
        menu.Items.Add(new ToolStripMenuItem(L.TrayOpenConfig, null, (_, _) => OpenFile(AppConfig.FilePath)));
        menu.Items.Add(new ToolStripMenuItem(L.TrayOpenLog, null, (_, _) => OpenFile(Log.FilePath)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(L.TrayExit, null, (_, _) => ExitThread()));
        menu.Opening += (_, _) => RefreshMenu();
        return menu;
    }

    public void ShowSettings()
    {
        if (settingsForm == null || settingsForm.IsDisposed)
        {
            settingsForm = new SettingsForm(app, ReopenSettings);
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
        string text =
            $"ICP Monitor - {(app.DeviceConnected ? $"{slot.Label}: {PageCatalog.Get(slot.Config.Page).DisplayName}" : L.TrayNotConnectedTip)}\n" +
            $"CPU {Value(s.CpuLoad, "%")} {Value(s.CpuTemp, "°C")}\n" +
            $"GPU {Value(s.GpuLoad, "%")} {Value(s.GpuTemp, "°C")}";
        notifyIcon.Text = text.Length > 127 ? text[..127] : text;
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

    /// <summary>Kleines DED-Symbol: gruene Textzeilen auf dunklem Display.</summary>
    internal static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var frame = new SolidBrush(Color.FromArgb(60, 60, 60));
            using var screen = new SolidBrush(Color.FromArgb(10, 20, 10));
            using var line = new SolidBrush(Color.FromArgb(80, 255, 80));
            g.FillRectangle(frame, 0, 4, 32, 24);
            g.FillRectangle(screen, 2, 6, 28, 20);
            int[] widths = [20, 26, 14, 22];
            for (int i = 0; i < widths.Length; i++) g.FillRectangle(line, 4, 8 + i * 4, widths[i], 2);
        }
        IntPtr handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            settingsForm?.Dispose();
            refreshTimer.Dispose();
            notifyIcon.Visible = false;
            notifyIcon.ContextMenuStrip?.Dispose();
            notifyIcon.Dispose();
            icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
