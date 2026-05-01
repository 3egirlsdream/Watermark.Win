# 拖拽可视化模板编辑器 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将模板编辑器预览区从静态图片改为 Canva 风格的可视化拖拽编辑，控件 overlay 可拖拽/缩放/旋转。

**Architecture:** 通过 Moveable.js 在预览图上叠加可交互 HTML 元素，JSInterop 桥接 Blazor C# 模型层。MoveableService 封装所有 JS 调用，Razor 组件只接触 C# 接口。拖拽过程纯 JS 操控 DOM，仅在结束时回写模型并触发 SkiaSharp 重新渲染。

**Tech Stack:** Moveable.js (CDN), Blazor JSInterop, C# DotNetObjectReference, SkiaSharp

---

## 文件结构

| 文件 | 职责 |
|---|---|
| `Watermark.Andorid/wwwroot/index.html` | 引入 Moveable.js CDN |
| `Watermark.Andorid/wwwroot/js/moveable-interop.js` | JS 侧：Moveable 实例管理 + C# 回调 |
| `Watermark.Razor/Services/MoveableOptions.cs` | 配置类：位置、尺寸、旋转、回调 |
| `Watermark.Razor/Services/MoveableService.cs` | 封装 JSInterop，暴露纯 C# API |
| `Watermark.Razor/Components/DesignCanvas.razor` | 交互式画布：预览图 + overlay 容器 |
| `Watermark.Razor/Components/Design.razor` | 改用 DesignCanvas，管理 SelectedControl |
| `Watermark.Razor/Components/EditComponentDialog.razor` | 大纲树选中 → 预览区高亮联动 |

---

### Task 1: 引入 Moveable.js CDN

**Files:**
- Modify: `Watermark.Andorid/wwwroot/index.html`

- [ ] **Step 1: 在 index.html 中引入 Moveable.js**

在 `<script src="_framework/blazor.webview.js" autostart="false"></script>` 之前加入：

```html
<script src="https://daybrush.com/moveable/release/latest/dist/moveable.min.js"></script>
```

- [ ] **Step 2: 验证编译**

```bash
dotnet build Watermark.sln
```

- [ ] **Step 3: Commit**

