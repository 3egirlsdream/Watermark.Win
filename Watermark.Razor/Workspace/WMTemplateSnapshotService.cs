#nullable enable

using System.Security.Cryptography;
using System.Text;
using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Produces a self-contained template snapshot for a workspace transaction.
/// Referenced template/logo images are copied into the session and paths in the
/// serialized canvas point at those immutable copies.
/// </summary>
public sealed class WMTemplateSnapshotService
{
    public async Task<string> CreateAsync(
        WMWorkspaceSession session,
        string templateId,
        WMCanvas source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateId);
        ArgumentNullException.ThrowIfNull(source);

        var canvas = Global.ReadConfig(Global.CanvasSerialize(source));
        var references = Global.EnumerateControls(canvas)
            .Select(control => control switch
            {
                WMLogo logo when !string.IsNullOrWhiteSpace(logo.Path) =>
                    new ResourceReference(logo.Path, value => logo.Path = value, true),
                WMContainer container when !string.IsNullOrWhiteSpace(container.Path) =>
                    new ResourceReference(container.Path, value => container.Path = value, false),
                _ => null
            })
            .Where(item => item is not null)
            .Cast<ResourceReference>()
            .ToArray();
        if (references.Length == 0) return Global.CanvasSerialize(canvas);

        var sessionDirectory = ResolveSessionDirectory(session);
        var snapshotDirectory = Path.Combine(
            sessionDirectory, "snapshots", "templates", Guid.NewGuid().ToString("N"));
        var resourceDirectory = Path.Combine(snapshotDirectory, "resources");
        Directory.CreateDirectory(resourceDirectory);
        var templateDirectory = Path.Combine(Global.AppPath.TemplatesFolder, templateId);
        var copied = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (var reference in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sourcePath = ResolveResourcePath(
                    reference.Path,
                    templateDirectory,
                    reference.AllowLogoLibrary ? Global.AppPath.LogoesFolder : null);
                if (sourcePath is null)
                    throw new FileNotFoundException($"模板资源不存在：{reference.Path}", reference.Path);

                if (!copied.TryGetValue(sourcePath, out var immutablePath))
                {
                    immutablePath = await CopyResourceAsync(
                        sourcePath, resourceDirectory, cancellationToken).ConfigureAwait(false);
                    copied[sourcePath] = immutablePath;
                }
                reference.Rewrite(immutablePath);
            }

            var json = Global.CanvasSerialize(canvas);
            var configPath = Path.Combine(snapshotDirectory, "config.json");
            await WriteAtomicAsync(configPath, Encoding.UTF8.GetBytes(json), cancellationToken)
                .ConfigureAwait(false);
            return json;
        }
        catch
        {
            TryDeleteDirectory(snapshotDirectory);
            throw;
        }
    }

    private static string ResolveSessionDirectory(WMWorkspaceSession session)
    {
        var artifact = session.Media.FirstOrDefault()?.Artifact
                       ?? throw new InvalidOperationException("没有可用于建立模板快照的素材。");
        var artifactPath = artifact.PreviewPath ?? artifact.FilePath;
        var artifactDirectory = Directory.GetParent(artifactPath)
                                ?? throw new InvalidOperationException("素材路径没有有效目录。");
        return artifactDirectory.Parent?.FullName ?? artifactDirectory.FullName;
    }

    private static string? ResolveResourcePath(
        string reference,
        string templateDirectory,
        string? fallbackDirectory)
    {
        var candidates = new List<string>();
        if (Path.IsPathFullyQualified(reference)) candidates.Add(reference);
        else
        {
            candidates.Add(Path.Combine(templateDirectory, reference));
            if (!string.IsNullOrWhiteSpace(fallbackDirectory))
                candidates.Add(Path.Combine(fallbackDirectory, reference));
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath)) return fullPath;
            }
            catch (Exception) when (candidate.Length > 0)
            {
                // Invalid legacy paths are reported as a missing resource below.
            }
        }
        return null;
    }

    private static async Task<string> CopyResourceAsync(
        string sourcePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(sourcePath);
        if (extension.Length is 0 or > 16) extension = ".bin";
        var nameHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath)))[..24];
        var destination = Path.Combine(destinationDirectory, $"{nameHash}{extension.ToLowerInvariant()}");
        if (File.Exists(destination) && new FileInfo(destination).Length > 0) return destination;

        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var input = new FileStream(
                             sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, destination, true);
            return destination;
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private static async Task WriteAtomicAsync(
        string path,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var output = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await output.WriteAsync(content, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private sealed record ResourceReference(
        string Path,
        Action<string> Rewrite,
        bool AllowLogoLibrary);
}
