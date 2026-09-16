using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MacExplorer.PluginSdk;
using MacExplorer.PluginUi;
using Watermark.MacExplorer.Plugin.Accounts;
using Watermark.MacExplorer.Plugin.Library;
using Watermark.MacExplorer.Plugin.Market;
using Watermark.MacExplorer.Plugin.Render;
using Watermark.Shared.Models;

namespace Watermark.MacExplorer.Plugin.Ui;

/// <summary>
/// 左侧已下载模板、右侧简版市场的一体面板。点击模板立即为选中的照片生成新文件，
/// 生成成功后用 <see cref="PluginWindows.Complete"/> 关闭窗口并回传产物。
/// </summary>
internal sealed class WatermarkPanelWindow : Window
{
    private readonly PluginInvocation invocation;
    private readonly IProgress<PluginProgress> host;
    private readonly CancellationTokenSource lifetime;
    private readonly TemplateLibraryService library = new();
    private readonly TemplateMarketService market = new();
    private readonly TemplateRenderService renderer = new();
    private readonly CoverLoader covers = new();

    private readonly StackPanel libraryRows = new() { Spacing = 6 };
    private readonly StackPanel marketRows = new() { Spacing = 8 };
    private readonly TextBlock libraryEmpty = UiKit.Text("还没有下载模板，可在右侧市场下载后使用。", 12, 0.6, wrap: true);
    private readonly TextBlock marketEmpty = UiKit.Text(string.Empty, 12, 0.6, wrap: true);
    private readonly TextBlock status = UiKit.Text("请选择模板开始生成。", 12, 0.75, wrap: true);
    private readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 4, IsVisible = false };
    private readonly TextBox search = UiKit.Input("搜索模板，留空查看推荐");
    private readonly Button loadMore = UiKit.Secondary("加载更多", () => { });
    private readonly List<MarketItem> marketItems = [];
    private readonly string[] sources;

    private RenderOutcome? results;
    private int cursor;
    private bool hasMore;
    private bool busy;

    public WatermarkPanelWindow(PluginInvocation invocation, IProgress<PluginProgress> hostProgress, CancellationToken token)
    {
        this.invocation = invocation;
        host = hostProgress;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        sources = invocation.Files.Where(file => !file.IsDirectory).Select(file => file.Path).ToArray();

        Title = "轻影水印";
        Width = 940;
        Height = 640;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = BuildLayout();

        search.Width = 260;
        loadMore.HorizontalAlignment = HorizontalAlignment.Left;
        loadMore.IsVisible = false;
        search.KeyDown += (_, args) => { if (args.Key == Key.Enter) _ = LoadMarketAsync(true); };
        loadMore.Click += (_, _) => _ = LoadMarketAsync(false);
        Opened += (_, _) => _ = InitializeAsync();
        Closed += (_, _) =>
        {
            lifetime.Cancel();
            covers.Dispose();
        };
    }

    /// <summary>宿主关闭窗口表示取消；生成成功时带回产物。</summary>
    public RenderOutcome Result => results ?? new RenderOutcome([], []);

    public bool Completed => results is { Outputs.Count: > 0 };

    private Control BuildLayout()
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var title = UiKit.Column(3, UiKit.Heading("轻影水印", 17), UiKit.Text($"已选择 {sources.Length} 张照片", 12, 0.7));
        var account = UiKit.Secondary("账号 / 会员", () => _ = OpenAccountAsync());
        account.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(title, 0);
        Grid.SetColumn(account, 2);
        header.Children.Add(title);
        header.Children.Add(account);

        var libraryContent = UiKit.Column(6, libraryRows, libraryEmpty);
        libraryContent.VerticalAlignment = VerticalAlignment.Top;
        var libraryScroll = new ScrollViewer { Content = libraryContent };
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        left.Children.Add(UiKit.Row(0, UiKit.Heading("已下载模板", 14)));
        Grid.SetRow(libraryScroll, 1);
        libraryScroll.Margin = new Thickness(0, 8, 0, 0);
        left.Children.Add(libraryScroll);

        var marketContent = UiKit.Column(8, marketRows, marketEmpty, loadMore);
        marketContent.VerticalAlignment = VerticalAlignment.Top;
        var marketScroll = new ScrollViewer { Content = marketContent };
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        right.Children.Add(UiKit.Row(8, search, UiKit.Primary("搜索", () => _ = LoadMarketAsync(true))));
        Grid.SetRow(marketScroll, 1);
        marketScroll.Margin = new Thickness(0, 8, 0, 0);
        right.Children.Add(marketScroll);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("320,*"),
            ColumnSpacing = 18,
            Margin = new Thickness(0, 12, 0, 0)
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        body.Children.Add(left);
        body.Children.Add(right);

        var footer = UiKit.Column(6, progress, status, UiKit.Text(BudgetHint(sources.Length), 11, 0.5, wrap: true));
        footer.Margin = new Thickness(0, 12, 0, 0);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20, 18, 20, 16) };
        Grid.SetRow(body, 1);
        Grid.SetRow(footer, 2);
        root.Children.Add(header);
        root.Children.Add(body);
        root.Children.Add(footer);
        return root;
    }

    /// <summary>宿主对 execute 的限时是 120 秒起、每多一个文件加 30 秒，面板停留也计入其中。</summary>
    private static string BudgetHint(int count)
    {
        var seconds = 120 + 30 * (count - 1);
        var minutes = seconds % 60 == 0 ? (seconds / 60).ToString() : (seconds / 60.0).ToString("0.#");
        return $"面板停留与生成共限时 {minutes} 分钟，请尽快选择模板。";
    }

    private async Task InitializeAsync()
    {
        if (string.Equals(invocation.CommandId, "market", StringComparison.OrdinalIgnoreCase))
        {
            status.Text = "在右侧搜索并下载模板，下载后点击左侧模板生成新照片。";
            search.Focus();
        }
        await LoadLibraryAsync();
        await LoadMarketAsync(true);
    }

    private async Task LoadLibraryAsync()
    {
        try
        {
            var templates = await library.LoadAsync(lifetime.Token);
            UiKit.OnUiThread(() =>
            {
                libraryRows.Children.Clear();
                libraryEmpty.IsVisible = templates.Count == 0;
                foreach (var template in templates) libraryRows.Children.Add(BuildLibraryRow(template));
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PluginLog.Error("读取本地模板失败", ex);
            UiKit.OnUiThread(() => libraryEmpty.Text = "读取本地模板失败：" + ex.Message);
        }
    }

    private Control BuildLibraryRow(TemplateItem template)
    {
        var frame = UiKit.Thumbnail(template.Name, 56, 42);
        var info = UiKit.Column(2, UiKit.Text(template.Name, 13), UiKit.Text("点击生成新照片", 11, 0.55));
        info.MaxWidth = 200;
        var button = new Button
        {
            Content = UiKit.Row(10, frame, info),
            Padding = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        button.Click += (_, _) => _ = GenerateAsync(template);
        _ = LoadCoverAsync(frame, template.CoverPath);
        return button;
    }

    private async Task LoadMarketAsync(bool reset)
    {
        if (busy) return;
        if (reset)
        {
            cursor = 0;
            marketItems.Clear();
            UiKit.OnUiThread(() =>
            {
                marketRows.Children.Clear();
                marketEmpty.Text = "正在加载模板市场…";
                marketEmpty.IsVisible = true;
                loadMore.IsVisible = false;
            });
        }
        try
        {
            var page = await market.SearchAsync(search.Text, cursor, lifetime.Token);
            marketItems.AddRange(page.Items);
            cursor = page.Cursor;
            hasMore = page.HasMore;
            RenderMarket();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            PluginLog.Error("加载模板市场失败", ex);
            UiKit.OnUiThread(() =>
            {
                marketEmpty.Text = "模板市场加载失败：" + ex.Message;
                marketEmpty.IsVisible = true;
            });
        }
    }

    private void RenderMarket()
    {
        UiKit.OnUiThread(() =>
        {
            marketRows.Children.Clear();
            marketEmpty.Text = marketItems.Count == 0 ? "没有找到匹配的模板。" : string.Empty;
            marketEmpty.IsVisible = marketItems.Count == 0;
            foreach (var item in marketItems) marketRows.Children.Add(BuildMarketRow(item));
            loadMore.Content = hasMore ? "加载更多" : "已显示全部";
            loadMore.IsVisible = hasMore;
        });
    }

    private Control BuildMarketRow(MarketItem item)
    {
        var frame = UiKit.Thumbnail(item.Name, 96, 64);
        var title = UiKit.Text(item.Name, 13, bold: true);
        title.MaxWidth = 260;
        title.HorizontalAlignment = HorizontalAlignment.Left;
        var info = UiKit.Column(3,
            title,
            UiKit.Text($"下载 {item.DownloadTimes}{(item.Recommend ? " · 精选" : string.Empty)}", 11, 0.55),
            UiKit.Text(item.Description ?? string.Empty, 11, 0.5));
        info.MaxWidth = 300;
        info.HorizontalAlignment = HorizontalAlignment.Left;

        var actions = UiKit.Column(6, LibraryButton(item));
        actions.VerticalAlignment = VerticalAlignment.Center;
        actions.Width = 108;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 12 };
        Grid.SetColumn(frame, 0);
        Grid.SetColumn(info, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(frame);
        grid.Children.Add(info);
        grid.Children.Add(actions);
        _ = LoadCoverAsync(frame, item.CoverPath);
        return UiKit.Card(grid, new Thickness(12), 12);
    }

    private Button LibraryButton(MarketItem item)
    {
        if (!market.IsDownloaded(item.Id)) return UiKit.Secondary("下载", () => _ = DownloadAsync(item));
        var done = UiKit.Secondary("已下载", () => { });
        done.IsEnabled = false;
        return done;
    }

    private async Task DownloadAsync(MarketItem item)
    {
        await RunAsync(async () =>
        {
            status.Text = $"正在下载「{item.Name}」…";
            var outcome = await market.DownloadAsync(item, lifetime.Token);
            status.Text = outcome.Message;
            if (!outcome.Succeeded) return;
            libraryEmpty.IsVisible = false;
            await LoadLibraryAsync();
            RenderMarket();
            status.Text = $"「{item.Name}」已下载，在左侧点击模板即可生成新照片。";
        });
    }

    private Task GenerateAsync(TemplateItem template) => RunAsync(() => GenerateCoreAsync(template));

    private async Task GenerateCoreAsync(TemplateItem template)
    {
        progress.IsVisible = true;
        progress.Value = 0;
        try
        {
            await renderer.PrepareFontsAsync(template, lifetime.Token);
            var reporter = new Progress<PluginProgress>(report =>
            {
                progress.Value = report.Percent ?? 0;
                status.Text = report.Message;
                host.Report(report);
            });
            var outcome = await renderer.RenderAsync(template, sources, invocation.WorkDirectory, reporter, lifetime.Token);
            if (outcome.Outputs.Count == 0)
            {
                status.Text = outcome.Warnings.Count > 0
                    ? "生成失败：" + string.Join("；", outcome.Warnings)
                    : "生成失败，请更换模板后重试。";
                return;
            }
            if (outcome.Warnings.Count > 0)
                PluginLog.Warning($"部分照片未生成（{template.Name}）：{string.Join("；", outcome.Warnings)}");
            results = outcome;
            status.Text = $"已生成 {outcome.Outputs.Count} 张照片，正在保存…";
            PluginWindows.Complete(this);
        }
        finally
        {
            progress.IsVisible = false;
        }
    }

    private async Task OpenAccountAsync()
    {
        await RunAsync(async () =>
        {
            await new AccountWindow(true, lifetime.Token).ShowDialog(this);
            status.Text = AccountService.IsMember
                ? $"会员有效期至 {Global.CurrentUser.EXPIRE_DATE:yyyy-MM-dd}。"
                : "已返回插件面板。";
            RenderMarket();
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        SetEnabled(false);
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            status.Text = "操作已取消。";
        }
        catch (Exception ex)
        {
            PluginLog.Error("面板操作失败", ex);
            status.Text = "操作失败：" + ex.Message;
        }
        finally
        {
            busy = false;
            SetEnabled(true);
        }
    }

    private void SetEnabled(bool enabled)
    {
        libraryRows.IsEnabled = enabled;
        marketRows.IsEnabled = enabled;
        search.IsEnabled = enabled;
        loadMore.IsEnabled = enabled;
        libraryRows.Opacity = enabled ? 1 : 0.6;
        marketRows.Opacity = enabled ? 1 : 0.6;
    }

    private async Task LoadCoverAsync(Border frame, string? coverPath)
    {
        if (coverPath is null) return;
        var bitmap = await covers.LoadAsync(coverPath);
        if (bitmap is null || lifetime.IsCancellationRequested) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (lifetime.IsCancellationRequested) return;
            UiKit.SetThumbnail(frame, bitmap);
        });
    }
}