```bash
git add Watermark.Andorid/wwwroot/index.html
git commit -m "feat: 引入 Moveable.js CDN

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 2: 创建 moveable-interop.js JS 桥接层

**Files:**
- Create: `Watermark.Andorid/wwwroot/js/moveable-interop.js`
- Modify: `Watermark.Andorid/wwwroot/index.html`

- [ ] **Step 1: 编写 moveable-interop.js**

```javascript
// moveable-interop.js — Moveable ↔ Blazor JSInterop 桥接
(function () {
  'use strict';
  var moveables = {};
  var dotnetRefs = {};
  var scale = 1;
  var canvasWidth = 6000;
  var canvasHeight = 4000;
  var overlayContainer = null;

  function m2d(modelVal) { return modelVal * scale; }
  function d2m(dispVal) { return Math.round(dispVal / scale); }

  window.MoveableInterop = {
    init: function (containerId, cw, ch) {
      overlayContainer = document.getElementById(containerId);
      canvasWidth = cw;
      canvasHeight = ch;
      this.recalcScale();
    },

    recalcScale: function () {
      if (!overlayContainer) return scale;
      var w = overlayContainer.clientWidth;
      var h = overlayContainer.clientHeight;
      scale = Math.min(w / canvasWidth, h / canvasHeight);
      return scale;
    },

    create: function (controlId, options, dotnetRef) {
      dotnetRefs[controlId] = dotnetRef;

      var el = document.createElement('div');
      el.id = 'moveable-overlay-' + controlId;
      el.className = 'moveable-overlay ' + (options.className || '');
      el.dataset.ctrlId = controlId;
      if (options.text) el.textContent = options.text;
      el.style.position = 'absolute';
      el.style.left = m2d(options.left) + 'px';
      el.style.top = m2d(options.top) + 'px';
      el.style.width = m2d(options.width) + 'px';
      el.style.height = m2d(options.height) + 'px';
      el.style.transform = 'rotate(' + (options.angle || 0) + 'deg)';
      el.style.boxSizing = 'border-box';
      el.style.pointerEvents = 'auto';
      el.style.cursor = 'move';
      el.style.userSelect = 'none';

      overlayContainer.appendChild(el);

      var moveable = new Moveable(document.body, {
        target: el,
        draggable: true,
        resizable: true,
        rotatable: true,
        snappable: true,
        bounds: { left: 0, top: 0, right: m2d(canvasWidth), bottom: m2d(canvasHeight) },
        snapContainer: overlayContainer,
        throttleDrag: 1,
        throttleResize: 1,
        throttleRotate: 1,
        keepRatio: false,
        snapThreshold: 8,
        snapRenderThreshold: 15,
        isDisplaySnapDigit: true,
        snapGap: true,
        snapElement: true,
        snapVertical: true,
        snapHorizontal: true,
        snapCenter: true,
        snapDigit: 0,
        maxSnapElementGuidelineDistance: 50,
        maxSnapElementGapDistance: 50,
        verticalGuidelines: [0, m2d(canvasWidth), m2d(canvasWidth) / 2],
        horizontalGuidelines: [0, m2d(canvasHeight), m2d(canvasHeight) / 2],
        elementGuidelines: getOtherOverlays(controlId),
      });

      moveable.on('drag', function (_a) {
        _a.target.style.left = _a.left + 'px';
        _a.target.style.top = _a.top + 'px';
      });

      moveable.on('dragEnd', function (_a) {
        if (_a.lastEvent) {
          dotnetRef.invokeMethodAsync('OnDragEnd',
            d2m(_a.lastEvent.left), d2m(_a.lastEvent.top));
        }
      });

      moveable.on('resize', function (_a) {
        _a.target.style.width = _a.width + 'px';
        _a.target.style.height = _a.height + 'px';
        _a.target.style.left = _a.drag.left + 'px';
        _a.target.style.top = _a.drag.top + 'px';
      });

      moveable.on('resizeEnd', function (_a) {
        if (_a.lastEvent) {
          dotnetRef.invokeMethodAsync('OnResizeEnd',
            d2m(_a.lastEvent.width), d2m(_a.lastEvent.height),
            d2m(_a.lastEvent.drag.left), d2m(_a.lastEvent.drag.top));
        }
      });

      moveable.on('rotate', function (_a) {
        _a.target.style.transform = _a.transform;
      });

      moveable.on('rotateEnd', function (_a) {
        if (_a.lastEvent) {
          dotnetRef.invokeMethodAsync('OnRotateEnd', _a.lastEvent.rotate);
        }
      });

      moveable.on('click', function (_a) {
        dotnetRef.invokeMethodAsync('OnSelect');
      });

      moveables[controlId] = moveable;
      updateAllElementGuidelines();
    },

    update: function (controlId, left, top, width, height, angle) {
      var el = document.getElementById('moveable-overlay-' + controlId);
      if (!el) return;
      el.style.left = m2d(left) + 'px';
      el.style.top = m2d(top) + 'px';
      el.style.width = m2d(width) + 'px';
      el.style.height = m2d(height) + 'px';
      el.style.transform = 'rotate(' + angle + 'deg)';
      var m = moveables[controlId];
      if (m) m.updateTarget();
    },

    updateText: function (controlId, text) {
      var el = document.getElementById('moveable-overlay-' + controlId);
      if (el) el.textContent = text;
    },

    setClassName: function (controlId, className) {
      var el = document.getElementById('moveable-overlay-' + controlId);
      if (el) el.className = 'moveable-overlay ' + className;
    },

    select: function (controlId) {
      Object.keys(moveables).forEach(function (cid) {
        var el = document.getElementById('moveable-overlay-' + cid);
        if (el) el.classList.remove('moveable-selected');
      });
      var el = document.getElementById('moveable-overlay-' + controlId);
      if (el) el.classList.add('moveable-selected');
    },

    destroy: function (controlId) {
      if (moveables[controlId]) {
        moveables[controlId].destroy();
        delete moveables[controlId];
      }
      delete dotnetRefs[controlId];
      var el = document.getElementById('moveable-overlay-' + controlId);
      if (el) el.remove();
      updateAllElementGuidelines();
    },
  };

  function getOtherOverlays(excludeId) {
    return Object.keys(moveables)
      .filter(function (cid) { return cid !== excludeId; })
      .map(function (cid) { return document.getElementById('moveable-overlay-' + cid); })
      .filter(function (el) { return el; });
  }

  function updateAllElementGuidelines() {
    Object.keys(moveables).forEach(function (cid) {
      if (moveables[cid]) {
        moveables[cid].elementGuidelines = getOtherOverlays(cid);
        moveables[cid].updateTarget();
      }
    });
  }
})();
```

- [ ] **Step 2: 在 index.html 中引入 moveable-interop.js**

在 `moveable.min.js` 之后、`_framework/blazor.webview.js` 之前加入：

```html
<script src="js/moveable-interop.js"></script>
```

- [ ] **Step 3: 验证编译**

```bash
dotnet build Watermark.sln
```

- [ ] **Step 4: Commit**

```bash
git add Watermark.Andorid/wwwroot/js/moveable-interop.js Watermark.Andorid/wwwroot/index.html
git commit -m "feat: 添加 moveable-interop.js JS 桥接层

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 3: 创建 MoveableOptions.cs 配置类

