using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using QBLyricEditor.Lyric;

namespace QBLyricEditor.UserControls;

public partial class LrcPreviewView : UserControl
{
    public LrcPreviewView()
    {
        InitializeComponent();

        _updateTimer = new DispatcherTimer();
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Interval = new TimeSpan(0, 0, 0, 0, 100);
        Unloaded += (s, e) => _updateTimer.Stop();
    }

    private readonly DispatcherTimer _updateTimer;
    private int _previousHighlightIndex = -1;
    private bool _suppressTimerTick;
    private static readonly SolidColorBrush HighlightBrush = new(Color.FromRgb(0x90, 0xEE, 0x90));

    /// <summary>
    /// 当前高亮行索引（供外部切换模式时同步选中状态）
    /// </summary>
    public int CurrentHighlightIndex => _previousHighlightIndex;

    /// <summary>
    /// 开始预览更新（启动定时器）
    /// </summary>
    public void StartPreview()
    {
        _previousHighlightIndex = -1;
        _suppressTimerTick = false;
        RefreshList();
        // 强制布局以确保 ItemContainerGenerator 已生成容器
        PreviewListPanel.UpdateLayout();
        _updateTimer.Start();
    }

    /// <summary>
    /// 直接高亮指定行并滚动到视图中央（用于模式切换时同步选中状态）
    /// </summary>
    public void HighlightLine(int index)
    {
        if (index < 0 || index >= PreviewListPanel.Items.Count)
            return;

        ClearHighlight();

        var item = PreviewListPanel.ItemContainerGenerator.ContainerFromIndex(index) as ListViewItem;
        if (item != null)
        {
            item.Background = HighlightBrush;
            _previousHighlightIndex = index;
            _suppressTimerTick = true;
            CenterItem(item);
        }
    }

    /// <summary>
    /// 停止预览更新
    /// </summary>
    public void StopPreview()
    {
        _updateTimer.Stop();
        ClearHighlight();
    }

    /// <summary>
    /// 刷新列表数据
    /// </summary>
    public void RefreshList()
    {
        _previousHighlightIndex = -1;
        PreviewListPanel.Items.Clear();
        foreach (var line in LrcManager.Instance.LrcList)
        {
            PreviewListPanel.Items.Add(line);
        }
        PreviewListPanel.Items.Refresh();
    }

    /// <summary>
    /// 定时器：更新当前播放位置对应的高亮行（绿色高亮 + 居中）
    /// </summary>
    private void UpdateTimer_Tick(object sender, EventArgs e)
    {
        var mainWindow = Application.Current.MainWindow as MainWindow;
        if (mainWindow == null || !mainWindow.IsMediaAvailable)
            return;

        // 如果刚通过 HighlightLine 手动指定了高亮行，跳过本次定时器更新
        if (_suppressTimerTick)
        {
            _suppressTimerTick = false;
            return;
        }

        // 暂停状态下不要根据播放位置反推高亮行：NAudio 的采样对齐会让读回的
        // Position 比目标行的时间戳略小，导致误判为上一行。暂停时保持当前
        // 高亮（由手动切换/HighlightLine 决定），只有真正播放时才随进度刷新。
        if (!mainWindow.IsPlaying)
            return;

        int index = LrcManager.Instance.GetNearestLrcIndex(mainWindow.MediaPlayer.Position);
        if (index < 0 || index >= PreviewListPanel.Items.Count)
            return;

        if (index == _previousHighlightIndex)
            return;

        ClearHighlight();

        var item = PreviewListPanel.ItemContainerGenerator.ContainerFromIndex(index) as ListViewItem;
        if (item != null)
        {
            item.Background = HighlightBrush;
            _previousHighlightIndex = index;

            CenterItem(item);
        }
    }

    /// <summary>
    /// 双击预览行 → 跳转并播放
    /// </summary>
    private void PreviewListPanel_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var item = PreviewListPanel.SelectedItem as LrcLine;
        if (item == null || !item.LrcTime.HasValue)
            return;

        var mainWindow = Application.Current.MainWindow as MainWindow;
        if (mainWindow == null || !mainWindow.IsMediaAvailable)
            return;

        mainWindow.MediaPlayer.Position = item.LrcTime.Value;
        mainWindow.PlayMedia();
        _suppressTimerTick = true;
    }

    /// <summary>
    /// 清除当前高亮
    /// </summary>
    private void ClearHighlight()
    {
        if (_previousHighlightIndex >= 0)
        {
            var prevItem = PreviewListPanel.ItemContainerGenerator.ContainerFromIndex(_previousHighlightIndex) as ListViewItem;
            if (prevItem != null)
            {
                prevItem.Background = Brushes.Transparent;
            }
            _previousHighlightIndex = -1;
        }
    }

    /// <summary>
    /// 将指定行滚动到视图中央
    /// </summary>
    private void CenterItem(ListViewItem item)
    {
        var scrollViewer = GetScrollViewer(PreviewListPanel);
        if (scrollViewer == null)
            return;

        item.UpdateLayout();

        double viewportHeight = scrollViewer.ViewportHeight;
        double scrollableHeight = scrollViewer.ScrollableHeight;

        // 视口尚未完成布局
        if (viewportHeight < 1 || double.IsNaN(viewportHeight))
            return;

        try
        {
            // 获取 item 在 ScrollViewer 可视区域内的相对 Y 坐标
            var transform = item.TransformToAncestor(scrollViewer);
            double itemTopInViewport = transform.Transform(new Point(0, 0)).Y;
            double itemCenter = itemTopInViewport + item.ActualHeight / 2;
            double viewportCenter = viewportHeight / 2;

            // 计算需要滚动的偏移量
            double delta = itemCenter - viewportCenter;
            double targetOffset = scrollViewer.VerticalOffset + delta;

            targetOffset = Math.Max(0, Math.Min(targetOffset, scrollableHeight));
            scrollViewer.ScrollToVerticalOffset(targetOffset);
        }
        catch
        {
            // TransformToAncestor 可能因视觉树未就绪而失败，忽略本次居中
        }
    }

    private static ScrollViewer GetScrollViewer(DependencyObject dep)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = VisualTreeHelper.GetChild(dep, i);
            if (child is ScrollViewer sv)
                return sv;
            var result = GetScrollViewer(child);
            if (result != null)
                return result;
        }
        return null;
    }
}
