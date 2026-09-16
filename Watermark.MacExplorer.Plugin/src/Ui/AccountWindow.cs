using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MacExplorer.PluginUi;
using Watermark.MacExplorer.Plugin.Accounts;
using Watermark.Shared.Models;

namespace Watermark.MacExplorer.Plugin.Ui;

/// <summary>登录、开通会员与账号管理窗口；成功返回时由宿主重新校验授权。</summary>
internal sealed class AccountWindow : Window
{
    private readonly AccountService accounts = new();
    private readonly CancellationTokenSource lifetime;
    private readonly TextBlock status = UiKit.Text(string.Empty, 12, 0.75, wrap: true);
    private readonly StackPanel body = UiKit.Column(12);
    private bool busy;

    public AccountWindow(bool manage, CancellationToken token)
    {
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Title = manage ? "轻影账号" : "登录 / 开通会员";
        Width = 470;
        Height = 560;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer
        {
            Padding = new Thickness(22, 18, 22, 18),
            Content = UiKit.Column(14,
                UiKit.Heading(manage ? "轻影账号" : "登录后即可开始 7 天免费试用"),
                body,
                UiKit.Divider(),
                status,
                Footer())
        };
        Opened += async (_, _) => await RestoreAsync();
        Closed += (_, _) => lifetime.Cancel();
        Render();
    }

    private Control Footer()
    {
        var actions = UiKit.Row(8,
            UiKit.Secondary("取消", Close),
            UiKit.Secondary("打开官网注册", () => AccountService.OpenUrl(AccountService.WebsiteUrl)));
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        return actions;
    }

    private async Task RestoreAsync()
    {
        if (AccountService.IsSignedIn) return;
        await RunAsync(async () =>
        {
            if (await accounts.RestoreAsync(lifetime.Token))
            {
                status.Text = "已恢复登录状态。";
                Render();
            }
        });
    }

    private void Render()
    {
        body.Children.Clear();
        var signedIn = AccountService.IsSignedIn;
        body.Children.Add(UiKit.Column(4,
            UiKit.Text(signedIn ? $"已登录：{AccountService.DisplayName}" : "尚未登录轻影账号。", 13),
            UiKit.Text(signedIn
                    ? AccountService.IsMember
                        ? $"会员有效期至 {Global.CurrentUser.EXPIRE_DATE:yyyy-MM-dd}。"
                        : "当前未开通会员，登录状态下可享受 7 天免费试用。"
                    : "轻影 Watermark 的会员可在本插件与桌面端、移动端通用。",
                12, 0.7, wrap: true)));

        if (signedIn) RenderMembership();
        else RenderSignIn();
    }

    private void RenderSignIn()
    {
        var email = UiKit.Input("邮箱");
        var password = UiKit.Input("密码");
        password.PasswordChar = '•';
        var signIn = UiKit.Primary("登录", () => _ = SignInAsync(email, password));
        password.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) _ = SignInAsync(email, password);
        };
        body.Children.Add(UiKit.Card(UiKit.Column(10,
            UiKit.Text("邮箱", 12, 0.7), email,
            UiKit.Text("密码", 12, 0.7), password,
            signIn)));
        body.Children.Add(UiKit.Text("还没有账号？请前往轻影官网注册后再回到这里登录。", 12, 0.7, wrap: true));
    }

    private void RenderMembership()
    {
        body.Children.Add(UiKit.Text("开通会员", 13, bold: true));
        var plans = UiKit.Column(8);
        foreach (var plan in AccountService.Plans)
        {
            var title = UiKit.Row(6, UiKit.Text(plan.Name, 13, bold: true), UiKit.Text($"¥{plan.Price:0.##}", 13, 0.85));
            if (plan.Recommended) title.Children.Add(UiKit.Text("推荐", 11, 0.55));
            var info = UiKit.Column(2, title, UiKit.Text(plan.Description, 12, 0.65, wrap: true));
            var buy = UiKit.Primary("开通", () => _ = PurchaseAsync(plan));
            var card = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(info, 0);
            Grid.SetColumn(buy, 1);
            buy.VerticalAlignment = VerticalAlignment.Center;
            card.Children.Add(info);
            card.Children.Add(buy);
            plans.Children.Add(UiKit.Card(card, new Thickness(12)));
        }
        body.Children.Add(plans);
        body.Children.Add(UiKit.Row(8,
            UiKit.Secondary("完成，返回插件", () => PluginWindows.Complete(this)),
            UiKit.Secondary("退出登录", () => _ = SignOutAsync())));
    }

    private async Task SignInAsync(TextBox email, TextBox password)
    {
        if (busy) return;
        await RunAsync(async () =>
        {
            var result = await accounts.SignInAsync(email.Text ?? string.Empty, password.Text ?? string.Empty, lifetime.Token);
            status.Text = result.Message;
            if (result.Succeeded)
            {
                password.Text = string.Empty;
                Render();
            }
        });
    }

    private async Task SignOutAsync()
    {
        if (busy) return;
        await RunAsync(async () =>
        {
            await accounts.SignOutAsync();
            status.Text = "已退出登录。";
            Render();
        });
    }

    private async Task PurchaseAsync(MembershipPlan plan)
    {
        if (busy) return;
        await RunAsync(async () =>
        {
            var progress = new Progress<string>(text => status.Text = text);
            var result = await accounts.PurchaseAsync(plan, string.Empty, progress, lifetime.Token);
            status.Text = result.Message;
            if (result.Succeeded)
            {
                Render();
                if (AccountService.IsMember) PluginWindows.Complete(this);
            }
        });
    }

    private async Task RunAsync(Func<Task> action)
    {
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
            PluginLog.Error("账号窗口操作失败", ex);
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
        body.IsEnabled = enabled;
        body.Opacity = enabled ? 1 : 0.6;
    }
}