**Files:**
- Create: `Watermark.Razor/Services/MoveableOptions.cs`

- [ ] **Step 1: 编写配置类**

```csharp
namespace Watermark.Razor.Services;

public class MoveableOptions
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Angle { get; set; }
    public string ClassName { get; set; } = "";
    public string Text { get; set; } = "";
}
```

- [ ] **Step 2: 验证编译**

```bash
dotnet build Watermark.Razor/Watermark.Razor.csproj
```

- [ ] **Step 3: Commit**

```bash
git add Watermark.Razor/Services/MoveableOptions.cs
git commit -m "feat: 添加 MoveableOptions 配置类

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 4: 创建 MoveableService.cs C# 服务

**Files:**
- Create: `Watermark.Razor/Services/MoveableService.cs`
- Modify: `Watermark.Razor/_Imports.razor`
- Modify: `Watermark.Andorid/MauiProgram.cs`

- [ ] **Step 1: 编写服务类**

```csharp
using Microsoft.JSInterop;

namespace Watermark.Razor.Services;

public class MoveableControlHandle : IAsyncDisposable
{
    private readonly DotNetObjectReference<MoveableControlHandle> _dotNetRef;
    private readonly IJSRuntime _jsRuntime;
    private readonly string _controlId;

    public string ControlId => _controlId;

    public event Func<string, double, double, Task>? OnDragEnd;
    public event Func<string, double, double, double, double, Task>? OnResizeEnd;
    public event Func<string, double, Task>? OnRotateEnd;
    public event Func<string, Task>? OnSelect;

    public MoveableControlHandle(IJSRuntime jsRuntime, string controlId)
    {
        _jsRuntime = jsRuntime;
        _controlId = controlId;
        _dotNetRef = DotNetObjectReference.Create(this);
    }

    public async Task CreateAsync(MoveableOptions options)
    {
        await _jsRuntime.InvokeVoidAsync("MoveableInterop.create", _controlId, options, _dotNetRef);
    }

    public async Task UpdateAsync(double left, double top, double width, double height, double angle)
    {
        await _jsRuntime.InvokeVoidAsync("MoveableInterop.update", _controlId, left, top, width, height, angle);
    }

    public async Task UpdateTextAsync(string text)
    {
        await _jsRuntime.InvokeVoidAsync("MoveableInterop.updateText", _controlId, text);
    }

    public async Task SetClassNameAsync(string className)
    {
        await _jsRuntime.InvokeVoidAsync("MoveableInterop.setClassName", _controlId, className);
    }

    public async Task SelectAsync()
    {
        await _jsRuntime.InvokeVoidAsync("MoveableInterop.select", _controlId);
    }

    public async Task DestroyAsync()
    {
        await _jsRuntime.InvokeVoidAsync("MoveableInterop.destroy", _controlId);
    }

    [JSInvokable]
    public async Task OnDragEnd(double left, double top)
    {
        if (OnDragEnd != null) await OnDragEnd.Invoke(_controlId, left, top);
    }

    [JSInvokable]
    public async Task OnResizeEnd(double width, double height, double left, double top)
    {
        if (OnResizeEnd != null) await OnResizeEnd.Invoke(_controlId, width, height, left, top);
    }

