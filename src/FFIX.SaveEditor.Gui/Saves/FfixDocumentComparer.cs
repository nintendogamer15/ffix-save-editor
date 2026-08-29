// SPDX-License-Identifier: MIT
using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Gui.Saves;

public sealed class FfixDocumentComparer : IEqualityComparer<SaveDocument>
{
    public static FfixDocumentComparer Instance { get; } = new();

    public bool Equals(SaveDocument? x, SaveDocument? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x is null || y is null) return false;
        return x.ToArray().AsSpan().SequenceEqual(y.ToArray());
    }

    public int GetHashCode(SaveDocument obj) => 0;
}
