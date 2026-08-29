using Avalonia.Headless.XUnit;
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
}