    [JSInvokable]
    public async Task OnRotateEnd(double angle)
    {
        if (OnRotateEnd != null) await OnRotateEnd.Invoke(_controlId, angle);
    }

    [JSInvokable]
    public async Task OnSelect()
    {
        if (OnSelect != null) await OnSelect.Invoke(_controlId);
    }

    public async ValueTask DisposeAsync()
    {
        await DestroyAsync();
        _dotNetRef.Dispose();
    }
}

public class MoveableService
{
    private readonly IJSRuntime _jsRuntime;
    private bool _initialized;

    public MoveableService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task InitAsync(string containerId, double canvasWidth, double canvasHeight)
    {
        if (_initialized) return;
        _initialized = true;
        await _jsRuntime.InvokeVoidAsync("MoveableInterop.init", containerId, canvasWidth, canvasHeight);
    }

    public MoveableControlHandle CreateHandle(string controlId)
    {
        return new MoveableControlHandle(_jsRuntime, controlId);
    }

    public async Task SelectAsync(string controlId)
    {
        await _jsRuntime.InvokeVoidAsync("MoveableInterop.select", controlId);
    }

    public async Task RecalcScaleAsync()
    {
        await _jsRuntime.InvokeVoidAsync("MoveableInterop.recalcScale");
    }
}
```

- [ ] **Step 2: 注册 _Imports 和 DI**

在 `Watermark.Razor/_Imports.razor` 末尾添加：

```razor
@using Watermark.Razor.Services
```

在 `Watermark.Andorid/MauiProgram.cs` 的 `builder.Services.AddSingleton<APIHelper>();` 之后添加：

```csharp
builder.Services.AddSingleton<MoveableService>();
```

- [ ] **Step 3: 验证编译**

```bash
dotnet build Watermark.sln
```

- [ ] **Step 4: Commit**

```bash
git add Watermark.Razor/Services/MoveableService.cs Watermark.Razor/_Imports.razor Watermark.Andorid/MauiProgram.cs
git commit -m "feat: 添加 MoveableService 和 MoveableControlHandle C# 服务

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 5: 创建 DesignCanvas.razor 交互式画布组件

**Files:**
- Create: `Watermark.Razor/Components/DesignCanvas.razor`
- Create: `Watermark.Razor/Components/DesignCanvas.razor.css`

- [ ] **Step 1: 编写 DesignCanvas.razor**

