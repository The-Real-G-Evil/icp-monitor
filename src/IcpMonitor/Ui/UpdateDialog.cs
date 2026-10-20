using IcpMonitor.Update;

namespace IcpMonitor.Ui;

/// <summary>Zeigt den Download-Fortschritt; liefert nach Erfolg den Pfad des gepruefen Installers.</summary>
internal sealed class UpdateDialog : Form
{
    private readonly UpdateService service;
    private readonly UpdateInfo info;
    private readonly ProgressBar progressBar = new() { Width = 360, Minimum = 0, Maximum = 100 };
    private readonly CancellationTokenSource cts = new();

    public UpdateDialog(UpdateService service, UpdateInfo info)
    {
        this.service = service;
        this.info = info;

        Text = L.UpdateTitle;
        Icon = TrayContext.CreateIcon(SystemInformation.IconSize);
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(14) };
        layout.Controls.Add(new Label { Text = L.UpdateDownloading(info.Version), AutoSize = true, Margin = new Padding(3, 3, 3, 8) });
        layout.Controls.Add(progressBar);
        var cancel = new Button { Text = L.Cancel, AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(3, 10, 3, 3) };
        cancel.Click += (_, _) => cts.Cancel();
        layout.Controls.Add(cancel);
        Controls.Add(layout);
        CancelButton = cancel;

        Shown += async (_, _) => await DownloadAsync();
    }

    /// <summary>Heruntergeladener, gepruefter und gesperrter Installer, sonst null. Der Aufrufer gibt ihn frei.</summary>
    public VerifiedInstaller? Installer { get; private set; }

    private async Task DownloadAsync()
    {
        try
        {
            Installer = await service.DownloadAsync(info, new Progress<int>(p => progressBar.Value = Math.Clamp(p, 0, 100)), cts.Token);
            DialogResult = DialogResult.OK;
        }
        catch (OperationCanceledException)
        {
            DialogResult = DialogResult.Cancel;
        }
        catch (Exception ex)
        {
            Log.Error("Update-Download fehlgeschlagen", ex);
            MessageBox.Show(this, L.UpdateInstallFailed(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            DialogResult = DialogResult.Abort;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            cts.Cancel();
            cts.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }
}
