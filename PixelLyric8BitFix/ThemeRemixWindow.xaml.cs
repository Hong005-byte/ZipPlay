using System;
using System.Windows;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// "🔀 混搭已存主题"的选择窗口：三个下拉各自代表配色/图标/动画的来源主题，点"生成混搭草稿"之后
    /// 把结果存进 <see cref="ResultJson"/>、DialogResult 置 true 关掉；点"取消"或者直接叉掉窗口，
    /// DialogResult 是 false/null，调用方（CustomThemeWindow）不应该做任何事。
    /// 调用方负责保证打开这个窗口之前已经至少存了 2 个主题——这里不重复做这个检查，只从
    /// CustomThemeStore.ListAll() 现读一份列表填三个下拉，1 个都没有的话三个下拉会是空的，
    /// "生成"按钮点了也不会有反应（SelectedItem 是 null）。
    /// </summary>
    public partial class ThemeRemixWindow : Window
    {
        public string? ResultJson { get; private set; }

        public ThemeRemixWindow()
        {
            InitializeComponent();

            var entries = CustomThemeStore.ListAll();
            CmbColorSource.ItemsSource = entries;
            CmbIconSource.ItemsSource = entries;
            CmbAnimationSource.ItemsSource = entries;

            // 默认三个下拉尽量选不同的主题（第 1/2/3 份，主题数不够就从头循环），这样第一次打开
            // 就能直接看到"三个来源不一样"的效果，比默认全选第一个、用户还得自己动手换更直观
            if (entries.Count > 0)
            {
                CmbColorSource.SelectedIndex = 0 % entries.Count;
                CmbIconSource.SelectedIndex = 1 % entries.Count;
                CmbAnimationSource.SelectedIndex = 2 % entries.Count;
            }
        }

        private void BtnGenerate_Click(object sender, RoutedEventArgs e)
        {
            if (CmbColorSource.SelectedItem is not CustomThemeEntry colorSource ||
                CmbIconSource.SelectedItem is not CustomThemeEntry iconSource ||
                CmbAnimationSource.SelectedItem is not CustomThemeEntry animationSource)
            {
                return; // 三个下拉在构造函数里只要 entries 非空就都选好了默认项，理论上到不了这里
            }

            ResultJson = CustomThemeRemixer.Remix(colorSource.Theme, iconSource.Theme, animationSource.Theme);
            DialogResult = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
