# PureViewer 项目记忆

## 项目信息
- **类型**: .NET 8 WPF 桌面应用
- **用途**: 图片大图查看器（指定目录浏览）
- **特点**: 精简UI，灰色背景，支持静态图+GIF动画，键盘快捷键

## 技术要点
- `UseWindowsForms=true` 启用 FolderBrowserDialog，但需 `<Using Remove="System.Windows.Forms" />` 避免与 WPF 命名空间冲突
- GIF 动画：GifBitmapDecoder + DispatcherTimer，读取 `/grctlext/Delay` 元数据获取帧延迟
- 静态图：BitmapImage + BitmapCacheOption.OnLoad
- 全屏：WindowStyle=None + WindowState=Maximized，退出时恢复

## 快捷键
- ←/A: 上一张 | →/D: 下一张 | F11/F: 全屏切换 | Esc: 退出全屏
