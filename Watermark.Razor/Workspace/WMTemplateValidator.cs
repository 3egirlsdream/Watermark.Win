#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public enum WMValidationSeverity
{
    Warning,
    Error
}

public sealed record WMTemplateValidationError(
    string? ControlId,
    string Field,
    string Message,
    WMValidationSeverity Severity = WMValidationSeverity.Error);

public sealed class WMTemplateValidationException : Exception
{
    public WMTemplateValidationException(IReadOnlyList<WMTemplateValidationError> errors)
        : base(errors.FirstOrDefault()?.Message ?? "模板配置无效")
    {
        Errors = errors;
    }

    public IReadOnlyList<WMTemplateValidationError> Errors { get; }
}

public static class WMTemplateValidator
{
    private const double MinimumOffsetPercent = -500;
    private const double MaximumOffsetPercent = 500;
    private const double MinimumScale = 0.05;
    private const double MaximumScale = 20;
    private static readonly string[] PhotoMetadataNameHints =
    [
        "机型", "镜头", "相机", "曝光", "拍摄", "时间", "日期", "坐标", "位置", "地点",
        "编号", "期号", "画幅", "胶卷", "参数", "光圈", "快门", "焦距", "感光", "ISO"
    ];

    public static IReadOnlyList<WMTemplateValidationError> Validate(WMCanvas canvas, string templateDirectory)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateDirectory);
        WMPosterTemplateMigration.UpgradeCodeConstructedLegacy(
            canvas,
            File.Exists(Path.Combine(templateDirectory, "default.jpg")));

        var errors = new List<WMTemplateValidationError>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        var active = new HashSet<IWMControl>(ReferenceEqualityComparer.Instance);
        var v2 = canvas.LayoutSchemaVersion >= WMLayoutMigration.CurrentSchemaVersion;
        if (v2)
            ValidateSiblingMetadata(canvas.Children ?? [], "0", errors);
        foreach (var root in canvas.Children ?? [])
            ValidateControl(root, root is WMContainer ? 1 : 0, v2, true, templateDirectory, ids, visited, active, errors);
        ValidateCanvasSizing(canvas, templateDirectory, errors);
        ValidatePosterAssetManifest(canvas, templateDirectory, errors);

        if (!string.IsNullOrWhiteSpace(canvas.Path))
            ValidateImagePath(canvas.ID, "Path", canvas.Path, templateDirectory, false, errors);

        return errors;
    }

    private static void ValidateCanvasSizing(
        WMCanvas canvas,
        string templateDirectory,
        List<WMTemplateValidationError> errors)
    {
        var sizing = canvas.CanvasSizing;
        if (sizing is null || !Enum.IsDefined(sizing.Mode))
        {
            errors.Add(new(canvas.ID, "CanvasSizing.Mode", "画布尺寸策略无效。"));
            return;
        }

        if (sizing.Mode == WMCanvasSizingMode.Fixed
            && (sizing.ReferenceWidth is < 1 or > 32768
                || sizing.ReferenceHeight is < 1 or > 32768))
        {
            errors.Add(new(
                canvas.ID,
                "CanvasSizing",
                "固定画布参考宽高必须介于 1 和 32768 像素之间。"));
        }

        if (sizing.Mode != WMCanvasSizingMode.FollowPrimary) return;
        if (WMPosterAssetSlots.FindPrimary(canvas) is null)
        {
            errors.Add(new(canvas.ID, "PosterManifest.PrimaryAssetSlotId", "跟随主图画布必须声明主图槽。"));
            return;
        }

        var hasRuntimeDefault = !string.IsNullOrWhiteSpace(canvas.Path)
                                && File.Exists(canvas.Path);
        var hasStoredDefault = File.Exists(Path.Combine(templateDirectory, "default.jpg"));
        if (!hasRuntimeDefault && !hasStoredDefault)
        {
            errors.Add(new(
                canvas.ID,
                "Path",
                "跟随主图画布必须提供默认主图，才能在未选择照片时确定画布比例。"));
        }
    }

    private static void ValidatePosterAssetManifest(
        WMCanvas canvas,
        string templateDirectory,
        List<WMTemplateValidationError> errors)
    {
        var slots = canvas.PosterManifest?.AssetSlots ?? [];
        var slotsById = new Dictionary<string, WMPosterAssetSlot>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            if (string.IsNullOrWhiteSpace(slot.Id))
            {
                errors.Add(new(canvas.ID, "PosterManifest.AssetSlots", "可替换图片槽标识不能为空。"));
                continue;
            }
            if (!slotsById.TryAdd(slot.Id, slot))
                errors.Add(new(canvas.ID, "PosterManifest.AssetSlots", "可替换图片槽标识必须唯一。"));
            if (slot.AcceptedMediaTypes.Count == 0
                || slot.AcceptedMediaTypes.Any(type =>
                    string.IsNullOrWhiteSpace(type)
                    || !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
            {
                errors.Add(new(
                    canvas.ID,
                    "PosterManifest.AssetSlots",
                    $"图片槽“{slot.Name}”必须声明至少一种 image/* 媒体类型。"));
            }
            if (!Enum.IsDefined(slot.Fit))
                errors.Add(new(canvas.ID, "PosterManifest.AssetSlots", $"图片槽“{slot.Name}”的裁切策略无效。"));
            if (!Enum.IsDefined(slot.Purpose))
                errors.Add(new(canvas.ID, "PosterManifest.AssetSlots", $"图片槽“{slot.Name}”的用途无效。"));
            if (slot.Crop?.Settings is null
                && slot.Crop is not null
                && (!double.IsFinite(slot.Crop.AspectRatio)
                    || slot.Crop.AspectRatio < 0
                    || slot.Crop.AspectRatio > 10))
            {
                errors.Add(new(
                    canvas.ID,
                    "PosterManifest.AssetSlots",
                    $"图片槽“{slot.Name}”的裁剪比例无效。"));
            }
            if (slot.Crop?.Settings is null
                && slot.Crop is not null
                && (!double.IsFinite(slot.Crop.RotationDegrees)
                    || slot.Crop.RotationDegrees is < -30 or > 30))
            {
                errors.Add(new(
                    canvas.ID,
                    "PosterManifest.AssetSlots",
                    $"图片槽“{slot.Name}”的素材旋转角度必须在 -30° 到 30° 之间。"));
            }
            if (slot.Crop?.Settings is { } cropSettings
                && (!double.IsFinite(cropSettings.CenterX)
                    || cropSettings.CenterX is < 0 or > 1
                    || !double.IsFinite(cropSettings.CenterY)
                    || cropSettings.CenterY is < 0 or > 1
                    || !double.IsFinite(cropSettings.VisibleWidth)
                    || cropSettings.VisibleWidth is <= 0 or > 1
                    || !double.IsFinite(cropSettings.VisibleHeight)
                    || cropSettings.VisibleHeight is <= 0 or > 1
                    || !double.IsFinite(cropSettings.StraightenDegrees)
                    || cropSettings.StraightenDegrees is < -45 or > 45
                    || !Enum.IsDefined(cropSettings.AspectPreset)))
            {
                errors.Add(new(
                    canvas.ID,
                    "PosterManifest.AssetSlots",
                    $"图片槽“{slot.Name}”的裁切参数无效。"));
            }
        }

        var slotOwners = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(canvas.PosterManifest?.PrimaryAssetSlotId))
        {
            var primaryId = canvas.PosterManifest.PrimaryAssetSlotId;
            if (!slotsById.TryGetValue(primaryId, out var primary))
            {
                errors.Add(new(
                    canvas.ID,
                    "PosterManifest.PrimaryAssetSlotId",
                    "主图槽引用不存在。"));
            }
            else
            {
                slotOwners[primaryId] = canvas.ID;
                if (primary.Purpose != WMPosterAssetPurpose.Photo)
                {
                    errors.Add(new(
                        canvas.ID,
                        "PosterManifest.PrimaryAssetSlotId",
                        "主图槽必须使用照片用途。"));
                }
                if (!ReferenceEquals(slots.FirstOrDefault(), primary))
                {
                    errors.Add(new(
                        canvas.ID,
                        "PosterManifest.AssetSlots",
                        "主图槽必须排在素材槽列表首位。"));
                }
            }
        }
        foreach (var control in Global.EnumerateControls(canvas))
        {
            var slotId = control.PosterMetadata?.AssetSlotId;
            if (string.IsNullOrWhiteSpace(slotId)) continue;
            if (!WMPosterAssetSlots.Supports(control))
            {
                errors.Add(new(control.ID, "PosterMetadata.AssetSlotId", "只有图片和容器背景可以引用可替换素材槽。"));
                continue;
            }
            if (!slotsById.ContainsKey(slotId))
            {
                errors.Add(new(control.ID, "PosterMetadata.AssetSlotId", "图层引用的可替换图片槽不存在。"));
                continue;
            }
            if (!slotOwners.TryAdd(slotId, control.ID))
            {
                errors.Add(new(
                    control.ID,
                    "PosterMetadata.AssetSlotId",
                    "一个可替换图片槽不能同时绑定多个图层。"));
            }
        }

        foreach (var slot in slots.Where(slot => !string.IsNullOrWhiteSpace(slot.Id)
                                                && !slotOwners.ContainsKey(slot.Id)))
        {
            errors.Add(new(
                canvas.ID,
                "PosterManifest.AssetSlots",
                $"图片槽“{slot.Name}”没有绑定图层。",
                WMValidationSeverity.Warning));
        }

        foreach (var slot in slots.Where(slot => !string.IsNullOrWhiteSpace(slot.Id)
                                                && slotOwners.TryGetValue(slot.Id, out _)))
        {
            var ownerId = slotOwners[slot.Id];
            var isPrimaryOwner = string.Equals(ownerId, canvas.ID, StringComparison.Ordinal);
            var resourcePath = isPrimaryOwner
                ? canvas.Path
                : Global.EnumerateControls(canvas)
                    .FirstOrDefault(control => string.Equals(control.ID, ownerId, StringComparison.Ordinal))
                    switch
                    {
                        WMContainer container => container.Path,
                        WMLogo logo => logo.Path,
                        _ => string.Empty
                    };
            var hasDefaultResource = !string.IsNullOrWhiteSpace(resourcePath)
                                     || (isPrimaryOwner
                                         && File.Exists(Path.Combine(templateDirectory, "default.jpg")));
            if (!string.IsNullOrWhiteSpace(slot.DefaultAssetId)
                && !hasDefaultResource)
            {
                errors.Add(new(
                    ownerId,
                    "PosterManifest.AssetSlots.DefaultAssetId",
                    $"图片槽“{slot.Name}”声明了默认素材，但绑定图层没有默认图片资源。"));
            }
            else if (string.IsNullOrWhiteSpace(slot.DefaultAssetId)
                     && hasDefaultResource)
            {
                errors.Add(new(
                    ownerId,
                    "PosterManifest.AssetSlots.DefaultAssetId",
                    $"图片槽“{slot.Name}”已有默认图片，但没有记录默认素材标识。",
                    WMValidationSeverity.Warning));
            }
        }
    }

    private static void ValidateControl(
        IWMControl control,
        int depth,
        bool v2,
        bool isRoot,
        string templateDirectory,
        HashSet<string> ids,
        HashSet<IWMControl> visited,
        HashSet<IWMControl> active,
        List<WMTemplateValidationError> errors)
    {
        if (!active.Add(control))
        {
            errors.Add(new(control.ID, "Hierarchy", "控件层级存在循环。"));
            return;
        }

        if (!visited.Add(control))
        {
            errors.Add(new(control.ID, "Hierarchy", "同一控件不能出现在多个位置。"));
            active.Remove(control);
            return;
        }

        if (string.IsNullOrWhiteSpace(control.ID) || !ids.Add(control.ID))
            errors.Add(new(control.ID, "ID", "控件标识必须唯一。"));

        ValidateTransform(control, errors);
        if (v2) ValidateStyle(control, isRoot, errors);
        switch (control)
        {
            case WMContainer container:
                if (depth > WMControlTree.MaxContainerDepth)
                    errors.Add(new(control.ID, "Hierarchy", $"容器层级不能超过 {WMControlTree.MaxContainerDepth} 层。"));
                ValidateContainerEffects(container, v2, errors);
                if (!string.IsNullOrWhiteSpace(container.Path))
                    ValidateImagePath(control.ID, "Path", container.Path, templateDirectory, false, errors);
                if (v2)
                    ValidateSiblingMetadata(container.Controls ?? [], container.ID, errors);
                foreach (var child in container.Controls ?? [])
                    ValidateControl(child, depth + (child is WMContainer ? 1 : 0), v2, false, templateDirectory, ids, visited, active, errors);
                break;
            case WMLogo logo when !string.IsNullOrWhiteSpace(logo.Path):
                ValidateImagePath(control.ID, "Path", logo.Path, templateDirectory, true, errors);
                break;
            case WMText text:
                ValidateFontPath(text, templateDirectory, errors);
                if (v2)
                {
                    ValidateFiniteRange(text.ID, "LetterSpacing", text.LetterSpacing, -1, 3, errors);
                    ValidatePhotoMetadataBinding(text, errors);
                }
                break;
        }

        active.Remove(control);
    }

    private static void ValidateSiblingMetadata(
        IReadOnlyList<IWMControl> siblings,
        string expectedParentId,
        List<WMTemplateValidationError> errors)
    {
        var sequences = new HashSet<int>();
        for (var index = 0; index < siblings.Count; index++)
        {
            var node = siblings[index];
            if (node.PNode is null)
            {
                errors.Add(new(node.ID, "Hierarchy", "V2 节点缺少父级和排序元数据。"));
                continue;
            }

            if (!string.Equals(node.PNode.PID, expectedParentId, StringComparison.Ordinal))
                errors.Add(new(node.ID, "Hierarchy", "节点父级元数据与实际控件树不一致。"));
            if (!sequences.Add(node.PNode.SEQ))
                errors.Add(new(node.ID, "Hierarchy", "同一父级内的节点排序序号不能重复。"));
            if (node.PNode.SEQ != index)
                errors.Add(new(node.ID, "Hierarchy", "同一父级内的节点排序序号必须从 0 连续排列。"));
        }
    }

    private static void ValidatePhotoMetadataBinding(WMText text, List<WMTemplateValidationError> errors)
    {
        if (!PhotoMetadataNameHints.Any(hint => (text.Name ?? string.Empty).Contains(hint, StringComparison.Ordinal)))
            return;

        var entries = text.Exifs ?? [];
        if (entries.Count == 0 || entries.All(entry => string.IsNullOrWhiteSpace(entry.Key)))
        {
            errors.Add(new(
                text.ID,
                "Exifs",
                "照片信息文字必须绑定相机元数据字段，示例值不能写成固定文字。",
                WMValidationSeverity.Warning));
        }
    }

    private static void ValidateTransform(IWMControl control, List<WMTemplateValidationError> errors)
    {
        var transform = control.Transform;
        if (transform == null) return;

        ValidateFiniteRange(control.ID, "Transform.OffsetXPercent", transform.OffsetXPercent, MinimumOffsetPercent, MaximumOffsetPercent, errors);
        ValidateFiniteRange(control.ID, "Transform.OffsetYPercent", transform.OffsetYPercent, MinimumOffsetPercent, MaximumOffsetPercent, errors);
        ValidateFiniteRange(control.ID, "Transform.ScaleX", transform.ScaleX, MinimumScale, MaximumScale, errors);
        ValidateFiniteRange(control.ID, "Transform.ScaleY", transform.ScaleY, MinimumScale, MaximumScale, errors);
        ValidateFiniteRange(control.ID, "Transform.Rotation", transform.Rotation, -180, 180, errors, maximumInclusive: false);
    }

    private static void ValidateContainerEffects(WMContainer container, bool v2, List<WMTemplateValidationError> errors)
    {
        if (!v2 || !container.ContainerProperties.EnableGaussianBlur) return;

        ValidateFiniteRange(container.ID, "ContainerProperties.GaussianDeep", container.ContainerProperties.GaussianDeep, 1, 60, errors);
        var background = container.BackgroundColor;
        var opaque = !string.IsNullOrWhiteSpace(background)
            && (background.Length == 7 || background.EndsWith("FF", StringComparison.OrdinalIgnoreCase));
        if (opaque)
        {
            errors.Add(new(
                container.ID,
                "BackgroundColor",
                "背景模糊容器使用了不透明背景色，模糊结果会被填充遮住。",
                WMValidationSeverity.Warning));
        }
    }

    private static void ValidateStyle(IWMControl control, bool isRoot, List<WMTemplateValidationError> errors)
    {
        var style = control.Style;
        if (isRoot && style.Position != WMPosition.Absolute)
            errors.Add(new(control.ID, "position", "根节点必须使用自由定位。"));
        ValidateLength(control.ID, "width", style.Width, 0, 100, errors);
        ValidateLength(control.ID, "height", style.Height, 0, 100, errors);
        ValidateInsets(control.ID, style, errors);
        ValidateThickness(control.ID, "margin", style.Margin, -25, 25, errors);
        ValidateThickness(control.ID, "padding", style.Padding, 0, 25, errors);
        ValidateFiniteRange(control.ID, "gap", style.Gap, 0, 25, errors);
        if (style.Position == WMPosition.Absolute)
        {
            ValidateFiniteRange(control.ID, "transform.scaleX", style.Transform.ScaleX, 0.1, 4, errors);
            ValidateFiniteRange(control.ID, "transform.scaleY", style.Transform.ScaleY, 0.1, 4, errors);
            ValidateFiniteRange(control.ID, "transform.rotation", style.Transform.Rotation, -180, 180, errors, maximumInclusive: false);
        }
        else if (style.Transform.OffsetXPercent != 0 || style.Transform.OffsetYPercent != 0
            || style.Transform.ScaleX != 1 || style.Transform.ScaleY != 1 || style.Transform.Rotation != 0)
        {
            errors.Add(new(control.ID, "transform", "流式布局节点不能使用变换；请改用外边距或自由定位边距。"));
        }
    }

    private static void ValidateInsets(string controlId, WMStyle style, List<WMTemplateValidationError> errors)
    {
        ValidateOptionalLength(controlId, "top", style.Top, -25, 125, errors);
        ValidateOptionalLength(controlId, "right", style.Right, -25, 125, errors);
        ValidateOptionalLength(controlId, "bottom", style.Bottom, -25, 125, errors);
        ValidateOptionalLength(controlId, "left", style.Left, -25, 125, errors);
    }

    private static void ValidateThickness(string controlId, string field, WMThickness thickness, double minimum, double maximum, List<WMTemplateValidationError> errors)
    {
        ValidateFiniteRange(controlId, $"{field}.top", thickness.Top, minimum, maximum, errors);
        ValidateFiniteRange(controlId, $"{field}.right", thickness.Right, minimum, maximum, errors);
        ValidateFiniteRange(controlId, $"{field}.bottom", thickness.Bottom, minimum, maximum, errors);
        ValidateFiniteRange(controlId, $"{field}.left", thickness.Left, minimum, maximum, errors);
    }

    private static void ValidateOptionalLength(string controlId, string field, WMStyleLength? value, double minimum, double maximum, List<WMTemplateValidationError> errors)
    {
        if (value is not null) ValidateLength(controlId, field, value, minimum, maximum, errors);
    }

    private static void ValidateLength(string controlId, string field, WMStyleLength value, double minimum, double maximum, List<WMTemplateValidationError> errors)
    {
        if (!value.IsAuto) ValidateFiniteRange(controlId, field, value.Value, minimum, maximum, errors);
    }

    private static void ValidateFiniteRange(string controlId, string field, double value, double minimum, double maximum, List<WMTemplateValidationError> errors, bool maximumInclusive = true)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum || (!maximumInclusive && value == maximum))
            errors.Add(new(controlId, field, $"{DisplayField(field)}必须介于 {minimum} 和 {maximum} 之间。"));
    }

    private static string DisplayField(string field) => field switch
    {
        "position" => "定位方式",
        "width" => "宽度",
        "height" => "高度",
        "top" => "上定位边距",
        "right" => "右定位边距",
        "bottom" => "下定位边距",
        "left" => "左定位边距",
        "margin.top" => "上外边距",
        "margin.right" => "右外边距",
        "margin.bottom" => "下外边距",
        "margin.left" => "左外边距",
        "padding.top" => "上内边距",
        "padding.right" => "右内边距",
        "padding.bottom" => "下内边距",
        "padding.left" => "左内边距",
        "gap" => "统一间距",
        "Exifs" => "拍摄信息配置",
        "LetterSpacing" => "字距",
        "ContainerProperties.GaussianDeep" => "背景模糊强度",
        "transform.scaleX" or "Transform.ScaleX" => "水平缩放",
        "transform.scaleY" or "Transform.ScaleY" => "垂直缩放",
        "transform.rotation" or "Transform.Rotation" => "旋转角度",
        "Transform.OffsetXPercent" => "水平坐标",
        "Transform.OffsetYPercent" => "垂直坐标",
        _ => "配置值"
    };

    private static void ValidateImagePath(string controlId, string field, string path, string templateDirectory, bool optional, List<WMTemplateValidationError> errors)
    {
        if (TryResolveTemplateResource(templateDirectory, path, out var resolved) && File.Exists(resolved)) return;
        var severity = optional ? WMValidationSeverity.Warning : WMValidationSeverity.Error;
        errors.Add(new(controlId, field, optional ? "可选图片资源不存在。" : "必需图片资源不存在或路径无效。", severity));
    }

    private static void ValidateFontPath(WMText text, string templateDirectory, List<WMTemplateValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(text.FontFamily) || text.FontFamily == Global.DefaultFont) return;
        if (Path.GetExtension(text.FontFamily).Length == 0 && !text.FontFamily.Contains(Path.DirectorySeparatorChar) && !text.FontFamily.Contains(Path.AltDirectorySeparatorChar)) return;
        if (TryResolveTemplateResource(templateDirectory, text.FontFamily, out var resolved) && File.Exists(resolved)) return;
        errors.Add(new(text.ID, "FontFamily", "字体资源不存在或路径无效。", WMValidationSeverity.Warning));
    }

    private static bool TryResolveTemplateResource(string templateDirectory, string resourcePath, out string resolved)
    {
        resolved = string.Empty;
        if (Path.IsPathRooted(resourcePath))
        {
            resolved = Path.GetFullPath(resourcePath);
            return true;
        }

        var root = Path.GetFullPath(templateDirectory);
        var candidate = Path.GetFullPath(Path.Combine(root, resourcePath));
        var relative = Path.GetRelativePath(root, candidate);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            return false;

        resolved = candidate;
        return true;
    }
}
