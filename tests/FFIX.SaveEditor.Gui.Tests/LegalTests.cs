using System.Reflection;
using FFIX.SaveEditor.Gui.Saves;

namespace FFIX.SaveEditor.Gui.Tests;

public sealed class LegalTests
{
    [Fact]
    public void CheckedInEmbeddedAndLockedFrameworkLicencesAgree()
    {
        string root = RepositoryRoot();
        Assembly assembly = typeof(FfixSaveCodec).Assembly;
        foreach (string file in new[] { "SaveEditor.Ui-0BSD.txt", "CommunityToolkit.Mvvm-MIT.txt" })
        {
            string checkedIn = File.ReadAllText(Path.Combine(root, "LICENSES", file));
            using Stream stream = assembly.GetManifestResourceStream("Legal/LICENSES/" + file)
                ?? throw new InvalidOperationException("Missing embedded legal resource: " + file);
            using StreamReader reader = new(stream);
            Assert.Equal(checkedIn, reader.ReadToEnd());
        }

        string lockText = File.ReadAllText(Path.Combine(root, "src", "FFIX.SaveEditor.Gui", "packages.lock.json"));
        Assert.Contains("\"CommunityToolkit.Mvvm\"", lockText, StringComparison.Ordinal);
        Assert.Contains("\"resolved\": \"8.4.2\"", lockText, StringComparison.Ordinal);
        Assert.Contains(
            "6ee70c4f02cdbd9790c9a64738cac97bba8e734e",
            File.ReadAllText(Path.Combine(root, "LICENSES", "SaveEditor.Ui-0BSD.txt")),
            StringComparison.Ordinal);
        Assert.Contains(
            "https://github.com/nintendogamer15/save-editor-gui-framework",
            File.ReadAllText(Path.Combine(root, ".gitmodules")),
            StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, ".gitmodules")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
