#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

/// <summary>
/// Coordinates one cancelable asset replacement. All previews stay inside one
/// editor transaction and confirmation produces one semantic history item.
/// </summary>
public sealed class WMPosterAssetSelectionSession
{
    private readonly WMTemplateEditorState editor;
    private bool completed;

    private WMPosterAssetSelectionSession(
        WMTemplateEditorState editor,
        string targetControlId)
    {
        this.editor = editor;
        TargetControlId = targetControlId;
        editor.BeginTransaction(
            "替换图片素材",
            WMTemplateChangeKind.Resource | WMTemplateChangeKind.Layout | WMTemplateChangeKind.Paint,
            [targetControlId]);
    }

    public string TargetControlId { get; }
    public WMPosterAssetReference? SelectedAsset { get; private set; }
    public WMPosterAssetFit Fit
    {
        get
        {
            var target = ResolveTarget();
            return WMPosterAssetSlots.Find(editor.Draft, target)?.Fit
                ?? WMPosterAssetFit.Cover;
        }
    }
    public WMCropSettings CropSettings
    {
        get
        {
            var asset = SelectedAsset;
            if (asset is null || asset.PixelWidth <= 0 || asset.PixelHeight <= 0)
                return WMCropSettings.Identity;
            var target = ResolveTarget();
            return WMPosterAssetSlots.Find(editor.Draft, target)?
                .GetCropSettings(asset.PixelWidth, asset.PixelHeight)
                ?? WMCropSettings.Identity;
        }
    }
    public bool CanConfirm => !completed && SelectedAsset is not null;

    public static WMPosterAssetSelectionSession Begin(
        WMTemplateEditorState editor,
        IWMControl target)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(target);
        if (!WMPosterAssetSlots.Supports(target))
            throw new ArgumentException("当前图层不支持替换图片素材。", nameof(target));
        if (target.IsLocked)
            throw new InvalidOperationException("图层已锁定，解锁后再替换素材。");
        if (editor.IsTransactionActive)
            throw new InvalidOperationException("请先完成当前编辑。");
        return new WMPosterAssetSelectionSession(editor, target.ID);
    }

    public void Preview(WMPosterAssetReference asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        EnsureActive();
        if (!asset.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前素材不是可用图片。");
        var target = ResolveTarget();
        editor.Mutate(
            "预览替换素材",
            () =>
            {
                switch (target)
                {
                    case WMLogo logo:
                        logo.Path = asset.ResourcePath;
                        logo.Enabled = true;
                        // An explicit user replacement must win over the
                        // metadata-driven camera brand fallback.
                        logo.AutoSetLogo = false;
                        break;
                    case WMContainer container:
                        container.Path = asset.ResourcePath;
                        container.ContainerProperties.Show = true;
                        break;
                }

                var slot = WMPosterAssetSlots.Ensure(editor.Draft, target);
                slot.DefaultAssetId = asset.Id;
                if (asset.PixelWidth > 0 && asset.PixelHeight > 0)
                {
                    var migrated = slot.GetCropSettings(asset.PixelWidth, asset.PixelHeight);
                    slot.SetCropSettings(migrated, asset.PixelWidth, asset.PixelHeight);
                }
            },
            WMTemplateChangeKind.Resource | WMTemplateChangeKind.Layout | WMTemplateChangeKind.Paint,
            [TargetControlId]);
        SelectedAsset = asset;
    }

    public void PreviewFit(WMPosterAssetFit fit)
    {
        EnsureActive();
        if (!Enum.IsDefined(fit))
            throw new ArgumentOutOfRangeException(nameof(fit));
        var target = ResolveTarget();
        editor.Mutate(
            "调整素材适配",
            () => WMPosterAssetSlots.Ensure(editor.Draft, target).Fit = fit,
            WMTemplateChangeKind.Layout | WMTemplateChangeKind.Paint,
            [TargetControlId]);
    }

    public void PreviewCrop(WMCropSettings settings)
    {
        EnsureActive();
        var asset = SelectedAsset
            ?? throw new InvalidOperationException("请先选择要裁切的图片素材。");
        if (asset.PixelWidth <= 0 || asset.PixelHeight <= 0)
            throw new InvalidOperationException("无法读取图片尺寸，暂时不能裁切该素材。");
        var normalized = WMCropPlanner.Normalize(
            settings,
            asset.PixelWidth,
            asset.PixelHeight);
        var target = ResolveTarget();
        editor.Mutate(
            "调整素材裁剪",
            () => WMPosterAssetSlots
                .Ensure(editor.Draft, target)
                .SetCropSettings(normalized, asset.PixelWidth, asset.PixelHeight),
            WMTemplateChangeKind.Layout | WMTemplateChangeKind.Paint,
            [TargetControlId]);
    }

    public bool Confirm()
    {
        EnsureActive();
        if (SelectedAsset is null) return false;
        completed = true;
        return editor.CommitTransaction();
    }

    public void Cancel()
    {
        if (completed) return;
        completed = true;
        editor.CancelTransaction();
    }

    private IWMControl ResolveTarget() =>
        WMControlTree.Find(editor.Draft, TargetControlId)
        ?? throw new InvalidOperationException("要替换素材的图层已不存在。");

    private void EnsureActive()
    {
        if (completed)
            throw new InvalidOperationException("素材替换会话已经结束。");
    }
}
