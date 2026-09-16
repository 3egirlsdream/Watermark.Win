# 轻影水印 · Mac Explorer 插件

在 [Mac Explorer](https://github.com/3egirlsdream/Mac-Explorer) 里右键选中照片，用「轻影 / Watermark」的账号、会员与模板库直接生成带水印的新图。

- 复用轻影账号与会员：与桌面端、移动端共用 `~/Documents/DFM/.sys/sys` 登录态与官网订单。
- 7 天试用由宿主账本管理，试用到期且未开通会员时不能生成。
- 面板左侧是已下载模板，右侧是简版市场（搜索、每页 7 条、下载即入库），点击模板立即生成。

## 目录结构

```
Watermark.MacExplorer.Plugin/
├── global.json              # 插件目录锁定 .NET 10 SDK，避免被仓库根 global.json(8.0.204) 影响
├── NuGet.Config             # MacExplorer.* → sdk/packages 本地源，其余 → nuget.org
├── sdk/                     # MacExplorer-PluginSDK 解压产物（不入库，版本见 sdk/sdk.json）
├── src/                     # 插件本体
├── tools/Smoke/             # 无头冒烟控制台
└── artifacts/               # 打包产物 *.mexplug（不入库）
```

## 一、准备 SDK

从 [Mac Explorer SDK 发布页](https://github.com/3egirlsdream/Mac-Explorer/releases/download/sdk-v1.0.1/MacExplorer-PluginSDK-1.0.1.zip) 下载 `MacExplorer-PluginSDK-1.0.1.zip`，解压到 `Watermark.MacExplorer.Plugin/sdk/`，使 `sdk/packages/`、`sdk/tools/` 直接位于该目录下。
SDK 1.0.1 附带宿主 `MinimumHostVersion=1.0.45`；旧版 SDK 解压目录可在 `sdk.json` 中查看版本。

## 二、构建与打包

```bash
cd Watermark.MacExplorer.Plugin

# 构建（插件目录自带 global.json 与 NuGet.Config）
dotnet build src/Watermark.MacExplorer.Plugin.csproj -c Release --configfile NuGet.Config

# 打包（输出必须是新路径，工具拒绝覆盖）
dotnet sdk/tools/MacExplorer.PluginPack.dll \
  src/bin/Release/net10.0/osx-arm64 \
  artifacts/Watermark.MacExplorer.Plugin-1.0.5.mexplug
```

`RuntimeIdentifier=osx-arm64` 是必需的：它把 `libSkiaSharp.dylib` 平铺到输出根目录，插件自带的 `NativeLibrary.SetDllImportResolver` 才能在宿主子进程里解析到原生 Skia。

## 二之一、包体构成

打包产物 4.6 MB（安装后展开 10.6 MB，18 个文件），构建时自动做三处裁剪：

- `libSkiaSharp.dylib` 是 x86_64+arm64 通用二进制，清单只声明 `architecture: arm64`，构建后由 `lipo -thin arm64` 瘦身（15.1 MB → 6.7 MB，占包体 63%）。
- `Watermark.Shared` 为自身 Blazor UI 引用了 Masa.Blazor、ASP.NET Core Components 及其传递依赖（BemIt、DeepCloner、OneOf、FluentValidation.DependencyInjectionExtensions 等），插件不触碰这条链路（`Global.Byte2Url` 等 `IJSRuntime` 重载仅 Razor 侧调用），通过 `ExcludeAssets="runtime"` 排除。
- Avalonia 12.0.4（`Avalonia`、`Avalonia.Remote.Protocol`、`MicroCom.Runtime`）同样排除：宿主 `PluginWorker` 的 ALC 对 `Avalonia*` 与 `MacExplorer.PluginUi` 一律 `LoadFromAssemblyName` 回落到默认上下文，插件目录里的副本本来就不会被加载。

裁剪后剩余：原生 Skia 6.7 MB，`Watermark.Shared` 0.9 MB，`MetadataExtractor` + `XmpCore`（EXIF/XMP 读写）0.9 MB，`Newtonsoft.Json` 0.6 MB，`FluentValidation` + `Qiniu` 0.6 MB，托管 `SkiaSharp` 0.4 MB，插件本体与其余若干 KB 级程序集约 0.4 MB。

正确性已由无头冒烟（渲染产物 JPEG 有效）与宿主式加载模拟（隔离 ALC 加载解压后的包，面板窗口正常创建并渲染，基类解析自宿主的 Avalonia 12.0.4）双重验证。

## 三、安装

Mac Explorer ≥ 1.0.45 → 设置 → 插件 → 安装插件… → 选择 `artifacts/Watermark.MacExplorer.Plugin-1.0.5.mexplug`。
安装后插件位于 `~/Library/Application Support/MacExplorer/Plugins/com.thankful.litograph/`，运行日志见同目录 `.logs/`。

右键选中 1–100 张 JPG/PNG/WebP 照片 → 「轻影水印」→「下载水印模板…」或「生成水印照片…」，两者打开同一个面板，仅初始焦点不同。

## 三之一、批量处理

`plugin.json` 中两个命令都声明了 `maxSelection: 100`，宿主一次调用会把选中的照片全部传入 `invocation.Files`，插件逐个渲染后一次性返回全部产物：

- 每个 `PluginOutput` 都带 `SourcePath` 指回自己的源照片。宿主 1.0.45 要求多文件调用**必须**这样做，否则整次调用被判为无效输出；宿主按它把每张新图写回各自源照片所在目录，因此跨目录多选也不会错位。
- 每张照片写入独立工作子目录（`<工作目录>/<序号>/`），同名产物互不覆盖；建议名仍是各自独立的 `原文件名_模板名.jpg`。
- 批量时进度上报带 `ShowInTaskPanel` 与 `TaskTitle`，宿主后台任务面板会出现「轻影水印 · 生成 N 张照片」并可随时取消；单张仍只显示状态栏文字，不占用任务面板。
- 上限取 100 是因为宿主一次最多接收 100 个输出，而本插件每张照片恰好产出一个。

## 四、无头冒烟

不启动宿主即可验证环境、账号、模板库、市场与渲染：

```bash
dotnet run --project tools/Smoke/Smoke.csproj -c Release -- \
  [--photo <照片路径>] [--template <本地模板ID>] [--download <市场模板ID>] [--keep]
```

检查项：数据目录、已下载模板、`check-access` 状态、`prepare` 对异常输入的拒绝（含 101 张的超限拒绝）、市场分页/搜索/封面缓存、渲染产物的 JPEG 有效性、尺寸与 EXIF 保留、输出文件名是否符合宿主的命名规则，以及批量渲染的 `SourcePath`、独立工作子目录、进度中的 `ShowInTaskPanel`/`TaskTitle`。
`--download` 会真实下载一个市场模板（写入 `~/Documents/DFM/Templates/`），`--keep` 保留产物目录便于肉眼比对。

## 五、行为约束

| 约束 | 说明 |
|---|---|
| 执行超时 | `execute` 限时 `120 + 30 × (文件数 − 1)` 秒（进度上报不重置计时），面板停留也计入其中；面板底部按实际张数显示限时，超时会被宿主终止。 |
| 试用与会员 | 未登录 → 先登录；登录后由宿主记录 7 天试用；试用到期且非会员 → 只能开通会员。试用计时不重置。 |
| 输出位置 | 每个产物按 `SourcePath` 写回各自源照片所在目录，名称 `原文件名_模板名.jpg`，重名由宿主自动追加序号。 |
| 取消 | 关闭面板 = 取消，宿主显示「已取消处理」且不落盘任何文件。 |
| 数据目录 | 与轻影各端一致，为 `~/Documents/DFM/`（模板、字体、缓存、登录态）。 |

## 六、与宿主的一级菜单差异

宿主当前版本（1.0.45）只为插件的一级菜单项挂子菜单、不挂点击执行，且 Avalonia 对含子菜单的项不会触发 `Click`。因此“点击一级菜单直接弹出面板”无法在不改动宿主的前提下实现，本插件采用**二级菜单两项 + 同一面板**。
若宿主后续支持一级项直点（`PluginManifest.PanelCommand` 之类的清单字段），插件侧只需在 `plugin.json` 增加该字段，命令与面板代码无需改动。
