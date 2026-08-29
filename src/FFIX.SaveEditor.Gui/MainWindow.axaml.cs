// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using FFIX.SaveEditor.Core;
using FFIX.SaveEditor.Gui.Editing;
using FFIX.SaveEditor.Gui.Saves;
using SaveEditor.Ui.Codecs;
using SaveEditor.Ui.Dialogs;
using SaveEditor.Ui.Editing;
using SaveEditor.Ui.Hosting;
using SaveEditor.Ui.Settings;
using SaveEditor.Ui.Shell;
using SaveEditor.Ui.Theming;
using SaveEditor.Ui.Workflow;

namespace FFIX.SaveEditor.Gui;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly string? initialPath;
    private readonly EditHistory history = new();
    private readonly DocumentSession<SaveDocument> session;
    private readonly FfixWorkspace workspace;
    private readonly EditorShellViewModel viewModel;
    private readonly ThemeController theme;
    private readonly Dictionary<string, ContentControl> sectionHosts = [];
    private bool disposed;
    private bool initialized;

    public MainWindow() : this(null, null) { }

    public MainWindow(string? initialPath) : this(initialPath, null) { }

    public MainWindow(string? initialPath, EditorSettingsStoreOptions? settingsOptions)
    {
        this.initialPath = initialPath;
        AvaloniaXamlLoader.Load(this);

        EditorSettingsStore settings = new(EditorApplicationId.Parse("FFIXSaveEditor"), settingsOptions);
        theme = new ThemeController(
            Application.Current!.Styles.OfType<SaveEditorTheme>().Single(),
            settings,
            CatppuccinAccent.Mauve);
        WindowEditorHost host = new(this);
        ThemedUserInteraction interaction = new(this, global::SaveEditor.Ui.Display.PathDisplayFormatter.Default);

        FfixSaveCodec codec = new();
        SaveCodecRegistry<SaveDocument> registry = new(
        [
            new CodecRegistration<SaveDocument>(new FfixSaveDetector(), codec),
        ]);
        SafeFileWorkflow<SaveDocument> workflow = new(new SafeFileWorkflowOptions<SaveDocument>
        {
            Registry = registry,
            Interaction = interaction,
            DocumentComparer = FfixDocumentComparer.Instance,
            MaxBytes = FfixSaveCodec.MaximumBytes,
            ConfirmAboveBytes = FfixSaveCodec.MaximumBytes,
            MaxSerializedBytes = FfixSaveCodec.MaximumBytes,
        });

        session = new DocumentSession<SaveDocument>(workflow, history, codec);
        workspace = new FfixWorkspace(session, history);
        session.PendingEditProbe = () => workspace.HasPendingEdits;
        session.DocumentChanged += (_, _) => workspace.BindDocument();
        workspace.Changed += (_, _) => RefreshBodies();

        viewModel = new EditorShellViewModel(session, interaction, settings, host, theme)
        {
            AboutMessage = EmbeddedLegalNotices.Load(),
            SafetyMessage =
                "Save As is the default write path. Overwrite + Backup verifies a backup and " +
                "checks the retained file before replacement. Symlinks and ordinary external " +
                "content changes are refused.\n\nThese safeguards do not eliminate a concurrent " +
                "replacement of the final pathname or one of its ancestor directories after the " +
                "last retained-file check. Do not save while another process is moving or replacing " +
                "the save path. Codecs run in-process and are not sandboxed.",
        };

        RegisterSections();
        EditorShell shell = this.FindControl<EditorShell>("Shell")!;
        shell.DataContext = viewModel;
        DragDropAdapter.Attach(shell, viewModel);

        Closed += (_, _) => Dispose();
        Loaded += async (_, _) => await InitializeAsync().ConfigureAwait(true);
    }

    public EditorShellViewModel ViewModel => viewModel;
    public FfixWorkspace Workspace => workspace;

    public async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized) return;
        initialized = true;
        await theme.InitializeAsync(cancellationToken).ConfigureAwait(true);
        await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(initialPath))
            await viewModel.OpenPathAsync(initialPath, cancellationToken).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        viewModel.Dispose();
        session.Dispose();
        GC.SuppressFinalize(this);
    }

    private void RegisterSections()
    {
        viewModel.RegisterSections(
        [
            Section("overview", "Overview", "Slot summary and common save actions"),
            Section("characters", "Characters", "Names, stats, equipment and maximization"),
            Section("support-abilities", "PS1 Support Abilities", "The 64 legacy support-ability bits"),
            Section("inventory", "Inventory", "Items and gear in the selected save"),
            Section("cards", "Tetra Master Cards", "Card collection and match record"),
        ]);
        RefreshBodies();
    }

    private SectionDescriptor Section(string key, string title, string subtitle)
    {
        ContentControl host = new();
        sectionHosts[key] = host;
        return new SectionDescriptor
        {
            Key = key,
            Title = title,
            Subtitle = subtitle,
            BodyMode = SectionBodyMode.Custom,
            Body = host,
        };
    }

    private void RefreshBodies()
    {
        if (sectionHosts.Count == 0) return;
        sectionHosts["overview"].Content = BuildOverview();
        sectionHosts["characters"].Content = BuildCharacters();
        sectionHosts["support-abilities"].Content = BuildAbilities();
        sectionHosts["inventory"].Content = BuildInventory();
        sectionHosts["cards"].Content = BuildCards();
        viewModel.RefreshSections();
    }

    private Control BuildOverview()
    {
        StackPanel panel = Panel();
        ComboBox slots = new()
        {
            ItemsSource = workspace.Slots.Select(reference => $"{reference.Label} — {reference.Summary}").ToArray(),
            SelectedIndex = workspace.SelectedSlotIndex,
            PlaceholderText = "Open a save to select an occupied slot",
        };
        slots.SelectionChanged += (_, _) =>
        {
            if (slots.SelectedIndex >= 0) workspace.SelectSlot(slots.SelectedIndex);
        };
        panel.Children.Add(Label("Save slot"));
        panel.Children.Add(slots);
        panel.Children.Add(new TextBlock { Text = workspace.Overview, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        IEditableSlot? slot = workspace.CurrentSlot;
        TextBox gil = Input(slot?.Gil.ToString() ?? string.Empty, "Gil");
        panel.Children.Add(Row(Label("Gil"), gil, Button("Apply gil", () => workspace.SetGil(gil.Text ?? string.Empty))));
        panel.Children.Add(Row(
            Button("Max selected character", workspace.MaxSelected),
            Button("Max all recruited characters", workspace.MaxAll),
            Button("Give all items and gear", () => workspace.GiveAllItems(99))));

        ComboBox known = new() { ItemsSource = GameData.ItemNames, PlaceholderText = "Pick an item or gear…" };
        TextBox item = Input(string.Empty, "Name, decimal ID, or 0xID");
        known.SelectionChanged += (_, _) => { if (known.SelectedItem is string name) item.Text = name; };
        NumericUpDown quantity = new() { Minimum = 1, Maximum = 99, Value = 99, Width = 90 };
        panel.Children.Add(Label("Add item / gear"));
        panel.Children.Add(known);
        panel.Children.Add(Row(item, quantity, Button("Add", () => workspace.AddItem(item.Text ?? string.Empty, (int)(quantity.Value ?? 99)))));
        return Scroll(panel);
    }

    private Control BuildCharacters()
    {
        StackPanel panel = Panel();
        IReadOnlyList<IEditableCharacter> characters = workspace.CurrentSlot?.Characters() ?? [];
        ComboBox picker = new()
        {
            ItemsSource = characters.Select(character => $"{character.Index}: {character.Name}" + (character.IsRecruited ? string.Empty : " (not recruited)")).ToArray(),
            SelectedIndex = workspace.SelectedCharacterIndex,
        };
        picker.SelectionChanged += (_, _) => { if (picker.SelectedIndex >= 0) workspace.SelectCharacter(picker.SelectedIndex); };
        panel.Children.Add(picker);

        IEditableCharacter? character = workspace.CurrentCharacter;
        if (character is null) return Scroll(panel);

        TextBox name = Input(character.Name, "Name");
        panel.Children.Add(Row(Label("Name"), name));
        Dictionary<string, TextBox> numbers = [];
        foreach ((string field, string label) in CharacterFields)
        {
            string? actual = character.Has(field) ? field : character.Has(field + "_base") ? field + "_base" : null;
            if (actual is null) continue;
            TextBox box = Input(character.Get(actual).ToString(), label);
            numbers[field] = box;
            panel.Children.Add(Row(Label(label), box));
        }

        Dictionary<string, TextBox> equipment = [];
        foreach (string field in SaveLayout.EquipmentSlots)
        {
            if (!character.Has(field)) continue;
            TextBox box = Input(character.Get(field).ToString(), field);
            equipment[field] = box;
            panel.Children.Add(Row(Label(ToTitle(field)), box));
        }

        panel.Children.Add(Button("Apply character", () => workspace.ApplyCharacter(new CharacterDraft(
            name.Text ?? string.Empty,
            numbers.ToDictionary(pair => pair.Key, pair => pair.Value.Text ?? string.Empty),
            equipment.ToDictionary(pair => pair.Key, pair => pair.Value.Text ?? string.Empty)))));
        return Scroll(panel);
    }

    private Control BuildAbilities()
    {
        StackPanel panel = Panel();
        IEditableCharacter? character = workspace.CurrentCharacter;
        if (character is null)
        {
            panel.Children.Add(Label("Open a save and select a character."));
            return Scroll(panel);
        }
        if (!workspace.CanEditSupportAbilities)
        {
            panel.Children.Add(Label("Support-ability editing is available only for PS1 saves."));
            return Scroll(panel);
        }

        HashSet<int> enabled = character.SupportAbilities().ToHashSet();
        List<CheckBox> checks = [];
        for (int index = 0; index < GameData.SupportAbilityNames.Count; index++)
        {
            CheckBox check = new() { Content = $"{index}: {GameData.SupportAbilityNames[index]}", IsChecked = enabled.Contains(index) };
            check.Click += (_, _) => workspace.SetPendingEdits(true);
            checks.Add(check);
            panel.Children.Add(check);
        }
        panel.Children.Add(Button("Apply support abilities", () => workspace.ApplySupportAbilities(
            checks.Select((check, index) => (check, index)).Where(pair => pair.check.IsChecked == true).Select(pair => pair.index).ToArray())));
        return Scroll(panel);
    }

    private Control BuildInventory()
    {
        StackPanel panel = Panel();
        foreach (InventoryItem item in workspace.CurrentSlot?.Items() ?? [])
            panel.Children.Add(Label($"{item.SlotIndex}: {item.Name} — {item.Quantity}"));
        if (panel.Children.Count == 0) panel.Children.Add(Label("No inventory entries."));
        return Scroll(panel);
    }

    private Control BuildCards()
    {
        StackPanel panel = Panel();
        if (workspace.CurrentSlot is { } slot)
        {
            panel.Children.Add(Label($"Record: {slot.CardRecord.Wins}W / {slot.CardRecord.Losses}L / {slot.CardRecord.Draws}D"));
            foreach (CardInfo card in slot.Cards())
                panel.Children.Add(Label($"{card.Index}: {card.TypeName} — {card.Attack:X2}{card.AttackTypeName}{card.PhysicalDefense:X2}{card.MagicDefense:X2}, arrows {card.Arrows:X2}"));
        }
        if (panel.Children.Count == 0) panel.Children.Add(Label("No Tetra Master cards."));
        return Scroll(panel);
    }

    private TextBox Input(string text, string watermark)
    {
        TextBox box = new() { Text = text, PlaceholderText = watermark, MinWidth = 180 };
        bool editing = false;
        box.GotFocus += (_, _) => editing = true;
        box.TextChanged += (_, _) => { if (editing) workspace.SetPendingEdits(true); };
        return box;
    }

    private Button Button(string caption, Func<EditResult> action)
    {
        Button button = new() { Content = caption };
        AutomationProperties.SetName(button, caption);
        button.Click += (_, _) => viewModel.StatusMessage = action().Message;
        return button;
    }

    private static StackPanel Panel() => new() { Margin = new Thickness(16), Spacing = 9 };
    private static ScrollViewer Scroll(Control control) => new() { Content = control };
    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private static StackPanel Row(params Control[] controls)
    {
        StackPanel row = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (Control control in controls) row.Children.Add(control);
        return row;
    }
    private static string ToTitle(string value) => char.ToUpperInvariant(value[0]) + value[1..];

    private static readonly (string Field, string Label)[] CharacterFields =
    [
        ("level", "Level"), ("exp", "Experience"), ("cur_hp", "Current HP"),
        ("max_hp", "Maximum HP"), ("cur_mp", "Current MP"), ("max_mp", "Maximum MP"),
        ("strength", "Strength"), ("speed", "Speed"), ("magic", "Magic"), ("spirit", "Spirit"),
    ];
}
