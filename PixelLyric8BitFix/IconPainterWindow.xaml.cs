using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 点格子画图标，不用再自己数字符网格——纯 UI 交互层，具体"网格状态怎么变成 JSON"全部委托给
    /// PixelIconEditor（那边的纯函数带了单元测试）。这个类只管：建格子、接鼠标事件、维护调色板 UI、
    /// 维护"帧"列表（逐帧动画）、画完之后决定"插入到编辑框"还是"只复制 JSON 片段"。
    ///
    /// 结果通过 <see cref="ResultIcon"/> + DialogResult 交回调用方（CustomThemeWindow）——DialogResult
    /// 为 true 时调用方自己决定"插入到编辑框"具体怎么做（智能替换 icon 字段，解析不了就退化成复制到
    /// 剪贴板），这个窗口本身不碰 CustomThemeWindow 的输入框，两边职责分开。
    ///
    /// 多帧模型：_frameGrids 是"每帧一份网格"的列表，_grid 永远是 _frameGrids[_currentFrameIndex] 的
    /// 引用别名（char[,] 是引用类型，画布上改 _grid[y,x] 就是在改列表里那份数组本身，不用切帧的时候
    /// 手动"存回列表"这一步）。切尺寸/调色板是所有帧共用的，改一次对全部帧生效；切帧只换"正在编辑
    /// 哪一份网格"，不影响别的帧已经画好的内容。
    /// </summary>
    public partial class IconPainterWindow : Window
    {
        private int _width = 8;
        private int _height = 8;
        private List<char[,]> _frameGrids = new();
        private int _currentFrameIndex;
        private char[,] _grid = new char[8, 8]; // 永远等于 _frameGrids[_currentFrameIndex]，切帧时重新赋值
        private readonly Dictionary<char, Color> _palette = new();
        private char? _selectedChar;
        private bool _isPainting;
        private bool _isErasing;
        private Rectangle[,] _cellShapes = new Rectangle[8, 8];
        private int _cellSize = 36;
        private bool _strokeChanged; // 这一笔（从按下到松手）有没有真的改动过任意一格——没改动的话不留撤销记录

        // 撤销/重做：每次"一笔画完/一次填充/清空/改尺寸/帧的增删复制重排"之前，把改动前的完整状态
        // （全部帧网格 + 尺寸 + 当前帧号）拷贝一份压进去。改动粒度是"一次操作"而不是"一个像素"——
        // 拖拽涂一整笔只占一条记录，撤销一次就是撤销一整笔，这也是大部分像素绘图工具的习惯。
        // 调色板本身的增删颜色故意不进撤销栈：颜色对象跟"用到它的格子"是分开的两件事，BuildIcon/
        // BuildFrames 只收真正用到的颜色，撤销漏掉一次调色板改动最多是留下几个没用上的颜色，
        // 不会导致画面本身跟撤销前对不上，用这点代价换实现简单。
        private readonly List<UndoState> _undoStack = new();
        private readonly List<UndoState> _redoStack = new();
        private const int MaxUndoDepth = 50;

        private sealed record UndoState(List<char[,]> FrameGrids, int Width, int Height, int CurrentFrameIndex);

        // 拖拽帧缩略图调整播放顺序用——记录"按下的是哪一帧"和"按下时的位置"，松手时如果鼠标没挪动
        // 超过阈值就当成普通点击（切到那一帧），挪动够了才当成拖拽重排，两种操作共用同一组鼠标事件，
        // 不用额外的"拖拽模式"开关。
        private int? _dragFrameIndex;
        private Point _dragStartPoint;
        private bool _dragMoved;
        private const double DragThreshold = 6;

        // "实际大小预览"按 frameDuration 循环播放全部帧——跟正在编辑哪一帧（_currentFrameIndex）是
        //两回事，故意分开：这样用户可以一边盯着预览看动画效果、一边切到别的帧接着画，两者不互相打扰。
        private readonly DispatcherTimer _previewFrameTimer = new();
        private int _previewFrameIndex;

        /// <summary>画完点"插入到编辑框"之后，调用方从这里取结果；DialogResult 不是 true 的话不用管这个字段。</summary>
        public CustomThemeIcon? ResultIcon { get; private set; }

        // existingIcon 非空的话表示"续画一个已有图标"（比如重新打开编辑一份已存主题时，输入框里
        // 已经有一个手写/画过的 icon 了）——用 PixelIconEditor.TryLoadFrames 摊开成网格状态（icon.frames
        // 有值就摊开全部帧，没有就退化成 icon.rows 那唯一一帧）；解析不出来（比如行宽不一致、
        // 帧与帧尺寸对不上）就当没有，从空白 8x8 单帧开始，不弹错误吓跑用户
        public IconPainterWindow(CustomThemeIcon? existingIcon = null)
        {
            InitializeComponent();
            _previewFrameTimer.Tick += PreviewFrameTimer_Tick;
            Closed += (s, e) => _previewFrameTimer.Stop();

            // 帧条本身只挂一次（不像帧缩略图那样每次 RebuildFrameStrip 都重新 new）——挂在 Children 会
            // 被清空重建的缩略图上的话，每次重建都要重新订阅，还容易忘记先取消订阅导致重复触发
            FrameStripPanel.MouseMove += FrameStripPanel_MouseMove;
            FrameStripPanel.MouseLeftButtonUp += FrameStripPanel_MouseLeftButtonUp;

            // Ctrl+Z / Ctrl+Y（或 Ctrl+Shift+Z）撤销重做——挂在 Window 上的 PreviewKeyDown 是隧道事件，
            // 会先于任何子控件收到，所以不管当前焦点在哪个控件上都能截到；焦点在 TextBox 上（比如正在
            // 改宽高/帧间隔）的时候特意放行，让文本框自己的原生撤销生效，不抢它的 Ctrl+Z
            PreviewKeyDown += IconPainterWindow_PreviewKeyDown;

            var loaded = existingIcon != null ? PixelIconEditor.TryLoadFrames(existingIcon) : null;
            double frameDuration = existingIcon != null ? CustomThemeValidator.GetFrameDurationSeconds(existingIcon) : CustomThemeValidator.DefaultFrameDurationSeconds;
            if (loaded is { } l)
            {
                _width = l.Width;
                _height = l.Height;
                _frameGrids = l.Grids;
                foreach (var (c, color) in l.Palette) _palette[c] = ToWpfColor(color);
            }
            else
            {
                SeedDefaultPalette();
                _frameGrids = new List<char[,]> { NewBlankGrid(_height, _width) };
            }
            _currentFrameIndex = 0;
            _grid = _frameGrids[0];

            TxtWidth.Text = _width.ToString();
            TxtHeight.Text = _height.ToString();
            TxtFrameDuration.Text = frameDuration.ToString("0.##", CultureInfo.InvariantCulture);

            RebuildPaletteUi();
            _selectedChar ??= _palette.Count > 0 ? new List<char>(_palette.Keys)[0] : null;
            RebuildCanvas();
            RebuildFrameStrip();
            UpdatePreviewPlayback();

            // 放在最后才订阅——见 XAML 里 TxtFrameDuration 那段注释：这个 TextBox 的初始值是在
            // InitializeComponent() 期间赋值的，那会儿要是已经接了这个处理器，会在 LivePreviewIcon/
            // TxtPreviewFrameHint 这些排在它后面的控件还没 Connect() 之前就摸上去，直接崩。
            // 到这里其它字段/控件都已经就绪，以后用户自己编辑这个框才会走这个处理器。
            TxtFrameDuration.TextChanged += TxtFrameDuration_TextChanged;
        }

        // 8 个够用又不重复的常见颜色，给"从空白开始画"的情况垫底——不用一打开画板就面对一个空调色板
        // 不知道从哪下手。续画已有图标的时候不会调用这个，直接用那份图标自己的调色板。
        private void SeedDefaultPalette()
        {
            _palette['#'] = Color.FromRgb(0xF9, 0xC7, 0x84);
            _palette['w'] = Colors.White;
            _palette['k'] = Color.FromRgb(0x1A, 0x1A, 0x1A);
            _palette['r'] = Color.FromRgb(0xE7, 0x4C, 0x3C);
            _palette['g'] = Color.FromRgb(0x2E, 0xCC, 0x71);
            _palette['b'] = Color.FromRgb(0x34, 0x98, 0xDB);
            _palette['y'] = Color.FromRgb(0xF1, 0xC4, 0x0F);
            _palette['p'] = Color.FromRgb(0x9B, 0x59, 0xB6);
        }

        private static char[,] NewBlankGrid(int height, int width)
        {
            var grid = new char[height, width];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    grid[y, x] = '.';
            return grid;
        }

        // PixelIconEditor（连同 CustomTheme/CustomThemeValidator）现在住在 PixelLyric8Bit.Core，颜色用的是
        // 平台无关的 RgbaColor，不是这边到处在用的 System.Windows.Media.Color——这几个小转换方法就是
        // 两边的桥，调 PixelIconEditor 的地方把 _palette（WPF Color）转一下再传，拿到结果再转回来，
        // 这个类自己内部（渲染、_palette 字段本身）完全不用碰 RgbaColor，只在跟 Core 打交道的边界转一次。
        private static Color ToWpfColor(RgbaColor c) => Color.FromArgb(c.A, c.R, c.G, c.B);
        private static RgbaColor ToRgba(Color c) => new(c.A, c.R, c.G, c.B);
        private static Dictionary<char, RgbaColor> ToRgbaPalette(IReadOnlyDictionary<char, Color> palette) =>
            palette.ToDictionary(kv => kv.Key, kv => ToRgba(kv.Value));

        // ── 撤销/重做 ─────────────────────────────────────────────────────

        private UndoState CaptureState() => new(
            _frameGrids.Select(g => (char[,])g.Clone()).ToList(), // char[,] 是引用类型，不 Clone 的话快照跟"现在"是同一份数组，后面一改这份快照也跟着变
            _width, _height, _currentFrameIndex);

        /// <summary>在"即将发生一次改动"之前调用，把改动前的状态存一份。任何新操作发生都会让"重做"
        /// 失去意义（改动前的未来已经变了），所以顺手清空 _redoStack——标准撤销栈的行为。</summary>
        private void PushUndo()
        {
            _undoStack.Add(CaptureState());
            if (_undoStack.Count > MaxUndoDepth) _undoStack.RemoveAt(0); // List 从头部删一个，栈深超过上限时把最旧的一条挤掉
            _redoStack.Clear();
        }

        private void ApplyState(UndoState s)
        {
            _frameGrids = s.FrameGrids;
            _width = s.Width;
            _height = s.Height;
            _currentFrameIndex = Math.Min(s.CurrentFrameIndex, _frameGrids.Count - 1);
            _grid = _frameGrids[_currentFrameIndex];
            TxtWidth.Text = _width.ToString();
            TxtHeight.Text = _height.ToString();
            RebuildCanvas();
            RebuildFrameStrip();
            UpdatePreviewPlayback();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Add(CaptureState()); // 撤销之前先把"现在"存进重做栈，不然撤完就再也回不来了
            var previous = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            ApplyState(previous);
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Add(CaptureState());
            var next = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            ApplyState(next);
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e) => Undo();
        private void BtnRedo_Click(object sender, RoutedEventArgs e) => Redo();

        private void IconPainterWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            if (Keyboard.FocusedElement is TextBox) return; // 尺寸/帧间隔输入框有自己的原生撤销，不抢

            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            if (e.Key == Key.Z && !shift) { Undo(); e.Handled = true; }
            else if (e.Key == Key.Y || (e.Key == Key.Z && shift)) { Redo(); e.Handled = true; } // Ctrl+Y 和 Ctrl+Shift+Z 都认，两种习惯都覆盖
        }

        // ── 调色板 UI ──────────────────────────────────────────────────────

        private void RebuildPaletteUi()
        {
            PalettePanel.Children.Clear();
            foreach (var (c, color) in _palette)
            {
                bool selected = _selectedChar == c;
                var swatch = new Border
                {
                    Width = 30,
                    Height = 30,
                    Margin = new Thickness(0, 0, 6, 6),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(color),
                    BorderBrush = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x2B)),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    Cursor = Cursors.Hand,
                    ToolTip = $"{PixelIconEditor.ColorToHex(ToRgba(color))}（{c}）— 左键选中，右键删除",
                };
                char capturedChar = c;
                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    _selectedChar = capturedChar;
                    RebuildPaletteUi(); // 重画一遍只是为了更新选中边框，调色板项不多，这点开销可以忽略
                };
                swatch.MouseRightButtonDown += (s, e) =>
                {
                    e.Handled = true; // 不弹右键菜单
                    TryDeleteColor(capturedChar);
                };
                PalettePanel.Children.Add(swatch);
            }
        }

        // 画布上（不管哪一帧）还在用这个颜色的话不让删——删掉会让那些格子的字符在调色板里找不到对应
        // 颜色，画出来变透明，等于悄悄改坏了用户已经画好的东西。让用户自己先换掉/擦掉再删更安全。
        private void TryDeleteColor(char c)
        {
            if (_frameGrids.Any(g => GridUsesChar(g, c)))
            {
                TxtSizeHint.Text = "这个颜色还在某一帧的画布上用到，先把用到它的格子换成别的颜色或者擦掉，再删这个颜色。";
                TxtSizeHint.Foreground = System.Windows.Media.Brushes.LightPink;
                return;
            }

            _palette.Remove(c);
            if (_selectedChar == c) _selectedChar = _palette.Count > 0 ? _palette.Keys.First() : null;
            RebuildPaletteUi();
            TxtSizeHint.Text = "✅ 已删除这个颜色。";
            TxtSizeHint.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0xE0, 0x8A));
        }

        private static bool GridUsesChar(char[,] grid, char c)
        {
            int h = grid.GetLength(0), w = grid.GetLength(1);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (grid[y, x] == c) return true;
            return false;
        }

        private void BtnAddColor_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var picked = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            string pickedHex = PixelIconEditor.ColorToHex(ToRgba(picked));

            // 已经有一模一样的颜色了就直接选中它，不重复分配一个新字符——不然调色板里会出现
            // 两个字符对应同一个颜色，浪费掉本来就有限的可分配字符池
            foreach (var (c, color) in _palette)
            {
                if (PixelIconEditor.ColorToHex(ToRgba(color)) == pickedHex)
                {
                    _selectedChar = c;
                    RebuildPaletteUi();
                    return;
                }
            }

            char? next = PixelIconEditor.NextAvailableChar(_palette.Keys);
            if (next == null)
            {
                TxtSizeHint.Text = "颜色种类已经到上限了（45 种），删不掉旧的只能先将就用现有的。";
                TxtSizeHint.Foreground = System.Windows.Media.Brushes.LightPink;
                return;
            }

            _palette[next.Value] = picked;
            _selectedChar = next.Value;
            RebuildPaletteUi();
        }

        // ── 画布 ──────────────────────────────────────────────────────────

        private void RebuildCanvas()
        {
            // 画布区域大致固定在 360x360 上下，格子边长按网格尺寸反推：8x8 这种小网格格子给到 36px
            // 上限（太大也没意义，纯粹占地方），64x64 这种大网格夹到 6px 下限（再小鼠标就点不准了）
            _cellSize = Math.Clamp(360 / Math.Max(_width, _height), 6, 36);

            var uniformGrid = new UniformGrid { Rows = _height, Columns = _width, Background = Brushes.Transparent };
            _cellShapes = new Rectangle[_height, _width];
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    var rect = new Rectangle
                    {
                        Width = _cellSize,
                        Height = _cellSize,
                        Fill = ResolveBrush(_grid[y, x]),
                        Stroke = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
                        StrokeThickness = 0.5,
                    };
                    _cellShapes[y, x] = rect;
                    uniformGrid.Children.Add(rect);
                }
            }

            uniformGrid.MouseLeftButtonDown += (s, e) =>
            {
                if (RbToolFill.IsChecked == true) { FillAtPosition(e.GetPosition(uniformGrid), erase: false); return; }
                PushUndo();
                _strokeChanged = false;
                _isPainting = true; _isErasing = false;
                PaintAtPosition(e.GetPosition(uniformGrid));
                uniformGrid.CaptureMouse();
            };
            uniformGrid.MouseRightButtonDown += (s, e) =>
            {
                e.Handled = true; // 不弹右键菜单
                if (RbToolFill.IsChecked == true) { FillAtPosition(e.GetPosition(uniformGrid), erase: true); return; }
                PushUndo();
                _strokeChanged = false;
                _isPainting = true; _isErasing = true;
                PaintAtPosition(e.GetPosition(uniformGrid));
                uniformGrid.CaptureMouse();
            };
            uniformGrid.MouseMove += (s, e) =>
            {
                var pos = e.GetPosition(uniformGrid);
                if (_isPainting) PaintAtPosition(pos);
                UpdateHoverCoord(pos);
            };
            uniformGrid.MouseLeave += (s, e) => TxtCellCoord.Text = "";
            uniformGrid.MouseLeftButtonUp += (s, e) => EndStroke(uniformGrid);
            uniformGrid.MouseRightButtonUp += (s, e) => EndStroke(uniformGrid);

            PaintGridHost.Children.Clear();
            // 洋葱皮先加（在下层），画布网格后加（在上层）——UniformGrid 里没画的格子是透明矩形，
            // 透过它们能看到下层这份半透明的上一帧内容当参照；已经画上颜色的格子会正常盖住下层，
            // 不会让当前帧的画面看起来"脏"。IsHitTestVisible=false 保证它不会挡住画布本身的点击。
            if (ChkOnionSkin.IsChecked == true && _currentFrameIndex > 0)
            {
                var onion = new Image
                {
                    Source = BuildFrameBitmap(_frameGrids[_currentFrameIndex - 1]),
                    Width = _cellSize * _width,
                    Height = _cellSize * _height,
                    Opacity = 0.35,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false,
                };
                RenderOptions.SetBitmapScalingMode(onion, BitmapScalingMode.NearestNeighbor);
                PaintGridHost.Children.Add(onion);
            }
            PaintGridHost.Children.Add(uniformGrid);
        }

        private void ChkOnionSkin_Changed(object sender, RoutedEventArgs e) => RebuildCanvas();

        private void UpdateHoverCoord(Point p)
        {
            int x = Math.Clamp((int)(p.X / _cellSize), 0, _width - 1);
            int y = Math.Clamp((int)(p.Y / _cellSize), 0, _height - 1);
            TxtCellCoord.Text = $"格子 ({x + 1}, {y + 1}) / {_width}×{_height}";
        }

        private void EndStroke(UIElement host)
        {
            if (!_isPainting) return;
            _isPainting = false;
            host.ReleaseMouseCapture();
            if (!_strokeChanged && _undoStack.Count > 0) _undoStack.RemoveAt(_undoStack.Count - 1); // 这一笔什么都没改，撤销栈里刚推的那份快照留着没意义
            RebuildFrameStrip(); // 当前帧的缩略图要跟着更新——画完一整笔（松手）才重建，不是每移动一格就重建
            UpdatePreviewPlayback();
        }

        private void PaintAtPosition(Point p)
        {
            int x = Math.Clamp((int)(p.X / _cellSize), 0, _width - 1);
            int y = Math.Clamp((int)(p.Y / _cellSize), 0, _height - 1);
            char target = _isErasing ? '.' : (_selectedChar ?? '.');

            PaintCell(x, y, target);

            if (ChkMirror.IsChecked == true)
            {
                int mirroredX = _width - 1 - x;
                PaintCell(mirroredX, y, target); // 宽度是奇数、点在正中间那一列的话 mirroredX == x，
                                                   // PaintCell 内部的"没变化就跳过"会自然处理，不用特判
            }
        }

        private void PaintCell(int x, int y, char target)
        {
            if (_grid[y, x] == target) return; // 没变化不用重画这一格
            _grid[y, x] = target;
            _cellShapes[y, x].Fill = ResolveBrush(target);
            _strokeChanged = true;
        }

        // 桶装填充：单击一下（不是拖拽）触发，从点到的格子出发做四连通 flood fill——具体算法在
        // PixelIconEditor.FloodFill（纯函数，带单元测试），这里只管鼠标坐标转格子坐标、推撤销记录、
        // 填完之后重画整个画布（flood fill 可能一次改掉一大片格子，不值得每格单独判断要不要重画）。
        private void FillAtPosition(Point p, bool erase)
        {
            int x = Math.Clamp((int)(p.X / _cellSize), 0, _width - 1);
            int y = Math.Clamp((int)(p.Y / _cellSize), 0, _height - 1);
            char target = erase ? '.' : (_selectedChar ?? '.');

            PushUndo();
            bool changed = PixelIconEditor.FloodFill(_grid, _width, _height, x, y, target);
            if (ChkMirror.IsChecked == true)
            {
                int mirroredX = _width - 1 - x;
                changed |= PixelIconEditor.FloodFill(_grid, _width, _height, mirroredX, y, target);
            }

            if (!changed)
            {
                _undoStack.RemoveAt(_undoStack.Count - 1); // 目标颜色本来就跟起点一样，没有任何格子被改，撤销记录留着没意义
                return;
            }

            RedrawAllCells();
            RebuildFrameStrip();
            UpdatePreviewPlayback();
        }

        private void RedrawAllCells()
        {
            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                    _cellShapes[y, x].Fill = ResolveBrush(_grid[y, x]);
        }

        private Brush ResolveBrush(char c) =>
            c != '.' && _palette.TryGetValue(c, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

        // ── 帧（逐帧动画） ────────────────────────────────────────────────

        // 缩略图跟"实际大小预览"用的是同一份渲染逻辑（PixelArt.BuildCustomIcon），保证帧列表里看到的
        // 缩略图跟真正播放出来的样子是一致的，不是简化版
        private BitmapSource BuildFrameBitmap(char[,] grid)
        {
            var rows = new string[_height];
            for (int y = 0; y < _height; y++)
            {
                var chars = new char[_width];
                for (int x = 0; x < _width; x++) chars[x] = grid[y, x];
                rows[y] = new string(chars);
            }
            return PixelArt.BuildCustomIcon(rows, _palette);
        }

        private void RebuildFrameStrip()
        {
            FrameStripPanel.Children.Clear();
            for (int i = 0; i < _frameGrids.Count; i++)
            {
                bool selected = i == _currentFrameIndex;
                var image = new Image { Source = BuildFrameBitmap(_frameGrids[i]), Stretch = Stretch.Uniform, Margin = new Thickness(4) };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);

                var thumb = new Border
                {
                    Width = 40,
                    Height = 40,
                    Margin = new Thickness(0, 0, 6, 6),
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x0F)),
                    BorderBrush = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x2B)),
                    BorderThickness = new Thickness(selected ? 2 : 1),
                    Cursor = Cursors.Hand,
                    ToolTip = $"第 {i + 1} 帧（点击切换到这一帧继续画）",
                    Child = image,
                };
                int capturedIndex = i;
                // 按下先不做事——等松手时再判断这是"点击切帧"还是"拖拽重排"（见 FrameStripPanel_MouseLeftButtonUp），
                // 两种操作共用同一次按下/松手，靠鼠标有没有挪动够距离区分
                thumb.MouseLeftButtonDown += (s, e) =>
                {
                    _dragFrameIndex = capturedIndex;
                    _dragStartPoint = e.GetPosition(FrameStripPanel);
                    _dragMoved = false;
                    FrameStripPanel.CaptureMouse();
                    e.Handled = true;
                };
                FrameStripPanel.Children.Add(thumb);
            }
        }

        private void FrameStripPanel_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_dragFrameIndex == null || _dragMoved) return;
            var pos = e.GetPosition(FrameStripPanel);
            if (Math.Abs(pos.X - _dragStartPoint.X) > DragThreshold || Math.Abs(pos.Y - _dragStartPoint.Y) > DragThreshold)
                _dragMoved = true;
        }

        private void FrameStripPanel_MouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
        {
            if (_dragFrameIndex == null) return;
            int from = _dragFrameIndex.Value;
            bool moved = _dragMoved;
            _dragFrameIndex = null;
            _dragMoved = false;
            FrameStripPanel.ReleaseMouseCapture();

            if (!moved) { SwitchToFrame(from); return; } // 没挪够距离，当成普通点击切帧

            int? to = HitTestFrameIndex(e.GetPosition(FrameStripPanel));
            if (to == null || to == from) return; // 拖到帧条外面/放回原位，什么都不做
            ReorderFrame(from, to.Value);
        }

        // 从拖放落点反查是"帧条里第几个缩略图"——命中测试出来的可能是缩略图内部的 Image（缩略图的
        // Child），从命中的元素往上找，直到找到一个确实是 FrameStripPanel 直接子元素的 Border 为止
        private int? HitTestFrameIndex(Point panelPos)
        {
            var result = VisualTreeHelper.HitTest(FrameStripPanel, panelPos);
            DependencyObject? node = result?.VisualHit;
            while (node != null && !(node is Border border && FrameStripPanel.Children.Contains(border)))
                node = VisualTreeHelper.GetParent(node);
            return node is Border matched ? FrameStripPanel.Children.IndexOf(matched) : (int?)null;
        }

        private void ReorderFrame(int from, int to)
        {
            PushUndo();
            var moved = _frameGrids[from];
            _frameGrids.RemoveAt(from);
            _frameGrids.Insert(to, moved);
            _currentFrameIndex = to; // 拖完之后跟着挪过去的这一帧走，不留在旧位置上
            _grid = _frameGrids[_currentFrameIndex];
            RebuildFrameStrip();
            UpdatePreviewPlayback();
        }

        private void SwitchToFrame(int index)
        {
            if (index < 0 || index >= _frameGrids.Count || index == _currentFrameIndex) return;
            _currentFrameIndex = index;
            _grid = _frameGrids[index];
            RebuildCanvas();
            RebuildFrameStrip(); // 重画一遍只是为了更新选中高亮
        }

        private void BtnAddFrame_Click(object sender, RoutedEventArgs e)
        {
            PushUndo();
            _frameGrids.Insert(_currentFrameIndex + 1, NewBlankGrid(_height, _width));
            _currentFrameIndex++;
            _grid = _frameGrids[_currentFrameIndex];
            RebuildCanvas();
            RebuildFrameStrip();
            UpdatePreviewPlayback();
        }

        // 走路循环这类动画大部分帧只是小范围挪动，从上一帧复制改会比每次从空白开始快得多
        private void BtnDuplicateFrame_Click(object sender, RoutedEventArgs e)
        {
            PushUndo();
            var copy = (char[,])_frameGrids[_currentFrameIndex].Clone(); // 元素是 char 值类型，Clone() 浅拷贝就够了
            _frameGrids.Insert(_currentFrameIndex + 1, copy);
            _currentFrameIndex++;
            _grid = _frameGrids[_currentFrameIndex];
            RebuildCanvas();
            RebuildFrameStrip();
            UpdatePreviewPlayback();
        }

        private void BtnDeleteFrame_Click(object sender, RoutedEventArgs e)
        {
            if (_frameGrids.Count <= 1) return; // 至少留 1 帧，不允许删空——没有"删完之后画布显示什么"这个状态
            PushUndo();
            _frameGrids.RemoveAt(_currentFrameIndex);
            _currentFrameIndex = Math.Min(_currentFrameIndex, _frameGrids.Count - 1);
            _grid = _frameGrids[_currentFrameIndex];
            RebuildCanvas();
            RebuildFrameStrip();
            UpdatePreviewPlayback();
        }

        private void TxtFrameDuration_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreviewPlayback();

        // ── 尺寸 / 清空 / 预览 ────────────────────────────────────────────

        private void BtnApplySize_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtWidth.Text, out int w) || !int.TryParse(TxtHeight.Text, out int h) ||
                w < CustomThemeValidator.MinIconSize || w > CustomThemeValidator.MaxIconSize ||
                h < CustomThemeValidator.MinIconSize || h > CustomThemeValidator.MaxIconSize)
            {
                TxtSizeHint.Text = $"宽/高都得是 {CustomThemeValidator.MinIconSize}~{CustomThemeValidator.MaxIconSize} 之间的整数。";
                TxtSizeHint.Foreground = System.Windows.Media.Brushes.LightPink;
                return;
            }

            TxtSizeHint.Text = "4~64 之间，改尺寸会清空全部帧的内容，先想好再改。";
            TxtSizeHint.Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x5E)); // 跟 App.xaml 的 HintTextBrush 同一个颜色

            // 尺寸是所有帧共用的（校验要求所有帧彼此同尺寸），改一次对全部帧生效——不是只清空当前这一帧，
            // 不然帧与帧尺寸就对不上了
            PushUndo();
            _width = w;
            _height = h;
            _frameGrids = _frameGrids.Select(_ => NewBlankGrid(_height, _width)).ToList();
            _grid = _frameGrids[_currentFrameIndex];
            RebuildCanvas();
            RebuildFrameStrip();
            UpdatePreviewPlayback();
        }

        // 只清空当前正在编辑的这一帧，不动别的帧——"清空"是针对"手上这张画布"的操作，
        // 不该把整个动画的其它帧一起清没了
        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            PushUndo();
            _frameGrids[_currentFrameIndex] = NewBlankGrid(_height, _width);
            _grid = _frameGrids[_currentFrameIndex];
            RebuildCanvas();
            RebuildFrameStrip();
            UpdatePreviewPlayback();
        }

        // 只有 1 帧就是静态预览（不启动计时器）；2 帧以上按 TxtFrameDuration 的间隔循环播放全部帧——
        // 每次内容/帧数/间隔变化（画完一笔、加/删/复制帧、改帧间隔）都重新调用这个方法，从头播一遍，
        // 不会累积出"计时器跑歪了"的状态
        private void UpdatePreviewPlayback()
        {
            _previewFrameTimer.Stop();
            _previewFrameIndex = 0;
            LivePreviewIcon.Source = _frameGrids.Count > 0 ? BuildFrameBitmap(_frameGrids[0]) : null;

            if (_frameGrids.Count <= 1)
            {
                TxtPreviewFrameHint.Text = "";
                return;
            }

            TxtPreviewFrameHint.Text = $"循环播放中，共 {_frameGrids.Count} 帧";
            double seconds = double.TryParse(TxtFrameDuration.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0
                ? parsed
                : CustomThemeValidator.DefaultFrameDurationSeconds;
            _previewFrameTimer.Interval = TimeSpan.FromSeconds(seconds);
            _previewFrameTimer.Start();
        }

        private void PreviewFrameTimer_Tick(object? sender, EventArgs e)
        {
            if (_frameGrids.Count <= 1) { _previewFrameTimer.Stop(); return; }
            _previewFrameIndex = (_previewFrameIndex + 1) % _frameGrids.Count;
            LivePreviewIcon.Source = BuildFrameBitmap(_frameGrids[_previewFrameIndex]);
        }

        // ── 导入图片 / 导出 PNG ──────────────────────────────────────────────

        // 把任意一张图片按当前网格尺寸"像素化"塞进当前帧——只替换当前正在编辑的这一帧，不动别的帧，
        // 跟"清空"是同一个作用范围原则。具体的降采样+颜色量化算法在 PixelIconEditor.QuantizeToGrid
        // （纯函数，带单元测试），这里只管：弹文件选择框、把图片解码成像素矩阵、调用量化、把结果塞回来。
        private void BtnImportImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入图片转成像素图标",
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            };
            if (dialog.ShowDialog() != true) return;

            RgbaColor?[,] pixels;
            try
            {
                pixels = DecodeImageToPixelGrid(dialog.FileName, _width, _height);
            }
            catch
            {
                TxtSizeHint.Text = "这张图片读不出来，可能格式不支持或者文件已经损坏。";
                TxtSizeHint.Foreground = System.Windows.Media.Brushes.LightPink;
                return;
            }

            PushUndo();
            // 最坏情况下图片里的颜色一个都没法复用调色板里已有的，budget 按"还剩多少个字符可以分配"算，
            // 保证 QuantizeToGrid 无论如何都不会超过 45 种颜色的硬上限（见 PixelIconEditor.AssignableChars）
            int budget = Math.Max(1, PixelIconEditor.AssignableChars.Length - _palette.Count);
            var (grid, newColors) = PixelIconEditor.QuantizeToGrid(pixels, ToRgbaPalette(_palette), budget);
            foreach (var (c, color) in newColors) _palette[c] = ToWpfColor(color);
            _frameGrids[_currentFrameIndex] = grid;
            _grid = grid;
            _selectedChar ??= _palette.Count > 0 ? _palette.Keys.First() : null;

            RebuildPaletteUi();
            RebuildCanvas();
            RebuildFrameStrip();
            UpdatePreviewPlayback();
            TxtSizeHint.Text = newColors.Count > 0
                ? $"✅ 已按当前 {_width}×{_height} 尺寸导入这张图片（新增了 {newColors.Count} 种颜色，只替换了当前这一帧）。"
                : $"✅ 已按当前 {_width}×{_height} 尺寸导入这张图片（颜色复用了已有调色板，只替换了当前这一帧）。";
            TxtSizeHint.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0xE0, 0x8A));
        }

        // 把图片解码成 targetWidth×targetHeight 的颜色矩阵——每个目标格子取它对应的那一块原图像素的
        // 平均颜色（区块平均降采样），比最近邻更适合把普通照片"像素化"，不会因为凑巧采样到一个
        // 边缘像素就整格颜色跑偏。平均下来的透明度低于 64（约 25%）就当这一格是空的（null）。
        private static RgbaColor?[,] DecodeImageToPixelGrid(string path, int targetWidth, int targetHeight)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad; // 立刻把文件整个读进内存，方法返回之后不再依赖这个文件句柄
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();

            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int srcW = converted.PixelWidth, srcH = converted.PixelHeight;
            var raw = new byte[srcH * srcW * 4];
            converted.CopyPixels(raw, srcW * 4, 0);

            var result = new RgbaColor?[targetHeight, targetWidth];
            for (int ty = 0; ty < targetHeight; ty++)
            {
                int sy0 = ty * srcH / targetHeight;
                int sy1 = Math.Max(sy0 + 1, (ty + 1) * srcH / targetHeight);
                for (int tx = 0; tx < targetWidth; tx++)
                {
                    int sx0 = tx * srcW / targetWidth;
                    int sx1 = Math.Max(sx0 + 1, (tx + 1) * srcW / targetWidth);

                    long sumR = 0, sumG = 0, sumB = 0, sumA = 0;
                    int count = 0;
                    for (int sy = sy0; sy < sy1 && sy < srcH; sy++)
                    {
                        for (int sx = sx0; sx < sx1 && sx < srcW; sx++)
                        {
                            int idx = (sy * srcW + sx) * 4; // BGRA 排列
                            sumB += raw[idx]; sumG += raw[idx + 1]; sumR += raw[idx + 2]; sumA += raw[idx + 3];
                            count++;
                        }
                    }

                    if (count == 0) { result[ty, tx] = null; continue; }
                    byte avgA = (byte)(sumA / count);
                    result[ty, tx] = avgA < 64 ? null : new RgbaColor(255, (byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count));
                }
            }
            return result;
        }

        // 导出当前帧为一张放大过的 PNG——直接导出 8x8/16x16 这种原始像素尺寸的图片在大部分看图工具里
        // 会小到看不清，固定放大 16 倍（最近邻，保持硬边缘，不会因为缩放变模糊）之后再存盘。
        private const int ExportScale = 16;

        private void BtnExportPng_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出当前帧为图片",
                Filter = "PNG 图片 (*.png)|*.png",
                FileName = "ZipPlay-像素图标.png",
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var scaled = ScaleNearestNeighbor(BuildFrameBitmap(_grid), ExportScale);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(scaled));
                using var stream = File.Create(dialog.FileName);
                encoder.Save(stream);
                TxtSizeHint.Text = "✅ 已导出当前帧为 PNG。";
                TxtSizeHint.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0xE0, 0x8A));
            }
            catch
            {
                TxtSizeHint.Text = "导出失败，可能是文件被占用或者没有写入权限。";
                TxtSizeHint.Foreground = System.Windows.Media.Brushes.LightPink;
            }
        }

        // 手动逐像素放大（每个源像素变成 scale×scale 的一块实心色块），不用 WPF 自带的缩放变换——
        // 那些默认走双线性插值，边缘会被磨糊，像素画放大最讲究的就是保留硬边缘
        private static BitmapSource ScaleNearestNeighbor(BitmapSource source, int scale)
        {
            int w = source.PixelWidth, h = source.PixelHeight;
            var src32 = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            var srcPixels = new byte[w * h * 4];
            src32.CopyPixels(srcPixels, w * 4, 0);

            int outW = w * scale, outH = h * scale;
            var outPixels = new byte[outW * outH * 4];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int srcIdx = (y * w + x) * 4;
                    for (int dy = 0; dy < scale; dy++)
                    {
                        int outRowStart = ((y * scale + dy) * outW + x * scale) * 4;
                        for (int dx = 0; dx < scale; dx++)
                        {
                            Array.Copy(srcPixels, srcIdx, outPixels, outRowStart + dx * 4, 4);
                        }
                    }
                }
            }

            var result = new WriteableBitmap(outW, outH, 96, 96, PixelFormats.Bgra32, null);
            result.WritePixels(new Int32Rect(0, 0, outW, outH), outPixels, outW * 4, 0);
            return result;
        }

        // ── 结果 ──────────────────────────────────────────────────────────

        // 只有 2 帧以上才带 frameDuration——1 帧的话这个字段没有意义，不写进去，产出的 JSON
        // 保持跟这个功能加进来之前一样干净
        private CustomThemeIcon BuildResultIcon()
        {
            var icon = PixelIconEditor.BuildFrames(_frameGrids, _width, _height, ToRgbaPalette(_palette));
            if (_frameGrids.Count > 1)
            {
                icon.FrameDuration = double.TryParse(TxtFrameDuration.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) && parsed > 0
                    ? parsed
                    : CustomThemeValidator.DefaultFrameDurationSeconds;
            }
            return icon;
        }

        private void BtnInsert_Click(object sender, RoutedEventArgs e)
        {
            ResultIcon = BuildResultIcon();
            DialogResult = true;
        }

        private void BtnCopyFragment_Click(object sender, RoutedEventArgs e)
        {
            var icon = BuildResultIcon();
            string fragment = PixelIconEditor.SerializeIconFragment(icon);
            try { Clipboard.SetText(fragment); } catch { /* 剪贴板偶尔被占用，不是关键功能，失败就算了 */ }

            // layers[i].icon 目前不支持逐帧动画（只显示第一帧静态），复制多帧片段过去粘贴依然合法，
            // 只是贴过去之后动不了——这句提示只在真的是多帧的时候多说一句，单帧完全不受影响
            TxtSizeHint.Text = _frameGrids.Count > 1
                ? "✅ 已复制 JSON 片段到剪贴板，粘到 layers[i].icon（或者任何需要一个 icon 对象的地方）就行——提醒一下，layers 目前还不支持逐帧动画，粘过去只会显示第一帧。"
                : "✅ 已复制 JSON 片段到剪贴板，粘到 layers[i].icon（或者任何需要一个 icon 对象的地方）就行。";
            TxtSizeHint.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0xE0, 0x8A));
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
