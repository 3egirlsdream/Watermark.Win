#nullable enable

using Watermark.Shared.Models;

namespace Watermark.Razor.Workspace;

public sealed class WMColorPresetLibrary : IWMColorPresetLibrary
{
    public IReadOnlyList<WMColorRecipe> Load() => PresetManager.LoadAllRecipes();

    public Task SaveAsync(
        WMColorRecipe recipe,
        string? referenceImagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PresetManager.SaveRecipeAsync(recipe, referenceImagePath);
    }

    public bool Delete(string presetId) => PresetManager.Delete(presetId);

    public string? GetReferenceThumbnailPath(WMColorRecipe recipe) =>
        PresetManager.GetReferenceThumbnailPath(recipe);
}
