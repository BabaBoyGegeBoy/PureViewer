# PureViewer 项目记忆

## 项目信息
- **类型**: .NET 8 WPF 桌面应用
- **用途**: 图片大图查看器（Pixiv 下载目录浏览）
- **特点**: 灰色背景，支持静态图+GIF动画，键盘快捷键，作者/标签筛选，缩放拖拽

## 技术要点
- `UseWindowsForms=true` + `<Using Remove="System.Windows.Forms" />` 避免 WPF 命名空间冲突
- `System.Windows.Point` 需全限定，避免与 `System.Drawing.Point` 冲突
- GIF 动画：GifBitmapDecoder + DispatcherTimer，读取 `/grctlext/Delay`
- 静态图：BitmapImage + BitmapCacheOption.OnLoad + DownloadCompleted 事件
- 缩放：ScaleTransform + ScrollViewer，Ctrl+滚轮缩放，放大后拖拽平移
- 作者解析：一级子文件夹名 `作者名_ID`，ExtractAuthorName 提取
- 标签解析：遍历 info.json 的 tags 数组，跨作者聚合
- 缩略图：DecodePixelWidth=120 低内存加载，WrapPanel 流式布局

## 快捷键
- ←/A: 上一张 | →/D: 下一张 | F11/F: 全屏切换 | Esc: 退出全屏
- Ctrl+滚轮/+/−: 缩放 | Ctrl+0: 重置缩放 | G: 切换缩略图/大图
- 纯滚轮: 翻页 | Tab: 切换作者侧栏

## UI 架构（当前：方案B）
- 极简顶栏 28px，不透明（#FF383838），始终可见
- 底栏默认折叠（Height=0），鼠标移至底部 6px 触发区展开（36px），3秒自动折叠
- 作者侧栏 Tab 键切换，不透明 170px（#FF333333）
- 图片区域背景 #FF2E2E2E
- 页码+缩放信息在顶栏右侧
- 3套 UI 方案备份在 UI_SCHEMES.md
