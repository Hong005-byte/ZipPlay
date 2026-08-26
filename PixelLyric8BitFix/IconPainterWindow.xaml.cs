using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 点格子画图标，不用再自己数字符网格——纯 UI 交互层，具体"网格状态怎么变成 JSON"全部委托给
    /// PixelIconEditor（那边的纯函数带了单元测试）。这个类只管：建格子、接鼠标事件、维护调色板 UI、
    /// 画完之后决定"插入到编辑框"还是"只复制 JSON 片段"。
    ///
    /// 结果通过 <see cref="ResultIcon"/> + DialogResult 交回调用方（CustomThemeWindow）——DialogResult
    /// 为 true 时调用方自己决定"插入到编辑框"具体怎么做（智能替换 icon 字段，解析不了就退化成复制到
    /// 剪贴板），这个窗口本身不碰 CustomThemeWindow 的输入框，两边职责分开。
    /// </summary>
    public partial class IconPainterWindow : Window
    {
        private int _width = 8;
        private int _height = 8;
        private char[,] _grid = new char[8, 8];
        private readonly Dictionary<char, Color> _palette = new();
        private char? _selectedChar;
        private bool _isPainting;
        private bool _isErasing;
        private Rectangle[,] _cellShapes = new Rectangle[8, 8];
        private int _cellSize = 36;

        /// <summary>画完点"插入到编辑框"之后，调用方从这里取结果；DialogResult 不是 true 的话不用管这个字段。</summary>
        public CustomThemeIcon? ResultIcon { get; private set; }

        // existingIcon 非空的话表示"续画一个已有图标"（比如重新打开编辑一份已存主题时，输入框里
        // 已经有一个手写/画过的 icon 了）——用 PixelIconEditor.TryLoadIcon 摊开成网格状态；
        // 解析不出来（比如手写的行宽不一致）就当没有，从空白 8x8 开始，不弹错误吓跑用户
        public IconPainterWindow(CustomThemeIcon? existingIcon = null)
        {
            InitializeComponent();

            var loaded = existingIcon != null ? PixelIconEditor.TryLoadIcon(existingIcon) : null;
            if (loaded is { } l)
            {
                _width = l.Width;
                _height = l.Height;
                _grid = l.Grid;
                foreach (var (c, color) in l.Palette) _palette[c] = color;
            }
            else
            {
                SeedDefaultPalette();
                ClearGrid();
            }

            TxtWidth.Text = _width.ToString();
            TxtHeight.Text = _height.ToString();

            RebuildPaletteUi();
            _selectedChar ??= _palette.Count > 0 ? new List<char>(_palette.Keys)[0] : null;
            RebuildCanvas();
            UpdateLivePreview();
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

        private void ClearGrid()
        {
            _grid = new char[_height, _width];
            for (int y = 0; y < _height; y++)
                for (int x = 0; x < _width; x++)
                    _grid[y, x] = '.';
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
                    ToolTip = $"{PixelIconEditor.ColorToHex(color)}（{c}）",
                };
                char capturedChar = c;
                swatch.MouseLeftButtonDown += (s, e) =>
                {
                    _selectedChar = capturedChar;
                    RebuildPaletteUi(); // 重画一遍只是为了更新选中边框，调色板项不多，这点开销可以忽略
                };
                PalettePanel.Children.Add(swatch);
            }
        }

        private void BtnAddColor_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var picked = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
            string pickedHex = PixelIconEditor.ColorToHex(picked);

            // 已经有一模一样的颜色了就直接选中它，不重复分配一个新字符——不然调色板里会出现
            // 两个字符对应同一个颜色，浪费掉本来就有限的可分配字符池
            foreach (var (c, color) in _palette)
            {
                if (PixelIconEditor.ColorToHex(color) == pickedHex)
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

            uniformGrid.MouseLeftButtonDown += (s, e) => { _isPainting = true; _isErasing = false; PaintAtPosition(e.GetPosition(uniformGrid)); uniformGrid.CaptureMouse(); };
            uniformGrid.MouseRightButtonDown += (s, e) => { _isPainting = true; _isErasing = true; PaintAtPosition(e.GetPosition(uniformGrid)); uniformGrid.CaptureMouse(); e.Handled = true; }; // 不弹右键菜单
            uniformGrid.MouseMove += (s, e) => { if (_isPainting) PaintAtPosition(e.GetPosition(uniformGrid)); };
            uniformGrid.MouseLeftButtonUp += (s, e) => EndStroke(uniformGrid);
            uniformGrid.MouseRightButtonUp += (s, e) => EndStroke(uniformGrid);

            PaintGridHost.Children.Clear();
            PaintGridHost.Children.Add(uniformGrid);
        }

        private void EndStroke(UIElement host)
        {
            if (!_isPainting) return;
            _isPainting = false;
            host.ReleaseMouseCapture();
            UpdateLivePreview(); // 画完一整笔（松手）才重新生成预览图，不是每移动一格就重建一次位图
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
        }

        private Brush ResolveBrush(char c) =>
            c != '.' && _palette.TryGetValue(c, out var color) ? new SolidColorBrush(color) : Brushes.Transparent;

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

            TxtSizeHint.Text = "4~64 之间，改尺寸会清空当前画的内容，先想好再改。";
            TxtSizeHint.Foreground = new SolidColorBrush(Color.FromRgb(0x5C, 0x6B, 0x5E)); // 跟 App.xaml 的 HintTextBrush 同一个颜色

            _width = w;
            _height = h;
            ClearGrid();
            RebuildCanvas();
            UpdateLivePreview();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            ClearGrid();
            RebuildCanvas();
            UpdateLivePreview();
        }

        private void UpdateLivePreview()
        {
            var icon = PixelIconEditor.BuildIcon(_grid, _width, _height, _palette);
            if (icon.Rows == null || icon.Palette == null) { LivePreviewIcon.Source = null; return; }
            try
            {
                var palette = CustomThemeValidator.BuildIconPalette(icon);
                LivePreviewIcon.Source = PixelArt.BuildCustomIcon(icon.Rows.ToArray(), palette);
            }
            catch (Exception ex)
            {
                // 理论上画板产出的网格/调色板不会触发这里（BuildIcon 自己保证了每行等宽、
                // 用到的字符都有配色），真出意外也只是预览图空着，不影响继续画
                AppLog.Error("IconPainterWindow.UpdateLivePreview", ex);
                LivePreviewIcon.Source = null;
            }
        }

        // ── 结果 ──────────────────────────────────────────────────────────

        private void BtnInsert_Click(object sender, RoutedEventArgs e)
        {
            ResultIcon = PixelIconEditor.BuildIcon(_grid, _width, _height, _palette);
            DialogResult = true;
        }

        private void BtnCopyFragment_Click(object sender, RoutedEventArgs e)
        {
            var icon = PixelIconEditor.BuildIcon(_grid, _width, _height, _palette);
            string fragment = PixelIconEditor.SerializeIconFragment(icon);
            try { Clipboard.SetText(fragment); } catch { /* 剪贴板偶尔被占用，不是关键功能，失败就算了 */ }

            TxtSizeHint.Text = "✅ 已复制 JSON 片段到剪贴板，粘到 layers[i].icon（或者任何需要一个 icon 对象的地方）就行。";
            TxtSizeHint.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0xE0, 0x8A));
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
