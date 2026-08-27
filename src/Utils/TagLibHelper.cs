using System.IO;
using System.Windows.Media.Imaging;

namespace QBLyricEditor.Utils;

public static class TagLibHelper
{
    /// <summary>
    /// 获取音乐文件的封面图
    /// </summary>
    public static BitmapImage GetAlbumArt(string filename)
    {
        var file = TagLib.File.Create(filename);
        var pictures = file.Tag.Pictures;
        // 没有内嵌封面是常见情况，不用异常做控制流，直接返回 null 交给调用方走默认封面
        if (pictures is null || pictures.Length == 0)
            return null;

        var bin = pictures[0].Data.Data;
        BitmapImage image = new BitmapImage();
        image.BeginInit();
        // 让 WPF 立即把数据读进内存并释放底层流，避免 MemoryStream 被无谓地长期持有
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = new MemoryStream(bin);
        image.EndInit();
        image.Freeze();

        return image;
    }

    /// <summary>
    /// 获取音乐文件的歌曲标题
    /// </summary>
    public static string GetTitle(string filename)
    {
        var file = TagLib.File.Create(filename);
        return file.Tag.Title;
    }
}
