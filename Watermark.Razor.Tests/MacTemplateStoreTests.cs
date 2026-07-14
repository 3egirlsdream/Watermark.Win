using Watermark.Razor.Components.Mac.Editor;
using Watermark.Shared.Enums;
using Watermark.Shared.Models;
using Xunit;

namespace Watermark.Razor.Tests;

public sealed class MacTemplateStoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InvalidCanvas_DoesNotReplaceExistingConfig()
    {
        var canvas = SplitCanvas();
        canvas.Children.Add(new WMContainer { ID = "DUPLICATE", Name = "one" });
        canvas.Children.Add(new WMContainer { ID = "DUPLICATE", Name = "two" });
        var directory = Path.Combine(root, canvas.ID);
        Directory.CreateDirectory(directory);
        var config = Path.Combine(directory, "config.json");
        await File.WriteAllTextAsync(config, "original");

        await Assert.ThrowsAsync<MacTemplateValidationException>(
            () => new MacTemplateStore().SaveAsync(canvas, root));

        Assert.Equal("original", await File.ReadAllTextAsync(config));
    }

    [Fact]
    public async Task ValidCanvas_ReplacesConfigAndRemovesTemporaryFile()
    {
        var canvas = SplitCanvas();
        await new MacTemplateStore().SaveAsync(canvas, root);
        var config = Path.Combine(root, canvas.ID, "config.json");
        Assert.Contains(canvas.ID, await File.ReadAllTextAsync(config));
        Assert.False(File.Exists(config + ".tmp"));
    }

    [Fact]
    public async Task InvalidNewCanvas_DoesNotCreateTemplateDirectory()
    {
        var canvas = SplitCanvas();
        canvas.Children.Add(new WMContainer { ID = "DUPLICATE" });
        canvas.Children.Add(new WMContainer { ID = "DUPLICATE" });
        var directory = Path.Combine(root, canvas.ID);

        await Assert.ThrowsAsync<MacTemplateValidationException>(() => new MacTemplateStore().SaveAsync(canvas, root));

        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public void Validator_ReportsCyclesInvalidTransformsAndResources()
    {
        var canvas = SplitCanvas();
        var rootContainer = new WMContainer { Path = "../outside.png" };
        rootContainer.Controls.Add(rootContainer);
        rootContainer.Transform = new WMTransform { ScaleX = double.NaN };
        canvas.Children.Add(rootContainer);

        var errors = MacTemplateValidator.Validate(canvas, Path.Combine(root, canvas.ID));

        Assert.Contains(errors, error => error.Field == "Hierarchy" && error.Severity == MacValidationSeverity.Error);
        Assert.Contains(errors, error => error.Field == "Transform.ScaleX" && error.Severity == MacValidationSeverity.Error);
        Assert.Contains(errors, error => error.Field == "Path" && error.Severity == MacValidationSeverity.Error);
    }

    [Fact]
    public void Validator_TreatsMissingLogoAsWarning()
    {
        var canvas = SplitCanvas();
        var container = new WMContainer();
        container.Controls.Add(new WMLogo { Path = "missing.png" });
        canvas.Children.Add(container);

        var errors = MacTemplateValidator.Validate(canvas, Path.Combine(root, canvas.ID));

        Assert.Contains(errors, error => error.ControlId != null && error.Severity == MacValidationSeverity.Warning);
        Assert.DoesNotContain(errors, error => error.Severity == MacValidationSeverity.Error);
    }

    [Fact]
    public void Validator_AcceptsOffsetBoundaries()
    {
        AssertTransformValid("Transform.OffsetXPercent", transform => transform.OffsetXPercent = -500);
        AssertTransformValid("Transform.OffsetXPercent", transform => transform.OffsetXPercent = 500);
        AssertTransformInvalid("Transform.OffsetXPercent", transform => transform.OffsetXPercent = -500.01);
        AssertTransformInvalid("Transform.OffsetXPercent", transform => transform.OffsetXPercent = 500.01);
        AssertTransformValid("Transform.OffsetYPercent", transform => transform.OffsetYPercent = -500);
        AssertTransformValid("Transform.OffsetYPercent", transform => transform.OffsetYPercent = 500);
        AssertTransformInvalid("Transform.OffsetYPercent", transform => transform.OffsetYPercent = -500.01);
        AssertTransformInvalid("Transform.OffsetYPercent", transform => transform.OffsetYPercent = 500.01);
    }

    [Fact]
    public void Validator_AcceptsScaleBoundaries()
    {
        AssertTransformValid("Transform.ScaleX", transform => transform.ScaleX = 0.05);
        AssertTransformValid("Transform.ScaleX", transform => transform.ScaleX = 20);
        AssertTransformInvalid("Transform.ScaleX", transform => transform.ScaleX = 0.049);
        AssertTransformInvalid("Transform.ScaleX", transform => transform.ScaleX = 20.01);
        AssertTransformValid("Transform.ScaleY", transform => transform.ScaleY = 0.05);
        AssertTransformValid("Transform.ScaleY", transform => transform.ScaleY = 20);
        AssertTransformInvalid("Transform.ScaleY", transform => transform.ScaleY = 0.049);
        AssertTransformInvalid("Transform.ScaleY", transform => transform.ScaleY = 20.01);
    }

    [Fact]
    public void Validator_AcceptsRotationLowerBoundaryAndRejectsExclusiveUpperBoundary()
    {
        AssertTransformValid("Transform.Rotation", transform => transform.Rotation = -180);
        AssertTransformValid("Transform.Rotation", transform => transform.Rotation = 179.999);
        AssertTransformInvalid("Transform.Rotation", transform => SetTransformField(transform, "rotation", 180));
    }

    [Fact]
    public void Validator_RejectsAllNonFiniteTransformValues()
    {
        foreach (var field in new[] { "offsetXPercent", "offsetYPercent", "scaleX", "scaleY", "rotation" })
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            AssertTransformInvalid($"Transform.{char.ToUpperInvariant(field[0])}{field[1..]}", transform => SetTransformField(transform, field, value));
    }

    [Fact]
    public async Task FailedReplacement_RemovesTemporaryFile()
    {
        var canvas = SplitCanvas();
        var directory = Path.Combine(root, canvas.ID);
        Directory.CreateDirectory(Path.Combine(directory, "config.json"));
        var temporary = Path.Combine(directory, "config.json.tmp");

        await Assert.ThrowsAnyAsync<IOException>(() => new MacTemplateStore().SaveAsync(canvas, root));

        Assert.False(File.Exists(temporary));
    }

    [Fact]
    public async Task UnsafeTemplateId_DoesNotWriteOutsideTemplatesRoot()
    {
        var canvas = SplitCanvas();
        canvas.ID = "../outside";

        await Assert.ThrowsAnyAsync<Exception>(() => new MacTemplateStore().SaveAsync(canvas, root));

        Assert.False(Directory.Exists(Path.Combine(root, "..", "outside")));
    }

    [Fact]
    public async Task AbsoluteDraftResources_AreCopiedOnlyWhenSavingAndSerializedRelatively()
    {
        var sourceDirectory = Path.Combine(root, "sources");
        Directory.CreateDirectory(sourceDirectory);
        var defaultImage = Path.Combine(sourceDirectory, "default-source.jpg");
        var containerImage = Path.Combine(sourceDirectory, "container.png");
        var logoImage = Path.Combine(sourceDirectory, "logo.png");
        await File.WriteAllBytesAsync(defaultImage, [1, 2, 3]);
        await File.WriteAllBytesAsync(containerImage, [4, 5, 6]);
        await File.WriteAllBytesAsync(logoImage, [7, 8, 9]);

        var canvas = SplitCanvas();
        canvas.Path = defaultImage;
        var container = new WMContainer { ID = "CONTAINER", Path = containerImage };
        container.Controls.Add(new WMLogo { ID = "LOGO", Path = logoImage });
        canvas.Children.Add(container);
        var templateDirectory = Path.Combine(root, canvas.ID);

        Assert.False(Directory.Exists(templateDirectory));
        await new MacTemplateStore().SaveAsync(canvas, root);

        Assert.True(File.Exists(Path.Combine(templateDirectory, "default.jpg")));
        var persisted = Global.ReadConfigFromPath(Path.Combine(templateDirectory, "config.json"));
        var persistedContainer = Assert.Single(persisted.Children);
        var persistedLogo = Assert.IsType<WMLogo>(Assert.Single(persistedContainer.Controls));
        Assert.False(Path.IsPathRooted(persistedContainer.Path));
        Assert.False(Path.IsPathRooted(persistedLogo.Path));
        Assert.True(File.Exists(Path.Combine(templateDirectory, persistedContainer.Path)));
        Assert.True(File.Exists(Path.Combine(templateDirectory, persistedLogo.Path)));
        Assert.DoesNotContain(sourceDirectory, await File.ReadAllTextAsync(Path.Combine(templateDirectory, "config.json")));
        Assert.Equal(defaultImage, canvas.Path);
        Assert.Equal(containerImage, container.Path);
        Assert.Equal(logoImage, Assert.IsType<WMLogo>(Assert.Single(container.Controls)).Path);
    }

    [Fact]
    public async Task FailedPublish_DoesNotCopyDraftResourcesIntoLiveTemplate()
    {
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.jpg");
        await File.WriteAllBytesAsync(source, [1, 2, 3]);
        var canvas = SplitCanvas();
        canvas.Path = source;
        var directory = Path.Combine(root, canvas.ID);
        Directory.CreateDirectory(Path.Combine(directory, "config.json"));

        await Assert.ThrowsAnyAsync<IOException>(() => new MacTemplateStore().SaveAsync(canvas, root));

        Assert.True(Directory.Exists(Path.Combine(directory, "config.json")));
        Assert.False(File.Exists(Path.Combine(directory, "default.jpg")));
    }

    [Fact]
    public async Task MissingAbsoluteDraftResource_DoesNotReplaceExistingConfig()
    {
        var canvas = SplitCanvas();
        canvas.Children.Add(new WMContainer { ID = "CONTAINER", Path = Path.Combine(root, "missing.png") });
        var directory = Path.Combine(root, canvas.ID);
        Directory.CreateDirectory(directory);
        var config = Path.Combine(directory, "config.json");
        await File.WriteAllTextAsync(config, "original");

        await Assert.ThrowsAsync<MacTemplateValidationException>(() => new MacTemplateStore().SaveAsync(canvas, root));

        Assert.Equal("original", await File.ReadAllTextAsync(config));
    }

    [Fact]
    public async Task EmptyCanvasPath_RemovesExistingDefaultImageOnlyOnSuccessfulSave()
    {
        var canvas = SplitCanvas();
        var directory = Path.Combine(root, canvas.ID);
        Directory.CreateDirectory(directory);
        var defaultImage = Path.Combine(directory, "default.jpg");
        await File.WriteAllBytesAsync(defaultImage, [1, 2, 3]);

        await new MacTemplateStore().SaveAsync(canvas, root);

        Assert.False(File.Exists(defaultImage));
    }

    [Fact]
    public async Task NormalCanvas_EmptyRuntimePathPreservesConventionBasedDefaultImage()
    {
        var canvas = SplitCanvas();
        canvas.CanvasType = CanvasType.Normal;
        var directory = Path.Combine(root, canvas.ID);
        Directory.CreateDirectory(directory);
        var defaultImage = Path.Combine(directory, "default.jpg");
        await File.WriteAllBytesAsync(defaultImage, [1, 2, 3]);

        await new MacTemplateStore().SaveAsync(canvas, root);

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(defaultImage));
    }

    private static WMCanvas SplitCanvas() => new()
    {
        Name = "test",
        CanvasType = CanvasType.Split,
        CustomWidth = 1000,
        CustomHeight = 800
    };

    private void AssertTransformValid(string field, Action<WMTransform> configure)
    {
        var errors = TransformErrors(configure);
        Assert.DoesNotContain(errors, error => error.Field == field);
    }

    private void AssertTransformInvalid(string field, Action<WMTransform> configure)
    {
        var errors = TransformErrors(configure);
        Assert.Contains(errors, error => error.Field == field && error.Severity == MacValidationSeverity.Error);
    }

    private IReadOnlyList<MacTemplateValidationError> TransformErrors(Action<WMTransform> configure)
    {
        var canvas = SplitCanvas();
        var container = new WMContainer { Transform = new WMTransform() };
        configure(container.Transform);
        canvas.Children.Add(container);
        return MacTemplateValidator.Validate(canvas, Path.Combine(root, canvas.ID));
    }

    private static void SetTransformField(WMTransform transform, string fieldName, double value) =>
        typeof(WMTransform).GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(transform, value);

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
