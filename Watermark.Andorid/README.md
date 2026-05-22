# 轻影 Litograph

基于 .NET 8 MAUI Blazor Hybrid 的多平台水印模板编辑工具，支持 macOS (Mac Catalyst)、iOS、Android、Windows。

## 环境要求

- .NET 8 SDK
- Xcode（含 Command Line Tools）— macOS/iOS 构建必需
- macOS 14.0+（用于 Mac Catalyst 构建）
- Android SDK — Android 构建必需（可通过 Visual Studio 或 Android Studio 安装）

## 打包命令

### 一、macOS (Mac Catalyst) 本地开发/测试

直接在 macOS 上运行，**不需要 Apple 开发者账号**。

```bash
# Debug 构建（仅 arm64）
dotnet build Watermark.Andorid.csproj -f net8.0-maccatalyst -c Debug -r maccatalyst-arm64

# 产物:
# bin/Debug/net8.0-maccatalyst/maccatalyst-arm64/Litograph.app

# 直接运行
open bin/Debug/net8.0-maccatalyst/maccatalyst-arm64/Litograph.app
```

```bash
# Release 构建（x64 + arm64 双架构）
dotnet publish Watermark.Andorid.csproj \
  -f net8.0-maccatalyst \
  -c Release

# 产物:
# .app:  bin/Release/net8.0-maccatalyst/osx-arm64/Litograph.app
# .app:  bin/Release/net8.0-maccatalyst/maccatalyst-x64/Litograph.app
# .pkg:  bin/Release/net8.0-maccatalyst/osx-arm64/publish/Litograph-{version}.pkg
```

> **本地运行被 Gatekeeper 拦截？** 运行 `xattr -cr Litograph.app && open Litograph.app` 即可。

### 二、macOS 本地直接分发（签名 + 公证 + DMG）

分发给其他用户安装，需要 Apple 开发者账号（$99/年）。签名后 Gatekeeper 不会弹出"无法验证"警告。

#### 前置条件

