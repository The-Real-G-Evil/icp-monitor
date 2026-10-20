using IcpMonitor.Pages;

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

    /// <param name="reopen">Schliesst das Fenster und oeffnet es neu (nach einem Sprachwechsel).</param>
    public SettingsForm(MonitorApp app, Action reopen)
    {
        this.app = app;
        this.reopen = reopen;
        draft = app.CloneConfig();

        Text = L.SettingsTitle;
        Icon = TrayContext.CreateIcon();
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(12), Dock = DockStyle.Fill };
        root.Controls.Add(BuildIcpPanel());
        root.Controls.Add(BuildSlotGroup());
        root.Controls.Add(BuildGeneralGroup());
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
}
