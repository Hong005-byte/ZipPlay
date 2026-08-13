# ZipPlay

一个 Windows 桌面小型歌词播放器（歌词浮窗）。跟随系统当前正在播放的媒体（Spotify、网易云音乐、浏览器播放等，只要接入了 Windows 系统媒体控制 SMTC 的都支持），自动抓取并实时同步显示歌词，支持 18 套可切换的像素/复古风格皮肤（含自定义主题）。

![开场动画](docs/screenshots/splash.png)

![皮肤选择](docs/screenshots/skin-gallery.png)

## 下载安装

前往 [Releases](https://github.com/Hong005-byte/ZipPlay/releases) 页面下载最新的 `ZipPlay-Setup-x.x.x.exe`，双击安装即可（装在当前用户目录下，不需要管理员权限，不会弹 UAC）。安装完成后打开程序，每次启动都会自动检查有没有更新，右上角会提示新版本。

## 功能

- **像素开场动画**：启动时一个像素小人从右边把 `ZIPPLAY` 的像素字拖进画面中央，黑底橙金配色，点一下可以跳过。
- **实时歌词同步**：不依赖轮询系统 API，而是订阅 `TimelinePropertiesChanged` / `PlaybackInfoChanged` 事件打时间锚点，本地插值计算当前播放位置，准确且几乎不耗资源。
- **多引擎免费歌词源**：同时向 [LRCLIB](https://lrclib.net)、网易云音乐、QQ音乐、酷狗音乐并发请求，谁先返回有效结果就用谁，切歌到歌词出现的等待时间取决于最快的那一个；抓到过的歌词会本地缓存，同一首歌下次再放直接秒出，不用重新联网。四引擎都找不到时，右键菜单可以手动导入本地 `.lrc` 文件兜底。
- **卡拉OK 扫光效果**：歌词逐字高亮，按估算进度模拟"已唱/未唱"的扫光过渡（免费歌词源只给整行时间戳，这是按字数估算出的近似效果）。左上角齿轮旁边有个开关，可以随时关掉退回整行切换。
- **歌词同步偏移**：不同播放器上报的播放位置跟实际听感之间有固有延迟，在歌词框上滚一下鼠标滚轮就能手动微调（每格 50ms），矫正之后自动记住。
- **18 套皮肤**，启动时或运行中随时可切换：
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
  - ☁️ 云朵漂浮风（三朵云各自不同速度飘过）
  - 🕯️ 烛光冥想风（一支蜡烛不规则闪烁，安静的室内氛围）
  - 🪴 绿植角落风（一盆小盆栽轻轻摇摆）
  - 🌊 海边黄昏风（夕阳呼吸 + 海面波光）
  - 🎨 **自定义主题**：设置页里能自己写主题——不是代码，是纯数据（JSON）：颜色、渐变背景、8x8 像素图标、从 6 种内置动画（呼吸发光/渐隐渐现/飘过/摇摆/旋转/不规则闪烁）里选一种。格式错了会告诉你具体哪个字段错，最多能存 5 个，页面里带一份完整示例照着改就行。
- **系统托盘**：可以收进托盘常驻后台，不用一直占着任务栏；托盘图标菜单可以随时拉回来或者退出。
- **Mini 模式**：双击窗口缩成一个贴合当前皮肤主题的小方块（还能拖着走），再点一下展开，回到缩小前的原位。
- **全局热键**：`Ctrl + Alt + L` 随时显示/隐藏播放器，就算窗口被其它程序挡住或者已经收进托盘也能用。
- **窗口位置记忆**：关闭时自动记住窗口位置，下次打开原地摆回去。
- **窗口尺寸预设**（小 / 中 / 大），选定后锁定大小，不会被误拖成全屏。
- **显示模式**：标准（标题 + 进度条 + 歌词）或极简（只显示当前歌词）。
- **自动 + 手动检查更新**：启动时后台静默检查一次；设置页里也有一个"检查更新"按钮，随时手动查，不用重开 app；发现新版本可以一键下载安装、自动重启进新版本。
- **本地诊断日志**：出问题（崩溃、托盘图标不出现、热键不生效之类）时，设置页能直接打开日志文件，方便排查或者反馈问题时带上。
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
├── SplashWindow.xaml(.cs)        开场动画：像素小人拖 ZIPPLAY 字进场
├── MainWindow.xaml(.cs)          主播放器窗口：歌词同步/卡拉OK效果/托盘/Mini模式/热键/皮肤渲染
├── SettingsWindow.xaml(.cs)      启动设置页：皮肤/尺寸/显示模式选择、缓存管理、手动检查更新
├── CustomThemeWindow.xaml(.cs)   自定义主题页：粘贴/编辑 JSON、校验报错、管理最多 5 个主题
├── AppSettings.cs                本地配置的读写与枚举定义
├── SkinTheme.cs                  17 套内置皮肤的配色/字体/图标集中定义
├── CustomTheme.cs                客制化主题的数据模型 + 详细校验
├── CustomThemeStore.cs           客制化主题的磁盘存取（最多 5 个）
├── PixelArt.cs                   运行时生成像素素材（Steve、小树、篝火、各皮肤图标等）
├── LyricsFetcher.cs              多引擎并发抓词（LRCLIB / 网易云 / QQ音乐 / 酷狗）
├── LyricsCache.cs                歌词本地缓存的存取
├── AppLog.cs                     本地诊断日志
├── UpdateChecker.cs               查 GitHub Release 判断有没有新版本（后台自动 + 设置页手动都用它）
└── Icon.ico                      应用图标

installer/
└── ZipPlay.iss                   Inno Setup 打包脚本
```

## 发布新版本（给维护者看）

1. 改代码，把 [PixelLyric8BitFix.csproj](PixelLyric8BitFix/PixelLyric8BitFix.csproj) 里的 `<Version>` 往上调（比如 `1.2.0`）
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
4. 在 GitHub 上发一个新 Release，tag 打成 `v1.2.0`（要跟 csproj 里的版本号对上，`UpdateChecker` 是拿 tag 名字去比大小的），把上一步的安装包作为附件传上去
5. 所有已经装过旧版本的人，下次打开 app 就会看到右上角的更新提示

## 免责声明

网易云音乐 / QQ音乐 / 酷狗音乐的歌词接口均为非官方接口，仅供个人学习使用，接口结构可能随时变化；LRCLIB 是完全开放的免费歌词数据库。自定义主题功能只接受纯数据（颜色/渐变/像素图标/内置动画），不执行任何用户代码。

## License

[MIT](LICENSE)
