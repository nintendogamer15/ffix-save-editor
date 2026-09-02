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