```razor
@using Microsoft.JSInterop
@using Watermark.Razor.Services
@using Watermark.Shared.Models
@inject IJSRuntime JSRuntime
@inject MoveableService MoveableSvc

<div class="design-canvas-wrapper">
    <div class="design-canvas-inner" @ref="_canvasInnerRef">
        @if (!string.IsNullOrEmpty(PreviewUrl))
        {
            <img class="design-canvas-bg" src="@PreviewUrl" />
        }
        <div class="moveable-overlay-container" id="@_overlayContainerId"
             style="position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;">
        </div>
    </div>
</div>

@code {
    [Parameter] public string PreviewUrl { get; set; } = "";
    [Parameter] public WMCanvas? Canvas { get; set; }
    [Parameter] public EventCallback<string> OnControlSelected { get; set; }

    private ElementReference _canvasInnerRef;
    private string _overlayContainerId = "";
    private bool _initialized;
    private Dictionary<string, MoveableControlHandle> _handles = new();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && Canvas != null)
        {
            _overlayContainerId = "overlay-container-" + Guid.NewGuid().ToString("N")[..8];
            await MoveableSvc.InitAsync(_overlayContainerId, Canvas.CustomWidth, Canvas.CustomHeight);
            _initialized = true;
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_initialized && Canvas != null)
        {
            await SyncOverlaysFromCanvas();
        }
    }

    public async Task SyncOverlaysFromCanvas()
    {
        if (Canvas == null) return;

        var activeIds = new HashSet<string>();
        CollectControlIds(Canvas, activeIds);
        foreach (var id in _handles.Keys.ToList())
        {
            if (!activeIds.Contains(id))
            {
                await _handles[id].DisposeAsync();
                _handles.Remove(id);
            }
        }

        foreach (var container in Canvas.Children)
        {
            foreach (var ctrl in container.Controls)
            {
                await EnsureHandle(ctrl);
                if (ctrl is WMContainer wc)
                {
                    foreach (var child in wc.Controls)
                        await EnsureHandle(child);
                }
            }
        }
    }

    private async Task EnsureHandle(IWMControl ctrl)
    {
        if (!_handles.ContainsKey(ctrl.ID))
        {
            var handle = MoveableSvc.CreateHandle(ctrl.ID);
            handle.OnDragEnd += async (id, l, t) =>
            {
                ctrl.Margin.Left = l;
                ctrl.Margin.Top = t;
            };
            handle.OnResizeEnd += async (id, w, h, l, t) =>
            {
                ctrl.Width = w;
                ctrl.Height = h;
                ctrl.Margin.Left = l;
                ctrl.Margin.Top = t;
            };
            handle.OnRotateEnd += async (id, a) =>
            {
                if (ctrl is WMContainer wc2) wc2.Angle = (int)a;
            };
            handle.OnSelect += async (id) =>
            {
                await OnControlSelected.InvokeAsync(id);
            };

            _handles[ctrl.ID] = handle;
            var options = new MoveableOptions
            {
                Left = ctrl.Margin.Left,
                Top = ctrl.Margin.Top,
                Width = ctrl.Width,
                Height = ctrl.Height,
                Angle = ctrl is WMContainer wc3 ? wc3.Angle : 0,
                Text = ctrl.Name,
                ClassName = ctrl switch
                {
                    WMText => "moveable-ctrl-text",
                    WMLogo => "moveable-ctrl-logo",
                    WMLine => "moveable-ctrl-line",
                    WMContainer => "moveable-ctrl-container",
                    _ => ""
                }
            };
            await handle.CreateAsync(options);
        }
        else
        {
            var handle = _handles[ctrl.ID];
            await handle.UpdateAsync(
                ctrl.Margin.Left, ctrl.Margin.Top,
                ctrl.Width, ctrl.Height,
                ctrl is WMContainer wc4 ? wc4.Angle : 0);
        }
    }

    private void CollectControlIds(WMCanvas canvas, HashSet<string> ids)
    {
        foreach (var container in canvas.Children)
        {
            foreach (var ctrl in container.Controls)
            {
                ids.Add(ctrl.ID);
                if (ctrl is WMContainer wc)
                    foreach (var child in wc.Controls)
                        ids.Add(child.ID);
            }
        }
    }

    public async Task SelectControlAsync(string controlId)
    {
        await MoveableSvc.SelectAsync(controlId);
    }
}
```

- [ ] **Step 2: 编写 DesignCanvas.razor.css**

```css
.design-canvas-wrapper {
    height: 100%;
    width: 100%;
    display: flex;
    align-items: center;
    justify-content: center;
}

.design-canvas-inner {
    position: relative;
    overflow: hidden;
    box-shadow: 0 4px 24px rgba(0,0,0,0.15);
    max-width: 100%;
    max-height: 100%;
}

.design-canvas-bg {
    display: block;
    width: 100%;
    height: 100%;
    object-fit: contain;
}

::deep .moveable-ctrl-text {
    display: flex; align-items: center; justify-content: center;
    font-weight: 700; white-space: nowrap; user-select: none;
}

::deep .moveable-ctrl-logo {
    display: flex; align-items: center; justify-content: center;
    background: rgba(200,200,200,0.6); border: 1px dashed #999;
}

::deep .moveable-ctrl-line {
    background: #333; min-height: 2px;
}

::deep .moveable-ctrl-container {
    border: 1.5px dashed rgba(74,144,217,0.6);
    background: rgba(74,144,217,0.08);
}

::deep .moveable-overlay {
    position: absolute; box-sizing: border-box;
    pointer-events: auto; cursor: move; user-select: none;
}

::deep .moveable-selected {
    outline: 2px solid #4A90D9; outline-offset: -1px;
}
```

- [ ] **Step 3: 验证编译**

```bash
dotnet build Watermark.sln
```

- [ ] **Step 4: Commit**

