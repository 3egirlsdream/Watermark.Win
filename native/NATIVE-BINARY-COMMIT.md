# 原生图像依赖提交说明

## 提交目标

本提交让应用构建直接使用已经生成并验证的原生二进制，不在 `dotnet build`
或发布过程中下载、解压或编译 LibRaw。LibRaw 0.22.1 的实现源码不进入仓库；
仓库只保留官方来源、版本、归档 SHA-256、CDDL 许可和版权清单。

应用构建会根据目标平台选择下列预编译产物：

| 平台 | 架构 | 产物 |
| --- | --- | --- |
| Mac Catalyst | arm64、x86_64 | `artifacts/maccatalyst/Watermark.Imaging.Native.xcframework` |
| iOS 设备 | arm64 | `artifacts/ios/Watermark.Imaging.Native.xcframework` |
| iOS 模拟器 | arm64、x86_64 | `artifacts/ios/Watermark.Imaging.Native.xcframework` |
| Android | arm64-v8a | `artifacts/android/arm64-v8a/libWatermark.Imaging.Native.so` |
| Android | x86_64 | `artifacts/android/x86_64/libWatermark.Imaging.Native.so` |
| Windows | x64 | `artifacts/win-x64/Watermark.Imaging.Native.dll` |
| Windows | ARM64 | `artifacts/win-arm64/Watermark.Imaging.Native.dll` |

`Watermark.Andorid.csproj` 通过 `NativeReference` 和 `AndroidNativeLibrary`
按目标框架打包 Apple/Android 产物；`Watermark.Win.csproj` 根据 Windows RID
复制对应 DLL。普通应用构建不会调用 `native/scripts` 下的原生重编脚本。

## 源码与依赖策略

### LibRaw 0.22.1

- 官方来源：<https://www.libraw.org/data/LibRaw-0.22.1.tar.gz>
- 官方归档 SHA-256：
  `a789dc4e2409e2901d93793a4e0b80c7b49d0d97cf6ad71c850eb7616acfd786`
- 选用许可：CDDL 1.0
- 仓库包含：`LICENSE.CDDL`、`COPYRIGHT`、归档校验文件和预编译产物
- 仓库不包含：`native/third_party/LibRaw-0.22.1` 下的 LibRaw 实现源码

`.gitignore` 明确忽略该源码目录，只允许提交 CDDL 许可和版权清单。原生重编
脚本不会自动下载 LibRaw；维护者要重新生成二进制时，需要自行从上述官方地址
取得对应归档并验证 SHA-256，然后解压到
`native/third_party/LibRaw-0.22.1`。该目录继续保持未跟踪状态。

### LibTIFF 4.7.2

- 官方来源：<https://download.osgeo.org/libtiff/tiff-4.7.2.tar.xz>
- 官方归档 SHA-256：
  `4996f0c4f93094719b1ca5c6279b20e588773ba8a247533e486416fb662ddb88`
- 完整源码随项目保存在 `native/third_party/tiff-4.7.2`
- 构建为静态库，启用 Deflate；关闭工具、测试、文档和可选图像编解码器

### zlib 与 C++ 运行时

- Apple 和 Android 使用系统 zlib。
- Windows 构建脚本使用 vcpkg 的 `x64-windows-static` 与
  `arm64-windows-static` zlib 1.3.2，并将其静态链接进最终 DLL。
- Windows 三个原生目标使用静态 MSVC 运行时 `/MT`。
- Android C++ 运行时使用 `-static-libstdc++`，最终 `.so` 不依赖
  `libc++_shared.so`。

## Manifest 与产物校验

`artifacts/manifest.json` 使用 schema v2，记录原生 ABI 版本、依赖配置、平台、
架构、相对路径和二进制 SHA-256。当前产物如下：

| 产物 | SHA-256 |
| --- | --- |
| Mac Catalyst XCFramework 静态库 | `94182e7497e8cc67b0ecf5e1b62f301babafeb43bb0b56c714ecf430d6ab82b8` |
| iOS 设备静态库 | `ad99d7687163bce3d0c808357489f3783ea3f02b049e17968e507dca017a0d68` |
| iOS 模拟器静态库 | `20e941a8fc20f426c1b68b191ed24bfd583011000813e838194533f56be3b70f` |
| Android arm64-v8a `.so` | `792eca5b8f0cb70fead0a9c240faaadc6e83f76cd422e70436d02e65cdb45698` |
| Android x86_64 `.so` | `b911a89ffc07adcf6a10507ff45852df67f12a5182af9ce5e6e089ebf308d0a5` |
| Windows x64 DLL | `05ed28cba4e912c03dffe313b56da7db4335c7c7f9fe5fcf7ca109215b4e13db` |
| Windows ARM64 DLL | `edad3a1200ee8736d01762324053d718d620e99e3f6440c853a6dda259fd3021` |

Manifest 可在 macOS 上通过以下命令从当前 artifacts 重新计算：

```sh
native/scripts/update-native-manifest.sh
```

该命令只读取现有二进制并计算哈希，不下载源码。

## 已执行验证

- Windows x64/ARM64 PE 架构与 15 个公开 `wmi_*` ABI 导出已检查。
- Windows DLL 只导入 Windows 系统库，不需要额外部署 LibRaw、LibTIFF、zlib
  或 Visual C++ Runtime DLL。
- Windows x64 可执行原生测试通过；ARM64 产物在 x64 虚拟机完成交叉构建、
  PE/导出/依赖静态检查。
- Android 两个 ELF 架构正确，公开 ABI 一致，只依赖 Android 系统的
  `libz.so`、`libm.so`、`libdl.so` 和 `libc.so`。
- `net8.0-android` Debug APK 构建为 0 错误，最终 APK 同时包含 arm64-v8a
  和 x86_64 原生库，包内哈希与 artifacts 一致。
- Mac Catalyst、iOS、Android 和 Windows 全部 7 个 manifest 哈希已重新计算并
  与文件逐项比对。

Android 验证没有替代真机测试；正式发布仍需使用 Release 配置、正式签名证书，
并在目标 Android 设备上完成启动与 RAW 解码冒烟测试。

## 许可文件

应用发布包会包含：

- `THIRD-PARTY-NOTICES`
- `licenses/LibRaw-CDDL-1.0.txt`
- `licenses/LibRaw-COPYRIGHT.txt`
- `licenses/LibTIFF-4.7.2.txt`

LibRaw 对应源码通过第三方声明中的官方地址、固定版本和 SHA-256 精确标识，不随
本次二进制提交进入仓库。
