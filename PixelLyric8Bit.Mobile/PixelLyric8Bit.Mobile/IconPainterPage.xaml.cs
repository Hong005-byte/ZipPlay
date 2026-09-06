using System.Linq;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;
using PixelLyric8BitFix;

namespace PixelLyric8Bit.Mobile;

/// <summary>
/// 🖌️ 图标画板——点格子画图标代替手写字符网格，纯逻辑全部在 PixelLyric8Bit.Core 的
/// PixelIconEditor（网格 ⇄ CustomThemeIcon 互转、桶装填充、续画已有图标……这批早就写好、有单元测试
/// 覆盖的代码，桌面版 IconPainterWindow 用的也是同一份），这个页面只负责"点格子"这件事本身——
/// 建格子、接 Tap 事件、画调色板、画帧缩略图，画完调 PixelIconEditor 转成 JSON。
///
/// 精简版：没有拖拽连续涂色（只支持一格一格点）、没有左右镜像绘制、没有图片导入量化、没有"动作"
/// （icon.actions）编辑器、没有撤销——这几样桌面版都有，先把"画格子代替手写网格"这个核心能力落地，
/// 复杂交互留到以后真要做的时候。
///
/// 打开时从 CustomThemePage 当前编辑框的 JSON 里找 icon 字段续画（Frame.Navigate 的导航参数，见
/// OnNavigatedTo）——跟桌面版"如果输入框里已经有一个图标，会先摊开续画"是同一个体验；没有可续画的
/// 东西（JSON 是空的/解析不出 icon/半成品）就从空白 8x8 网格 + 8 个预置颜色开始，跟桌面版
/// SeedDefaultPalette 是同一份颜色，不是自己另配的。
///
/// "插入到编辑框"用 PendingResultJson 这个静态字段带着结果走——Frame 导航本身没有内建的"返回值"，
/// 这边设好值再 GoBack，CustomThemePage.OnNavigatedTo 回去的时候会检查这个字段，见该文件说明。
/// 没有直接给 CustomThemePage 一个实例引用互相摸对方的字段，是因为两个页面本来就该是各管各的，
/// 这个静态字段是两者之间唯一的耦合点，仅此而已。
/// </summary>
public sealed partial class IconPainterPage : Page
{
    private const int CellSize = 28;
    private const int DefaultSize = 8;

    // 桌面版 IconPainterWindow.SeedDefaultPalette 同一份预置颜色，不是自己另配的，保证两边"从空白
    // 开始画"看到的默认调色板长一样
    private static readonly (char Char, byte R, byte G, byte B)[] DefaultPalette =
    {
        ('#', 0xF9, 0xC7, 0x84),
        ('w', 0xFF, 0xFF, 0xFF),
        ('k', 0x1A, 0x1A, 0x1A),
        ('r', 0xE7, 0x4C, 0x3C),
        ('g', 0x2E, 0xCC, 0x71),
        ('b', 0x34, 0x98, 0xDB),
        ('y', 0xF1, 0xC4, 0x0F),
        ('p', 0x9B, 0x59, 0xB6),
    };

    public static string? PendingResultJson; // CustomThemePage.OnNavigatedTo 消费之后会清空

    private string _originalThemeJson = "";
    private int _width = DefaultSize;
    private int _height = DefaultSize;
    private System.Collections.Generic.List<char[,]> _frames = new();
    private int _currentFrameIndex;
    private System.Collections.Generic.Dictionary<char, RgbaColor> _palette = new();
    private char _selectedChar = '#';
    private Border[,]? _cellBorders;

    public IconPainterPage()
    {
        this.InitializeComponent();
    }

    // 每次真的导航过来都重新从当前编辑框内容续画一遍——不缓存这个页面实例（没设
    // NavigationCacheMode），每次打开都是全新状态，见类顶部注释
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _originalThemeJson = e.Parameter as string ?? "";

