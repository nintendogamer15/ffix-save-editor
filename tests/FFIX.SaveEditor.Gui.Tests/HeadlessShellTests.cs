using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FFIX.SaveEditor.Core;
using SaveEditor.Ui.Settings;

namespace FFIX.SaveEditor.Gui.Tests;

public sealed class HeadlessShellTests
{
    [AvaloniaFact]
    public async Task ShellHasFiveSectionsAndLoadsInitialPathIntoRecents()
    {
        string root = Path.Combine(Path.GetTempPath(), "ffix-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string save = Path.Combine(root, "save.ps1");
        byte[] block = new byte[SaveLayout.LegacyBlockSize];
        "SC"u8.CopyTo(block);
        block[SaveLayout.LegacyLeaderLevelOffset] = 1;
        LegacyTextCodec.Encode("Zidane", SaveLayout.LegacyLeaderNameLength).CopyTo(block, SaveLayout.LegacyLeaderNameOffset);
        BinarySlot slot = new(block, SaveFormat.Legacy);
        slot.Character(0).Set("level", 1);
        slot.Character(0).Name = "Zidane";
        LegacyChecksum.Repair(block);
        await File.WriteAllBytesAsync(save, block);

        MainWindow window = new(save, new EditorSettingsStoreOptions { BaseDirectory = Path.Combine(root, "settings") });
        try
        {
            Assert.Equal(
                ["overview", "characters", "support-abilities", "inventory", "cards"],
                window.ViewModel.Sections.Select(section => section.Key));
            Assert.True(window.ViewModel.CanChangeAppearance);
            Assert.Equal(2, window.ViewModel.ThemeModes.Count);
            Assert.Equal(14, window.ViewModel.Accents.Count);

            await window.InitializeAsync();

            Assert.NotNull(window.Workspace.Document);
            Assert.Contains(window.ViewModel.Recents, recent => string.Equals(recent.Path, save, StringComparison.Ordinal));
            window.Workspace.SetPendingEdits(true);
            Assert.True(window.ViewModel.HasUnsavedWork);
            Assert.Contains("concurrent", window.ViewModel.SafetyMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            window.Workspace.SetPendingEdits(false);
            window.Close();
            window.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task SelectingACharacterKeepsTheCharactersSectionSelected()
    {
        string root = Path.Combine(Path.GetTempPath(), "ffix-headless-char-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string save = Path.Combine(root, "save.ps1");
        await File.WriteAllBytesAsync(save, OccupiedLegacyBlock());

        MainWindow window = new(save, new EditorSettingsStoreOptions { BaseDirectory = Path.Combine(root, "settings") });
        try
        {
            await window.InitializeAsync();
            window.Show();
            window.ViewModel.SelectedSection = window.ViewModel.Sections.Single(section => section.Key == "characters");
            window.Workspace.SelectCharacter(1);
            Assert.Equal(1, window.Workspace.SelectedCharacterIndex);
            Assert.Equal("characters", window.ViewModel.SelectedSection?.Key);
        }
        finally
        {
            window.Close();
            window.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task OverviewAddItemQuantityIsNotAClippedNumericUpDown()
    {
        string root = Path.Combine(Path.GetTempPath(), "ffix-headless-qty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string save = Path.Combine(root, "save.ps1");
        await File.WriteAllBytesAsync(save, OccupiedLegacyBlock());

        MainWindow window = new(save, new EditorSettingsStoreOptions { BaseDirectory = Path.Combine(root, "settings") });
        try
        {
            await window.InitializeAsync();
            Control overview = (Control)window.ViewModel.Sections.Single(section => section.Key == "overview").Body!;
            Assert.Empty(Walk(overview).OfType<NumericUpDown>());
            TextBox quantity = Assert.Single(Walk(overview).OfType<TextBox>(), box => box.Text == "99");
            Assert.True(Math.Max(quantity.Width, quantity.MinWidth) >= 48);
        }
        finally
        {
            window.Close();
            window.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task InventoryAndOverviewUseSearchableItemPickers()
    {
        string root = Path.Combine(Path.GetTempPath(), "ffix-headless-items-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string save = Path.Combine(root, "save.ps1");
        byte[] block = OccupiedLegacyBlock();
        BinarySlot slot = new(block, SaveFormat.Legacy);
        Assert.True(slot.SetItem(GameData.ResolveItemId("Potion"), 7));
        LegacyChecksum.Repair(block);
        await File.WriteAllBytesAsync(save, block);

        MainWindow window = new(save, new EditorSettingsStoreOptions { BaseDirectory = Path.Combine(root, "settings") });
        try
        {
            await window.InitializeAsync();
            IReadOnlyList<InventoryItem> items = window.Workspace.CurrentSlot!.Items();
            Assert.NotEmpty(items);

            Control inventory = (Control)window.ViewModel.Sections.Single(section => section.Key == "inventory").Body!;
            List<AutoCompleteBox> inventoryPickers = Walk(inventory).OfType<AutoCompleteBox>().ToList();
            Assert.Equal(items.Count + 1, inventoryPickers.Count);
            for (int index = 0; index < items.Count; index++)
            {
                AutoCompleteBox picker = inventoryPickers[index];
                Assert.Equal(items[index].Name, picker.Text);
                Assert.Same(GameData.ItemNames, picker.ItemsSource);
                Assert.Equal(AutoCompleteFilterMode.Contains, picker.FilterMode);
                Assert.Equal(0, picker.MinimumPrefixLength);
                Assert.False(picker.IsTextCompletionEnabled);
            }
            AutoCompleteBox addPicker = inventoryPickers[^1];
            Assert.True(string.IsNullOrEmpty(addPicker.Text));
            Assert.Same(GameData.ItemNames, addPicker.ItemsSource);
            Assert.Equal(AutoCompleteFilterMode.Contains, addPicker.FilterMode);
            Assert.Equal(0, addPicker.MinimumPrefixLength);
            Assert.False(addPicker.IsTextCompletionEnabled);
            Assert.DoesNotContain(Walk(inventory).OfType<ComboBox>(), box => box.PlaceholderText == "Pick an item or gear…");
            Assert.Contains(Walk(inventory).OfType<TextBox>(), box => box.Text == "7");

            Control overview = (Control)window.ViewModel.Sections.Single(section => section.Key == "overview").Body!;
            AutoCompleteBox overviewAdd = Assert.Single(Walk(overview).OfType<AutoCompleteBox>());
            Assert.Same(GameData.ItemNames, overviewAdd.ItemsSource);
            Assert.Equal(AutoCompleteFilterMode.Contains, overviewAdd.FilterMode);
            Assert.Equal(0, overviewAdd.MinimumPrefixLength);
            Assert.False(overviewAdd.IsTextCompletionEnabled);
            Assert.DoesNotContain(Walk(overview).OfType<ComboBox>(), box => box.PlaceholderText == "Pick an item or gear…");
            Assert.Contains(Walk(overview).OfType<ComboBox>(), box => box.PlaceholderText == "Open a save to select an occupied slot");
            Assert.DoesNotContain(Walk(overview).OfType<ComboBox>(), box => ReferenceEquals(box.ItemsSource, GameData.ItemNames));
        }
        finally
        {
            window.Close();
            window.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] OccupiedLegacyBlock()
    {
        byte[] block = new byte[SaveLayout.LegacyBlockSize];
        "SC"u8.CopyTo(block);
        block[SaveLayout.LegacyLeaderLevelOffset] = 1;
        LegacyTextCodec.Encode("Zidane", SaveLayout.LegacyLeaderNameLength).CopyTo(block, SaveLayout.LegacyLeaderNameOffset);
        BinarySlot slot = new(block, SaveFormat.Legacy);
        slot.Character(0).Set("level", 1);
        slot.Character(0).Name = "Zidane";
        LegacyChecksum.Repair(block);
        return block;
    }

    private static IEnumerable<Control> Walk(Control root)
    {
        yield return root;
        List<Control> children = [];
        if (root is ContentControl contentControl && contentControl.Content is Control content)
            children.Add(content);
        if (root is Decorator decorator && decorator.Child is Control decorated)
            children.Add(decorated);
        if (root is Panel panel)
            children.AddRange(panel.Children);
        children.AddRange(root.GetLogicalChildren().OfType<Control>());
        foreach (Control child in children.Distinct())
        {
            foreach (Control nested in Walk(child))
                yield return nested;
        }
    }
}
