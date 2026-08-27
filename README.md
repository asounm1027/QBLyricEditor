# QBLyricEditor

一个用于制作 / 编辑 LRC 格式歌词的 WPF 桌面应用（.NET 10）。

> 本软件基于 [BYJRK/LyricEditor](https://github.com/BYJRK/LyricEditor) 项目二次开发，在原有基础上增加了预览功能及一些其它细节优化。

## 功能特性

- 导入音频与歌词（支持 mp3 / wav / flac / m4a / aac 等音频格式，lrc / txt 歌词）
- 逐行打点计时，支持时间微调与整体 / 选中行时间平移
- 变速不变调播放（NAudio + SoundTouch）
- 三种编辑视图：逐行编辑 / 纯文本编辑 / 只读预览
- 撤销 / 重做
- 歌词文本编码自动探测，支持 UTF-8 / 系统 ANSI（如 GBK）导出
- 自动缓存，意外退出后可恢复未完成内容
- 支持发布为单文件自包含 exe（绿色版）

## 技术栈

- WPF（.NET 10，`net10.0-windows`）
- NAudio + SoundTouch.Net（音频播放、变速不变调）
- TagLibSharp（读取音频标题 / 内嵌封面）
- Ude（文本编码探测）
- Nerdbank.GitVersioning（版本号管理）

## 构建与运行

```bash
# 构建
dotnet build

# 运行
dotnet run --project src

# 发布单文件自包含 exe（绿色版）
dotnet publish src -c Release -r win-x64 -p:PublishProfile=FolderProfile
```

## 版本

当前版本：3.1.5

## 致谢

本软件基于 [BYJRK/LyricEditor](https://github.com/BYJRK/LyricEditor) 项目修改而来，感谢原作者的开源工作。
