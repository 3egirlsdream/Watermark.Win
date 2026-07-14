using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacMultiFrameWorkflowTests
{
    [Fact]
    public void MultiFrameConfiguration_IsEmbeddedInInspectorAndHasNoModalOrExportAction()
    {
        var root = FindRepositoryRoot();
        var workspace = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "Components", "Mac",
            "MacMultiFrameWorkspace.razor"));
        var inspector = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "Components", "Mac",
            "MacModeInspector.razor"));
        var workspaceStyles = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "Components", "Mac",
            "MacMultiFrameWorkspace.razor.css"));
        var mainView = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "BlazorPages",
            "MainViewOSX.razor"));

        Assert.DoesNotContain("<MDialog", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("导出", workspace, StringComparison.Ordinal);
        Assert.Contains("预览合成", workspace, StringComparison.Ordinal);
        Assert.Contains("应用合成", workspace, StringComparison.Ordinal);
        Assert.Contains("停止处理", workspace, StringComparison.Ordinal);
        Assert.Contains("<MSelect", workspace, StringComparison.Ordinal);
        Assert.Contains("<MacMultiFrameWorkspace", inspector, StringComparison.Ordinal);
        Assert.Contains("grid-template-columns: minmax(0, 1fr) auto auto;", workspaceStyles,
            StringComparison.Ordinal);
        Assert.Contains(".multi-frame-workspace.embedded .frame-main span", workspaceStyles,
            StringComparison.Ordinal);
        Assert.DoesNotContain("OpenMultiFrame", inspector, StringComparison.Ordinal);
        Assert.DoesNotContain("showMultiFrame", mainView, StringComparison.Ordinal);
    }

    [Fact]
    public void FormalApply_DoesNotRequireSuccessfulPreview()
    {
        var root = FindRepositoryRoot();
        var workspace = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "Components", "Mac",
            "MacMultiFrameWorkspace.razor"));

        Assert.Contains("private bool CanApply => CanExecute;", workspace, StringComparison.Ordinal);
        Assert.DoesNotContain("CanExecute && PreviewFingerprint == CreateFingerprint()", workspace, StringComparison.Ordinal);
        Assert.Contains("Formal apply always runs the full-resolution pipeline", workspace, StringComparison.Ordinal);
        var stackEngine = File.ReadAllText(Path.Combine(root, "Watermark.Shared", "Models",
            "WMMultiFrameStackEngine.cs"));
        Assert.Contains("previewEngine.ExecuteAsync(request, settings", stackEngine, StringComparison.Ordinal);
        Assert.Contains("HighPrecision = new WMHighPrecisionArtifact", stackEngine, StringComparison.Ordinal);
        Assert.DoesNotContain("request.IsPreview ?", stackEngine, StringComparison.Ordinal);
    }

    [Fact]
    public void ImportCompletion_RefreshesMultiFrameInputsWithoutModeToggle()
    {
        var root = FindRepositoryRoot();
        var mainView = File.ReadAllText(Path.Combine(root, "Watermark.Razor", "BlazorPages",
            "MainViewOSX.razor"));
        var importStart = mainView.IndexOf("void ImportLocalImages()", StringComparison.Ordinal);
        var importEnd = mainView.IndexOf("async Task<int> GetRecommendedProxyEdgeAsync()", importStart,
            StringComparison.Ordinal);
        Assert.True(importStart >= 0 && importEnd > importStart);
        var importBody = mainView[importStart..importEnd];

        Assert.Contains("StackDialogInputs = [];", importBody, StringComparison.Ordinal);
        Assert.Contains("MultiFramePreviewSrc = null;", importBody, StringComparison.Ordinal);
        Assert.Contains("RefreshMultiFrameInputs();", importBody, StringComparison.Ordinal);
        Assert.True(importBody.IndexOf("RefreshMultiFrameInputs();", StringComparison.Ordinal)
                    < importBody.IndexOf("CurrentImage = Images.FirstOrDefault();", StringComparison.Ordinal));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Watermark.sln")))
            directory = directory.Parent;
        return directory?.FullName
               ?? throw new DirectoryNotFoundException("Unable to find the Watermark repository root.");
    }
}
