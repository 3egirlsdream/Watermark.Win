# 需求文档

## 简介

对水印相框大师（Davici Frame Master）官网首页进行现代化重新设计。参考苹果官网等现代化首页风格，引入全屏分区布局、滚动驱动的过渡动画效果，提升品牌视觉质感和用户体验。技术栈基于 Blazor + MudBlazor，通过自定义 CSS 动画和 JavaScript Intersection Observer 实现滚动触发效果。

## 术语表

- **Homepage**: 水印相框大师官网首页，路由为 "/"，对应 MainView.razor 组件
- **Section**: 首页中的一个全屏或近全屏内容分区，每个 Section 占据视口的大部分高度
- **Scroll_Animation_Engine**: 基于 Intersection Observer API 的 JavaScript 模块，负责检测元素进入视口并触发 CSS 动画
- **Hero_Section**: 首页顶部的全屏主视觉区域，包含产品名称、标语和主行动按钮
- **Feature_Section**: 展示产品核心功能特点的内容分区
- **Download_Section**: 包含多平台下载按钮的内容分区
- **Template_Gallery_Section**: 展示模板图片的网格画廊分区
- **Footer_Section**: 页面底部区域，包含备案信息和链接
- **Sticky_Navigation**: 固定在页面顶部的导航栏，随滚动显示/隐藏或改变样式
- **MudBlazor**: 项目使用的 Blazor Material Design 组件库

## 需求

### 需求 1：全屏分区页面布局

**用户故事：** 作为访客，我希望首页采用全屏分区布局，以便获得类似苹果官网的沉浸式浏览体验。

#### 验收标准

1. THE Homepage SHALL 将内容组织为至少五个垂直排列的 Section：Hero_Section、Feature_Section、产品截图展示区、Template_Gallery_Section 和 Footer_Section
2. WHEN 页面加载完成时，THE Hero_Section SHALL 占据视口 100% 的高度（100vh）
3. THE Feature_Section SHALL 使用大面积留白和居中排版展示产品功能特点
4. THE Homepage SHALL 在每个 Section 之间保持视觉上的清晰分隔，使用背景色交替或渐变过渡

### 需求 2：Hero 主视觉区域

**用户故事：** 作为访客，我希望首页顶部有一个震撼的主视觉区域，以便第一时间了解产品定位并产生兴趣。

#### 验收标准

1. THE Hero_Section SHALL 居中显示产品名称"水印相框大师"和英文名"Davici Frame Master"
2. THE Hero_Section SHALL 显示一句产品标语"强大的全功能图片模板编辑工具"
3. THE Hero_Section SHALL 包含一个醒目的主行动按钮，引导用户前往 Download_Section
4. WHEN 页面首次加载时，THE Hero_Section 中的文字和按钮 SHALL 以淡入上移的动画效果依次出现
5. THE Hero_Section SHALL 使用渐变背景或纯色背景营造高端视觉氛围

### 需求 3：滚动驱动的过渡动画

**用户故事：** 作为访客，我希望页面滚动时内容以流畅的动画效果出现，以便获得现代化的交互体验。

#### 验收标准

1. THE Scroll_Animation_Engine SHALL 使用 Intersection Observer API 检测元素是否进入视口
2. WHEN 一个 Section 的内容元素滚动进入视口时，THE Scroll_Animation_Engine SHALL 为该元素添加对应的 CSS 动画类名触发动画
3. THE Homepage SHALL 支持以下动画类型：淡入（fade-in）、从下方滑入（slide-up）、从左侧滑入（slide-left）、从右侧滑入（slide-right）和缩放出现（scale-in）
4. THE Homepage 中的动画持续时间 SHALL 在 0.4 秒到 0.8 秒之间，使用 ease-out 缓动函数
5. WHEN 元素已经触发过动画后，THE Scroll_Animation_Engine SHALL 保持该元素的可见状态，避免重复触发

### 需求 4：固定导航栏

**用户故事：** 作为访客，我希望页面顶部有一个固定导航栏，以便在浏览长页面时随时访问关键功能。

#### 验收标准

