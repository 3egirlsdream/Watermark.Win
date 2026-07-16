#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed class WMTemplateStore
{
    private const string DefaultImageFileName = "default.jpg";
    private const string ResourceDirectoryName = "resources";

    public Task SaveAsync(WMCanvas canvas)
        => SaveAsync(canvas, Global.AppPath.TemplatesFolder);

    public async Task SaveAsync(WMCanvas canvas, string templatesRoot)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(templatesRoot);
        EnsureSafeTemplateId(canvas.ID);

        var root = Path.GetFullPath(templatesRoot);
        var directory = Path.Combine(root, canvas.ID);
        var errors = WMTemplateValidator.Validate(canvas, directory);
        var blocking = errors.Where(error => error.Severity == WMValidationSeverity.Error).ToList();
        if (blocking.Count > 0)
            throw new WMTemplateValidationException(blocking);

        var persistedCanvas = CloneForPersistence(canvas);
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, $".{canvas.ID}.{Guid.NewGuid():N}.tmp");
        var backup = Path.Combine(root, $".{canvas.ID}.{Guid.NewGuid():N}.backup");
        try
        {
            Directory.CreateDirectory(staging);
            if (Directory.Exists(directory))
                CopyDirectory(directory, staging);

            var pendingCopies = PrepareResources(persistedCanvas, directory, staging);
            foreach (var copy in pendingCopies)
            {
                var destinationDirectory = Path.GetDirectoryName(copy.Destination)!;
                Directory.CreateDirectory(destinationDirectory);
                File.Copy(copy.Source, copy.Destination, true);
            }

            var target = Path.Combine(staging, "config.json");
            var temporary = target + ".tmp";
            await File.WriteAllTextAsync(temporary, Global.CanvasSerialize(persistedCanvas));
            try
            {
                File.Move(temporary, target, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }

            // Normal templates load their default image by convention from
            // <template>/default.jpg. Canvas.Path is runtime-only, so an empty
            // value after loading does not mean the user asked us to delete it.
            if (persistedCanvas.CanvasType != Watermark.Shared.Enums.CanvasType.Normal
                && string.IsNullOrWhiteSpace(persistedCanvas.Path))
            {
                var defaultImage = Path.Combine(staging, DefaultImageFileName);
                if (File.Exists(defaultImage)) File.Delete(defaultImage);
            }

            if (Directory.Exists(directory)) Directory.Move(directory, backup);
            try
            {
                Directory.Move(staging, directory);
            }
            catch
            {
                if (Directory.Exists(backup) && !Directory.Exists(directory))
                    Directory.Move(backup, directory);
                throw;
            }

            TryDeleteDirectory(backup);
        }
        finally
        {
            TryDeleteDirectory(staging);
            if (Directory.Exists(backup) && !Directory.Exists(directory))
                Directory.Move(backup, directory);
        }
    }

    private static WMCanvas CloneForPersistence(WMCanvas canvas)
    {
        var clone = Global.ReadConfig(Global.CanvasSerialize(canvas));
        clone.Path = canvas.Path;
        clone.Exif = canvas.Exif.ToDictionary(
            pair => pair.Key,
            pair => new Dictionary<string, string>(pair.Value));
        return clone;
    }

    private static List<ResourceCopy> PrepareResources(WMCanvas canvas, string templateDirectory, string stagingDirectory)
    {
        var copies = new List<ResourceCopy>();
        canvas.Path = NormalizeResource(
            canvas.Path,
            templateDirectory,
            stagingDirectory,
            DefaultImageFileName,
            required: true,
            copies);

        var resourceIndex = 0;
        foreach (var control in Global.EnumerateControls(canvas))
        {
            switch (control)
            {
                case WMContainer container:
                    container.Path = NormalizeResource(
                        container.Path,
                        templateDirectory,
                        stagingDirectory,
                        ResourceName(++resourceIndex, container.Path),
                        required: true,
                        copies);
                    break;
                case WMLogo logo:
                    logo.Path = NormalizeResource(
                        logo.Path,
                        templateDirectory,
                        stagingDirectory,
                        ResourceName(++resourceIndex, logo.Path),
                        required: false,
                        copies);
                    break;
                case WMText text when IsFileResource(text.FontFamily):
                    text.FontFamily = NormalizeResource(
                        text.FontFamily,
                        templateDirectory,
                        stagingDirectory,
                        ResourceName(++resourceIndex, text.FontFamily),
                        required: false,
                        copies);
                    break;
            }
        }

        return copies;
    }

    private static string NormalizeResource(
        string? resourcePath,
        string templateDirectory,
        string stagingDirectory,
        string destinationName,
        bool required,
        List<ResourceCopy> copies)
    {
        if (string.IsNullOrWhiteSpace(resourcePath)) return string.Empty;
        if (!Path.IsPathRooted(resourcePath)) return resourcePath;

        var source = Path.GetFullPath(resourcePath);
        if (!File.Exists(source)) return required ? resourcePath : string.Empty;

        var root = Path.GetFullPath(templateDirectory);
        var relativeSource = Path.GetRelativePath(root, source);
        if (IsSafeRelativePath(relativeSource)) return relativeSource;

        var relativeDestination = destinationName == DefaultImageFileName
            ? DefaultImageFileName
            : Path.Combine(ResourceDirectoryName, destinationName);
        copies.Add(new ResourceCopy(source, Path.Combine(stagingDirectory, relativeDestination)));
        return relativeDestination;
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            var info = new DirectoryInfo(directory);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
            var childDestination = Path.Combine(destination, info.Name);
            Directory.CreateDirectory(childDestination);
            CopyDirectory(directory, childDestination);
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try
        {
            Directory.Delete(path, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsSafeRelativePath(string path) =>
        path != ".."
        && !path.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        && !Path.IsPathRooted(path);

    private static bool IsFileResource(string fontFamily) =>
        !string.IsNullOrWhiteSpace(fontFamily)
        && (Path.IsPathRooted(fontFamily)
            || Path.GetExtension(fontFamily).Length > 0
            && (fontFamily.Contains(Path.DirectorySeparatorChar)
                || fontFamily.Contains(Path.AltDirectorySeparatorChar)));

    private static string ResourceName(int index, string? sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        return $"asset-{index:000}{extension}";
    }

    private sealed record ResourceCopy(string Source, string Destination);

    private static void EnsureSafeTemplateId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id is "." or ".." || Path.IsPathRooted(id)
            || id.Contains('/') || id.Contains('\\') || Path.GetFileName(id) != id)
            throw new ArgumentException("模板 ID 无效。", nameof(id));
    }
}

