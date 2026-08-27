using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ude;

namespace QBLyricEditor.Utils;

public static class FileHelper
{
    /// <summary>
    /// 支持的音乐文件格式
    /// </summary>
    public static HashSet<string> MediaExtensions { get; } = new HashSet<string>
    {
        ".mp3",
        ".wav",
        ".3gp",
        ".mp4",
        ".avi",
        ".wmv",
        ".wma",
        ".aac",
        ".flac",
        ".m4a",
    };

    /// <summary>
    /// 支持的歌词文件后缀
    /// </summary>
    public static HashSet<string> LyricExtensions { get; } = new HashSet<string> { ".lrc", ".txt" };

    /// <summary>
    /// 自动保存缓存文件的完整路径（位于用户本地数据目录下的 QBLyricEditor 文件夹中，而非程序运行目录）
    /// </summary>
    public static string TempFileName
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QBLyricEditor"
            );
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "temp.txt");
        }
    }

    /// <summary>
    /// 判断读入文本的编码格式
    /// </summary>
    public static Encoding GetEncoding(string filename)
    {
        var bytes = File.ReadAllBytes(filename);
        var cdet = new CharsetDetector();
        cdet.Feed(bytes, 0, bytes.Length);
        cdet.DataEnd();
        var encoding = cdet.Charset;
        // 检测失败（内容过短或特征不明显）时退回 UTF-8，避免 GetEncoding(null) 抛异常
        if (string.IsNullOrEmpty(encoding))
            return Encoding.UTF8;
        try
        {
            return Encoding.GetEncoding(encoding);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    /// <summary>
    /// 系统 ANSI 编码（例如中文系统下的 GBK）。
    /// 注意：.NET Core/5+ 上 Encoding.Default 恒为 UTF-8，不再随系统区域变化，
    /// 因此“非 UTF-8 导出”不能直接用 Encoding.Default，必须显式取本地代码页。
    /// 需要在启动时调用过 Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)。
    /// </summary>
    public static Encoding AnsiEncoding
    {
        get
        {
            try
            {
                return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
            }
            catch
            {
                // 极端情况下（代码页不可用）退回 UTF-8，保证不会崩溃
                return Encoding.UTF8;
            }
        }
    }
}
