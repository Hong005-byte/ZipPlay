using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelLyric8BitFix
{
    /// <summary>
    /// 个人资料：名字 + 头像 + 导出/导入打包文件。一份 UI 两种用法，靠构造函数的 isFirstRun
    /// 区分文案和收尾动作——首次启动那次问完直接进 HomeWindow（没有"返回"这一说，这是流程里的
    /// 第一步）；之后从 HomeWindow 顶部问候语点进来改资料，走普通的模态对话框，保存/取消都只是
    /// 把这个小窗口关掉。
    /// </summary>
    public partial class WelcomeWindow : Window
    {
        private readonly bool _isFirstRun;

        // 只有首次启动这条路才需要——App 是 ShutdownMode="OnExplicitShutdown"：这个窗口不管怎么被关掉
        // （点"开始使用"正常走完、还是直接按标题栏的 X），只要没有正常继续到下一步就得自己退出整个
        // App。之后从 HomeWindow 点进来改资料那条路是普通模态对话框，关掉就只是关掉，不用这套判断
        private WindowProceedGuard? _nav;

        // 导入个人资料走"选文件 → 内嵌确认 → 真正覆盖本地数据"三步，这里存的是选完文件、还没点
        // "确定导入"之前的那份数据；点取消或者关窗口都不会碰本地数据
        private ProfileBundle? _pendingImportBundle;

        public WelcomeWindow(bool isFirstRun)
        {
            InitializeComponent();
            _isFirstRun = isFirstRun;

            var settings = AppSettings.Load();
            TxtName.Text = settings.UserName ?? "";
            RefreshAvatarDisplay();
            UpdateNamePlaceholder();

            if (!isFirstRun)
            {
                TxtTitle.Text = "✏️ 个人资料";
                TxtSubtitle.Text = "改完点保存就行，下次生成分享卡片会用上新的名字/头像。";
                BtnContinue.Content = "💾 保存";
                BtnCancel.Visibility = Visibility.Visible;
            }
            else
            {
                _nav = new WindowProceedGuard(this);
            }

            TxtName.Focus();
        }

        private void RefreshAvatarDisplay()
        {
            var avatar = AvatarStore.Load();
            AvatarBrush.ImageSource = avatar;
            TxtAvatarPlaceholder.Visibility = avatar == null ? Visibility.Visible : Visibility.Collapsed;
            // 没设头像的时候"移除"按钮点了也没意义，直接不显示，省得用户点一下才发现没反应
            BtnRemoveAvatar.Visibility = avatar == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateNamePlaceholder()
        {
            TxtNamePlaceholder.Visibility = string.IsNullOrEmpty(TxtName.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtName_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => UpdateNamePlaceholder();

        // 名字填完直接回车就能继续/保存，不用非得伸手去点按钮
        private void TxtName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                BtnContinue_Click(sender, e);
            }
        }

        // 头像圆圈本身就能点，跟"更换头像"按钮走的是同一套选图逻辑
        private void AvatarClickArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ChooseAvatar();

        private void BtnChooseAvatar_Click(object sender, RoutedEventArgs e) => ChooseAvatar();

        // 选一张任意图片当头像：AvatarStore 自己负责缩小 + 存成本地 PNG，这里只管选文件 + 选完刷新一下预览
        private void ChooseAvatar()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选一张图片当头像",
                Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            };
            if (dialog.ShowDialog(this) != true) return;

            if (AvatarStore.TrySaveFrom(dialog.FileName))
            {
                RefreshAvatarDisplay();
            }
            else
            {
                ShowStatus("⚠️ 这张图片读取失败，换一张试试", isError: true);
            }
        }

        private void BtnRemoveAvatar_Click(object sender, RoutedEventArgs e)
        {
            AvatarStore.Clear();
            RefreshAvatarDisplay();
        }

        // 名字/头像/听歌记录打包成一个 JSON 存到用户自己选的位置——不替用户擅自决定存放位置
        private void BtnExportProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出个人资料",
                Filter = "JSON 文件 (*.json)|*.json",
                FileName = "ZipPlay-个人资料.json",
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                ProfileBundleStore.ExportTo(dialog.FileName);
                ShowStatus("✅ 已导出", isError: false);
            }
            catch (Exception ex)
            {
                AppLog.Error("WelcomeWindow.BtnExportProfile_Click", ex);
                ShowStatus("⚠️ 导出失败，详情已经记到日志文件里了", isError: true);
            }
        }

        // 选完文件先只解析、不落地——真正覆盖本地数据得等用户在下面的确认条里点"确定导入"
        private void BtnImportProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入个人资料",
                Filter = "JSON 文件 (*.json)|*.json",
            };
            if (dialog.ShowDialog(this) != true) return;

            var bundle = ProfileBundleStore.TryImportFrom(dialog.FileName);
            if (bundle == null)
            {
                // 这次选的文件读不出来，不代表用户想放弃之前那次成功解析、还没确认的导入——但也不能让
                // 那条"确定导入"的确认条继续挂着：留着的话用户可能会以为自己刚才点的就是这次选的文件，
                // 结果点"确定导入"应用的其实是上一次选的那份数据。宁可让用户重新选一遍，也不要悄悄套用旧的
                _pendingImportBundle = null;
                PanelImportConfirm.Visibility = Visibility.Collapsed;
                ShowStatus("⚠️ 这个文件读不出个人资料，确认选的是导出时生成的那份 JSON", isError: true);
                return;
            }

            _pendingImportBundle = bundle;
            TxtProfileStatus.Visibility = Visibility.Collapsed;
            PanelImportConfirm.Visibility = Visibility.Visible;
        }

        // 用户点了"确定导入"，这时候才真的覆盖本地数据、刷新这个窗口自己的名字输入框/头像预览
        private void BtnConfirmImport_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingImportBundle == null) return;

            ProfileBundleStore.ApplyToLocal(_pendingImportBundle);
            _pendingImportBundle = null;
            PanelImportConfirm.Visibility = Visibility.Collapsed;

            TxtName.Text = AppSettings.Load().UserName ?? "";
            RefreshAvatarDisplay();
            ShowStatus("✅ 已导入", isError: false);
        }

        private void BtnCancelImport_Click(object sender, RoutedEventArgs e)
        {
            _pendingImportBundle = null;
            PanelImportConfirm.Visibility = Visibility.Collapsed;
        }

        private void ShowStatus(string text, bool isError)
        {
            TxtProfileStatus.Text = text;
            TxtProfileStatus.Foreground = new SolidColorBrush(isError ? Color.FromRgb(0xE0, 0x8A, 0x72) : Color.FromRgb(0x6E, 0xE7, 0xB0));
            TxtProfileStatus.Visibility = Visibility.Visible;
        }

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppSettings.Load();
            settings.UserName = string.IsNullOrWhiteSpace(TxtName.Text) ? null : TxtName.Text.Trim();

            if (_isFirstRun)
            {
                // 这一步只代表"填完名字"，不代表用户已经在 HomeWindow 里确认过要不要跳过启动首页——
                // 先强制关掉"跳过"，等真的点了 HomeWindow 的"开始播放"才由那边的勾选框状态决定要不要跳过。
                // 不这样做的话，SkipStartupSettings 默认就是 true：填完名字直接关掉 HomeWindow（没点
                // "开始播放"）也会把 settings.json 存下来，下次启动就直接跳过 HomeWindow 进播放器，
                // 用户还没来得及挑皮肤/尺寸就被跳过去了
                settings.SkipStartupSettings = false;
            }
            settings.Save();

            if (_isFirstRun)
            {
                _nav!.ProceedTo(new HomeWindow());
            }
            else
            {
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
    }
}
