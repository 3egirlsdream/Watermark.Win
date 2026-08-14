#nullable enable

using System.IO;
using System.IO.Compression;
using System.Management;
using System.Reflection;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Watermark.Win.Models;

namespace Watermark.Shared.Models;

/// <summary>
/// WPF implementation of the native capabilities consumed by the shared
/// desktop UI. Keep product behavior aligned with the Mac Catalyst host while
/// using Windows dialogs, clipboard, shell and window management APIs.
/// </summary>
public sealed class ClientInstance(APIHelper api, IWindowService windows) : IClientInstance
{
    private const string WindowsUpdateChannel = "WatermarkV3";
    private const string PhotoFilter =
        "照片与 RAW|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.tif;*.tiff;*.dng;*.cr2;*.cr3;*.nef;*.nrw;*.arw;*.sr2;*.raf;*.orf;*.rw2;*.rwl;*.pef;*.3fr;*.iiq;*.srw|普通照片|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.tif;*.tiff|RAW 照片|*.dng;*.cr2;*.cr3;*.nef;*.nrw;*.arw;*.sr2;*.raf;*.orf;*.rw2;*.rwl;*.pef;*.3fr;*.iiq;*.srw";
    private const string FontFilter = "字体文件|*.ttf;*.otf";

    public string UpdateMessage { get; set; } = string.Empty;
    public string UpdateVersion { get; set; } = string.Empty;
    public string LinkPath { get; set; } = string.Empty;
    public string AppTitle { get; set; } = "轻影";

