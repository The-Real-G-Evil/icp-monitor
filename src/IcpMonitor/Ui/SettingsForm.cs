using System.Diagnostics;
using IcpMonitor.Games;
using IcpMonitor.Pages;
using IcpMonitor.Update;

namespace IcpMonitor.Ui;

/// <summary>
/// Einstellungen: bildet die sechs Seitentasten des ICP und das DED nach.
/// Taste anklicken, Seite waehlen - die Vorschau zeigt sofort, was auf dem ICP erscheinen wird.
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Color IcpBody = Color.FromArgb(40, 41, 44);
    private static readonly Color KeyFace = Color.FromArgb(24, 24, 26);
    private static readonly Color KeySelected = Color.FromArgb(28, 70, 28);
    private static readonly Color KeyBorder = Color.FromArgb(95, 95, 98);
    private static readonly Color Accent = Color.FromArgb(110, 255, 110);

    private readonly MonitorApp app;
    private readonly AppConfig draft;
    private readonly Button[] slotButtons = new Button[AppConfig.SlotButtons.Length];
    private readonly Label[] slotLabels = new Label[AppConfig.SlotButtons.Length];
    private readonly DedPreview preview = new();
    private readonly GroupBox slotGroup = new();
    private readonly ComboBox pageCombo = new();
    private readonly Label descriptionLabel = new();
    private readonly TableLayoutPanel rssPanel = new();
    private readonly TextBox feedsBox = new();
    private readonly NumericUpDown rotateBox = new();
    private readonly Button applyButton = new();
    private readonly ComboBox languageCombo = new();
    private readonly Action reopen;
    private readonly System.Windows.Forms.Timer previewTimer = new() { Interval = 400 };
    private readonly System.Windows.Forms.Timer feedsDebounce = new() { Interval = 800 };

    private int selected;
    private Page? previewPage;
    private bool loading;
    private bool dirty;

    private readonly Func<Task> checkUpdates;
    private readonly CheckedListBox gamesList = new();
    private readonly Button removeGameButton = new();

    /// <param name="reopen">Schliesst das Fenster und oeffnet es neu (nach einem Sprachwechsel).</param>
    /// <param name="checkUpdates">Sucht von Hand nach einer neuen Version (Tray-Logik).</param>
    public SettingsForm(MonitorApp app, Action reopen, Func<Task> checkUpdates)
    {
        this.app = app;
        this.reopen = reopen;
        this.checkUpdates = checkUpdates;
        draft = app.CloneConfig();

        Text = L.SettingsTitle;
        Icon = TrayContext.CreateIcon(SystemInformation.IconSize);
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
        root.Controls.Add(BuildTabs(
            (L.TabKeys, Stack(BuildIcpPanel(), BuildSlotGroup())),
            (L.TabGames, BuildGamesTab()),
            (L.SettingsGeneral, Stack(BuildGeneralGroup(), BuildUpdatesGroup()))));
        root.Controls.Add(BuildButtonBar());
        Controls.Add(root);

        previewTimer.Tick += (_, _) => RefreshPreview();
        feedsDebounce.Tick += (_, _) =>
        {
            feedsDebounce.Stop();
            CurrentSlot.Rss!.Feeds = feedsBox.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            RebuildPreviewPage();
        };

        app.StateChanged += OnAppStateChanged;
        SelectSlot(app.SlotIndex);
        previewTimer.Start();
    }

    private SlotConfig CurrentSlot => draft.Slot(AppConfig.SlotButtons[selected]);

    // ---------- Aufbau ----------

    /// <summary>Reiter, alle so gross wie der groesste Inhalt (TabControl waechst nicht selbst mit).</summary>
    private static TabControl BuildTabs(params (string Title, Control Content)[] pages)
    {
        var tabs = new TabControl();
        Size max = Size.Empty;
        foreach ((string title, Control content) in pages)
        {
            var page = new TabPage(title) { Padding = new Padding(8), UseVisualStyleBackColor = true };
            content.Location = new Point(page.Padding.Left, page.Padding.Top);
            page.Controls.Add(content);
            tabs.TabPages.Add(page);
            Size preferred = content.GetPreferredSize(Size.Empty);
            max = new Size(Math.Max(max.Width, preferred.Width), Math.Max(max.Height, preferred.Height));
        }
        tabs.Size = new Size(max.Width + 16 + 8, max.Height + 16 + tabs.ItemSize.Height + 12);
        return tabs;
    }

    /// <summary>Steuerelemente untereinander, gleich breit.</summary>
    private static Control Stack(params Control[] controls)
    {
        var stack = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
        foreach (Control control in controls) stack.Controls.Add(control);
        return stack;
    }

    /// <summary>Vorrang fuer Spiele: Liste der Programme, die eine Pause ausloesen.</summary>
    private Control BuildGamesTab()
    {
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };

        var enabled = new CheckBox { Text = L.GamesAutoPause, AutoSize = true, Checked = draft.Pause.Enabled, Margin = new Padding(3, 3, 3, 6) };
        enabled.CheckedChanged += (_, _) =>
        {
            draft.Pause.Enabled = enabled.Checked;
            gamesList.Enabled = enabled.Checked;
            MarkDirty();
        };
        layout.Controls.Add(enabled);
        layout.Controls.Add(new Label { Text = L.GamesExplain, AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 8) });

        gamesList.Size = new Size(620, 220);
        gamesList.CheckOnClick = true;
        gamesList.IntegralHeight = false;
        gamesList.Enabled = draft.Pause.Enabled;
        gamesList.ItemCheck += (_, e) =>
        {
            if (loading) return;
            var item = (GameItem)gamesList.Items[e.Index];
            bool on = e.NewValue == CheckState.Checked;
            if (item.BuiltIn != null) draft.Pause.BuiltIn[item.BuiltIn.Id] = on;
            else if (item.Custom != null) item.Custom.Enabled = on;
            MarkDirty();
        };
        gamesList.SelectedIndexChanged += (_, _) => removeGameButton.Enabled = gamesList.SelectedItem is GameItem { Custom: not null };
        layout.Controls.Add(gamesList);
        FillGamesList();

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        var addExe = new Button { Text = L.GamesAddExe, AutoSize = true };
        addExe.Click += (_, _) => AddGameFromFile();
        var addRunning = new Button { Text = L.GamesAddRunning, AutoSize = true };
        addRunning.Click += (_, _) => AddGameFromRunning();
        removeGameButton.Text = L.GamesRemove;
        removeGameButton.AutoSize = true;
        removeGameButton.Enabled = false;
        removeGameButton.Click += (_, _) =>
        {
            if (gamesList.SelectedItem is not GameItem { Custom: { } custom }) return;
            draft.Pause.Custom.Remove(custom);
            FillGamesList();
            MarkDirty();
        };
        buttons.Controls.AddRange([addExe, addRunning, removeGameButton]);
        layout.Controls.Add(buttons);
        return layout;
    }

    private void FillGamesList()
    {
        loading = true;
        gamesList.Items.Clear();
        foreach (GameDefinition game in GameCatalog.BuiltIn)
        {
            string exes = string.Join(", ", game.Processes.Select(p => p + ".exe"));
            gamesList.Items.Add(new GameItem($"{game.Name}  ({exes})", game, null), draft.Pause.IsEnabled(game));
        }
        foreach (CustomProgram custom in draft.Pause.Custom)
        {
            gamesList.Items.Add(new GameItem($"{custom.Name}  ({custom.Process}.exe)", null, custom), custom.Enabled);
        }
        loading = false;
        removeGameButton.Enabled = false;
    }

    private void AddGameFromFile()
    {
        using var dialog = new OpenFileDialog { Filter = L.GamesExeFilter, Title = L.GamesAddExe.TrimEnd('.', ' ') };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string? description = null;
        try { description = FileVersionInfo.GetVersionInfo(dialog.FileName).FileDescription; } catch { }
        string process = GameCatalog.ProcessNameFromPath(dialog.FileName);
        AddCustomGame(string.IsNullOrWhiteSpace(description) ? process : description.Trim(), process);
    }

    private void AddGameFromRunning()
    {
        using var dialog = new ProcessPickerDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedProcess == null) return;
        AddCustomGame(dialog.SelectedName ?? dialog.SelectedProcess, dialog.SelectedProcess);
    }

    private void AddCustomGame(string name, string process)
    {
        bool known = draft.Pause.Custom.Any(c => c.Process.Equals(process, StringComparison.OrdinalIgnoreCase))
            || GameCatalog.BuiltIn.Any(g => g.Processes.Contains(process, StringComparer.OrdinalIgnoreCase));
        if (known) return;
        draft.Pause.Custom.Add(new CustomProgram { Name = name, Process = process });
        FillGamesList();
        MarkDirty();
    }

    private Control BuildUpdatesGroup()
    {
        var group = new GroupBox { Text = L.UpdatesGroup, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10), Margin = new Padding(3, 10, 3, 3) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        layout.Controls.Add(new Label { Text = L.InstalledVersion(UpdateService.CurrentVersion), AutoSize = true, Margin = new Padding(3, 3, 3, 6) });
        var auto = new CheckBox { Text = L.UpdatesAuto, AutoSize = true, Checked = draft.CheckForUpdates };
        auto.CheckedChanged += (_, _) =>
        {
            draft.CheckForUpdates = auto.Checked;
            MarkDirty();
        };
        layout.Controls.Add(auto);
        var now = new Button { Text = L.UpdatesCheckNow, AutoSize = true, Margin = new Padding(3, 8, 3, 3) };
        now.Click += async (_, _) =>
        {
            now.Enabled = false;
            try { await checkUpdates(); }
            finally { if (!now.IsDisposed) now.Enabled = true; }
        };
        layout.Controls.Add(now);
        group.Controls.Add(layout);
        return group;
    }

    /// <summary>Nachbildung des ICP: DED oben, darunter die sechs Seitentasten.</summary>
    private Control BuildIcpPanel()
    {
        var body = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            BackColor = IcpBody,
            Padding = new Padding(16, 14, 16, 10),
            Margin = new Padding(0, 0, 0, 10),
            Anchor = AnchorStyles.None,
        };

        preview.Anchor = AnchorStyles.None;
        preview.Margin = new Padding(0, 0, 0, 14);
        body.Controls.Add(preview);

        var keys = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.None, BackColor = IcpBody };
        for (int i = 0; i < AppConfig.SlotButtons.Length; i++)
        {
            int index = i;
            var cell = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = new Padding(6, 0, 6, 0) };

            var key = new Button
            {
                Text = AppConfig.SlotButtons[i],
                Size = new Size(84, 46),
                FlatStyle = FlatStyle.Flat,
                BackColor = KeyFace,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.None,
            };
            key.FlatAppearance.BorderColor = KeyBorder;
            key.FlatAppearance.BorderSize = 2;
            key.Click += (_, _) => SelectSlot(index);
            slotButtons[i] = key;

            var label = new Label
            {
                AutoSize = false,
                Size = new Size(96, 34),
                TextAlign = ContentAlignment.TopCenter,
                ForeColor = Color.Gainsboro,
                Font = new Font("Segoe UI", 8f),
                Anchor = AnchorStyles.None,
            };
            slotLabels[i] = label;

            cell.Controls.Add(key);
            cell.Controls.Add(label);
            keys.Controls.Add(cell);
        }
        body.Controls.Add(keys);
        return body;
    }

    private Control BuildSlotGroup()
    {
        slotGroup.Dock = DockStyle.Fill;
        slotGroup.AutoSize = true;
        slotGroup.Padding = new Padding(10);

        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        grid.Controls.Add(new Label { Text = L.SettingsPage, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        pageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        pageCombo.Width = 260;
        pageCombo.DisplayMember = nameof(PageType.DisplayName);
        pageCombo.Items.AddRange(PageCatalog.Types);
        pageCombo.SelectedIndexChanged += (_, _) => OnPageTypeChanged();
        grid.Controls.Add(pageCombo, 1, 0);

        descriptionLabel.AutoSize = true;
        descriptionLabel.MaximumSize = new Size(620, 0);
        descriptionLabel.ForeColor = SystemColors.GrayText;
        descriptionLabel.Margin = new Padding(3, 6, 3, 6);
        grid.Controls.Add(descriptionLabel, 0, 1);
        grid.SetColumnSpan(descriptionLabel, 2);

        rssPanel.AutoSize = true;
        rssPanel.ColumnCount = 2;
        rssPanel.Dock = DockStyle.Fill;
        rssPanel.Margin = new Padding(0);
        var feedsLabel = new Label { Text = L.SettingsFeeds, AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        rssPanel.Controls.Add(feedsLabel, 0, 0);
        rssPanel.SetColumnSpan(feedsLabel, 2);
        feedsBox.Multiline = true;
        feedsBox.ScrollBars = ScrollBars.Vertical;
        feedsBox.WordWrap = false;
        feedsBox.Size = new Size(620, 96);
        feedsBox.TextChanged += (_, _) =>
        {
            if (loading) return;
            MarkDirty();
            feedsDebounce.Stop();
            feedsDebounce.Start();
        };
        rssPanel.Controls.Add(feedsBox, 0, 1);
        rssPanel.SetColumnSpan(feedsBox, 2);
        rssPanel.Controls.Add(new Label { Text = L.SettingsRotate, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        rotateBox.Minimum = 3;
        rotateBox.Maximum = 300;
        rotateBox.Width = 70;
        rotateBox.ValueChanged += (_, _) =>
        {
            if (loading) return;
            CurrentSlot.Rss!.RotateSeconds = (int)rotateBox.Value;
            MarkDirty();
        };
        rssPanel.Controls.Add(rotateBox, 1, 2);
        grid.Controls.Add(rssPanel, 0, 2);
        grid.SetColumnSpan(rssPanel, 2);

        var showButton = new Button { Text = L.SettingsShowNow, AutoSize = true, Margin = new Padding(3, 10, 3, 3) };
        showButton.Click += (_, _) =>
        {
            if (dirty) Apply();
            app.SelectSlot(selected);
        };
        grid.Controls.Add(showButton, 0, 3);
        grid.SetColumnSpan(showButton, 2);

        slotGroup.Controls.Add(grid);
        return slotGroup;
    }

    /// <summary>Einstellungen, die nicht an einer Taste haengen: Sprache, ICP-Testseite.</summary>
    private Control BuildGeneralGroup()
    {
        var group = new GroupBox { Text = L.SettingsGeneral, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(10), Margin = new Padding(3, 10, 3, 3) };
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };

        row.Controls.Add(new Label { Text = L.SettingsLanguage, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
        languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        languageCombo.Width = 170;
        languageCombo.DisplayMember = nameof(LanguageChoice.Text);
        languageCombo.Items.Add(new LanguageChoice(L.Auto, L.SettingsLanguageAuto(L.SystemLanguage)));
        foreach (Language language in Enum.GetValues<Language>()) languageCombo.Items.Add(new LanguageChoice(L.Code(language), L.Name(language)));
        languageCombo.SelectedItem = languageCombo.Items.Cast<LanguageChoice>()
            .FirstOrDefault(c => c.Code.Equals(draft.Language, StringComparison.OrdinalIgnoreCase)) ?? languageCombo.Items[0];
        languageCombo.SelectedIndexChanged += (_, _) =>
        {
            if (languageCombo.SelectedItem is not LanguageChoice choice) return;
            draft.Language = choice.Code;
            MarkDirty();
        };
        row.Controls.Add(languageCombo);

        var testButton = new Button { Text = L.SettingsTestPage, AutoSize = true, Margin = new Padding(30, 3, 3, 3) };
        testButton.Click += (_, _) => app.ToggleTestPage();
        row.Controls.Add(testButton);

        group.Controls.Add(row);
        return group;
    }

    private Control BuildButtonBar()
    {
        var bar = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        // Schliessen fragt bei ungespeicherten Aenderungen nach (OnFormClosing).
        var close = new Button { Text = L.Close, AutoSize = true, MinimumSize = new Size(80, 0) };
        close.Click += (_, _) => Close();
        applyButton.Text = L.Apply;
        applyButton.AutoSize = true;
        applyButton.Enabled = false;
        applyButton.Click += (_, _) => Apply(reopenOnLanguageChange: true);
        var ok = new Button { Text = L.Ok, AutoSize = true, MinimumSize = new Size(80, 0) };
        ok.Click += (_, _) =>
        {
            if (dirty) Apply();
            Close();
        };
        bar.Controls.AddRange([close, applyButton, ok]);
        AcceptButton = ok;
        CancelButton = close;
        return bar;
    }

    // ---------- Verhalten ----------

    private void SelectSlot(int index)
    {
        // Noch nicht uebernommene Feed-Eingabe nicht verlieren.
        if (feedsDebounce.Enabled)
        {
            feedsDebounce.Stop();
            CurrentSlot.Rss!.Feeds = feedsBox.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        selected = index;
        SlotConfig slot = CurrentSlot;
        slot.Rss ??= new RssOptions();

        loading = true;
        slotGroup.Text = L.SettingsKey(slot.Button);
        pageCombo.SelectedItem = PageCatalog.Get(slot.Page);
        feedsBox.Text = string.Join(Environment.NewLine, slot.Rss.Feeds);
        rotateBox.Value = Math.Clamp(slot.Rss.RotateSeconds, (int)rotateBox.Minimum, (int)rotateBox.Maximum);
        loading = false;

        UpdatePageTypeUi();
        RebuildPreviewPage();
        UpdateKeys();
    }

    private void OnPageTypeChanged()
    {
        if (loading || pageCombo.SelectedItem is not PageType type) return;
        CurrentSlot.Page = type.Id;
        MarkDirty();
        UpdatePageTypeUi();
        RebuildPreviewPage();
        UpdateKeys();
    }

    private void UpdatePageTypeUi()
    {
        PageType type = PageCatalog.Get(CurrentSlot.Page);
        descriptionLabel.Text = type.Description();
        rssPanel.Visible = type.Id == "RSS";
    }

    private void RebuildPreviewPage()
    {
        previewPage = app.CreatePage(CurrentSlot);
        RefreshPreview();
    }

    private void RefreshPreview() => preview.ShowScreen(app.RenderPreview(previewPage, AppConfig.SlotButtons[selected]));

    /// <summary>Tastenbeschriftung: zugewiesene Seite; die gerade auf dem ICP aktive Taste ist markiert.</summary>
    private void UpdateKeys()
    {
        int active = app.SlotIndex;
        for (int i = 0; i < slotButtons.Length; i++)
        {
            bool isSelected = i == selected;
            slotButtons[i].BackColor = isSelected ? KeySelected : KeyFace;
            slotButtons[i].FlatAppearance.BorderColor = isSelected ? Accent : KeyBorder;

            SlotConfig slot = draft.Slot(AppConfig.SlotButtons[i]);
            string name = slot.Page == PageCatalog.None ? "–" : PageCatalog.Get(slot.Page).DisplayName;
            slotLabels[i].Text = i == active ? $"▶ {name}" : name;
            slotLabels[i].ForeColor = i == active ? Accent : Color.Gainsboro;
        }
    }

    private void OnAppStateChanged()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(UpdateKeys);
    }

    private void MarkDirty()
    {
        dirty = true;
        applyButton.Enabled = true;
    }

    private void Apply(bool reopenOnLanguageChange = false)
    {
        if (feedsDebounce.Enabled)
        {
            feedsDebounce.Stop();
            CurrentSlot.Rss!.Feeds = feedsBox.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }
        try
        {
            Language before = L.Current;
            app.ApplyConfig(draft.Clone());
            dirty = false;
            applyButton.Enabled = false;
            // Neue Sprache: Fenster neu aufbauen (bei OK wird es ohnehin geschlossen).
            if (L.Current != before && reopenOnLanguageChange) BeginInvoke(reopen);
        }
        catch (Exception ex)
        {
            Log.Error("Konfiguration konnte nicht gespeichert werden", ex);
            MessageBox.Show(this, L.SaveFailed(ex.Message), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (dirty && e.CloseReason == CloseReason.UserClosing)
        {
            var answer = MessageBox.Show(this, L.AskSaveChanges, Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (answer == DialogResult.Yes) Apply();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            app.StateChanged -= OnAppStateChanged;
            previewTimer.Dispose();
            feedsDebounce.Dispose();
            Icon?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record LanguageChoice(string Code, string Text);

    /// <summary>Eintrag der Spiele-Liste: entweder eingebaut oder selbst hinzugefuegt.</summary>
    private sealed record GameItem(string Text, GameDefinition? BuiltIn, CustomProgram? Custom)
    {
        public override string ToString() => Text;
    }
}
