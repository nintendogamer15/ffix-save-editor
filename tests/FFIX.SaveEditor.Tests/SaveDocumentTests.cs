// SPDX-License-Identifier: MIT
using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Tests;

public sealed class SaveDocumentTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void OpenRejectsMissingFileSelection(string? path)
    {
        var exception = Assert.Throws<SaveFormatException>(() => SaveDocument.Open(path));
        Assert.Equal("No save file was selected.", exception.Message);
    }
}