    public string Key()
    {
        var material = GetMachineIdentifier().Replace("-", string.Empty, StringComparison.Ordinal) + "CATLNMSL";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(material)).Replace("-", string.Empty, StringComparison.Ordinal);
    }

    public void Haptic()
    {
        // Windows desktop has no equivalent feedback contract.
    }

    public Task<IEnumerable<string>> PickMultipleAsync()
    {
        var files = InvokeOnUiThread(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择照片",
                DefaultExt = ".jpg",
                Multiselect = true,
                Filter = PhotoFilter
            };
            return ShowDialog(dialog) == true ? dialog.FileNames : [];
        });
        return Task.FromResult<IEnumerable<string>>(files);
    }

    public Task<string> PickAsync() => PickFileAsync("选择照片", PhotoFilter, ".jpg");

    public Version GetVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version
        ?? typeof(ClientInstance).Assembly.GetName().Version
        ?? new Version(1, 0);

    public async Task<bool> CheckUpdate(string platform = "WatermarkAndroid")
    {
        if (string.IsNullOrWhiteSpace(platform)
            || string.Equals(platform, "WatermarkAndroid", StringComparison.OrdinalIgnoreCase))
            platform = WindowsUpdateChannel;

        var result = await Connections.HttpGetAsync<WMClientVersion>(
            APIHelper.HOST + $"/api/CloudSync/GetVersion?Client={Uri.EscapeDataString(platform)}",
            Encoding.Default).ConfigureAwait(false);
        if (result?.success != true)
            throw new InvalidOperationException(result?.message?.content ?? "更新服务暂时不可用。");
        if (string.IsNullOrWhiteSpace(result.data?.VERSION)
            || !Version.TryParse(result.data.VERSION, out var availableVersion))
            throw new InvalidOperationException("更新服务返回了无效的版本信息。");

        UpdateMessage = result.data.MEMO ?? string.Empty;
        UpdateVersion = result.data.VERSION;
        if (availableVersion <= GetVersion())
        {
            LinkPath = string.Empty;
            return false;
        }

        if (!TryGetHttpUri(result.data.PATH, out var downloadUri))
            throw new InvalidOperationException("更新服务返回了无效的下载地址。");
        LinkPath = downloadUri.AbsoluteUri;
        return true;
    }

    public Task SetTextAsync(string uri)
    {
        InvokeOnUiThread(() => Clipboard.SetText(uri ?? string.Empty));
        return Task.CompletedTask;
    }

    public Task OpenExternalUrlAsync(string url)
    {
        if (!TryGetHttpUri(url, out var uri))
            throw new ArgumentException("Only HTTP and HTTPS links can be opened.", nameof(url));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    public Task<API<string>> AliPays(decimal cost, string tradeName) => Task.FromResult(new API<string>
    {
        success = false,
        message = new APISub { content = "当前平台暂不支持支付" }
    });

    public async Task ReLogin()
    {
        var local = await Global.ReadLocalAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(local.Item1)) return;
        var login = await api.LoginIn(local.Item1, local.Item2, true).ConfigureAwait(false);
        if (login?.success == true && login.data?.data is not null)
            Global.CurrentUser = Global.SetUserInfo(login.data.data);
    }

    public async Task<bool> IsOutOfDate(string client = "Watermark_A")
    {
        var result = await Connections.HttpGetAsync<WMClientVersion>(
            APIHelper.HOST + $"/api/CloudSync/GetVersion?Client={Uri.EscapeDataString(client)}",
            Encoding.Default).ConfigureAwait(false);
        return result?.success == true
               && Version.TryParse(result.data?.VERSION, out var available)
               && available > GetVersion();
    }

    public bool Save(byte[] b64, string fn)
    {
        try
        {
            Directory.CreateDirectory(Global.OutPutPath);
            File.WriteAllBytes(Path.Combine(Global.OutPutPath, "DFX_" + Path.GetFileName(fn)), b64);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public Task<string> OpenFolder()
    {
        var folder = InvokeOnUiThread(() =>
        {
            var dialog = new OpenFolderDialog { Title = "选择文件夹" };
            return ShowDialog(dialog) == true ? dialog.FolderName : string.Empty;
        });
        return Task.FromResult(folder);
    }

    public Task<bool> RevealFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Task.FromResult(false);
        var directory = Path.GetFullPath(path);
        if (!Directory.Exists(directory)) return Task.FromResult(false);

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory)
        {
            UseShellExecute = true
        });
        return Task.FromResult(true);
    }

    public void SetColor(string color = "#F5F5F5")
    {
        // WPF owns its native title bar; workspace colors are rendered by Blazor.
    }

    public async Task<WMDesignFunc> GetWMDesignFunc(string canvasId)
    {
        var canvas = await Global.GetCanvas(canvasId).ConfigureAwait(false) ?? new WMCanvas { ID = canvasId };
        canvas.Exif.TryAdd(canvasId, ExifHelper.DefaultMeta);
        var design = new WMDesignFunc { CurrentCanvas = canvas };

        design.SelectLogo = async logo =>
        {
            var source = await PickAsync();
            if (string.IsNullOrWhiteSpace(source)) return;
            logo.Path = CopyTemplateAsset(source, canvasId);
        };
        design.SelectContainer = async container =>
        {
            var source = await PickAsync();
            if (!string.IsNullOrWhiteSpace(source)) container.Path = source;
        };
        design.SelectDefaultImageEvt = PickAsync;
        design.ImportFontEvt = () => ImportFontAsync(canvasId);
        design.ImportFontEvt2 = ImportFontAsync;
        design.HotKeyEvt = _ => { };
        return design;
    }

    public async Task Update(Action<long, long> downloadProgressChanged)
    {
        if (!TryGetHttpUri(LinkPath, out var uri))
            throw new InvalidOperationException("更新下载地址无效，请重新检查更新。");
        await OpenExternalUrlAsync(uri.AbsoluteUri).ConfigureAwait(false);
        downloadProgressChanged?.Invoke(1, 1);
    }

    public async Task<bool> Download(string directory, string fileName, string extension)
    {
        try
        {
            var safeName = Path.GetFileName(fileName);
            var safeExtension = extension.Trim().TrimStart('.').ToLowerInvariant();
            var packagedFile = Path.Combine(AppContext.BaseDirectory, $"{safeName}.{safeExtension}");
            if (!File.Exists(packagedFile)) return false;

            Directory.CreateDirectory(directory);
            if (safeExtension == "zip")
            {
                var target = Path.Combine(directory, safeName);
                Directory.CreateDirectory(target);
                await Task.Run(() => ZipFile.ExtractToDirectory(packagedFile, target, true)).ConfigureAwait(false);
            }
            else if (safeExtension is "otf" or "ttf")
            {
                File.Copy(packagedFile, Path.Combine(directory, $"{safeName}.{safeExtension}"), true);
            }
            else
            {
                return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Exit() => InvokeOnUiThread(() => Application.Current?.Shutdown());

    public void OpenDesign(WMCanvas canvas)
    {
        // The shared router owns template designer navigation on both desktops.
    }

    public Task InteropInit(string appId) => Task.CompletedTask;

    public void WindowMinimize() => InvokeOnUiThread(windows.Minimize);
    public void WindowZoom() => InvokeOnUiThread(windows.Maximize);
    public void WindowClose() => InvokeOnUiThread(() => windows.Close());
    public void WindowStartDrag() => InvokeOnUiThread(WindowService.StartMove);
    public void WindowDragMove() => InvokeOnUiThread(WindowService.UpdateWindowPos);
    public void WindowEndDrag() => InvokeOnUiThread(WindowService.StopMove);

    private static string GetMachineIdentifier()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                new SelectQuery("select UUID from Win32_ComputerSystemProduct"));
            foreach (ManagementObject item in searcher.Get())
            using (item)
            {
                var uuid = item["UUID"]?.ToString();
                if (!string.IsNullOrWhiteSpace(uuid)) return uuid;
            }
        }
        catch
        {
            // Fall through to the Windows installation identifier.
        }

        try
        {
            var machineGuid = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography",
                "MachineGuid",
                null)?.ToString();
            if (!string.IsNullOrWhiteSpace(machineGuid)) return machineGuid;
        }
        catch
        {
            // A locked-down machine can deny registry access.
        }

        return $"{Environment.MachineName}-{Environment.UserName}";
    }

    private static string CopyTemplateAsset(string source, string templateId)
    {
        var destinationDirectory = Path.Combine(Global.AppPath.TemplatesFolder, templateId);
        Directory.CreateDirectory(destinationDirectory);
        var fileName = Path.GetFileName(source)
                       ?? throw new InvalidDataException("选择的素材缺少文件名。");
        File.Copy(source, Path.Combine(destinationDirectory, fileName), true);
        return fileName;
    }

    private static async Task ImportFontAsync(string templateId)
    {
        var source = await PickFileAsync("选择字体文件", FontFilter, ".ttf");
        if (string.IsNullOrWhiteSpace(source)) return;
        var fileName = Path.GetFileName(source);
        Directory.CreateDirectory(Global.AppPath.FontFolder);
        File.Copy(source, Path.Combine(Global.AppPath.FontFolder, fileName), true);
        var templateDirectory = Path.Combine(Global.AppPath.TemplatesFolder, templateId);
        Directory.CreateDirectory(templateDirectory);
        File.Copy(source, Path.Combine(templateDirectory, fileName), true);
    }

    private static Task<string> PickFileAsync(string title, string filter, string defaultExtension)
    {
        var selected = InvokeOnUiThread(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                DefaultExt = defaultExtension,
                Multiselect = false,
                Filter = filter
            };
            return ShowDialog(dialog) == true ? dialog.FileName : string.Empty;
        });
        return Task.FromResult(selected);
    }

    private static bool? ShowDialog(FileDialog dialog) => Application.Current?.MainWindow is { } owner
        ? dialog.ShowDialog(owner)
        : dialog.ShowDialog();

    private static bool? ShowDialog(OpenFolderDialog dialog) => Application.Current?.MainWindow is { } owner
        ? dialog.ShowDialog(owner)
        : dialog.ShowDialog();

    private static T InvokeOnUiThread<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        return dispatcher is null || dispatcher.CheckAccess() ? action() : dispatcher.Invoke(action);
    }

    private static void InvokeOnUiThread(Action action) => InvokeOnUiThread(() =>
    {
        action();
        return true;
    });

    private static bool TryGetHttpUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var candidate)
            && candidate.Scheme is "http" or "https")
        {
            uri = candidate;
            return true;
        }
        uri = null!;
        return false;
    }
}