        var icon = PixelIconEditor.TryExtractIcon(_originalThemeJson);
        var loaded = icon != null ? PixelIconEditor.TryLoadFrames(icon) : null;
        if (loaded is { } l)
        {
            _width = l.Width;
            _height = l.Height;
            _frames = l.Grids;
            _palette = l.Palette.Count > 0 ? l.Palette : SeedDefaultPalette();
        }
        else
        {
            _width = DefaultSize;
            _height = DefaultSize;
            _frames = new System.Collections.Generic.List<char[,]> { NewBlankGrid(_width, _height) };
            _palette = SeedDefaultPalette();
        }

        _currentFrameIndex = 0;
        _selectedChar = _palette.Keys.OrderBy(c => c).FirstOrDefault('#');
        UpdateSizeHint(loaded != null);
        TxtPaletteError.Text = "";
        TxtInsertResult.Text = "";

        RenderPalette();
        RenderCanvas();
        RenderFrameStrip();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    // ── 画布尺寸：4~64，跟桌面版同一个范围（CustomThemeValidator.MinIconSize/MaxIconSize） ──────

    private const int MinSize = 4;
    private const int MaxSize = 64;

    private void UpdateSizeHint(bool continuing)
    {
        TxtSizeHint.Text = $"{_height} 行 x {_width} 列" + (continuing ? "（续画已有图标）" : "（新图标）");
        TxtWidthValue.Text = _width.ToString();
        TxtHeightValue.Text = _height.ToString();
    }

    private void BtnWidthMinus_Click(object sender, RoutedEventArgs e) => ResizeCanvas(_width - 1, _height);

    private void BtnWidthPlus_Click(object sender, RoutedEventArgs e) => ResizeCanvas(_width + 1, _height);

    private void BtnHeightMinus_Click(object sender, RoutedEventArgs e) => ResizeCanvas(_width, _height - 1);

    private void BtnHeightPlus_Click(object sender, RoutedEventArgs e) => ResizeCanvas(_width, _height + 1);

    // 改小从右/下边裁掉超出的部分，改大在右/下边补透明格子（'.'）——已经画的内容永远停在左上角，
    // 不会因为调过一次尺寸就整份清空重画，这样"先随便画 8x8，画到一半发现不够大再往外扩"才有意义，
    // 不用被迫一开始就想清楚最终尺寸。所有帧一起改，保持"帧与帧必须同尺寸"这条规则（
    // PixelIconEditor.BuildFrames/TryLoadFrames 都要求这个），不会出现有的帧改了有的没改
    private void ResizeCanvas(int newWidth, int newHeight)
    {
        newWidth = Math.Clamp(newWidth, MinSize, MaxSize);
        newHeight = Math.Clamp(newHeight, MinSize, MaxSize);
        if (newWidth == _width && newHeight == _height) return; // 已经是上限/下限了，+/- 没有实际效果

        for (int i = 0; i < _frames.Count; i++)
        {
            var oldGrid = _frames[i];
            var newGrid = NewBlankGrid(newWidth, newHeight);
            int copyWidth = Math.Min(_width, newWidth);
            int copyHeight = Math.Min(_height, newHeight);
            for (int y = 0; y < copyHeight; y++)
                for (int x = 0; x < copyWidth; x++)
                    newGrid[y, x] = oldGrid[y, x];
            _frames[i] = newGrid;
        }

        _width = newWidth;
        _height = newHeight;
        UpdateSizeHint(continuing: false); // 尺寸已经改过了，"续画已有图标"这个说法不再准确，跟新画的没区别
        RenderCanvas();
        RenderFrameStrip();
    }

    private static System.Collections.Generic.Dictionary<char, RgbaColor> SeedDefaultPalette()
    {
        var palette = new System.Collections.Generic.Dictionary<char, RgbaColor>();
        foreach (var (c, r, g, b) in DefaultPalette) palette[c] = new RgbaColor(255, r, g, b);
        return palette;
    }

    private static char[,] NewBlankGrid(int width, int height)
    {
        var grid = new char[height, width];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                grid[y, x] = '.';
        return grid;
    }

