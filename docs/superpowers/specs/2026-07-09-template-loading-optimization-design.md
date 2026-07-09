# Mac Template Loading Optimization Design

## Context

The Mac Catalyst workspace currently initializes local templates in `Watermark.Razor/BlazorPages/MainViewOSX.razor` by scanning every template directory, reading every `config.json`, initializing fonts, generating every preview image, and creating a browser object URL for each preview. The same full local initialization is triggered again when returning from dialogs such as "我的模板" and "模板市场".

This makes the Mac home workspace feel slow because returning to the home view repeats work even when local templates have not changed. The local home template list and the template market are different systems:

- Home templates are local folders under `Global.AppPath.TemplatesFolder`.
- Market templates come from the server through `APIHelper.GetWatermarks`.

The server market API supports paging and sorting, but it does not support filtering by recommendation state or canvas type. Because the UI categories "推荐", "热门", "最新", and "拼图" are derived from the full result set, this design does not change market loading into category-specific paging.

## Goals

- Make the Mac home workspace show local templates quickly.
- Avoid full local template reloads when returning to the home workspace.
- Rebuild only local templates that were added, deleted, or modified.
- Reuse already generated preview URLs when templates have not changed.
- Keep market behavior correct while reducing repeated requests and repeated local reloads.
- Keep the first implementation focused on Mac Catalyst behavior.

## Non-Goals

- Do not rename `Watermark.Andorid`.
- Do not redesign template editing, template rendering, or image export behavior.
- Do not add inaccurate category paging for the market while the backend cannot filter by category.
- Do not require backend API changes in this iteration.
- Do not broadly refactor shared Windows, Web, Android, or iOS flows.

## Recommended Approach

Create a Mac-focused local template cache and refresh flow. The home workspace should reuse cached local templates immediately, then run a lightweight filesystem diff in the background. Only changed templates should be parsed and re-rendered.

For the market, keep the existing full-result category derivation for correctness. Add or preserve caching so repeated market opens do not always call the server, and make closing the market trigger a local incremental refresh instead of a full local reload.

## Local Template Cache

Introduce a cache entry model for local Mac templates:

- `TemplateId`
- `FolderPath`
- `ConfigPath`
- `ConfigLastWriteTimeUtc`
- `Canvas`
- `PreviewSrc`
- `PreviewGeneratedAt`
- `LoadState`: `Ready`, `PreviewPending`, or `Error`
- `ErrorMessage`

The cache should support these operations:

- `GetOrRefreshAsync(force: false)`: return cached entries quickly, then perform a filesystem diff.
- `ForceReloadAsync()`: clear the cache, revoke old preview URLs, and rebuild from disk.
- `Invalidate(templateId)`: mark one template stale after download, edit, or delete.
- `RefreshChangedAsync()`: compare cached entries with template folders and process only changes.

The cache should live behind a Mac-oriented scoped service named `MacTemplateLibraryService`. Keeping it scoped fits the Blazor Hybrid runtime and keeps scanning, diffing, and preview lifecycle logic out of `MainViewOSX.razor`.

## Refresh Rules

During a normal refresh:

- New folder with `config.json`: read config, initialize fonts, add entry with `PreviewPending`.
- Removed folder: remove entry and revoke its old `PreviewSrc`.
- Existing folder with changed `config.json` timestamp: read config again, revoke old preview, mark `PreviewPending`.
- Existing folder with unchanged timestamp: keep existing `Canvas` and `PreviewSrc`.
- Invalid config: keep an `Error` entry or skip display, but do not block the rest of the list.

Returning from "我的模板" or "模板市场" should call the incremental refresh path, not the full reload path. The manual refresh button should call `ForceReloadAsync()`.

## Preview Generation

Preview generation should be separated from list availability:

- The home sidebar should render as soon as configs are available.
- Templates without previews should show a stable placeholder.
- Preview generation should run in the background and update entries as each preview completes.
- Generation concurrency should be limited to 1 or 2 because rendering uses Skia, fonts, images, and browser object URLs.
- The first implementation should queue previews in sidebar order: normal templates by name, then split templates by name. Search-aware reprioritization can be added later if needed.
- Applying a template to an image should use the `Canvas`, not wait for preview generation.

Object URLs must be revoked when a preview is replaced, when an entry is removed, and when the cache is force-cleared.

## Market Loading

The market should remain correct under the current backend contract:

- Keep deriving "推荐", "热门", "最新", and "拼图" from the fetched server list.
- Do not replace this with category-specific paging until the server supports filters such as `recommend`, `canvasType`, `visible`, and `keyword`.
- Cache the fetched market list to avoid repeated requests while the data is still fresh.
- Avoid closing the market causing a full local home template reload.
- The market UI should progressively render already fetched data to reduce DOM work.

If market network cost becomes the next bottleneck, the next separate design should add backend filtering parameters and then switch the market UI to true category paging.

## Component Flow

`MainViewOSX.razor` should become a consumer of the local template service:

1. On initialization, request cached local templates.
2. Render the sidebar with available entries.
3. Start or await an incremental refresh.
4. Receive updates as changed templates and previews are processed.
5. On dialog close, request incremental refresh.
6. On manual refresh, request force reload.

`MacModeSidebar.razor` should tolerate missing previews by displaying placeholders and should not require every template to be fully rendered before the list appears.

`MyTemplates.razor` should keep its own dialog-specific cloud and favorite sections in the first pass. After local mutations such as download, delete, or edit, it should trigger cache invalidation or incremental refresh for the home workspace instead of forcing a full home reload.

## Error Handling

- A broken local template should not block loading other templates.
- Preview generation failure should leave the template selectable if the config loaded successfully.
- Errors should be visible enough for debugging through `ErrorMessage`, logs, or a non-blocking UI hint.
- Market fetch failure should keep any existing cached market data and show a non-blocking message.

## Testing And Verification

Manual verification should cover:

- First Mac home load shows template names before all previews finish.
- Returning to home without local template changes does not regenerate all previews.
- Downloading a template from the market adds only the new or changed local template.
- Deleting a template removes only that template and revokes its preview URL.
- Editing a template refreshes only the edited template.
- Manual refresh still rebuilds all local templates.
- Market close does not cause full local template reload.
- Market categorization remains correct under the current full-result fetch model.

Implementation verification should include:

- Build the affected MAUI/Razor project where practical.
- Add focused unit tests for the cache diff logic if the project test structure allows it.
- Exercise the Mac Catalyst path manually if local tooling permits.
