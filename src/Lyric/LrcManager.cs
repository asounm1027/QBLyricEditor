using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using QBLyricEditor.Utils;

namespace QBLyricEditor.Lyric;

public partial class LrcManager
{
    public static LrcManager Instance { get; } = new LrcManager();

    public List<LrcLine> LrcList { get; private set; } = [];

    public int Count => LrcList.Count;

    public void Clear()
    {
        AddHistory(-1);
        LrcList.Clear();
    }

    public void LoadFromFile(string filename)
    {
        var encoding = FileHelper.GetEncoding(filename);
        var text = File.ReadAllText(filename, encoding);
        LoadFromText(text);
    }

    public bool LoadFromText(string text)
    {
        // 不论导入成功与否，均清空当前的显示
        Clear();

        // 导入的内容为空
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lines = NewLineRegex().Split(text);

        // 查找形如 [00:00.000] 的时间标记
        var reTimeMark = TimestampRegex();
        // 查找形如 [al:album] 的歌词信息
        var reLrcInfo = LyricInfoRegex();

        // 文本中不包含时间信息
        if (!reTimeMark.IsMatch(text))
        {
            foreach (var line in lines)
            {
                // 即便是不包含时间信息的歌词文本，也可能出现歌词信息
                if (reLrcInfo.IsMatch(line))
                {
                    LrcList.Add(new LrcLine(null, line.Trim('[', ']')));
                }
                // 否则将会为当前歌词行添加空白的时间标记，即便当前行是空行
                else
                    LrcList.Add(new LrcLine(0, line));
            }
        }
        // 文本中包含时间信息
        else
        {
            // 如果在解析过程中发现存在单行的多时间标记的情况，会在最后进行排序
            bool multiLrc = false;

            int lineNumber = 1;
            try
            {
                foreach (var line in lines)
                {
                    // 在确认文本中包含时间标记的情况下，会忽略所有空行
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        lineNumber++;
                        continue;
                    }

                    var matches = reTimeMark.Matches(line);
                    // 出现了类似 [00:00.000][00:01.000] 的包含多个时间信息的歌词行
                    if (matches.Count > 1)
                    {
                        // 取最后一个时间标记之后的全部内容作为歌词文本，
                        // 不能用正则单独匹配，否则歌词正文含有 ] 字符时会被截断
                        var lastMatch = matches[matches.Count - 1];
                        var lrc = line[(lastMatch.Index + lastMatch.Length)..];
                        foreach (var match in matches)
                        {
                            LrcList.Add(new LrcLine(LrcHelper.ParseTimeSpan(match.ToString().Trim('[', ']')), lrc));
                        }

                        multiLrc = true;
                    }
                    // 常规的单行歌词 [00:00.000]
                    else if (matches.Count == 1)
                    {
                        LrcList.Add(LrcLine.Parse(line));
                    }
                    else if (reLrcInfo.IsMatch(line))
                    {
                        LrcList.Add(new LrcLine(null, reLrcInfo.Match(line).ToString().Trim('[', ']')));
                    }
                    // 说明正常的歌词里面出现了一个不是空行，却没有时间标记的内容，则添加空时间标记
                    else
                    {
                        LrcList.Add(new LrcLine(TimeSpan.Zero, line));
                    }
                    lineNumber++;
                }
                // 如果出现单行出现多个歌词信息的情况，所以进行排序
                if (multiLrc)
                    LrcList = LrcList.OrderBy(x => x.LrcTime).ToList();
            }
            catch
            {
                MessageBox.Show($"歌词文本第{lineNumber}行存在格式问题，请在检查后重试。");
                LrcList.Clear();
                return false;
            }
        }
        return true;
    }

    public void Sort()
    {
        AddHistory(-1);
        LrcList = LrcList.OrderBy(x => x.LrcTime).ToList();
    }

    public LrcManager()
    {
        // 创建一个空的历史纪录，用来撤销到导入歌词前
        AddHistory(-1);
    }

    #region 历史记录

    /// <summary>
    /// 存储历史记录
    /// </summary>
    private List<History> HistoryList = new List<History>();

    /// <summary>
    /// 历史记录指针
    /// </summary>
    private int HistoryPointer = 0;

    /// <summary>
    /// 最大历史记录数量
    /// </summary>
    public int MaxHistoryCount { get; set; } = 20;

    /// <summary>
    /// 历史记录的数量
    /// </summary>
    private int HistoryCount => HistoryList.Count;

    private bool CanUndo => HistoryPointer > 0;
    private bool CanRedo => HistoryPointer < HistoryCount - 1;

    /// <summary>
    /// 将当前的歌词列表添加到历史纪录中，并移动历史记录指针
    /// </summary>
    public void AddHistory(int index = -1)
    {
        // 如果可以重做，说明历史记录中存在需要被清理的内容
        if (CanRedo)
            HistoryList.RemoveRange(HistoryPointer + 1, HistoryCount - 1 - HistoryPointer);
        HistoryList.Add(new History(LrcList, index));
        HistoryPointer++;

        // 超出上限时从头部裁剪，避免历史记录（每条都是整份歌词列表的深拷贝）无限增长
        if (HistoryCount > MaxHistoryCount)
        {
            int overflow = HistoryCount - MaxHistoryCount;
            HistoryList.RemoveRange(0, overflow);
            HistoryPointer -= overflow;
        }
    }

    public void AddHistory(ListView list)
    {
        AddHistory(list.SelectedIndex);
    }

    /// <summary>
    /// 撤销
    /// </summary>
    public void Undo(ListView list)
    {
        if (!CanUndo)
            return;
        var h = HistoryList[--HistoryPointer];
        LrcList = h.LrcList;
        UpdateLrcList(list, h.SelectedIndex);
    }

    /// <summary>
    /// 重做
    /// </summary>
    public void Redo(ListView list)
    {
        if (!CanRedo)
            return;
        var h = HistoryList[++HistoryPointer];
        LrcList = h.LrcList;
        UpdateLrcList(list, h.SelectedIndex);
    }

    #endregion

    /// <summary>
    /// 重设所有时间
    /// </summary>
    public void ResetAllTime(ListView list)
    {
        AddHistory(list);
        foreach (var line in LrcList)
        {
            if (line.LrcTime is null)
                continue;
            line.LrcTime = TimeSpan.Zero;
        }
        UpdateLrcList(list);
    }

    /// <summary>
    /// 整体时间平移
    /// </summary>
    public void ShiftAllTime(ListView list, TimeSpan offset)
    {
        AddHistory(list);
        foreach (var line in LrcList)
        {
            if (line.LrcTime is null)
                continue;
            line.LrcTime += offset;
            if (line.LrcTime < TimeSpan.Zero)
                line.LrcTime = TimeSpan.Zero;
        }
        UpdateLrcList(list);
    }

    /// <summary>
    /// 添加新行
    /// </summary>
    public void AddNewLine(ListView list, TimeSpan time)
    {
        int index = list.SelectedIndex;
        AddHistory(index);
        LrcList.Insert(index + 1, new LrcLine(time));
        UpdateLrcList(list, index + 1);
    }

    /// <summary>
    /// 删除行
    /// </summary>
    public void DeleteLine(ListView list)
    {
        int index = list.SelectedIndex;
        if (index < 0)
            return;
        AddHistory(index);
        LrcList.RemoveAt(index);
        // 如果删除的是最后一行，则选中上一行；否则保持不变
        if (index >= Count)
            index--;
        UpdateLrcList(list, index);
    }

    /// <summary>
    /// 根据歌词列表更新列表项的显示
    /// </summary>
    public void UpdateLrcList(ListView list)
    {
        list.Items.Clear();

        foreach (var line in LrcList)
        {
            list.Items.Add(line);
        }

        list.Items.Refresh();
    }

    /// <summary>
    /// 根据歌词列表更新列表项的显示，并选中指定一项
    /// </summary>
    public void UpdateLrcList(ListView list, int index)
    {
        UpdateLrcList(list);
        list.SelectedIndex = index;
        list.ScrollIntoView(list.SelectedItem);
    }

    /// <summary>
    /// 批量删除选中的行
    /// </summary>
    public void DeleteLines(ListView list)
    {
        var indices = list.SelectedIndices().OrderByDescending(i => i).ToList();
        if (indices.Count == 0)
            return;
        AddHistory(list.SelectedIndex);
        foreach (var i in indices)
        {
            LrcList.RemoveAt(i);
        }
        // 选中被删区域的第一行，或最后一行
        int newIndex = Math.Min(indices.Last(), LrcList.Count - 1);
        UpdateLrcList(list, newIndex);
    }

    /// <summary>
    /// 批量上移选中的行（整体移动，保持相对顺序）
    /// </summary>
    public void MoveUp(ListView list)
    {
        var indices = list.SelectedIndices().OrderBy(i => i).ToList();
        if (indices.Count == 0 || indices[0] <= 0)
            return;
        AddHistory(list.SelectedIndex);

        // 先提取再重插，避免相邻选择时原地 swap 导致的索引错乱
        var selected = indices.Select(i => LrcList[i]).ToList();
        for (int i = indices.Count - 1; i >= 0; i--)
            LrcList.RemoveAt(indices[i]);
        for (int i = 0; i < indices.Count; i++)
            LrcList.Insert(indices[i] - 1, selected[i]);

        UpdateLrcList(list);
        // 恢复多选：所有选中行都上移了 1
        foreach (var i in indices)
            list.SelectedItems.Add(list.Items[i - 1]);
        list.ScrollIntoView(list.Items[indices[0] - 1]);
    }

    /// <summary>
    /// 批量下移选中的行（整体移动，保持相对顺序）
    /// </summary>
    public void MoveDown(ListView list)
    {
        var indices = list.SelectedIndices().OrderBy(i => i).ToList();
        if (indices.Count == 0 || indices.Last() >= LrcList.Count - 1)
            return;
        AddHistory(list.SelectedIndex);

        // 先提取再重插，避免相邻选择时原地 swap 导致的索引错乱
        var selected = indices.Select(i => LrcList[i]).ToList();
        for (int i = indices.Count - 1; i >= 0; i--)
            LrcList.RemoveAt(indices[i]);
        for (int i = 0; i < indices.Count; i++)
            LrcList.Insert(indices[i] + 1, selected[i]);

        UpdateLrcList(list);
        // 恢复多选：所有选中行都下移了 1
        foreach (var i in indices)
            list.SelectedItems.Add(list.Items[i + 1]);
        list.ScrollIntoView(list.Items[indices.Last() + 1]);
    }

    /// <summary>
    /// 拖动重排：将当前选中的行移动到目标位置
    /// </summary>
    public void ReorderLines(ListView list, int targetIndex)
    {
        var selectedIndices = list.SelectedIndices().OrderBy(i => i).ToList();
        if (selectedIndices.Count == 0)
            return;

        // 目标位置已在选中范围内，无需移动
        if (targetIndex >= selectedIndices[0] && targetIndex <= selectedIndices[selectedIndices.Count - 1] + 1)
            return;

        AddHistory(list.SelectedIndex);

        var selectedLines = selectedIndices.Select(i => LrcList[i]).ToList();

        // 从后往前删除，避免索引偏移
        for (int i = selectedIndices.Count - 1; i >= 0; i--)
        {
            LrcList.RemoveAt(selectedIndices[i]);
        }

        // 调整目标位置（考虑已删除的行）并钳制到有效范围
        int adjustedTarget = targetIndex;
        foreach (var si in selectedIndices)
        {
            if (si < targetIndex)
                adjustedTarget--;
        }
        adjustedTarget = Math.Min(adjustedTarget, LrcList.Count);

        for (int i = 0; i < selectedLines.Count; i++)
        {
            LrcList.Insert(adjustedTarget + i, selectedLines[i]);
        }

        UpdateLrcList(list);
        for (int i = 0; i < selectedLines.Count; i++)
        {
            list.SelectedItems.Add(list.Items[adjustedTarget + i]);
        }
        list.ScrollIntoView(list.Items[adjustedTarget]);
    }

    /// <summary>
    /// 对选中行进行时间偏移
    /// </summary>
    public void ShiftSelectedTime(ListView list, TimeSpan offset)
    {
        var indices = list.SelectedIndices().OrderBy(i => i).ToList();
        if (indices.Count == 0)
            return;
        AddHistory(list.SelectedIndex);
        foreach (var i in indices)
        {
            var line = LrcList[i];
            if (line.LrcTime is null)
                continue;
            line.LrcTime += offset;
            if (line.LrcTime < TimeSpan.Zero)
                line.LrcTime = TimeSpan.Zero;
        }
        UpdateLrcList(list);
        foreach (var i in indices)
        {
            list.SelectedItems.Add(list.Items[i]);
        }
    }

    /// <summary>
    /// 获取当前时间对应的歌词行索引
    /// </summary>
    public int GetNearestLrcIndex(TimeSpan time)
    {
        // 不能假设 LrcList 按时间有序（用户可能没有排序、手动拖拽过顺序、
        // 或只对部分行做过时间平移），必须完整扫描找到 <= time 的最大时间对应的行，
        // 否则遇到乱序数据会提前退出导致返回错误索引，与 GetNearestLrc 的结果不一致。
        int bestIndex = -1;
        TimeSpan? bestTime = null;
        for (int i = 0; i < LrcList.Count; i++)
        {
            var t = LrcList[i].LrcTime;
            if (t.HasValue && t.Value <= time && (!bestTime.HasValue || t.Value > bestTime.Value))
            {
                bestTime = t.Value;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    /// <summary>
    /// 获取当前时间对应的歌词
    /// </summary>
    public string GetNearestLrc(TimeSpan time)
    {
        var line = LrcList
            .Where(x => x.LrcTime != null && x.LrcTime <= time)
            .OrderByDescending(x => x.LrcTime)
            .FirstOrDefault();

        return line != null ? line.LrcText : string.Empty;
    }

    /// <summary>
    /// 返回能够用于写 lrc 文件的文本
    /// </summary>
    public override string ToString()
    {
        return string.Join(Environment.NewLine, LrcList.Select(x => x.ToString()));
    }

    [GeneratedRegex(@"\r?\n")]
    private static partial Regex NewLineRegex();

    [GeneratedRegex(@"\[\d+\:\d+\.\d+\]")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"\[\w+\:.+\]")]
    private static partial Regex LyricInfoRegex();
}

public static class ListViewExtensions
{
    /// <summary>
    /// 获取 ListView 中所有选中项的索引
    /// </summary>
    public static IEnumerable<int> SelectedIndices(this System.Windows.Controls.ListView list)
    {
        foreach (var item in list.SelectedItems)
        {
            int index = list.Items.IndexOf(item);
            if (index >= 0)
                yield return index;
        }
    }
}

public class History
{
    public List<LrcLine> LrcList { get; init; }
    public int SelectedIndex { get; init; }

    public History(List<LrcLine> list, int index)
    {
        // 必须逐个克隆 LrcLine（深拷贝），否则后续对同一对象的原地属性修改
        // （打点、微调步进、平移等）会连带把这份"历史快照"也改掉，导致撤销失效
        LrcList = list.Select(l => new LrcLine(l)).ToList();
        SelectedIndex = index;
    }
}
