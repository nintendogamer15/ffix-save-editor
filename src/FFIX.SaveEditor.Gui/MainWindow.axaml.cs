// SPDX-License-Identifier: MIT
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using FFIX.SaveEditor.Core;
using FFIX.SaveEditor.Gui.ViewModels;

namespace FFIX.SaveEditor.Gui;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();
    private SaveDocument? _document;
    private IEditableSlot? _slot;
    private string? _savePath;

    public MainWindow() : this(null) { }

    public MainWindow(string? initialPath)
    {
        InitializeComponent();
        DataContext = _viewModel;
        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            PathBox.Text = initialPath;
            Open(initialPath);
        }
    }

    private async void BrowseInput(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Open Final Fantasy IX save",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new("FFIX saves") { Patterns = ["*.dat", "*.sav", "*.mcr", "*.mcd", "*.bin", "*.mc", "*.mci", "*.ps", "*.psm", "*.dff", "*.ps1", "*.mcs"] },
                FilePickerFileTypes.All,
            ],
        });
        var path = files.FirstOrDefault()?.Path.LocalPath;
        if (path is not null)
        {
            PathBox.Text = path;
            Open(path);
        }
    }

    private void LoadFile(object? sender, RoutedEventArgs e) => Open(PathBox.Text ?? string.Empty);

    private void ToggleTheme(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is { } application)
            application.RequestedThemeVariant = application.ActualThemeVariant == ThemeVariant.Dark
                ? ThemeVariant.Light : ThemeVariant.Dark;
    }

    private void Open(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path.Trim());
            var candidate = SaveDocument.Open(fullPath);
            var references = candidate.ListSlots();
            if (references.Count == 0) throw new SaveFormatException("The file contains no occupied FFIX save slots.");
            _document = candidate;
            _savePath = fullPath;
            PathBox.Text = fullPath;
            OutputBox.Text = DefaultOutput(fullPath);
            _viewModel.LoadSlots(candidate);
            LoadSelectedSlot();
            _viewModel.AppendLog($"Loaded {fullPath}: {SaveDocument.FormatLabel(candidate.Format)}, {references.Count} occupied save(s).");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SaveFormatException or ArgumentException)
        {
            Error($"Could not open save: {exception.Message}");
        }
    }

    private void SlotChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_document is not null && _viewModel.SelectedSlot is not null) LoadSelectedSlot();
    }

    private void LoadSelectedSlot(int? selectedCharacter = null)
    {
        if (_document is null || _viewModel.SelectedSlot is null) return;
        _slot = _document.LoadSlot(_viewModel.SelectedSlot.Reference);
        _viewModel.LoadSlot(_viewModel.SelectedSlot.Reference, _slot);
        if (selectedCharacter is not null)
        {
            var row = _viewModel.Characters.FirstOrDefault(x => x.Index == selectedCharacter);
            if (row is not null) _viewModel.SelectedCharacter = row;
        }
        LoadCharacterEditor();
        GilBox.Text = _slot.Gil.ToString();
    }

    private void CharacterChanged(object? sender, SelectionChangedEventArgs e) => LoadCharacterEditor();

    private void LoadCharacterEditor()
    {
        if (_slot is null || _viewModel.SelectedCharacter is null)
        {
            _viewModel.LoadAbilities(null);
            return;
        }
        var character = _slot.Character(_viewModel.SelectedCharacter.Index);
        NameBox.Text = character.Name;
        SetText(LevelBox, character, "level");
        SetText(ExperienceBox, character, "exp");
        SetText(CurrentHpBox, character, "cur_hp");
        SetText(MaximumHpBox, character, "max_hp");
        SetText(CurrentMpBox, character, "cur_mp");
        SetText(MaximumMpBox, character, "max_mp");
        SetText(StrengthBox, character, "strength");
        SetText(SpeedBox, character, "speed");
        SetText(MagicBox, character, "magic");
        SetText(SpiritBox, character, "spirit");
        SetEquipment(WeaponBox, character, "weapon");
        SetEquipment(HeadBox, character, "head");
        SetEquipment(ArmBox, character, "arm");
        SetEquipment(ArmorBox, character, "armor");
        SetEquipment(AccessoryBox, character, "accessory");
        _viewModel.LoadAbilities(character);
    }

    private void ApplyCharacter(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedCharacter is null) { Error("Select a character first."); return; }
        var index = _viewModel.SelectedCharacter.Index;
        EditSlot(candidate =>
        {
            var character = candidate.Character(index);
            character.Name = NameBox.Text ?? string.Empty;
            ApplyNumber(character, "level", LevelBox);
            ApplyNumber(character, "exp", ExperienceBox);
            ApplyNumber(character, "cur_hp", CurrentHpBox);
            ApplyNumber(character, "max_hp", MaximumHpBox);
            ApplyNumber(character, "cur_mp", CurrentMpBox);
            ApplyNumber(character, "max_mp", MaximumMpBox);
            ApplyNumber(character, "strength", StrengthBox);
            ApplyNumber(character, "speed", SpeedBox);
            ApplyNumber(character, "magic", MagicBox);
            ApplyNumber(character, "spirit", SpiritBox);
            ApplyEquipment(character, "weapon", WeaponBox);
            ApplyEquipment(character, "head", HeadBox);
            ApplyEquipment(character, "arm", ArmBox);
            ApplyEquipment(character, "armor", ArmorBox);
            ApplyEquipment(character, "accessory", AccessoryBox);
        }, $"Updated character row {index}.", index);
    }

    private void ApplyAbilities(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedCharacter is null) { Error("Select a character first."); return; }
        var index = _viewModel.SelectedCharacter.Index;
        EditSlot(candidate =>
        {
            var character = candidate.Character(index);
            if (character.Format != SaveFormat.Legacy)
                throw new InvalidOperationException("Support-ability editing is only available for PS1 saves.");
            foreach (var ability in _viewModel.Abilities)
                character.SetSupportAbility(ability.Index, ability.IsEnabled);
        }, $"Updated support abilities for character row {index}.", index);
    }

    private void SetGil(object? sender, RoutedEventArgs e) => EditSlot(candidate =>
    {
        candidate.Gil = ParseNumber(GilBox, "gil", SaveLayout.MaximumGil);
    }, "Updated gil.");

    private void MaxSelected(object? sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedCharacter is null) { Error("Select a character first."); return; }
        var index = _viewModel.SelectedCharacter.Index;
        EditSlot(candidate => candidate.Character(index).MaxOut(), $"Maxed character row {index}.", index);
    }

    private void MaxAll(object? sender, RoutedEventArgs e) => EditSlot(candidate =>
    {
        foreach (var character in candidate.Characters().Where(x => x.IsRecruited)) character.MaxOut();
    }, "Maxed every recruited character.");

    private void GiveAllItems(object? sender, RoutedEventArgs e) => EditSlot(candidate =>
    {
        var added = SaveDocument.GiveAllItems(candidate, Quantity);
        if (added == 0) throw new InvalidOperationException("No item entries could be added; the inventory may be full.");
    }, $"Added all known items and gear at quantity {Quantity} where space allowed.");

    private void AddItem(object? sender, RoutedEventArgs e)
    {
        var token = ItemBox.Text?.Trim();
        if (string.IsNullOrEmpty(token)) { Error("Enter or pick an item/gear name first."); return; }
        EditSlot(candidate =>
        {
            var itemId = GameData.ResolveItemId(token);
            if (!candidate.SetItem(itemId, Quantity)) throw new InvalidOperationException("Inventory is full.");
        }, $"Added {token} at quantity {Quantity}.");
    }

    private void KnownItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (KnownItemCombo.SelectedItem is string selected) ItemBox.Text = selected;
    }

    private async void BrowseOutput(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new()
        {
            Title = "Write edited FFIX save",
            SuggestedFileName = _savePath is null ? "save.edited.dat" : Path.GetFileName(DefaultOutput(_savePath)),
            FileTypeChoices = [FilePickerFileTypes.All],
        });
        if (file is not null) OutputBox.Text = file.Path.LocalPath;
    }

    private void WriteNew(object? sender, RoutedEventArgs e)
    {
        if (_document is null || _savePath is null) { Error("Load a save before writing."); return; }
        var output = string.IsNullOrWhiteSpace(OutputBox.Text) ? DefaultOutput(_savePath) : OutputBox.Text!.Trim();
        try
        {
            SafeFileWriter.WriteNew(_savePath, output, _document);
            _viewModel.AppendLog($"Wrote edited copy: {output}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error($"Write failed: {exception.Message}");
        }
    }

    private async void WriteInPlace(object? sender, RoutedEventArgs e)
    {
        if (_document is null || _savePath is null) { Error("Load a save before writing."); return; }
        if (!await ConfirmDialog.Ask(this, $"Overwrite {_savePath}?\nA new numbered .bak backup will be written first.")) return;
        try
        {
            var backup = SafeFileWriter.WriteInPlaceWithBackup(_savePath, _document);
            _viewModel.AppendLog($"Wrote in place; backup: {backup}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Error($"In-place write failed: {exception.Message}");
        }
    }

    private void EditSlot(Action<IEditableSlot> operation, string message, int? selectedCharacter = null)
    {
        if (_document is null || _slot is null || _viewModel.SelectedSlot is null)
        {
            Error("Load a save and select a slot before editing.");
            return;
        }
        try
        {
            var candidate = _slot.Clone();
            operation(candidate);
            _document.CommitSlot(_viewModel.SelectedSlot.Reference, candidate);
            LoadSelectedSlot(selectedCharacter);
            _viewModel.AppendLog(message + " The edited slot was finalized and reloaded successfully.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or SaveFormatException or OverflowException)
        {
            Error($"Edit failed; no partial edit was applied: {exception.Message}");
        }
    }

    private static void ApplyNumber(IEditableCharacter character, string requestedField, TextBox box)
    {
        var field = ActualField(character, requestedField);
        if (field is null) return;
        character.Set(field, ParseNumber(box, requestedField, character.MaximumFor(field)));
    }

    private static void ApplyEquipment(IEditableCharacter character, string field, TextBox box)
    {
        if (!character.Has(field) || string.IsNullOrWhiteSpace(box.Text)) return;
        character.Set(field, GameData.ResolveItemId(box.Text));
    }

    private static int ParseNumber(TextBox box, string label, int maximum)
    {
        if (!int.TryParse(box.Text, out var value) || value < 0 || value > maximum)
            throw new ArgumentException($"{label} must be a whole number from 0 to {maximum:N0}.");
        return value;
    }

    private static string? ActualField(IEditableCharacter character, string requested)
    {
        if (character.Has(requested)) return requested;
        var basis = requested + "_base";
        return character.Has(basis) ? basis : null;
    }

    private static void SetText(TextBox box, IEditableCharacter character, string requested)
    {
        var field = ActualField(character, requested);
        box.Text = field is null ? string.Empty : character.Get(field).ToString();
        box.IsEnabled = field is not null;
    }

    private static void SetEquipment(TextBox box, IEditableCharacter character, string field)
    {
        box.Text = character.Has(field) ? character.Get(field).ToString() : string.Empty;
        box.IsEnabled = character.Has(field);
    }

    private int Quantity => (int)(QuantityBox.Value ?? 99);
    private void Error(string message) => _viewModel.AppendLog("ERROR: " + message);
    private static string DefaultOutput(string path) => Path.Combine(Path.GetDirectoryName(path)!,
        Path.GetFileNameWithoutExtension(path) + ".edited" + Path.GetExtension(path));
}
