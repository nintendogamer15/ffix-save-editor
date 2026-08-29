// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Text;

namespace FFIX.SaveEditor.Gui;

internal static class EmbeddedLegalNotices
{
    private const string Prefix = "Legal/";

    public static string Load()
    {
        Assembly assembly = typeof(EmbeddedLegalNotices).Assembly;
        string[] names = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (names.Length == 0) return "Embedded legal notices could not be loaded.";

        StringBuilder text = new("FINAL FANTASY IX SAVE EDITOR\n\nEMBEDDED LICENCE AND NOTICE TEXTS\n");
        foreach (string name in names)
        {
            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"Embedded legal resource is missing: {name}");
            using StreamReader reader = new(stream);
            text.Append("\n===== ").Append(name[Prefix.Length..]).AppendLine(" =====");
            text.AppendLine(reader.ReadToEnd().TrimEnd());
        }
        return text.ToString();
    }
}
