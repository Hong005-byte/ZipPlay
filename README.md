# ZipPlay

一个 Windows 桌面小型歌词播放器（歌词浮窗）。跟随系统当前正在播放的媒体（Spotify、网易云音乐、浏览器播放等，只要接入了 Windows 系统媒体控制 SMTC 的都支持），自动抓取并实时同步显示歌词，支持 13 套可切换的像素/复古风格皮肤。

![开场动画](docs/screenshots/splash.png)

![皮肤选择](docs/screenshots/skin-gallery.png)

## 下载安装

前往 [Releases](https://github.com/Hong005-byte/ZipPlay/releases) 页面下载最新的 `ZipPlay-Setup-x.x.x.exe`，双击安装即可（装在当前用户目录下，不需要管理员权限，不会弹 UAC）。安装完成后打开程序，每次启动都会自动检查有没有更新，右上角会提示新版本。

## 功能

- **像素开场动画**：启动时一个像素小人从右边把 `ZIPPLAY` 的像素字拖进画面中央，黑底橙金配色，点一下可以跳过。
- **实时歌词同步**：不依赖轮询系统 API，而是订阅 `TimelinePropertiesChanged` / `PlaybackInfoChanged` 事件打时间锚点，本地插值计算当前播放位置，准确且几乎不耗资源。
- **多引擎免费歌词源**：同时向 [LRCLIB](https://lrclib.net)、网易云音乐、QQ音乐、酷狗音乐并发请求，谁先返回有效结果就用谁，切歌到歌词出现的等待时间取决于最快的那一个。
- **13 套皮肤**，启动时或运行中随时可切换：
  - 🧱 Minecraft 像素风（会走路的 Steve + 小树）
  - ▬ 简约风
  - 📺 复古 CRT 终端风（扫描线滚动 + 屏幕闪烁）
  - 🌆 霓虹赛博朋克风（霓虹灯管式闪烁）
  - 💿 黑胶唱片机风（旋转唱片 + 静止唱臂）
  - 🧊 玻璃拟态风（周期性裂开又愈合）
  - ☕ 复古咖啡馆 / lofi 风（热气袅袅上升）
  - 🌙 极光雪夜风（流动极光 + 飘雪）
  - 🌧️ 雨夜窗景风（雨滴沿窗滑落）
  - ✨ 星空太空风（满天星星不同步闪烁）
  - 🏕️ 篝火露营风（火苗闪烁 + 飘散火星）
  - 🌸 樱花风（花瓣飘落摇曳）
  - 📼 复古磁带机风（双卷盘转动的走带效果）
- **窗口尺寸预设**（小 / 中 / 大），选定后锁定大小，不会被误拖成全屏。
- **显示模式**：标准（标题 + 进度条 + 歌词）或极简（只显示当前歌词）。
- **自动 + 手动检查更新**：启动时后台静默检查一次；设置页里也有一个"检查更新"按钮，随时手动查，不用重开 app。
- 所有像素素材（Steve、小树、泥土纹理、ZIPPLAY 像素字等）都是运行时用代码逐像素生成的 `WriteableBitmap`，不依赖任何外部图片文件。

## 运行要求

- Windows 10 1809 (build 17763) 或更高版本（需要系统媒体控制 SMTC 支持）
- 用上面的安装包不需要额外装 .NET——是自包含发布，运行时已经打包在里面了
- 从源码构建/运行需要 [.NET 8 SDK](https://dotnet.microsoft.com/download)

## 从源码运行

```bash
git clone https://github.com/Hong005-byte/ZipPlay.git
cd ZipPlay/PixelLyric8BitFix
dotnet run
```

程序会先弹出启动设置页，选好皮肤 / 尺寸 / 显示模式后点"开始播放"即可。主窗口右上角的齿轮图标（或右键菜单）可以随时回到设置页更换皮肤。

## 项目结构

```
PixelLyric8BitFix/
├── SplashWindow.xaml(.cs)   开场动画：像素小人拖 ZIPPLAY 字进场
├── MainWindow.xaml(.cs)     主播放器窗口：歌词同步、多引擎抓词、皮肤渲染
├── SettingsWindow.xaml(.cs) 启动设置页：皮肤/尺寸/显示模式选择、手动检查更新
├── AppSettings.cs           本地配置的读写与枚举定义
├── PixelArt.cs              运行时生成像素素材（Steve、小树、篝火、咖啡杯、ZIPPLAY 像素字等）
├── UpdateChecker.cs         查 GitHub Release 判断有没有新版本（后台自动 + 设置页手动都用它）
└── Icon.ico                 应用图标

installer/
└── ZipPlay.iss              Inno Setup 打包脚本
```

## 发布新版本（给维护者看）

1. 改代码，把 [PixelLyric8BitFix.csproj](PixelLyric8BitFix/PixelLyric8BitFix.csproj) 里的 `<Version>` 往上调（比如 `1.1.0`）
2. 自包含单文件发布：
   ```bash
   cd PixelLyric8BitFix
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
   ```
3. 把 [installer/ZipPlay.iss](installer/ZipPlay.iss) 里的 `MyAppVersion` 也改成一样的号，然后用 Inno Setup 编译：
   ```bash
   "C:\Program Files\Inno Setup 7\ISCC.exe" installer\ZipPlay.iss
   ```
   产物在 `installer\output\ZipPlay-Setup-x.x.x.exe`
4. 在 GitHub 上发一个新 Release，tag 打成 `v1.1.0`（要跟 csproj 里的版本号对上，`UpdateChecker` 是拿 tag 名字去比大小的），把上一步的安装包作为附件传上去
5. 所有已经装过旧版本的人，下次打开 app 就会看到右上角的更新提示

## 免责声明

网易云音乐 / QQ音乐 / 酷狗音乐的歌词接口均为非官方接口，仅供个人学习使用，接口结构可能随时变化；LRCLIB 是完全开放的免费歌词数据库。

## License

[MIT](LICENSE)