```bash
git add Watermark.Razor/Components/DesignCanvas.razor Watermark.Razor/Components/DesignCanvas.razor.css
git commit -m "feat: 创建 DesignCanvas 交互式画布组件

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 6: 修改 Design.razor 集成 DesignCanvas

**Files:**
- Modify: `Watermark.Razor/Components/Design.razor`

- [ ] **Step 1: 替换左侧预览区域**

将 `<MImage Contain Src="@url" Style="height:100%;width:100%" />` 替换为 `<DesignCanvas>`：

```razor
<div style="width:calc(100% - 300px);height:calc(100% - 36px);background:#e3e3e3;padding:10px;">
    @if (!string.IsNullOrEmpty(url) && CurrentCanvas != null)
    {
        <DesignCanvas @ref="_designCanvas" PreviewUrl="@url" Canvas="CurrentCanvas"
                      OnControlSelected="OnControlSelected" />
    }
</div>
```

- [ ] **Step 2: 添加 @code 成员**

```csharp
private DesignCanvas? _designCanvas;
private IWMControl? SelectedControl { get; set; }

private async Task OnControlSelected(string controlId)
{
    SelectedControl = FindControl(controlId);
    await InvokeAsync(StateHasChanged);
}

private IWMControl? FindControl(string id)
{
    if (CurrentCanvas == null) return null;
    foreach (var container in CurrentCanvas.Children)
    {
        foreach (var ctrl in container.Controls)
        {
            if (ctrl.ID == id) return ctrl;
            if (ctrl is WMContainer wc)
                foreach (var child in wc.Controls)
                    if (child.ID == id) return child;
        }
    }
    return null;
}

public async Task SelectFromOutline(string controlId)
{
    SelectedControl = FindControl(controlId);
    if (_designCanvas != null)
        await _designCanvas.SelectControlAsync(controlId);
    await InvokeAsync(StateHasChanged);
}
```

- [ ] **Step 3: 属性变更后同步 overlay**

修改 `PropertyChanged` 方法：

```csharp
async void PropertyChanged(object sender, PropertyChangedEventArgs args)
{
    PreviewImageRefresh();
    _ = SyncOverlaysAfterChange();
}

private async Task SyncOverlaysAfterChange()
{
    if (_designCanvas != null)
        await _designCanvas.SyncOverlaysFromCanvas();
}
```

- [ ] **Step 4: 验证编译**

```bash
dotnet build Watermark.sln
```

- [ ] **Step 5: Commit**

```bash
git add Watermark.Razor/Components/Design.razor
git commit -m "feat: Design.razor 集成 DesignCanvas 交互式画布

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 7: 修改 EditComponentDialog.razor 大纲树选中联动

**Files:**
- Modify: `Watermark.Razor/Components/EditComponentDialog.razor`

- [ ] **Step 1: 添加选中回调参数**

在 `@code` 块中添加：

```csharp
[Parameter]
public EventCallback<string> OnControlSelected { get; set; }
```

- [ ] **Step 2: 控件名称点击选中**

所有层级的 `<span class="edit-subtitle-font">@comp.Name</span>` 改为：

```razor
<span class="edit-subtitle-font" style="cursor:pointer"
      @onclick="() => OnControlSelected.InvokeAsync(comp.ID)">
    @comp.Name
</span>
```

对于 Lv2 容器下的子控件和 Lv1 普通控件都做同样修改。

- [ ] **Step 3: 验证编译**

```bash
dotnet build Watermark.sln
```

- [ ] **Step 4: Commit**

```bash
git add Watermark.Razor/Components/EditComponentDialog.razor
git commit -m "feat: 大纲树控件点击联动预览区 overlay 高亮

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---

### Task 8: 端到端验证与修复

**Files:** 无新建，修复集成问题

- [ ] **Step 1: 构建整个解决方案**

```bash
dotnet build Watermark.sln
```

修复所有编译错误。

- [ ] **Step 2: 运行应用并手动测试**

1. 打开一个模板进入设计页面
2. 确认预览图正常显示，控件 overlay 出现在预览图上方
3. 拖拽控件 → 位置跟手 + 边界约束
4. 缩放控件 → 大小跟手
5. 旋转控件 → 旋转手柄可用
6. 点击大纲树控件名 → 预览区 overlay 高亮
7. 修改控件属性 → overlay 同步更新

- [ ] **Step 3: 修复发现的问题并 Commit**

```bash
git add -A
git commit -m "fix: 交互式画布集成问题修复

Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
```

---
