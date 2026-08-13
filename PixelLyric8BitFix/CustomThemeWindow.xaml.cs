using System;
using System.Windows;
using System.Windows.Controls;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 自定义主题的管理页：展示怎么写（带一份完整示例）、粘贴/编辑 JSON、详细校验错误、
    /// 管理最多 5 个已存的客制化主题。跟启动设置页是分开的独立窗口，符合"客制化要开在一个新页面"的要求。
    /// </summary>
    public partial class CustomThemeWindow : Window
    {
        // 正在编辑哪一个已有主题的文件名；null 表示这次保存是"新建"，会走 5 个上限的检查
        private string? _editingFileName;

        /// <summary>true 表示这次窗口关闭时至少成功保存/删除过一次，调用方（设置页）据此决定要不要刷新主题列表。</summary>
        public bool ThemesChanged { get; private set; }

        public CustomThemeWindow()
        {
            InitializeComponent();
            TxtExample.Text = BuildExampleJson();
            RefreshThemeList();
        }

        // 拿现有的 Sunset 皮肤当例子——现成的、已经在用的配色，比瞎编一份更有说服力，也顺便验证了
        // "内置皮肤" 和 "客制化皮肤" 走的是同一套字段，不是另外发明了一套格式
        private static string BuildExampleJson() =>
@"{
  ""name"": ""我的海边黄昏"",
  ""font"": ""Segoe UI Light"",
  ""colors"": {
    ""title"": ""#FFF3E0"",
    ""artist"": ""#F2C6A0"",
    ""accent"": ""#F9C784"",
    ""lyric"": ""#FFF3E0"",
    ""glow"": ""#F9C784"",
    ""glowBlur"": 4,
    ""lyricBoxBg"": ""#B32A1F40"",
    ""lyricBoxBorder"": ""#F9C784""
  },
  ""background"": {
    ""type"": ""gradient"",
    ""direction"": ""vertical"",
    ""stops"": [""#F2994A"", ""#EA7093"", ""#4A3B78""]
  },
  ""icon"": {
    ""palette"": { ""#"": ""#F9C784"", ""w"": ""#4A3B78"" },
    ""rows"": [
      ""........"",
      ""..####.."",
      "".######."",
      ""########"",
      ""wwwwwwww"",
      ""wwwwwwww"",
      ""........"",
      ""........""
    ]
  },
  ""animation"": { ""type"": ""pulse"", ""duration"": 2.6 }
}";

        private void BtnCopyExample_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(TxtExample.Text); } catch { /* 剪贴板偶尔会被别的程序占用，不是关键功能，失败就算了 */ }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            HideMessages();

            string json = TxtInput.Text;
            if (string.IsNullOrWhiteSpace(json))
            {
                ShowErrors(new[] { "还没粘贴任何内容。" });
                return;
            }

            var (theme, errors) = CustomThemeValidator.ParseAndValidate(json);
            if (theme == null || errors.Count > 0)
            {
                ShowErrors(errors);
                return;
            }

            var (success, error, savedFileName) = CustomThemeStore.Save(theme, _editingFileName);
            if (!success)
            {
                ShowErrors(new[] { error ?? "保存失败。" });
                return;
            }

            ThemesChanged = true;
            _editingFileName = savedFileName;
            TxtEditingHint.Text = $"正在编辑：{theme.Name}（再次保存会覆盖更新这一份，不会新建）";
            TxtEditingHint.Visibility = Visibility.Visible;

            TxtSuccess.Text = $"✅ 「{theme.Name}」保存成功，回到设置页的皮肤选择器里就能看到了。";
            SuccessBox.Visibility = Visibility.Visible;

            RefreshThemeList();
        }

        private void ShowErrors(System.Collections.Generic.IEnumerable<string> errors)
        {
            ErrorPanel.Children.Clear();
            foreach (var err in errors)
            {
                ErrorPanel.Children.Add(new TextBlock
                {
                    Text = "• " + err,
                    Foreground = System.Windows.Media.Brushes.LightPink,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2),
                });
            }
            ErrorBox.Visibility = Visibility.Visible;
        }

        private void HideMessages()
        {
            ErrorBox.Visibility = Visibility.Collapsed;
            SuccessBox.Visibility = Visibility.Collapsed;
        }

        private void RefreshThemeList()
        {
            ThemeListPanel.Children.Clear();
            var themes = CustomThemeStore.ListAll();

            TxtThemeCount.Text = $"已保存的客制化主题 ({themes.Count}/{CustomThemeStore.MaxThemes})";
            TxtNoThemes.Visibility = themes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            foreach (var entry in themes)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var name = new TextBlock
                {
                    Text = entry.Theme.Name ?? "(未命名)",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(name, 0);

                var editBtn = new Button
                {
                    Content = "✏️ 编辑",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.LightGray,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                editBtn.Click += (s, e) =>
                {
                    HideMessages();
                    string? raw = CustomThemeStore.LoadRawJson(entry.FileName);
                    if (raw == null) return;
                    TxtInput.Text = raw;
                    _editingFileName = entry.FileName;
                    TxtEditingHint.Text = $"正在编辑：{entry.Theme.Name}（保存会覆盖更新这一份，不会新建）";
                    TxtEditingHint.Visibility = Visibility.Visible;
                };
                Grid.SetColumn(editBtn, 1);

                var deleteBtn = new Button
                {
                    Content = "🗑",
                    Padding = new Thickness(8, 2, 8, 2),
                    FontSize = 10,
                    Margin = new Thickness(6, 0, 0, 0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.LightPink,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "删除这个主题",
                };
                string fileNameForDelete = entry.FileName;
                deleteBtn.Click += (s, e) =>
                {
                    CustomThemeStore.Delete(fileNameForDelete);
                    ThemesChanged = true;
                    if (_editingFileName == fileNameForDelete)
                    {
                        _editingFileName = null;
                        TxtEditingHint.Visibility = Visibility.Collapsed;
                    }
                    RefreshThemeList();
                };
                Grid.SetColumn(deleteBtn, 2);

                row.Children.Add(name);
                row.Children.Add(editBtn);
                row.Children.Add(deleteBtn);
                ThemeListPanel.Children.Add(row);
            }
        }
    }
}
