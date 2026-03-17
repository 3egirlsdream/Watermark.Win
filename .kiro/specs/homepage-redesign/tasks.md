# 实施计划：首页重新设计

## 概述

将水印相框大师官网首页从平铺式布局重构为苹果官网风格的全屏分区布局，包含滚动驱动动画、固定导航栏和响应式适配。实施顺序为：基础设施（CSS/JS 文件 + 布局修改）→ 页面分区结构 → 动画引擎 → 响应式适配 → 集成联调。

## 任务

- [x] 1. 创建基础设施文件并修改布局配置
  - [x] 1.1 创建 `wwwroot/homepage.css` 文件，定义滚动动画基类和动画关键帧
    - 定义 `.scroll-animate` 基类（初始 opacity:0）
    - 定义 `.fade-in.animated`、`.slide-up.animated`、`.slide-left.animated`、`.slide-right.animated`、`.scale-in.animated` 五种动画
    - 定义 `.stagger-delay-1` 到 `.stagger-delay-12` 交错延迟类
    - 所有动画 duration 0.4s~0.8s，timing-function ease-out，仅使用 transform 和 opacity
    - 添加 `.scroll-animate` 的 3 秒超时 fallback 规则（JS 加载失败时自动显示）
    - _需求：3.3, 3.4, 10.1_

  - [x] 1.2 在 `homepage.css` 中定义固定导航栏样式
    - 定义 `.sticky-nav` 样式：position:fixed, top:0, z-index:1000, 默认隐藏（transform: translateY(-100%)）
    - 定义 `.sticky-nav.visible` 样式：从上方滑入动画
    - 添加 backdrop-filter: blur 毛玻璃效果
    - _需求：4.1, 4.3, 4.4, 4.5_

  - [x] 1.3 在 `homepage.css` 中定义各 Section 样式
    - 定义 `.hero-section` 样式：min-height:100vh，渐变背景，居中排版
    - 定义 `.feature-section`、`.showcase-section`、`.download-section`、`.gallery-section` 样式
    - 定义 `.footer-section` 样式：深色背景，居中内容
    - Section 之间使用背景色交替实现视觉分隔
    - _需求：1.1, 1.2, 1.3, 1.4, 2.5, 8.2, 8.3_

  - [x] 1.4 在 `homepage.css` 中添加响应式媒体查询
    - 添加 `@media (max-width: 768px)` 断点
    - 多列布局调整为单列
    - Hero 文字缩小
    - 模板画廊从 6 列调整为 2~3 列
    - 导航栏移动端适配
    - _需求：9.1, 9.2, 9.3, 9.4_

  - [x] 1.5 创建 `wwwroot/homepage.js` 文件，实现滚动动画引擎
    - 实现 `window.HomepageAnimations.init()` 方法
    - 创建 IntersectionObserver（threshold: 0.15），观察所有 `.scroll-animate` 元素
    - 元素进入视口时添加 `.animated` 类并 unobserve（防止重复触发）
    - 监听 scroll 事件控制 `.sticky-nav` 的 `.visible` 类切换（scrollY > window.innerHeight）
    - 添加 IntersectionObserver 不支持时的降级处理（直接添加 animated 类）
    - _需求：3.1, 3.2, 3.5, 4.3, 4.4, 10.3_

  - [x] 1.6 修改 `Components/App.razor`，引入 homepage.css 和 homepage.js
    - 在 `<head>` 中添加 `<link rel="stylesheet" href="homepage.css" />`
    - 在 `<body>` 底部添加 `<script src="homepage.js"></script>`
    - 移除 App.razor 底部 `<style>` 块中的 `.page { overflow-y: hidden }` 规则
    - _需求：10.4_

- [x] 2. 检查点 - 确保基础设施文件创建正确
  - 确保所有文件创建无误，如有问题请向用户确认。

- [x] 3. 重构 MainView.razor 为全屏分区布局
  - [x] 3.1 重构 `MainView.razor` 整体结构，替换现有布局为分区结构
    - 移除现有的 `<div Style="background:#FFFFFF;...">` 包裹和旧布局代码
    - 添加固定导航栏 `<nav class="sticky-nav" id="stickyNav">`，包含 LogoIcon、产品名称、下载按钮
    - 添加 Hero Section `<section class="hero-section" id="heroSection">`，包含产品名称、英文名、标语、CTA 按钮（锚点到 #downloadSection）
    - Hero 内容元素添加 `.scroll-animate .slide-up` 类
    - _需求：1.1, 2.1, 2.2, 2.3, 2.4, 4.2_

  - [x] 3.2 实现 Feature Section 功能卡片区
    - 在 `@code` 块中定义 `FeatureItem` record 和 `features` 列表（5 项功能）
    - 使用 MudGrid + MudCard 渲染功能卡片，每张卡片包含图标和描述文字
    - 每张卡片添加 `.scroll-animate .fade-in .stagger-delay-N` 类实现交错动画
    - _需求：1.3, 5.1, 5.2, 5.3, 5.4_

  - [x] 3.3 实现产品截图展示区
    - 图文并排布局，左侧文字描述，右侧产品截图
    - 添加 `.scroll-animate .slide-left` 和 `.slide-right` 动画类
    - _需求：1.1_

  - [x] 3.4 实现 Download Section 下载区
    - 添加 `id="downloadSection"` 锚点
    - 保留原有 4 个平台下载按钮（Windows、Android、macOS、iPhone），href 地址不变
    - 每个按钮包含平台图标和名称
    - 下方显示隐私协议链接文字
    - 添加 `.scroll-animate` 动画类
    - _需求：6.1, 6.2, 6.3, 6.4, 6.5_

  - [x] 3.5 实现 Template Gallery Section 模板画廊
    - 添加标题文字"精选模板"
    - 使用网格布局展示 12 张模板图片，保留现有 `strings` 列表数据
    - 图片添加圆角和阴影效果
    - 图片使用 `loading="lazy"` 懒加载
    - 每张图片添加 `.scroll-animate .scale-in .stagger-delay-N` 类
    - _需求：7.1, 7.2, 7.3, 7.4, 10.2_

  - [x] 3.6 实现 Footer Section 页脚
    - 深色背景，居中显示
    - 保留备案信息"蜀ICP备2024092556号"及链接 https://beian.miit.gov.cn/
    - _需求：8.1, 8.2, 8.3_

  - [x] 3.7 在 `OnAfterRenderAsync` 中调用 JS 初始化
    - 使用 `IJSRuntime.InvokeVoidAsync("HomepageAnimations.init")` 初始化动画引擎
    - 用 try-catch 包裹调用，处理 JS 加载失败场景
    - 保留现有的 `APIHelper` 页面访问记录逻辑
    - _需求：3.1, 3.2_

- [x] 4. 检查点 - 确保页面结构和动画正常工作
  - 确保所有测试通过，如有问题请向用户确认。

- [x] 5. 添加图片错误处理和 CSS 平滑滚动
  - [x] 5.1 为模板图片和产品截图添加 `onerror` 回退处理
    - 图片加载失败时显示占位背景色
    - _需求：10.1（错误处理）_

  - [x] 5.2 在 `homepage.css` 中添加 `html { scroll-behavior: smooth }` 实现 CTA 按钮平滑滚动
    - _需求：2.3_

- [x] 6. 最终检查点 - 确保所有功能完整
  - 确保所有测试通过，如有问题请向用户确认。

## 备注

- 标记 `*` 的任务为可选任务，可跳过以加速 MVP 交付
- 每个任务引用了具体的需求编号以确保可追溯性
- 检查点确保增量验证
- 所有下载链接地址和模板图片数据保持与现有代码一致
