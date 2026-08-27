# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

QBLyricEditor 是一个 WPF (.NET 10, `net10.0-windows`) 桌面应用，用于制作/编辑 LRC 格式歌词：导入音频与歌词、逐行打点计时、时间平移/微调、预览、导出。仓库只有单个项目 `src/QBLyricEditor.csproj`，无测试工程。

## Commands

```bash
# 构建（在仓库根目录或 src/ 子目录均可，slnx 只包含一个项目）
dotnet build

# 运行
dotnet run --project src

# 发布单文件自包含 exe（"绿色版"，使用仓库自带的 FolderProfile 发布配置）
dotnet publish src -c Release -r win-x64 -p:PublishProfile=FolderProfile
# 产物路径：Release/（仓库根目录下，由 FolderProfile.pubxml 的 PublishDir 配置，不在 bin/ 深层路径里）
```

没有测试工程、没有 lint 配置文件；不要凭空引入测试框架或 lint 规则，除非用户明确要求。

版本号由 `version.json`（Nerdbank.GitVersioning，`.config/dotnet-tools.json` 里的 `nbgv` 工具）根据 git 提交自动生成，升级版本只需改 `version.json` 的 `version` 字段，不要手动改 csproj 里的版本相关属性。窗口标题的版本号（`MainWindow.xaml.cs` 里的 `AppTitle` 常量，当前为 `QBLyricEditor v3.1.5`）是独立手动维护的显示字符串，与 nbgv 生成的程序集版本号不是同一套，升级时需要单独改。

`bin/`、`obj/`、`Release/`、根目录下的 `*.zip` 均已加入 `.gitignore`，属于可通过上述命令重新生成的产物，不提交到仓库；打包绿色版发布包时手动压缩 `Release/` 内的 `exe`/`pdb`/`dll.config` 为 `QBLyricEditor-<版本号>-portable.zip`（如 `QBLyricEditor-3.1.5-portable.zip`），压缩完成后删除 `Release/` 下压缩包以外的中转文件（`exe`/`pdb`/`dll.config`），只保留 zip。

## 架构

### 分层

- **`Lyric/`** — 核心数据与业务逻辑，不依赖 UI 控件类型之外的 WPF 类型：
  - `LrcLine.cs`：单行歌词的模型（时间戳 + 文本），负责单行的 `Parse`/`ToString`（LRC 行格式）与排序比较。区分三种行：歌曲信息行（`[key:value]`，无时间戳）、正常/空白歌词行（`[mm:ss.fff]text`）、纯空行。
  - `LrcManager.cs`：全局单例 `LrcManager.Instance`，持有 `LrcList`（当前歌词的唯一真实来源），负责整份文本的导入解析（`LoadFromText`/`LoadFromFile`）、导出（`ToString`）、批量操作（增删/排序/移动/拖拽重排/整体或选中行时间平移）以及撤销/重做历史（`History` 快照，深拷贝 `LrcLine`）。所有 UI 面板都是这个单例状态的视图，改状态后要调用 `UpdateLrcList`/相应的 `Update*Panel`/`RefreshList` 让各面板重新同步。
  - `LrcHelper.cs`：时间戳字符串 `mm:ss.fff` 与 `TimeSpan` 之间的转换（含两位/三位毫秒的短/长格式）。

- **`Utils/`** — 与歌词模型无关的基础设施：
  - `AudioPlayer.cs`：封装 NAudio（`AudioFileReader` + `WaveOutEvent`）与 SoundTouch.Net（`SoundTouchWaveStream`）实现变速不变调播放；`Position` setter 会同时 `Flush()` SoundTouch 缓冲以避免跳转后残留音频。`PlaybackEnded` 事件区分"自然播放到末尾"与"手动 Stop"。
  - `FileHelper.cs`：支持的媒体/歌词文件后缀集合；用 Ude 做文本编码探测（`GetEncoding`）；`AnsiEncoding` 显式取本地代码页（.NET Core 后 `Encoding.Default` 恒为 UTF-8，不能用来实现"非 UTF-8 导出"）；`TempFileName` 指向 `%LocalAppData%/QBLyricEditor/temp.txt`，用于退出时自动缓存/下次启动自动恢复。
  - `TagLibHelper.cs`：用 TagLibSharp 读取音频文件的标题/内嵌封面。

- **`UserControls/`** — 三种可互相切换的歌词编辑视图，均挂载在 `MainWindow` 的 `LrcPanelContainer` 里，通过设置 `Content` 切换：
  - `LrcLineView`：逐行列表编辑（打点、时间微调、拖拽排序、批量选中操作），是"编辑模式"的主视图。
  - `LrcTextView`：纯文本编辑（整份 LRC 文本直接编辑，正则查找替换，清除所有时间标记）。
  - `LrcPreviewView`：只读预览，按播放位置定时高亮当前行并居中滚动；播放中才随播放位置刷新高亮（暂停时保持手动指定的高亮，避免因音频采样对齐导致误判上一行）。

- **`MainWindow.xaml.cs`**：应用的编排层/状态机，持有 `LrcPanelType`（`LrcLinePanel`/`LrcTextPanel`）与"打点/预览子模式"两层模式切换逻辑、`AudioPlayer` 实例、配置读写（`App.config` 的 `AppSettings`）、快捷键绑定（F5 打点、F1 帮助、Ctrl+P 播放、Ctrl+Z/Y 撤销重做、Insert 插入行）、文件拖放导入。窗口关闭时保存配置与自动缓存文件（两者各自 try/catch，互不影响）。

### 关键约定

- **单例状态**：`LrcManager.Instance` 是全局唯一的歌词数据源；不要在多处维护并行的歌词列表副本。任何修改 `LrcList` 的操作都应通过 `LrcManager` 的方法完成（它们会一并处理撤销历史记录），而不是绕过它直接改列表。
- **撤销历史是深拷贝快照**：`History` 构造时必须逐个 `new LrcLine(l)` 克隆，否则后续对同一 `LrcLine` 对象的原地属性修改会连带污染历史快照。新增会修改 `LrcLine` 属性的操作前，记得调用 `AddHistory`。
- **LRC 行解析必须容忍正文中的 `]` 字符**：`LrcLine.Parse` 按第一个 `]` 分割 tag/content（而不是 `Split(']')` 要求恰好两段），多时间戳行取最后一个时间标记之后的全部内容作为歌词文本；这是为了避免歌词正文本身含 `]` 时被误判为格式错误或截断内容。
- **编码处理**：读文件用 `FileHelper.GetEncoding` 自动探测；写文件（导出/自动缓存）按用户设置在 UTF-8 与 `FileHelper.AnsiEncoding`（本地代码页，而非 `Encoding.Default`）之间选择。
- **发布相关的 csproj 条件属性**：`PublishSingleFile`/`IncludeNativeLibrariesForSelfExtract`/`EnableCompressionInSingleFile` 都用 `Condition` 限定只在带 `RuntimeIdentifier`（且自包含）的 `dotnet publish` 下生效，普通 `dotnet build`/`dotnet run` 不受影响；改动这部分时注意保留这些条件，否则可能触发 NETSDK 相关报错或警告。
- **窗口标题**：`MainWindow.xaml.cs` 里的 `AppTitle` 常量固定为 `QBLyricEditor v3.1.5`；未导入音频时标题就是 `AppTitle`，导入音频后追加歌名（优先用 TagLib 读到的内嵌标题，否则用文件名）。帮助菜单里的"说明信息"（`Resources.resx` 的 `Info` 字符串，`Info_Click` 弹出）与标题栏版本号是两套独立内容，改标题格式时不要连带改动说明信息文本。