    private static Color ToUiColor(RgbaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private string[] GridToRows(char[,] grid)
    {
        var rows = new string[_height];
        for (int y = 0; y < _height; y++)
        {
            var chars = new char[_width];
            for (int x = 0; x < _width; x++) chars[x] = grid[y, x];
            rows[y] = new string(chars);
        }
        return rows;
    }

    // ── 调色板 ────────────────────────────────────────────────────────────────

    private void RenderPalette()
    {
        PalettePanel.Children.Clear();

        // 橡皮擦排最前面——'.' 本身不是调色板里的一个字符，是"没画"，单独一个按钮表示，
        // 选中之后点格子就是清空那一格，不是画成某种颜色
        var eraser = new Button
        {
            Content = "🧹",
            Width = 44,
            Height = 44,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromArgb(255, 0x33, 0x33, 0x33)),
            BorderBrush = _selectedChar == '.' ? new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xFF, 0xFF)) : null,
            BorderThickness = _selectedChar == '.' ? new Thickness(3) : new Thickness(0),
        };
        eraser.Click += (_, _) => { _selectedChar = '.'; RenderPalette(); };
        PalettePanel.Children.Add(eraser);

        foreach (char c in _palette.Keys.OrderBy(c => c))
        {
            var color = _palette[c];
            bool selected = c == _selectedChar;
            var swatch = new Button
            {
                Content = c.ToString(),
                Width = 44,
                Height = 44,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(ToUiColor(color)),
                Foreground = new SolidColorBrush(ToUiColor(RgbaColor.PickReadableForeground(color))),
                BorderBrush = selected ? new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xFF, 0xFF)) : null,
                BorderThickness = selected ? new Thickness(3) : new Thickness(0),
            };
            swatch.Click += (_, _) => { _selectedChar = c; RenderPalette(); };
            PalettePanel.Children.Add(swatch);
        }
    }

    private void BtnAddColor_Click(object sender, RoutedEventArgs e)
    {
        string hex = TxtNewColorHex.Text.Trim();
        if (!PixelIconEditor.TryParseHex(hex.StartsWith("#") ? hex : "#" + hex, out var color))
        {
            TxtPaletteError.Text = $"\"{hex}\" 不是合法的十六进制颜色，格式要是 #RRGGBB。";
            return;
        }

        char? next = PixelIconEditor.NextAvailableChar(_palette.Keys);
        if (next == null)
        {
            TxtPaletteError.Text = "颜色种类到上限了（45 种），先别加新颜色。";
            return;
        }

        _palette[next.Value] = color;
        _selectedChar = next.Value;
        TxtNewColorHex.Text = "";
        TxtPaletteError.Text = "";
        RenderPalette();
    }

    // ── 画布 ──────────────────────────────────────────────────────────────────

    private void RenderCanvas()
    {
        CanvasGrid.RowDefinitions.Clear();
        CanvasGrid.ColumnDefinitions.Clear();
        CanvasGrid.Children.Clear();

        for (int i = 0; i < _height; i++) CanvasGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellSize) });
        for (int i = 0; i < _width; i++) CanvasGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellSize) });

        _cellBorders = new Border[_height, _width];
        var grid = _frames[_currentFrameIndex];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                var cell = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0x33, 0x33, 0x33)),
                    BorderThickness = new Thickness(0.5),
                    Background = CellBrush(grid[y, x]),
                };
                int cx = x, cy = y; // 闭包捕获循环变量，显式拷贝一份，不然点哪个格子都会点到最后一格
                cell.Tapped += (_, _) => PaintCell(cx, cy);
                Grid.SetRow(cell, y);
                Grid.SetColumn(cell, x);
                CanvasGrid.Children.Add(cell);
                _cellBorders[y, x] = cell;
            }
        }
    }

    // 空格子用一个跟画布底色区分得开、但不是纯黑/纯白的灰色，不是真的棋盘格纹理（那需要图片资源，
    // 图省事跳过）——够用来分辨"这格没画"和"画了一个深色"，不追求跟桌面版完全一样的视觉效果
    private Brush CellBrush(char c) =>
        c != '.' && _palette.TryGetValue(c, out var color)
            ? new SolidColorBrush(ToUiColor(color))
            : new SolidColorBrush(Color.FromArgb(255, 0x24, 0x24, 0x24));

    private void PaintCell(int x, int y)
    {
        _frames[_currentFrameIndex][y, x] = _selectedChar;
        if (_cellBorders != null) _cellBorders[y, x].Background = CellBrush(_selectedChar);
    }

    // ── 帧列表 ────────────────────────────────────────────────────────────────

    private void RenderFrameStrip()
    {
        FrameStripPanel.Children.Clear();
        TxtFrameLabel.Text = $"帧：{_currentFrameIndex + 1} / {_frames.Count}";

        for (int i = 0; i < _frames.Count; i++)
        {
            var bitmap = PixelIconRenderer.Render(GridToRows(_frames[i]), _palette, scale: 4);
            bool selected = i == _currentFrameIndex;
            var thumb = new Button
            {
                Content = new Image { Source = bitmap, Width = 36, Height = 36, Stretch = Stretch.Uniform },
                Margin = new Thickness(0, 0, 8, 0),
                BorderBrush = selected ? new SolidColorBrush(Color.FromArgb(255, 0xFF, 0xFF, 0xFF)) : null,
                BorderThickness = selected ? new Thickness(3) : new Thickness(0),
            };
            int idx = i; // 闭包捕获，同样的坑
            thumb.Click += (_, _) => { _currentFrameIndex = idx; RenderCanvas(); RenderFrameStrip(); };
            FrameStripPanel.Children.Add(thumb);
        }
    }

    private void BtnAddFrame_Click(object sender, RoutedEventArgs e)
    {
        _frames.Insert(_currentFrameIndex + 1, NewBlankGrid(_width, _height));
        _currentFrameIndex++;
        RenderCanvas();
        RenderFrameStrip();
    }

    private void BtnDuplicateFrame_Click(object sender, RoutedEventArgs e)
    {
        var copy = (char[,])_frames[_currentFrameIndex].Clone();
        _frames.Insert(_currentFrameIndex + 1, copy);
        _currentFrameIndex++;
        RenderCanvas();
        RenderFrameStrip();
    }

    private void BtnDeleteFrame_Click(object sender, RoutedEventArgs e)
    {
        if (_frames.Count <= 1)
        {
            TxtInsertResult.Text = "只剩 1 帧了，画板至少要留一帧，不能再删。";
            return;
        }
        _frames.RemoveAt(_currentFrameIndex);
        if (_currentFrameIndex >= _frames.Count) _currentFrameIndex = _frames.Count - 1;
        RenderCanvas();
        RenderFrameStrip();
    }

    // ── 插入到编辑框 ──────────────────────────────────────────────────────────

    private async void BtnInsert_Click(object sender, RoutedEventArgs e)
    {
        var icon = PixelIconEditor.BuildFrames(_frames, _width, _height, _palette);
        if (_frames.Count > 1 && double.TryParse(TxtFrameDuration.Text, out double duration) && duration > 0)
        {
            icon.FrameDuration = duration;
        }

        string? merged = PixelIconEditor.TryInsertIconIntoJson(_originalThemeJson, icon);
        if (merged != null)
        {
            PendingResultJson = merged;
            if (Frame.CanGoBack) Frame.GoBack();
            return;
        }

        // 编辑框里那份 JSON 本身语法就有问题（连花括号/引号都没配对），插不进去——图标数据本身
        // 还在，退化成复制到剪贴板让用户自己贴，跟桌面版 IconPainterWindow 同样的兜底路径
        string fragment = PixelIconEditor.SerializeIconFragment(icon);
        try
        {
            var package = new DataPackage();
            package.SetText(fragment);
            Clipboard.SetContent(package);
            TxtInsertResult.Text = "编辑框里的 JSON 格式有问题，插不进去——图标片段已复制到剪贴板，自己贴到 icon 字段。";
        }
        catch
        {
            // 剪贴板偶尔会被占用，不是关键功能，失败就把片段直接显示出来让用户手动复制
            TxtInsertResult.Text = "编辑框里的 JSON 格式有问题，插不进去，剪贴板也用不了，把这段贴到 icon 字段：\n" + fragment;
        }
    }
}