1. THE Sticky_Navigation SHALL 固定在页面顶部，z-index 值确保始终在其他内容之上
2. THE Sticky_Navigation SHALL 包含产品 Logo、产品名称和下载按钮
3. WHEN 页面向下滚动超过 Hero_Section 的高度时，THE Sticky_Navigation SHALL 以从上方滑入的动画出现
4. WHILE 页面处于 Hero_Section 可视范围内，THE Sticky_Navigation SHALL 保持隐藏状态
5. THE Sticky_Navigation SHALL 使用半透明毛玻璃效果（backdrop-filter: blur）作为背景

### 需求 5：功能特点展示区

**用户故事：** 作为访客，我希望以直观的方式了解产品的核心功能，以便评估产品是否满足我的需求。

#### 验收标准

1. THE Feature_Section SHALL 以卡片或图文并排的形式展示以下功能特点：基础功能免费、会员全平台通用、不损失画质、模板设计与编辑、拼图功能
2. WHEN Feature_Section 中的功能卡片滚动进入视口时，THE Scroll_Animation_Engine SHALL 以交错延迟的方式依次触发每张卡片的动画
3. THE Feature_Section 中每张功能卡片 SHALL 包含一个图标和对应的功能描述文字
4. THE Feature_Section SHALL 使用 MudBlazor 的 MudGrid 或 MudStack 组件实现响应式布局

### 需求 6：多平台下载区域

**用户故事：** 作为访客，我希望方便地找到适合我设备的下载链接，以便快速获取产品。

#### 验收标准

1. THE Download_Section SHALL 提供四个平台的下载按钮：Windows、Android、macOS、iPhone
2. THE Download_Section 中每个下载按钮 SHALL 包含对应平台的图标和平台名称
3. WHEN Download_Section 滚动进入视口时，THE Scroll_Animation_Engine SHALL 触发下载按钮的动画效果
4. THE Download_Section SHALL 保留现有的下载链接地址不变
5. THE Download_Section SHALL 在下载按钮下方显示隐私协议链接文字"使用水印相框大师即表示你同意其许可 & 隐私声明"

### 需求 7：模板画廊展示区

**用户故事：** 作为访客，我希望看到丰富的模板示例，以便了解产品的模板设计能力。

#### 验收标准

1. THE Template_Gallery_Section SHALL 以网格布局展示现有的 12 张模板图片
2. WHEN Template_Gallery_Section 滚动进入视口时，THE Scroll_Animation_Engine SHALL 以交错延迟的方式依次触发每张图片的缩放出现动画
3. THE Template_Gallery_Section 中的图片 SHALL 使用圆角和阴影效果提升视觉质感
4. THE Template_Gallery_Section SHALL 在图片网格上方显示标题文字"精选模板"或类似引导文案

### 需求 8：页脚区域

**用户故事：** 作为访客，我希望在页面底部找到备案信息，以便确认网站的合规性。

#### 验收标准

1. THE Footer_Section SHALL 显示备案信息"蜀ICP备2024092556号"并链接到 https://beian.miit.gov.cn/
2. THE Footer_Section SHALL 使用深色背景与页面主体形成视觉对比
3. THE Footer_Section SHALL 居中显示内容，保持简洁的排版风格

### 需求 9：响应式适配

**用户故事：** 作为移动端访客，我希望首页在手机和平板上也能正常浏览，以便在任何设备上了解产品。

#### 验收标准

1. THE Homepage SHALL 在视口宽度小于 768px 时将多列布局调整为单列布局
2. THE Hero_Section 中的文字大小 SHALL 在移动端适当缩小以适应屏幕宽度
3. THE Template_Gallery_Section 的网格列数 SHALL 在移动端从 6 列调整为 2 列或 3 列
4. THE Sticky_Navigation SHALL 在移动端保持可用，下载按钮可折叠为菜单形式

### 需求 10：性能与加载体验

**用户故事：** 作为访客，我希望首页加载快速且动画流畅，以便获得良好的浏览体验。

#### 验收标准

1. THE Homepage 中的所有 CSS 动画 SHALL 仅使用 transform 和 opacity 属性以确保 GPU 加速渲染
2. THE Template_Gallery_Section 中的图片 SHALL 使用懒加载（loading="lazy"）策略，仅在接近视口时加载
3. THE Scroll_Animation_Engine SHALL 使用 Intersection Observer API 而非 scroll 事件监听，以减少性能开销
4. THE Homepage 的自定义 CSS 和 JavaScript SHALL 分别存放在独立文件中（homepage.css 和 homepage.js），避免内联样式膨胀
