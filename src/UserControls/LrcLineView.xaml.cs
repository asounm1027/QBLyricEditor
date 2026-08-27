using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using QBLyricEditor.Lyric;

namespace QBLyricEditor.UserControls;

public partial class LrcLineView : UserControl
{
    public LrcLineView()
    {
        InitializeComponent();

        LrcLinePanel.Items.Clear();
        CurrentTimeText.Clear();
        CurrentLrcText.Clear();
    }

    public TimeSpan TimeOffset { get; set; } = new TimeSpan(0, 0, 0, 0, -150);

    /// <summary>
    /// 当前选中的时间调整步进（毫秒）
    /// </summary>
    public int TimeAdjustStep { get; set; } = 50;

    #region 拖拽排序

    private Point _dragStartPoint;
    private bool _isDragPending;

    #endregion

    public bool HasSelection
    {
        get => SelectedIndex != -1;
    }
    public int SelectedIndex
    {
        get => LrcLinePanel.SelectedIndex;
        set => LrcLinePanel.SelectedIndex = value;
    }
    public bool ReachEnd
    {
        get => SelectedIndex == LrcLinePanel.Items.Count - 1;
    }

    /// <summary>
    /// 修改了单行的信息后，更新歌词列表的显示
    /// </summary>
    public void RefreshLrcPanel()
    {
        LrcLinePanel.Items.Refresh();
    }

    /// <summary>
    /// 同步 LrcManager.Instance 与歌词列表
    /// </summary>
    public void UpdateLrcPanel()
    {
        LrcManager.Instance.UpdateLrcList(LrcLinePanel);
    }

    /// <summary>
    /// 根据选择的行数更改下方文本框的内容
    /// </summary>
    public void UpdateBottomTextBoxes()
    {
        // 如果只选中了一项
        if (LrcLinePanel.SelectedItems.Count == 1)
        {
            LrcLine line = LrcLinePanel.SelectedItem as LrcLine;
            if (!(line.LrcTime is null))
                CurrentTimeText.Text = line.LrcTimeText;
            else
                CurrentTimeText.Clear();
            CurrentLrcText.Text = line.LrcText;
        }
    }

