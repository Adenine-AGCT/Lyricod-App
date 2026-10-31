# Lyricod for Windows

[English](README.md) | 简体中文

Lyricod 是一款 Windows 歌词工具，支持逐字同步歌词、桌面歌词、歌词岛和独立歌词窗口。

支持 Spotify、QQ 音乐、网易云音乐、Apple Music，以及部分浏览器音乐播放器。可以显示翻译、音译，并调整歌词样式和同步时间。

[下载 Lyricod](https://github.com/Indarkdust/Lyricod-App/releases)

![Lyricod Glass 歌词岛与同步歌词](assets/island-glass.gif)

## 歌词怎么显示？

### 歌词岛

歌词岛默认显示在屏幕顶部，随着歌曲播放逐字高亮歌词。它会根据当前歌词调整宽度，也能显示翻译、音译或下一句。歌词颜色可以跟随专辑封面，也可以自己设置。

![歌词岛随音乐逐字高亮](assets/island.gif)

鼠标移到歌词岛上时，可以让它展开成迷你播放器，或者暂时隐藏，方便操作被挡住的区域。也可以结合两种操作：鼠标移入时，歌词岛会暂时隐藏。想展开迷你播放器，可以先横向划过歌词岛，再向下移动（悬浮模式下也可以向上）。暂停一段时间后，歌词岛可以自动淡出。

![通过鼠标手势展开歌词岛迷你播放器](assets/island-gesture.gif)

<details>
<summary>查看鼠标展开手势示意图</summary>

![先横向划过歌词岛，再向下移动](assets/island-gesture-guide.zh-CN.png)

</details>

想调整歌词岛的位置，可以在「设置 > 歌词岛」中切换到「悬浮」模式。悬浮模式下，歌词岛四角都有圆角，解锁后可以自由拖动。靠近屏幕边缘时，它会朝屏幕内侧展开，避免歌词或迷你播放器超出屏幕。

选择「Lyricod Glass」主题，可以让歌词岛使用透明背景。Windows 11 上默认使用液态玻璃效果，带有轻微模糊、边缘折射和高光。也可以选择透明或毛玻璃效果，并调整透明度、色调、模糊程度和边缘折射。为了避免重复捕获画面，正常使用液态玻璃效果时，歌词岛会从截图、录屏和屏幕共享画面中排除。

### 桌面歌词

将一行或两行歌词悬浮在其他窗口上方。锁定后不会挡住鼠标操作；想换个位置时，解锁后直接拖动。

![桌面歌词逐字高亮效果](assets/desktop-lyrics.gif)

### 歌词窗口

在独立窗口中查看整首歌词，背景可以显示专辑封面。歌词会随着播放滚动，点击某一行可以跳转到相应位置。按 F11 可以切换全屏。

![独立歌词窗口与专辑封面背景](assets/lyrics-window.png)

Lyricod 也会留在系统托盘中。点击图标，可以控制播放、开关歌词岛和桌面歌词、选择歌词，或调整当前歌曲的歌词时间。常用操作也能设置全局快捷键。

<img src="assets/tray-panel.png" alt="Lyricod 托盘面板，包含播放控制和歌词设置" width="360">

## 支持哪些播放器？

Lyricod 通过 Windows 媒体控件获取正在播放的歌曲信息。通常只要播放器能出现在 Windows 的媒体控件中，Lyricod 就能识别它，例如 Spotify、QQ 音乐、网易云音乐、Apple Music，以及部分浏览器音乐网站。

同时打开多个播放器时，Lyricod 默认跟随正在播放的一个。也可以在「设置 > 通用」中手动选择。

有些播放器不会提供准确的播放进度。遇到这种情况，Lyricod 会自行计时；如果暂停或拖动进度，歌词可能会不同步。网易云音乐的具体情况见下文。

对于浏览器中的视频，Lyricod 只会在能够匹配到歌曲时显示歌词。你也可以在「设置 > 歌词」中关闭浏览器及其他应用的歌词搜索。

## 歌词从哪里来？

Lyricod 从网易云音乐、QQ 音乐和 [LRCLIB](https://lrclib.net) 搜索歌词。根据歌曲和来源的不同，可以获取逐字歌词、逐行歌词、翻译和音译。找到的歌词会保存在本机，下次播放时可以直接加载。

如果匹配到了错误的歌曲，可以打开「选择歌词」，查看不同来源的搜索结果和歌词预览，再手动选择。Lyricod 会记住你选的歌词和对应的时间调整，清除歌词缓存后也会保留这些设置。

![选择歌词时可对比来源并预览内容](assets/choose-lyrics.png)

歌词快了或慢了，可以在托盘面板或「设置 > 歌词」中调整同步时间。

## 下载和安装

从 [Releases 页面](https://github.com/Indarkdust/Lyricod-App/releases) 下载适合电脑的版本：

- **x64**：适用于大多数 Intel、AMD 处理器的 Windows 电脑。
- **ARM64**：适用于使用 Arm 处理器的 Windows 电脑。
- **安装版（setup.exe）**：运行安装程序即可。安装到当前用户，不需要管理员权限。
- **便携版（zip）**：解压后运行 `Lyricod.exe`，不要移动或删除旁边的 `app` 文件夹。

启动后，Lyricod 会显示在 Windows 系统托盘中。如果没有看到主窗口，可以到任务栏右下角查找 Lyricod 图标。

当前版本尚未进行代码签名，Windows 首次运行时可能会显示安全提示。请先确认下载文件的来源，再自行决定是否继续运行。

Lyricod 每天检查一次是否有新版本，可在「设置 > 关于」中关闭。安装版和便携版都支持在应用内更新，也可以手动下载新版。

设置和歌词缓存在 `%LOCALAPPDATA%\Lyricod`，更新时会保留。卸载程序也不会自动删除这个文件夹，如需清除数据，可以手动删除。

## 网易云音乐歌词不同步？

网易云音乐可以向 Windows 提供歌曲信息，但通常不会提供准确的播放位置。所以 Lyricod 需要自己计时，暂停或拖动进度后，歌词可能出现偏差。

如需更准确的同步，可以使用 **InfLink-rs** 插件。它需要通过 **BetterNCM** 加载。这两个项目由第三方开发者维护，会修改网易云音乐的运行环境，请自行决定是否安装。

最方便的方法是直接打开 Lyricod 的「设置 > 通用 > 网易云音乐歌词同步」。Lyricod 会下载一个[辅助工具](https://github.com/darkdust-c/Lyricod-CM-Helper)，安装 BetterNCM 和 InfLink-rs，并在安装前校验下载的文件。目前仅支持 64 位网易云音乐 3.x。如果想手动安装，也可以按照下面的步骤操作：

1. 退出网易云音乐，从 [BetterNCM Installer 发布页](https://github.com/std-microblock/BetterNCM-Installer/releases/latest)下载并运行 `betterncm_installer.exe`。
2. 打开网易云音乐，在 BetterNCM 插件商店中安装 **InfLink-rs**；也可以从 [InfLink-rs 发布页](https://github.com/apoint123/inflink-rs/releases/latest)下载 `InfLink-rs.plugin`，放进 `C:\betterncm\plugins`。
3. 重启网易云音乐。InfLink-rs 默认会开启 **SMTC**。如果 Lyricod 仍无法获取播放进度，请检查 InfLink-rs 的设置。

设置完成后，Lyricod 会自动识别播放器提供的进度信息。可以在「设置 > 关于 > 诊断信息」中查看。

InfLink-rs 已在网易云音乐 3.1.20 至 3.1.41 和 2.10.13 版本上测试过。播放器更新后如有问题，可以先检查插件更新；需要移除 BetterNCM 时，可以删除网易云音乐安装目录中的 `msimg32.dll`；如果是通过 Lyricod 安装的，也可以在「设置 > 通用 > 网易云音乐歌词同步」中点击「移除」。不要同时安装旧版 InfLink 和 InfLink-rs。

## 自定义

可以在设置中调整字体、字号、歌词颜色、背景、翻译和音译，以及歌词岛的模式、主题、大小、动画和鼠标悬停操作。歌词岛、桌面歌词和歌词窗口可以分别设置，也支持全局快捷键。

默认歌词字体为微软雅黑。也可以选择电脑上已安装的字体，或在设置中从小米下载 **MiSans**。Lyricod 不随程序附带 MiSans，使用时遵循小米的许可协议。

## 隐私

搜索歌词时，Lyricod 会把歌曲标题、歌手和时长发送给网易云音乐、QQ 音乐和 LRCLIB。如果开启「为浏览器和其他应用查找歌词」，浏览器等应用中播放内容的标题、歌手或频道信息也可能用于歌词搜索。可以在「设置 > 歌词」中关闭这一功能。除此之外，Lyricod 不会发送你观看或浏览内容的其他信息。

歌词岛的音量条通过 Windows 音频接口读取当前播放器的音频信息。Lyricod 不会录制、保存或上传音频。可以在「设置 > 歌词岛」中关闭音量条跟随音乐的功能。

自动检查更新会每天访问一次 GitHub，可在「设置 > 关于」中关闭。

## 系统要求

- Windows 10 1809 或更新版本，支持 x64 和 ARM64。
- 音量条跟随音乐跳动需要 Windows 11；Windows 10 上使用预设动画。

## 致谢

Lyricod 的灵感来自 [Lyricify](https://github.com/WXRIW/Lyricify-App)。Lyricod Island 参考了 WXRIW 设计的 Dynamic Lyrics Island（CC BY-SA 4.0）。QQ 音乐逐字歌词解密代码移植自 [Lyricify Lyrics Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)（Apache License 2.0）。拼音功能使用 [ToolGood.Words](https://github.com/toolgood/ToolGood.Words)（Apache License 2.0）；托盘图标和部分窗口功能使用 [WinUIEx](https://github.com/dotMorten/WinUIEx)（MIT）。Lyricod Glass 的液态玻璃效果使用 Microsoft 的 [Win2D](https://github.com/microsoft/Win2D) 绘制。

歌词版权归各自权利人所有。Lyricod 与 Microsoft、网易和腾讯没有关联。

## 许可

Lyricod for Windows 采用 [Apache License 2.0](LICENSE)。第三方代码、资源及其许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
