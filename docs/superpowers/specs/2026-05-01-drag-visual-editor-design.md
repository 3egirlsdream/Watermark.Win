# 拖拽可视化模板编辑器 设计文档

**目标：** 将模板编辑器的配置方式从纯表单操作改为 Canva/Figma 风格的可视化拖拽，在预览图上直接操控水印控件（文字、Logo、线、容器）。

**方案：** 基于 Moveable.js（~100KB）实现拖拽/缩放/旋转/吸附，通过 JSInterop 桥接 Blazor C# 模型层，所有 JS 调用封装为 C# 服务类 `MoveableService`。

---

## 1. 坐标体系

- **模型坐标（SkiaSharp 画布）**：以 `WMCanvas.CustomWidth × CustomHeight` 为基准，控件位置由 `IWMControl.Margin.Left/Top` 和 `IWMControl.Width/Height` 定义，单位像素。
- **显示坐标（浏览器预览区）**：预览图以 `object-fit: contain` 显示在容器中，控件 overlay 的 CSS 像素坐标 = 模型坐标 × 缩放比。
- **缩放比** = `min(容器宽/画布宽, 容器高/画布高)`，由 JS 侧 `getBoundingClientRect()` 获取容器尺寸后计算。
- **角度**：CSS `transform: rotate(deg)` 与 SkiaSharp 角度一致，不需要换算。

换算逻辑封装在 `MoveableService` 内部，Razor 组件只接触模型坐标。

## 2. 组件结构

### 2.1 新增组件

| 文件 | 职责 |
|---|---|
| `Services/MoveableService.cs` | 注入 `IJSRuntime`，暴露 `CreateAsync` / `UpdateAsync` / `DestroyAsync` / `SelectAsync`，内部做坐标换算 |
| `Services/MoveableOptions.cs` | 配置类：位置、尺寸、是否可旋转、回调委托 |
| `Components/DesignCanvas.razor` | 替代 Design.razor 左侧的 `<MImage>`，包含预览图 + 控件 overlay 容器 |
| `Components/ControlOverlay.razor` | 单个控件的 overlay 渲染，按控件类型（文字/Logo/线/容器）显示不同内容 |
| `wwwroot/js/moveable-interop.js` | Moveable.js 初始化/销毁/更新/选中，DOM 事件回调 C# |

### 2.2 改动组件

| 文件 | 改动 |
|---|---|
| `Components/Design.razor` | 左侧预览区替换为 `<DesignCanvas>`，新增 `SelectedControl` 状态 |
| `Components/DesignConfiguration.razor` | 新增 `SelectedControl` 参数，大纲选中联动预览区高亮，属性修改实时反映 |
| `Components/EditComponentDialog.razor` | 大纲树点击选中联动 `SelectedControl` |
| `wwwroot/index.html` | 引入 Moveable.js CDN/本地脚本 |

## 3. JSInterop 接口

### 3.1 C# → JS（通过 MoveableService 封装）

| 方法 | 参数 | 说明 |
|---|---|---|
| `moveableInterop.create` | `dotnetRef, controlId, bounds, options` | 创建控件 overlay + Moveable 实例 |
| `moveableInterop.update` | `controlId, bounds` | 更新 overlay 位置/尺寸 |
| `moveableInterop.destroy` | `controlId` | 删除 overlay + Moveable 实例 |
| `moveableInterop.select` | `controlId` | 选中并高亮 overlay |

### 3.2 JS → C#（通过 DotNetObjectReference 回调）

| 回调方法 | 参数 | 触发时机 |
|---|---|---|
| `OnDragEnd` | `controlId, left, top` | 拖拽结束 |
| `OnResizeEnd` | `controlId, width, height` | 缩放结束 |
| `OnRotateEnd` | `controlId, angle` | 旋转结束 |
| `OnSelect` | `controlId` | 点击 overlay 选中 |

**性能策略：** Moveable 的 `onDrag`/`onResize`/`onRotate` 事件全程更新 DOM（无跨进程调用），仅 `End` 事件回调 C# 写入模型，避免频繁 JSInterop 通信。JS 侧用 `requestAnimationFrame` 节流。

## 4. 控件 Overlay 渲染规则

| 控件类型 | Overlay 渲染方式 | Moveable 选项 |
|---|---|---|
| WMText | 显示文字内容，字体/颜色与模型一致 | draggable, resizable, rotatable |
| WMLogo | 显示 logo 缩略图 | draggable, resizable, rotatable |
| WMLine | 渲染为水平细线 | draggable, resizable, rotatable |
| WMContainer | 半透明虚线边框 + 名称标签 | draggable, resizable, rotatable |

## 5. 选中态与面板联动

```
用户点击 overlay → Moveable.OnSelect → C# SelectedControl = IWMControl
  → StateHasChanged()
  → DesignCanvas 显示 Moveable 句柄（蓝色边框+缩放点+旋转手柄）
  → DesignConfiguration 展示该控件属性编辑器

用户在大纲树点击控件 → SelectedControl = IWMControl
  → JS moveableInterop.select(controlId)
  → DesignCanvas 对应 overlay 高亮
```

## 6. 吸附功能（Moveable 内置）

- 元素边缘吸附到其他元素边缘或画布边界
- 拖拽时显示辅助线（对齐线）
- 吸附阈值 5px，可在 Moveable options 中配置

## 7. 不涉及的部分

- 不修改 SkiaSharp 渲染管线
- 不修改 WMCanvas / IWMControl 数据模型
- 不修改保存/加载逻辑
- 不修改移动端（Android/iOS）布局，仅桌面端引入拖拽编辑
