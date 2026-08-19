using FFIX.SaveEditor.Core;

namespace FFIX.SaveEditor.Tests;

public sealed class SafeFileWriterTests
{
    [Fact]
    public void RefusesInputPathAndPreservesNumberedBackups()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ffix-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "save.raw");
            var original = TestSaveFactory.CreateLegacyBlock();
            File.WriteAllBytes(path, original);
            var document = SaveDocument.Open(path);
            var reference = document.ListSlots()[0];
            var slot = document.LoadSlot(reference);
            slot.Gil = 999;
            document.CommitSlot(reference, slot);
            Assert.Throws<IOException>(() => SafeFileWriter.WriteNew(path, path, document));
            var first = SafeFileWriter.WriteInPlaceWithBackup(path, document);
            var second = SafeFileWriter.WriteInPlaceWithBackup(path, document);
            Assert.Equal("save.raw.bak", Path.GetFileName(first));
            Assert.Equal("save.raw.bak.1", Path.GetFileName(second));
            Assert.Equal(original, File.ReadAllBytes(first));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
