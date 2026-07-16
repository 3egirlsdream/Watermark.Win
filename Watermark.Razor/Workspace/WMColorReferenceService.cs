#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Stages a reference image once inside the active session and analyzes the
/// staged file. The resulting profile is self-contained in the color recipe.
/// </summary>
public sealed class WMColorReferenceService(
    IWMColorAnalysisService analysisService,
    IWMProcessingScheduler scheduler,
    IWMExecutionProfileProvider executionProfiles) : IWMColorReferenceService
{
    public async Task<WMColorReferenceImport> ImportAsync(
        string sessionId,
        IWMPhotoImportSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(source);
        if (!string.Equals(sessionId, Path.GetFileName(sessionId), StringComparison.Ordinal))
            throw new ArgumentException("会话 ID 无效。", nameof(sessionId));

        var sessionDirectory = Path.Combine(
            Global.AppPath.BasePath,
            "Cache",
            "editing-sessions",
            sessionId);
        if (!Directory.Exists(sessionDirectory))
            throw new DirectoryNotFoundException("编辑会话目录不存在。");

        var referenceDirectory = Path.Combine(sessionDirectory, "references");
        Directory.CreateDirectory(referenceDirectory);
        var extension = SafeImageExtension(source.DisplayName);
        var target = Path.Combine(referenceDirectory, $"{Guid.NewGuid():N}{extension}");
        var temporary = target + ".tmp";
        try
        {
            await using (var input = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             256 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (new FileInfo(temporary).Length == 0)
                throw new InvalidDataException("参考图片为空。");
            File.Move(temporary, target, true);

            var profile = await scheduler.RunAsync(
                _ => analysisService.Analyze(target, cancellationToken),
                executionProfiles.GetInteractiveProfile(),
                96L * 1024 * 1024,
                cancellationToken).ConfigureAwait(false);
            profile.SourceName = source.DisplayName;
            return new WMColorReferenceImport(source.DisplayName, target, profile);
        }
        catch
        {
            TryDelete(target);
            throw;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    private static string SafeImageExtension(string displayName)
    {
        var extension = Path.GetExtension(Path.GetFileName(displayName)).ToLowerInvariant();
        return extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".heic" or ".heif"
            ? extension
            : ".img";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