需要以下证书（通过 Xcode → Settings → Accounts 管理，或在 [Apple Developer Portal](https://developer.apple.com/account/) 创建）：

- **Developer ID Application** — 签名 .app 和 .dmg
- **Developer ID Installer** — 签名 .pkg（可选，当前使用 DMG 分发无需此证书）

本项目的 Team ID：`AXBYDPHN88`

#### 步骤

```bash
# 1. Publish 构建
dotnet publish Watermark.Andorid.csproj \
  -f net8.0-maccatalyst \
  -c Release

# 2. 对 .app 签名，启用 Hardened Runtime（公证必需）
codesign --force --deep --options runtime --timestamp \
  --sign "Developer ID Application: XinJi Jiang (AXBYDPHN88)" \
  bin/Release/net8.0-maccatalyst/osx-arm64/Litograph.app

# 3. 验证签名
codesign -dvv bin/Release/net8.0-maccatalyst/osx-arm64/Litograph.app

# 4. 创建 DMG
hdiutil create -size 200m -fs HFS+ -volname "轻影" -ov temp.rw.dmg
hdiutil attach temp.rw.dmg -nobrowse -quiet
cp -R bin/Release/net8.0-maccatalyst/osx-arm64/Litograph.app /Volumes/轻影/
hdiutil detach /Volumes/轻影 -quiet
hdiutil convert temp.rw.dmg -format UDZO -o Litograph-{version}.dmg
rm -f temp.rw.dmg

# 5. 对 DMG 签名
codesign --force --sign "Developer ID Application: XinJi Jiang (AXBYDPHN88)" \
  --timestamp Litograph-{version}.dmg

# 6. 提交公证（需要 Apple ID + App 专用密码）
xcrun notarytool submit Litograph-{version}.dmg \
  --apple-id "your-apple-id@example.com" \
  --password "app-specific-password" \
  --team-id "AXBYDPHN88" \
  --wait

# 7. 公证成功则装订票据到 DMG
xcrun stapler staple Litograph-{version}.dmg
```

> **提示**：首次使用 `notarytool` 前，建议用 `notarytool store-credentials` 存储凭据，之后用 `--keychain-profile` 引用即可，避免每次输入密码。

### 三、App Store 发布

需要 Apple Developer 账号、分发证书和 Provisioning Profile。

#### 前置准备

1. 在 [Apple Developer Portal](https://developer.apple.com/account/) 创建 App ID：`com.top.thankful.watermark.andorid`
2. 创建 Mac Catalyst 分发证书（Apple Distribution）和 Provisioning Profile
3. 在 [App Store Connect](https://appstoreconnect.apple.com/) 创建应用记录
4. 确保 `Platforms/MacCatalyst/Entitlements.plist` 中 `app-sandbox` 为 `true`（上架强制要求）
5. 确保已签署所有有效的法律协议（"协议、税务和银行业务"）

#### 打包上传命令

```bash
dotnet publish Watermark.Andorid.csproj \
  -f net8.0-maccatalyst \
  -c Release \
  -r maccatalyst-arm64 \
  -p:CreatePackage=true \
  -p:CodesignKey="Apple Distribution: XinJi Jiang (AXBYDPHN88)" \
  -p:CodesignProvision="Your_Provisioning_Profile_Name" \
  -p:PackageSigningKey="3rd Party Mac Developer Installer: XinJi Jiang (AXBYDPHN88)"

# 上传到 App Store Connect
xcrun altool --upload-app \
  -f bin/Release/net8.0-maccatalyst/maccatalyst-arm64/publish/Litograph-{version}.pkg \
  -t macos \
  -u "your-apple-id@example.com" \
  -p "app-specific-password"
```

### 四、Android 构建

```bash
# Debug 构建
dotnet build Watermark.Andorid.csproj -f net8.0-android -c Debug

# Release 构建（APK）
dotnet publish Watermark.Andorid.csproj -f net8.0-android -c Release

# 产物:
# bin/Release/net8.0-android/publish/com.top.thankful.watermark.andorid.apk
```

> Android Release 当前未配置签名（`AndroidKeyStore=False`），发布到应用商店前需配置签名密钥。

### 五、iOS 构建

```bash
dotnet build Watermark.Andorid.csproj -f net8.0-ios -c Debug
dotnet publish Watermark.Andorid.csproj -f net8.0-ios -c Release
```

### 六、Windows 构建

仅在 Windows 系统上可用：

```powershell
dotnet build Watermark.Andorid.csproj -f net8.0-windows10.0.19041.0 -c Debug
dotnet publish Watermark.Andorid.csproj -f net8.0-windows10.0.19041.0 -c Release
```

---

## macOS Release 配置说明

`Release|net8.0-maccatalyst` 的 PropertyGroup（[Watermark.Andorid.csproj](Watermark.Andorid.csproj#L87)）：

| 配置项 | 值 | 说明 |
|--------|-----|------|
| `PublishTrimmed` | `true` | .NET 8 Mac Catalyst SDK 强制要求，不可改为 false |
| `MtouchLink` | `None` | 禁用裁剪（避免 Masa.Blazor 代码被误删） |
| `UseInterpreter` | `true` | 启用 Mono 解释器，解决 Masa.Blazor 与 AOT 不兼容 |
| `SelfContained` | `true` | 自包含发布，无需用户安装 .NET 运行时 |
| `RuntimeIdentifiers` | `maccatalyst-x64;maccatalyst-arm64` | 同时生成 x64 和 arm64 双架构 |
| `EnableAppSandbox` | `true` | 启用 App Sandbox（发布 App Store 必需） |

---

## 常见问题

### Q: 为什么需要 `UseInterpreter=true`？

.NET 8 的 Mac Catalyst SDK 强制要求 `PublishTrimmed=true`，这会触发完全 AOT 编译。Masa.Blazor 组件库使用了运行时动态生成代码的模式（lambda 闭包、反射），这些代码无法被 AOT 预编译，导致：

> Attempting to JIT compile method ... while running in aot-only mode

`UseInterpreter=true` 让代码走解释器执行而非 AOT，规避此问题。性能会有一定损失，但这是 .NET 8 下的唯一可行方案。

**彻底修复需要升级到 .NET 10**（.NET 10 不再强制 `PublishTrimmed=true`，可以像 FKFinder 一样关闭 AOT）。

### Q: 为什么本地运行时弹出"Apple 无法验证是否包含恶意软件"？

原因：「App Sandbox 开启 + 无有效签名」。本项目 `Entitlements.plist` 中 `app-sandbox` 为 `true`，macOS 必须验证沙箱授权。ad-hoc 本地签名没有签发沙箱权限的资格，所以 Gatekeeper 拒绝启动。

解决方法：
- **本地测试**：`xattr -cr Litograph.app && open Litograph.app`
- **分发给用户**：必须用 Developer ID 证书签名 + 公证（见"本地直接分发"章节）
- **临时绕过**：将 `app-sandbox` 改为 `false`（发布 App Store 前需改回）

### Q: 为什么 FKFinder 本地运行没有 Gatekeeper 提示？

FKFinder 是文件管理器，需要全盘访问，`app-sandbox` 设为 `false`。沙箱关闭时无敏感授权需要验证，ad-hoc 签名即可通过 Gatekeeper。

### Q: 公证失败 "A required agreement is missing or has expired"

登录 [App Store Connect](https://appstoreconnect.apple.com/) → "协议、税务和银行业务"，签署/续签待处理的协议（通常是 Paid Applications Agreement）。

### Q: 公证失败 "The executable does not have the hardened runtime enabled"

签名时必须加 `--options runtime` 参数启用 Hardened Runtime：
```bash
codesign --force --deep --options runtime --timestamp --sign "Developer ID Application: ..."  Litograph.app
```

### Q: 公证失败 "The binary is not signed with a valid Developer ID certificate"

检查签名证书类型。直接分发应使用 **Developer ID Application** 证书（而非 Apple Distribution 证书）。同时确保 .pkg 的签名证书是 **Developer ID Installer**（而非 3rd Party Mac Developer Installer）。

### Q: `stapler staple` 失败 Error 65

可能是公证票据尚未同步，等待 10-30 秒重试。如果持续失败，检查 DMG 本身是否签了名（DMG 必须先签名再公证，顺序不能错）。

---

## 常用开发命令

```bash
# 还原依赖
dotnet restore Watermark.Andorid.csproj

# Debug 构建（macOS）
dotnet build Watermark.Andorid.csproj -f net8.0-maccatalyst -c Debug

# 清理构建产物
dotnet clean Watermark.Andorid.csproj

# 深度清理（含 obj/bin）
rm -rf bin obj

# 移除 Gatekeeper 隔离属性
xattr -cr bin/Debug/net8.0-maccatalyst/maccatalyst-arm64/Litograph.app
open bin/Debug/net8.0-maccatalyst/maccatalyst-arm64/Litograph.app
```

## 项目结构

```
Watermark.Andorid/
├── Platforms/
│   ├── MacCatalyst/      # macOS 平台入口、Entitlements、Info.plist
│   ├── Android/          # Android 平台入口、Manifest
│   ├── iOS/              # iOS 平台入口
│   └── Windows/          # Windows 平台入口
├── Models/               # 平台特定数据模型
├── Resources/            # 资源文件（图标、启动屏、字体、图片）
│   ├── AppIcon/
│   ├── Splash/
│   ├── Fonts/
│   ├── Images/
│   └── Raw/
├── wwwroot/              # Blazor Web 静态资源
├── App.xaml              # MAUI 应用入口
├── MauiProgram.cs        # MAUI 服务注册
├── MainPage.xaml         # BlazorWebView 宿主页
├── Routes.razor          # Blazor 路由
└── Watermark.Andorid.csproj
```

## 环境变量 (GitHub Actions / CI)

```yaml
# macOS 构建
- name: Build macOS
  run: |
    dotnet publish Watermark.Andorid.csproj \
      -f net8.0-maccatalyst \
      -c Release

# Android 构建
- name: Build Android
  run: |
    dotnet publish Watermark.Andorid.csproj \
      -f net8.0-android \
      -c Release
```
