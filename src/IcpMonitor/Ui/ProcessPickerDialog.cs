using System.Diagnostics;

namespace IcpMonitor.Ui;

/// <summary>Auswahl aus den laufenden Programmen - Programme mit Fenster stehen oben.</summary>
internal sealed class ProcessPickerDialog : Form
{
    private readonly ListView list = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HideSelection = false,
        Size = new Size(520, 360),
    };

    public ProcessPickerDialog()
    {
        Text = L.GamesPickTitle;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        list.Columns.Add(L.GamesColumnProcess, 170);
        list.Columns.Add(L.GamesColumnWindow, 320);
        foreach (var (name, title) in RunningProcesses())
        {
            list.Items.Add(new ListViewItem([name, title]) { Tag = name });
        }

        var ok = new Button { Text = L.Ok, AutoSize = true, MinimumSize = new Size(80, 0), DialogResult = DialogResult.OK, Enabled = false };
        var cancel = new Button { Text = L.Cancel, AutoSize = true, DialogResult = DialogResult.Cancel };
        list.SelectedIndexChanged += (_, _) => ok.Enabled = list.SelectedItems.Count > 0;
        list.DoubleClick += (_, _) => { if (list.SelectedItems.Count > 0) { DialogResult = DialogResult.OK; } };

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([cancel, ok]);
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(10) };
        layout.Controls.Add(list);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    /// <summary>Prozessname (ohne .exe) des gewaehlten Programms.</summary>
    public string? SelectedProcess => list.SelectedItems.Count > 0 ? (string)list.SelectedItems[0].Tag! : null;

    /// <summary>Fenstertitel als lesbarer Name, sonst der Prozessname.</summary>
    public string? SelectedName => list.SelectedItems.Count > 0
        ? (string.IsNullOrWhiteSpace(list.SelectedItems[0].SubItems[1].Text) ? SelectedProcess : list.SelectedItems[0].SubItems[1].Text)
        : null;

    private static IEnumerable<(string Name, string Title)> RunningProcesses()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                string title = "";
                try { title = process.MainWindowTitle; } catch { }
                if (!result.TryGetValue(process.ProcessName, out string? known) || (known.Length == 0 && title.Length > 0))
                {
                    result[process.ProcessName] = title;
                }
            }
        }
        return result
            .OrderBy(p => p.Value.Length == 0)
            .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(p => (p.Key, p.Value));
    }
}