    /// <summary>
    /// 歌词窗口的选择项发生改变
    /// </summary>
    private void LrcLinePanel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!HasSelection)
            return;
        UpdateBottomTextBoxes();
    }

    /// <summary>
    /// 时间框获得焦点时记录一次历史（作为这次编辑开始前的状态），
    /// 避免在 TextChanged 里逐字符记录导致历史被打字过程刷屏
    /// </summary>
    private void CurrentTimeText_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!HasSelection)
            return;
        LrcManager.Instance.AddHistory(LrcLinePanel);
    }

    /// <summary>
    /// 更改时间框的文本，更新主列表
    /// </summary>
    private void CurrentTime_Changed(object sender, TextChangedEventArgs e)
    {
        if (!HasSelection)
            return;

        int index = SelectedIndex;
        if (LrcHelper.TryParseTimeSpan(CurrentTimeText.Text, out TimeSpan time))
        {
            LrcManager.Instance.LrcList[index].LrcTime = time;
            ((LrcLine)LrcLinePanel.Items[index]).LrcTime = time;
            RefreshLrcPanel();
        }
        else if (string.IsNullOrWhiteSpace(CurrentTimeText.Text))
        {
            LrcManager.Instance.LrcList[index].LrcTime = null;
            ((LrcLine)LrcLinePanel.Items[index]).LrcTime = null;
            RefreshLrcPanel();
        }
    }

    /// <summary>
    /// 歌词框获得焦点时记录一次历史（同上，避免逐字符记录）
    /// </summary>
    private void CurrentLrcText_GotFocus(object sender, RoutedEventArgs e)
    {
        if (!HasSelection)
            return;
        LrcManager.Instance.AddHistory(LrcLinePanel);
    }

    /// <summary>
    /// 更改歌词框的文本，更新主列表
    /// </summary>
    private void CurrentLrc_Changed(object sender, TextChangedEventArgs e)
    {
        if (!HasSelection)
            return;

        int index = SelectedIndex;
        LrcManager.Instance.LrcList[index].LrcText = CurrentLrcText.Text;
        ((LrcLine)LrcLinePanel.Items[index]).LrcText = CurrentLrcText.Text;
        RefreshLrcPanel();
    }

    /// <summary>
    /// 在时间框中使用滚轮
    /// </summary>
    private void CurrentTimeText_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!HasSelection)
            return;
        if (string.IsNullOrWhiteSpace(CurrentTimeText.Text))
            return;
        // 如果选中行没有时间戳（信息行）
        int index = SelectedIndex;
        if (index < 0 || !LrcManager.Instance.LrcList[index].LrcTime.HasValue)
            return;

        int deltaMs = e.Delta > 0 ? TimeAdjustStep : -TimeAdjustStep;
        LrcManager.Instance.AddHistory(LrcLinePanel);
        AdjustCurrentLineTime(new TimeSpan(0, 0, 0, 0, deltaMs));
    }

    /// <summary>
    /// 双击主列表，跳转播放时间并自动开始播放
    /// </summary>
    private void LrcLinePanel_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!HasSelection)
            return;

        LrcLine line = LrcLinePanel.SelectedItem as LrcLine;
        if (!line.LrcTime.HasValue)
            return;

        var mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.MediaPlayer.Position = line.LrcTime.Value;
        mainWindow.PlayMedia();
    }

    /// <summary>
    /// 在主列表上使用按键
    /// </summary>
    private void LrcLinePanel_KeyUp(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Delete:
                DeleteLine();
                e.Handled = true;
                break;
            case Key.Space:
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    SetCurrentLineTimeFromKeyboard();
                }
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// 由键盘空格触发的打点操作（需要从 MainWindow 获取播放位置）
    /// </summary>
    private void SetCurrentLineTimeFromKeyboard()
    {
        var mainWindow = (MainWindow)Application.Current.MainWindow;
        if (!mainWindow.IsMediaAvailable)
            return;
        SetCurrentLineTime(mainWindow.MediaPlayer.Position);
    }

    private void AdjustCurrentLineTime(TimeSpan delta)
    {
        int index = SelectedIndex;

        var currentTime = LrcManager.Instance.LrcList[index].LrcTime.Value.Add(delta);
        if (currentTime < TimeSpan.Zero)
            currentTime = TimeSpan.Zero;

        LrcManager.Instance.LrcList[index].LrcTime = currentTime;
        ((LrcLine)LrcLinePanel.Items[index]).LrcTime = currentTime;

        UpdateBottomTextBoxes();
    }

    public void SetCurrentLineTime(TimeSpan time)
    {
        if (!HasSelection)
            return;
        int index = SelectedIndex;

        // 判断是否为歌曲信息行
        if (!LrcManager.Instance.LrcList[index].LrcTime.HasValue)
            return;

        time += TimeOffset;
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;

        LrcManager.Instance.AddHistory(LrcLinePanel);

        LrcManager.Instance.LrcList[index].LrcTime = time;
        ((LrcLine)LrcLinePanel.Items[index]).LrcTime = time;

        // 根据是否到达最后一行来设定下一个选中行
        if (!ReachEnd)
        {
            SelectedIndex++;
        }
        else
        {
            SelectedIndex = -1;
        }

        RefreshLrcPanel();
        LrcLinePanel.ScrollIntoView(LrcLinePanel.SelectedItem);
    }

    public void ResetAllTime() => LrcManager.Instance.ResetAllTime(LrcLinePanel);

    public void ShiftAllTime(TimeSpan offset) => LrcManager.Instance.ShiftAllTime(LrcLinePanel, offset);

    public void Undo() => LrcManager.Instance.Undo(LrcLinePanel);

    public void Redo() => LrcManager.Instance.Redo(LrcLinePanel);

    public void AddNewLine(TimeSpan time) => LrcManager.Instance.AddNewLine(LrcLinePanel, time);

    public void DeleteLine()
    {
        if (LrcLinePanel.SelectedItems.Count > 1)
            LrcManager.Instance.DeleteLines(LrcLinePanel);
        else
            LrcManager.Instance.DeleteLine(LrcLinePanel);
    }

    public void MoveUp() => LrcManager.Instance.MoveUp(LrcLinePanel);

    public void MoveDown() => LrcManager.Instance.MoveDown(LrcLinePanel);

    /// <summary>
    /// - / + 微调按钮
    /// </summary>
    private void TimeAdjustButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HasSelection)
            return;
        if (string.IsNullOrWhiteSpace(CurrentTimeText.Text))
            return;

        int deltaMs = ((Button)sender).Name == "TimeIncreaseButton" ? TimeAdjustStep : -TimeAdjustStep;
        LrcManager.Instance.AddHistory(LrcLinePanel);
        AdjustCurrentLineTime(new TimeSpan(0, 0, 0, 0, deltaMs));
    }

    #region 拖拽排序事件处理

    private void LrcLinePanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragPending = true;

        // 如果点击的是已选中行且处于多选状态，阻止 ListView 破坏多选
        if (LrcLinePanel.SelectedItems.Count > 1)
        {
            var hit = LrcLinePanel.InputHitTest(e.GetPosition(LrcLinePanel));
            var clickedItem = FindListViewItem(hit as DependencyObject);
            if (clickedItem != null && clickedItem.IsSelected)
            {
                e.Handled = true;
            }
        }
    }

    private void LrcLinePanel_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragPending)
            return;
        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        Point currentPoint = e.GetPosition(null);
        Vector diff = _dragStartPoint - currentPoint;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            _isDragPending = false;
            if (LrcLinePanel.SelectedItems.Count > 0)
            {
                DragDrop.DoDragDrop(LrcLinePanel, new DataObject("LrcLineReorder", true), DragDropEffects.Move);
            }
        }
    }

    private void LrcLinePanel_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("LrcLineReorder"))
            return;
        e.Effects = DragDropEffects.Move;

        Point pt = e.GetPosition(LrcLinePanel);

        // 自动滚动
        double scrollOffset = 0;
        if (pt.Y < 30)
            scrollOffset = -3;
        else if (pt.Y > LrcLinePanel.ActualHeight - 30)
            scrollOffset = 3;

        if (scrollOffset != 0)
        {
            var scrollViewer = GetScrollViewer(LrcLinePanel);
            scrollViewer?.ScrollToVerticalOffset(scrollViewer.VerticalOffset + scrollOffset);
        }

        // 更新插入位置指示线
        UpdateDragIndicator(pt);

        e.Handled = true;
    }

    private void LrcLinePanel_DragLeave(object sender, DragEventArgs e)
    {
        DragInsertIndicator.Visibility = Visibility.Collapsed;
    }

    private void LrcLinePanel_Drop(object sender, DragEventArgs e)
    {
        DragInsertIndicator.Visibility = Visibility.Collapsed;

        if (!e.Data.GetDataPresent("LrcLineReorder"))
            return;

        // 计算目标位置
        Point pt = e.GetPosition(LrcLinePanel);
        int targetIndex = ResolveTargetIndex(pt);

        LrcManager.Instance.ReorderLines(LrcLinePanel, targetIndex);
        e.Handled = true;
    }

    /// <summary>
    /// 根据鼠标位置计算目标插入索引
    /// </summary>
    private int ResolveTargetIndex(Point pt)
    {
        // 先尝试通过 hit test 找到当前鼠标下的 ListViewItem
        var hit = LrcLinePanel.InputHitTest(pt);
        var hitItem = FindListViewItem(hit as DependencyObject);

        if (hitItem != null)
        {
            int itemIndex = LrcLinePanel.ItemContainerGenerator.IndexFromContainer(hitItem);

            // 判断鼠标在 item 的上半部分还是下半部分
            var itemTransform = hitItem.TransformToAncestor(LrcLinePanel);
            double itemTop = itemTransform.Transform(new Point(0, 0)).Y;
            double itemMid = itemTop + hitItem.ActualHeight / 2;

            if (pt.Y > itemMid)
                itemIndex++; // 插入到该行之后

            return itemIndex;
        }

        // 鼠标在空白区域 → 插入到末尾
        return LrcLinePanel.Items.Count;
    }

    /// <summary>
    /// 更新拖拽插入位置指示线
    /// </summary>
    private void UpdateDragIndicator(Point pt)
    {
        int targetIndex = ResolveTargetIndex(pt);

        // 获取目标位置之前的那个 item（用于定位指示线）
        ListViewItem refItem = null;
        double indicatorY = 0;

        if (targetIndex < LrcLinePanel.Items.Count)
        {
            refItem = LrcLinePanel.ItemContainerGenerator.ContainerFromIndex(targetIndex) as ListViewItem;
            if (refItem != null)
            {
                var transform = refItem.TransformToAncestor(LrcLinePanel);
                indicatorY = transform.Transform(new Point(0, 0)).Y;
            }
            else
            {
                indicatorY = targetIndex * 30; // fallback 估算
            }
        }
        else
        {
            // 插入到末尾
            var lastItem = LrcLinePanel.ItemContainerGenerator.ContainerFromIndex(LrcLinePanel.Items.Count - 1) as ListViewItem;
            if (lastItem != null)
            {
                var transform = lastItem.TransformToAncestor(LrcLinePanel);
                indicatorY = transform.Transform(new Point(0, 0)).Y + lastItem.ActualHeight;
            }
            else
            {
                indicatorY = LrcLinePanel.Items.Count * 30;
            }
        }

        DragInsertIndicator.Margin = new Thickness(0, indicatorY, 0, 0);
        DragInsertIndicator.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 在可视树中向上查找 ListViewItem
    /// </summary>
    private static ListViewItem FindListViewItem(DependencyObject obj)
    {
        while (obj != null)
        {
            if (obj is ListViewItem lvi)
                return lvi;
            obj = System.Windows.Media.VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    /// <summary>
    /// 获取 ListView 内部的 ScrollViewer
    /// </summary>
    private static ScrollViewer GetScrollViewer(DependencyObject dep)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(dep); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(dep, i);
            if (child is ScrollViewer sv)
                return sv;
            var result = GetScrollViewer(child);
            if (result != null)
                return result;
        }
        return null;
    }

    #endregion
}
