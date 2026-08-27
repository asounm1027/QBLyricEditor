using QBLyricEditor.Lyric;
using QBLyricEditor.UserControls;
using QBLyricEditor.Utils;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace QBLyricEditor;

public enum LrcPanelType
{
    LrcLinePanel,
    LrcTextPanel
}

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// 窗口标题的固定前缀（软件名 + 版本号），导入音频后会在其后追加歌名
    /// </summary>
    private const string AppTitle = "QBLyricEditor v3.1.5";

    public MainWindow()
    {
        InitializeComponent();

        Title = AppTitle;

        LrcLinePanel = (LrcLineView)LrcPanelContainer.Content;
        LrcTextPanel = new LrcTextView();
        LrcPreviewPanel = new LrcPreviewView();

        // 缓存步进按钮引用
        StepPresetButtons = new Button[] { StepPreset1, StepPreset2, StepPreset3 };

        MediaPlayer.PlaybackEnded += MediaPlayer_PlaybackEnded;

        Timer = new DispatcherTimer();
        Timer.Tick += new EventHandler(Timer_Tick);
        Timer.Interval = new TimeSpan(0, 0, 0, 0, 20);
        Timer.Start();
    }

    #region 成员变量

    private LrcPanelType CurrentLrcPanel = LrcPanelType.LrcLinePanel;

    /// <summary>
    /// 打点模式内的子模式：false = 编辑（默认），true = 预览
    /// </summary>
    private bool IsPreviewSubMode = false;
    private LrcPreviewView LrcPreviewPanel;

    /// <summary>
    /// 表示播放器是否正在播放
    /// </summary>
    private bool isPlaying = false;

    private LrcLineView LrcLinePanel;
    private LrcTextView LrcTextPanel;

    /// <summary>
    /// 音频播放器（NAudio）
    /// </summary>
    public AudioPlayer MediaPlayer { get; } = new AudioPlayer();

    public TimeSpan ShortTimeShift { get; private set; } = new TimeSpan(0, 0, 2);
    public TimeSpan LongTimeShift { get; private set; } = new TimeSpan(0, 0, 5);

    private string fileName;

    /// <summary>
    /// 步进预设值（毫秒）
    /// </summary>
    private int[] StepPresetValues = new int[3] { 50, 500, 1000 };

    /// <summary>
    /// 当前选中的步进预设索引（0/1/2）
    /// </summary>
    private int SelectedStepPresetIndex = 0;

    /// <summary>
    /// 步进预设按钮引用缓存
    /// </summary>
    private Button[] StepPresetButtons;

    #endregion

    #region 计时器

    DispatcherTimer Timer;

    /// <summary>
    /// 每个计时器时刻，更新时间轴上的全部信息
    /// </summary>
    private void Timer_Tick(object sender, EventArgs e)
    {
        if (!IsMediaAvailable)
            return;

        var current = MediaPlayer.Position;
        CurrentTimeText.Text = $"{current.Minutes:00}:{current.Seconds:00}";

        TimeBackground.Value = current.TotalSeconds / MediaPlayer.TotalTime.TotalSeconds;
        CurrentLrcText.Text = LrcManager.Instance.GetNearestLrc(current);
    }

    #endregion

    #region 媒体播放器

    public bool IsMediaAvailable => MediaPlayer.IsLoaded;

    /// <summary>
    /// 是否正在播放（暂停/未播放时预览面板不应根据播放位置重新计算高亮行，
    /// 避免因音频采样对齐导致的时间误差把高亮行反复拉回上一行）
    /// </summary>
    public bool IsPlaying => isPlaying;

    private void Play()
    {
        if (!IsMediaAvailable)
            return;

        MediaPlayer.Play();

        PlayButton.Tag = true;

        isPlaying = true;
    }

    public void PlayMedia()
    {
        Play();
    }

    private void Pause()
    {
        if (!IsMediaAvailable)
            return;

        MediaPlayer.Pause();

        PlayButton.Tag = false;

        isPlaying = false;
    }

    private void Stop()
    {
        if (!IsMediaAvailable)
            return;

        MediaPlayer.Stop();

        PlayButton.Tag = false;

        isPlaying = false;
    }

    #endregion

    #region 内部方法

    private void UpdateLrcView()
    {
        LrcLinePanel.UpdateLrcPanel();
        LrcTextPanel.Text = LrcManager.Instance.ToString();
        LrcPreviewPanel.RefreshList();
    }

    private void ImportMedia(string filename)
    {
        try
        {
            MediaPlayer.Load(filename);
        }
        catch (Exception ex)
        {
            // 音频本身加载失败：必须明确告知用户，否则播放按钮点了没反应，
            // 用户完全不知道是文件问题还是程序问题
            MessageBox.Show(
                $"无法加载音频文件：\n{filename}\n\n{ex.Message}",
                "导入失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }

        // 更新时间轴上的总时间
        var totalTime = MediaPlayer.TotalTime;
        TotalTimeText.Text = $"{totalTime.Minutes:00}:{totalTime.Seconds:00}";
        CurrentTimeText.Text = "00:00";
        PlayButton.Tag = false;
        isPlaying = false;

        // 标题/封面读取失败属于次要信息，静默降级即可，不打断用户
        try
        {
            var title = TagLibHelper.GetTitle(filename);
            if (string.IsNullOrWhiteSpace(title))
                title = Path.GetFileNameWithoutExtension(filename);
            Title = $"{AppTitle} {title}";
        }
        catch
        {
            Title = $"{AppTitle} {Path.GetFileNameWithoutExtension(filename)}";
        }

        try
        {
            // 没有内嵌封面时返回 null，此时同样使用默认封面
            Cover.Source = TagLibHelper.GetAlbumArt(filename) ?? ResourceHelper.GetIcon("disc.png");
        }
        catch
        {
            Cover.Source = ResourceHelper.GetIcon("disc.png");
        }
    }

    #endregion

    #region 菜单按钮

    /// <summary>
    /// 界面读取，用于初始化
    /// </summary>
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        #region 读取配置

        // 退出时自动缓存
        AutoSaveTemp.IsChecked = bool.Parse(ConfigurationManager.AppSettings["AutoSaveTemp"]);
        // 导出 UTF-8
        ExportUTF8.IsChecked = bool.Parse(ConfigurationManager.AppSettings["ExportUTF8"]);
        // 时间取近似值
        LrcLine.IsShort =
            ApproxTime.IsChecked =
                bool.Parse(ConfigurationManager.AppSettings["ApproxTime"]);
        // 时间偏差（改变 Text 会触发 TextChanged 事件，下同）
        TimeOffset.Text = ConfigurationManager.AppSettings["TimeOffset"];
        // 快进快退
        ShortShift.Text = ConfigurationManager.AppSettings["ShortTimeShift"];
        LongShift.Text = ConfigurationManager.AppSettings["LongTimeShift"];
        // 步进预设
        if (int.TryParse(ConfigurationManager.AppSettings["StepPreset1"], out int sp1))
            StepPresetValues[0] = Math.Clamp(sp1, 10, 600000);
        if (int.TryParse(ConfigurationManager.AppSettings["StepPreset2"], out int sp2))
            StepPresetValues[1] = Math.Clamp(sp2, 10, 600000);
        if (int.TryParse(ConfigurationManager.AppSettings["StepPreset3"], out int sp3))
            StepPresetValues[2] = Math.Clamp(sp3, 10, 600000);
        if (int.TryParse(ConfigurationManager.AppSettings["SelectedStepPreset"], out int sel))
            SelectedStepPresetIndex = Math.Clamp(sel, 0, 2);

        RefreshStepPresetButtons();
        ApplyStepPreset(SelectedStepPresetIndex);

        #endregion

        // 打开缓存文件
        if (AutoSaveTemp.IsChecked && File.Exists(FileHelper.TempFileName))
        {
            LrcManager.Instance.LoadFromFile(FileHelper.TempFileName);
            UpdateLrcView();
        }
    }

    /// <summary>
    /// 程序退出，关闭计时器，修改配置文件
    /// </summary>
    private void Window_Closed(object sender, EventArgs e)
    {
        Timer.Stop();
        MediaPlayer.Dispose();

        // 先保存缓存文件（防止编辑内容丢失），再保存配置文件；
        // 两者分别捕获异常，避免一个失败连带跳过另一个
        try
        {
            if (AutoSaveTemp.IsChecked)
            {
                Encoding encoding = ExportUTF8.IsChecked ? Encoding.UTF8 : FileHelper.AnsiEncoding;
                File.WriteAllText(FileHelper.TempFileName, LrcManager.Instance.ToString(), encoding);
            }
            else if (File.Exists(FileHelper.TempFileName))
            {
                File.Delete(FileHelper.TempFileName);
            }
        }
        catch
        {
            // 缓存保存失败不应阻止程序退出
        }

        try
        {
            // 保存配置文件
            Configuration cfa = ConfigurationManager.OpenExeConfiguration(
                ConfigurationUserLevel.None
            );

            cfa.AppSettings.Settings["AutoSaveTemp"].Value = AutoSaveTemp.IsChecked.ToString();
            cfa.AppSettings.Settings["ExportUTF8"].Value = ExportUTF8.IsChecked.ToString();
            cfa.AppSettings.Settings["ApproxTime"].Value = LrcLine.IsShort.ToString();
            cfa.AppSettings.Settings["TimeOffset"].Value = (
                -LrcLinePanel.TimeOffset.TotalMilliseconds
            ).ToString();
            cfa.AppSettings.Settings["ShortTimeShift"].Value =
                ShortTimeShift.TotalSeconds.ToString();
            cfa.AppSettings.Settings["LongTimeShift"].Value = LongTimeShift.TotalSeconds.ToString();
            cfa.AppSettings.Settings["StepPreset1"].Value = StepPresetValues[0].ToString();
            cfa.AppSettings.Settings["StepPreset2"].Value = StepPresetValues[1].ToString();
            cfa.AppSettings.Settings["StepPreset3"].Value = StepPresetValues[2].ToString();
            cfa.AppSettings.Settings["SelectedStepPreset"].Value = SelectedStepPresetIndex.ToString();

            cfa.Save();
        }
        catch
        {
            // 配置保存失败（例如单文件发布下 OpenExeConfiguration 异常）不应阻止程序退出
        }
    }

    /// <summary>
    /// 导入音频文件
    /// </summary>
    private void ImportMedia_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog();
        ofd.Filter = "媒体文件|*.mp3;*.wav;*.3gp;*.mp4;*.avi;*.wmv;*.wma;*.aac;*.flac;*.m4a|所有文件|*.*";

        if (ofd.ShowDialog() == true)
        {
            ImportMedia(ofd.FileName);
            fileName = ofd.FileName;
        }
    }

    /// <summary>
    /// 导入歌词文件
    /// </summary>
    private void ImportLyric_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog ofd = new OpenFileDialog();
        ofd.Filter = "歌词文件|*.lrc;*.txt|所有文件|*.*";

        if (ofd.ShowDialog() == true)
        {
            LrcManager.Instance.LoadFromFile(ofd.FileName);
            UpdateLrcView();
            fileName = ofd.FileName;
        }
    }

    /// <summary>
    /// 将歌词保存为文本文件
    /// </summary>
    private void ExportLyric_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog ofd = new SaveFileDialog();
        ofd.Filter = "歌词文件|*.lrc|文本文件|*.txt|所有文件|*.*";

        if (!string.IsNullOrEmpty(fileName))
        {
            ofd.FileName = Path.GetFileNameWithoutExtension(fileName);
        }

        if (ofd.ShowDialog() == true)
        {
            Encoding encoding = ExportUTF8.IsChecked ? Encoding.UTF8 : FileHelper.AnsiEncoding;
            File.WriteAllText(ofd.FileName, LrcManager.Instance.ToString(), encoding);
        }
    }

    /// <summary>
    /// 从剪贴板粘贴歌词文本
    /// </summary>
    private void ImportLyricFromClipboard_Click(object sender, RoutedEventArgs e)
    {
        LrcManager.Instance.LoadFromText(Clipboard.GetText());
        UpdateLrcView();
    }

    /// <summary>
    /// 将歌词文本复制到剪贴板
    /// </summary>
    private void ExportLyricToClipboard_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(LrcManager.Instance.ToString());
    }

    /// <summary>
    /// 配置选项发生变化
    /// </summary>
    private void Settings_Checked(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        switch (item.Name)
        {
            case "ApproxTime":
                LrcLine.IsShort = item.IsChecked;
                if (LrcPanelContainer.Content is LrcLineView view)
                {
                    view.LrcLinePanel.Items.Refresh();
                }
                break;
        }
    }

    /// <summary>
    /// 播放自然结束，复位播放按钮
    /// </summary>
    private void MediaPlayer_PlaybackEnded(object sender, EventArgs e)
    {
        PlayButton.Tag = false;
        isPlaying = false;
    }

    /// <summary>
    /// 音量滑块
    /// </summary>
    private void VolumeSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e
    )
    {
        if (MediaPlayer is null)
            return;
        MediaPlayer.Volume = (float)e.NewValue;
    }

    /// <summary>
    /// 播放速度滑块
    /// </summary>
    private void SpeedSlider_ValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e
    )
    {
        if (MediaPlayer is null)
            return;
        MediaPlayer.Tempo = e.NewValue;
    }

    /// <summary>
    /// 处理窗口的文件拖入事件
    /// </summary>
    public void Window_Drop(object sender, DragEventArgs e)
    {
        string[] filePath = ((string[])e.Data.GetData(DataFormats.FileDrop));

        foreach (var file in filePath)
        {
            string ext = Path.GetExtension(file).ToLower();
            if (FileHelper.MediaExtensions.Contains(ext))
            {
                ImportMedia(file);
                fileName = file;
            }
            else if (FileHelper.LyricExtensions.Contains(ext))
            {
                LrcManager.Instance.LoadFromFile(file);
                UpdateLrcView();
                fileName = file;
            }
        }
    }

    public void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
            e.Effects = DragDropEffects.Link;
        else
            e.Effects = DragDropEffects.None;
    }

    /// <summary>
    /// 关闭按钮
    /// </summary>
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }

    private void TimeOffset_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LrcLinePanel is null)
            return;
        if (int.TryParse(TimeOffset.Text, out int offset))
        {
            LrcLinePanel.TimeOffset = new TimeSpan(0, 0, 0, 0, -offset);
        }
    }

    private void TimeShift_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LrcLinePanel is null)
            return;

        TextBox box = sender as TextBox;
        if (int.TryParse(box.Text, out int value))
        {
            switch (box.Name)
            {
                case "ShortShift":
                    ShortTimeShift = new TimeSpan(0, 0, value);
                    break;

                case "LongShift":
                    LongTimeShift = new TimeSpan(0, 0, value);
                    break;
            }
        }
    }

    /// <summary>
    /// 重置所有歌词行的时间
    /// </summary>
    private void ResetAllTime_Click(object sender, RoutedEventArgs e)
    {
        LrcLinePanel.ResetAllTime();
    }

    /// <summary>
    /// 平移所有歌词行的时间（单位：毫秒）
    /// </summary>
    private void ShiftAllTime_Click(object sender, RoutedEventArgs e)
    {
        string str = InputBox.Show(this, "输入", "请输入时间偏移量(ms)：");
        if (double.TryParse(str, out double offset))
        {
            LrcLinePanel.ShiftAllTime(new TimeSpan(0, 0, 0, 0, (int)offset));
        }
    }

    /// <summary>
    /// 对选中行进行时间平移（单位：毫秒）
    /// </summary>
    private void ShiftSelectedTime_Click(object sender, RoutedEventArgs e)
    {
        if (LrcLinePanel.LrcLinePanel.SelectedItems.Count == 0)
        {
            MessageBox.Show("请先在歌词列表中选择一行或多行。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string str = InputBox.Show(this, "输入", "请输入时间偏移量(ms)：");
        if (double.TryParse(str, out double offset))
        {
            LrcManager.Instance.ShiftSelectedTime(LrcLinePanel.LrcLinePanel, new TimeSpan(0, 0, 0, 0, (int)offset));
        }
    }

    #region 步进预设

    private void StepPreset_Click(object sender, RoutedEventArgs e)
    {
        int index = Array.IndexOf(StepPresetButtons, sender as Button);
        if (index < 0)
            return;
        ApplyStepPreset(index);
    }

    private void StepPreset_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        int index = Array.IndexOf(StepPresetButtons, sender as Button);
        if (index < 0)
            return;
        string str = InputBox.Show(this, "修改步进预设", $"请输入新的步进值(ms)：", StepPresetValues[index].ToString());
        if (int.TryParse(str, out int value))
        {
            value = Math.Clamp(value, 10, 600000);
            StepPresetValues[index] = value;
            RefreshStepPresetButtons();
            if (index == SelectedStepPresetIndex)
                ApplyStepPreset(index);
        }
    }

    private void RefreshStepPresetButtons()
    {
        for (int i = 0; i < 3; i++)
        {
            int val = StepPresetValues[i];
            StepPresetButtons[i].Content = val >= 1000 ? $"{val / 1000.0:0.#}k" : val.ToString();
        }
    }

    private void ApplyStepPreset(int index)
    {
        SelectedStepPresetIndex = index;
        LrcLinePanel.TimeAdjustStep = StepPresetValues[index];
        for (int i = 0; i < 3; i++)
        {
            bool selected = (i == index);
            StepPresetButtons[i].Background = selected
                ? new SolidColorBrush(Color.FromRgb(0x26, 0xA0, 0xDA))
                : Brushes.Transparent;
            StepPresetButtons[i].Foreground = selected
                ? Brushes.White
                : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            StepPresetButtons[i].BorderBrush = selected
                ? new SolidColorBrush(Color.FromRgb(0x26, 0xA0, 0xDA))
                : new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        }
    }

    #endregion

    #endregion

    #region 工具按钮

    /// <summary>
    /// 切换面板
    /// </summary>
    private void SwitchLrcPanel_Click(object sender, RoutedEventArgs e)
    {
        // 切换时暂停播放
        if (isPlaying)
            Pause();

        switch (CurrentLrcPanel)
        {
            // 切换回纯文本
            case LrcPanelType.LrcLinePanel:
                UpdateLrcView();
                LrcPanelContainer.Content = LrcTextPanel;
                CurrentLrcPanel = LrcPanelType.LrcTextPanel;
                // 切换到文本编辑模式时，按钮旋转180度，且相关按钮不可用
                ((Image)((Button)sender).Content).LayoutTransform = new RotateTransform(180);
                ToolsForLrcLineOnly.Visibility = Visibility.Collapsed;
                FlagButton.Visibility = Visibility.Hidden;
                ClearAllTime.IsEnabled = true;
                SortTime.IsEnabled = false;
                ShiftAllTime.IsEnabled = false;
                ShiftSelectedTime.IsEnabled = false;
                PreviewToggleButton.Visibility = Visibility.Collapsed;
                StepPresetBorder.Visibility = Visibility.Collapsed;
                break;

            // 切换回歌词行
            case LrcPanelType.LrcTextPanel:
                // 在回到歌词行模式前，要检查当前文本能否进行正确转换
                if (!LrcManager.Instance.LoadFromText(LrcTextPanel.Text))
                    return;
                UpdateLrcView();
                LrcPanelContainer.Content = LrcLinePanel;
                CurrentLrcPanel = LrcPanelType.LrcLinePanel;
                IsPreviewSubMode = false;
                // 切换到文本编辑模式时，按钮旋转角度复原，且相关按钮可用
                ((Image)((Button)sender).Content).LayoutTransform = new RotateTransform(0);
                ToolsForLrcLineOnly.Visibility = Visibility.Visible;
                FlagButton.Visibility = Visibility.Visible;
                ClearAllTime.IsEnabled = false;
                SortTime.IsEnabled = true;
                ShiftAllTime.IsEnabled = true;
                ShiftSelectedTime.IsEnabled = true;
                PreviewToggleButton.Visibility = Visibility.Visible;
                PreviewToggleButton.Tag = false;
                PreviewListIcon.Visibility = Visibility.Visible;
                PreviewEyeIcon.Visibility = Visibility.Collapsed;
                StepPresetBorder.Visibility = Visibility.Visible;
                break;
        }
    }

    /// <summary>
    /// 播放按钮
    /// </summary>
    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (!isPlaying)
        {
            Play();
        }
        else
        {
            Pause();
        }
    }

    /// <summary>
    /// 停止按钮
    /// </summary>
    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        Stop();
    }

    /// <summary>
    /// 快进快退
    /// </summary>
    private void TimeShift_Click(object sender, RoutedEventArgs e)
    {
        if (!IsMediaAvailable)
            return;

        switch (((Button)sender).Name)
        {
            case "ShortShiftLeft":
                MediaPlayer.Position -= ShortTimeShift;
                break;
            case "ShortShiftRight":
                MediaPlayer.Position += ShortTimeShift;
                break;
            case "LongShiftLeft":
                MediaPlayer.Position -= LongTimeShift;
                break;
            case "LongShiftRight":
                MediaPlayer.Position += LongTimeShift;
                break;
        }
    }

    /// <summary>
    /// 时间轴点击
    /// </summary>
    private void TimeClickBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsMediaAvailable)
            return;

        double current = e.GetPosition(TimeClickBar).X;
        double percent = current / TimeClickBar.ActualWidth;
        TimeBackground.Value = percent;

        MediaPlayer.Position = TimeSpan.FromMilliseconds(
            MediaPlayer.TotalTime.TotalMilliseconds * percent
        );
    }

    /// <summary>
    /// 撤销
    /// </summary>
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        switch (CurrentLrcPanel)
        {
            case LrcPanelType.LrcLinePanel:
                LrcLinePanel.Undo();
                break;

            case LrcPanelType.LrcTextPanel:
                LrcTextPanel.LrcTextPanel.Undo();
                break;
        }
    }

    /// <summary>
    /// 重做
    /// </summary>
    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        switch (CurrentLrcPanel)
        {
            case LrcPanelType.LrcLinePanel:
                LrcLinePanel.Redo();
                break;

            case LrcPanelType.LrcTextPanel:
                LrcTextPanel.LrcTextPanel.Redo();
                break;
        }
    }

    /// <summary>
    /// 将媒体播放位置应用到当前歌词行
    /// </summary>
    private void SetTime_Click(object sender, RoutedEventArgs e)
    {
        if (!IsMediaAvailable)
            return;
        if (CurrentLrcPanel != LrcPanelType.LrcLinePanel)
            return;

        LrcLinePanel.SetCurrentLineTime(MediaPlayer.Position);
    }

    /// <summary>
    /// 添加新行
    /// </summary>
    private void AddNewLine_Click(object sender, RoutedEventArgs e)
    {
        LrcLinePanel.AddNewLine(MediaPlayer.Position);
    }

    /// <summary>
    /// 删除行
    /// </summary>
    private void DeleteLine_Click(object sender, RoutedEventArgs e)
    {
        LrcLinePanel.DeleteLine();
    }

    /// <summary>
    /// 上移一行
    /// </summary>
    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        LrcLinePanel.MoveUp();
    }

    /// <summary>
    /// 下移一行
    /// </summary>
    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        LrcLinePanel.MoveDown();
    }

    /// <summary>
    /// 清空所有时间标记
    /// </summary>
    private void ClearAllTime_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentLrcPanel != LrcPanelType.LrcTextPanel)
            return;
        LrcTextPanel.ClearAllTime();
    }

    /// <summary>
    /// 强制排序
    /// </summary>
    private void SortTime_Click(object sender, RoutedEventArgs e)
    {
        LrcManager.Instance.Sort();
        LrcLinePanel.UpdateLrcPanel();
    }

    /// <summary>
    /// 清空全部内容
    /// </summary>
    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        switch (CurrentLrcPanel)
        {
            case LrcPanelType.LrcLinePanel:
                LrcManager.Instance.Clear();
                LrcLinePanel.UpdateLrcPanel();
                break;

            case LrcPanelType.LrcTextPanel:
                LrcTextPanel.Clear();
                break;
        }
    }

    /// <summary>
    /// 切换打点/预览子模式
    /// </summary>
    private void PreviewToggle_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentLrcPanel != LrcPanelType.LrcLinePanel)
            return;

        // 切换时暂停播放
        if (isPlaying)
            Pause();

        if (!IsPreviewSubMode)
        {
            // 编辑 → 预览：将选中行的时间戳设为播放位置，确保预览定时器高亮该行
            int selectedIndex = LrcLinePanel.SelectedIndex;
            if (selectedIndex >= 0 && IsMediaAvailable)
            {
                var line = LrcManager.Instance.LrcList[selectedIndex];
                if (line.LrcTime.HasValue)
                {
                    MediaPlayer.Position = line.LrcTime.Value.Add(TimeSpan.FromTicks(1));
                }
            }

            UpdateLrcView();
            // 先挂到可视树，再启动预览/设置高亮，确保 ItemContainerGenerator 已能生成容器
            LrcPanelContainer.Content = LrcPreviewPanel;
            LrcPreviewPanel.StartPreview();
            if (selectedIndex >= 0)
            {
                LrcPreviewPanel.HighlightLine(selectedIndex);
            }
            IsPreviewSubMode = true;
            PreviewToggleButton.Tag = true;
            PreviewListIcon.Visibility = Visibility.Collapsed;
            PreviewEyeIcon.Visibility = Visibility.Visible;
            // 隐藏编辑模式专属的工具栏
            ToolsForLrcLineOnly.Visibility = Visibility.Collapsed;
            FlagButton.Visibility = Visibility.Hidden;
            // 预览模式下禁用水印平移菜单
            ShiftAllTime.IsEnabled = false;
            ShiftSelectedTime.IsEnabled = false;
            // 隐藏微调步进
            StepPresetBorder.Visibility = Visibility.Collapsed;
        }
        else
        {
            // 预览 → 编辑：将预览当前高亮行设为编辑模式的选中行
            int highlightIndex = LrcPreviewPanel.CurrentHighlightIndex;

            LrcPreviewPanel.StopPreview();
            LrcPanelContainer.Content = LrcLinePanel;
            IsPreviewSubMode = false;
            PreviewToggleButton.Tag = false;
            PreviewListIcon.Visibility = Visibility.Visible;
            PreviewEyeIcon.Visibility = Visibility.Collapsed;
            // 恢复编辑模式工具栏
            ToolsForLrcLineOnly.Visibility = Visibility.Visible;
            FlagButton.Visibility = Visibility.Visible;
            // 恢复时间平移菜单
            ShiftAllTime.IsEnabled = true;
            ShiftSelectedTime.IsEnabled = true;
            // 恢复微调步进
            StepPresetBorder.Visibility = Visibility.Visible;

            // 直接选中预览高亮的行
            if (highlightIndex >= 0 && highlightIndex < LrcLinePanel.LrcLinePanel.Items.Count)
            {
                LrcLinePanel.SelectedIndex = highlightIndex;
                LrcLinePanel.LrcLinePanel.ScrollIntoView(LrcLinePanel.LrcLinePanel.SelectedItem);
                LrcLinePanel.UpdateBottomTextBoxes();
            }
        }
    }

    /// <summary>
    /// 软件信息
    /// </summary>
    private void Info_Click(object sender, RoutedEventArgs e)
    {
        var info = string.Format(Properties.Resources.Info, AppVersion.DisplayVersion);
        var res = MessageBox.Show(info, "相关信息", MessageBoxButton.OKCancel);
        if (res == MessageBoxResult.OK)
            Process.Start("https://zhuanlan.zhihu.com/p/32588196");
    }

    #endregion

    #region 快捷键

    private void SetTimeShortcut_Executed(object sender, ExecutedRoutedEventArgs e) =>
        SetTime_Click(this, e);

    private void HelpShortcut_Executed(object sender, ExecutedRoutedEventArgs e) =>
        Info_Click(this, e);

    private void PlayShortcut_Executed(object sender, ExecutedRoutedEventArgs e) =>
        PlayButton_Click(this, e);

    private void UndoShortcut_Executed(object sender, ExecutedRoutedEventArgs e) =>
        Undo_Click(this, e);

    private void RedoShortcut_Executed(object sender, ExecutedRoutedEventArgs e) =>
        Redo_Click(this, e);

    private void InsertShortcut_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (CurrentLrcPanel == LrcPanelType.LrcLinePanel)
        {
            AddNewLine_Click(this, null);
        }
    }

    #endregion
}
