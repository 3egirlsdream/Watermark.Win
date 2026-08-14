using Watermark.Razor.Workspace;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class WMLocalExportSinkTests
{
    [Fact]
    public async Task SystemPickerDestination_SavesIntoExplicitlySelectedDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), $"watermark-export-location-{Guid.NewGuid():N}");
        var renderedPath = Path.Combine(root, "rendered", "preview.jpg");
        var selectedDirectory = Path.Combine(root, "selected");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(renderedPath)!);
            await File.WriteAllBytesAsync(renderedPath, [1, 2, 3]);

            var sink = new WMLocalExportSink();
            var savedPath = await sink.SaveAsync(
                renderedPath,
                "export.jpg",
                WMExportFormat.Jpeg8,
                WMExportDestinationKind.SystemPicker,
                selectedDirectory);

            Assert.Equal(Path.GetFullPath(selectedDirectory), Path.GetDirectoryName(savedPath));
            Assert.True(File.Exists(savedPath));
            Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(savedPath));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task SystemPickerDestination_RejectsMissingDirectoryInsteadOfUsingDefault()
    {
        var renderedPath = Path.Combine(Path.GetTempPath(), $"watermark-export-render-{Guid.NewGuid():N}.jpg");
        try
        {
            await File.WriteAllBytesAsync(renderedPath, [1]);
            var sink = new WMLocalExportSink();

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sink.SaveAsync(
                renderedPath,
                "export.jpg",
                WMExportFormat.Jpeg8,
                WMExportDestinationKind.SystemPicker,
                destinationDirectory: null));

            Assert.Contains("选择导出目录", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { if (File.Exists(renderedPath)) File.Delete(renderedPath); } catch { }
        }
    }
}
