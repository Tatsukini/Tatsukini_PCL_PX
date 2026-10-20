# PCL PX 改动披露

本文件逐条记录 **PCL PX**（开发者：**Tatsukini**）相对上游 [Meloong-Git/PCL](https://github.com/Meloong-Git/PCL) 的全部改动，供上游开发者、使用者与审查者核对。按《PCL 存储库合理使用指南》二.1、二.3 的要求，本仓库是关于页声明的展开版本。

- **上游基线**：commit `6b9a50df911c9d68b69b24c979efba477fb14b67`（`2.99.0.0 正式版`，2026-10-20）
- **本版本**：`PX beta0.0.1`（`VersionBaseName`，见 `Modules/Base/ModBase.vb:9`）
- **规模**：修改既有文件 26 个（+1121 / −176 行），新增源文件 13 个，删除仓库文件 4 类
- **未做**：不改上游许可、不做任何形式的收款、不做遥测、不劫持更新

<br />

## 一、新增功能

### 1. 内存优化（更多 → 小工具）
- 新增 `Modules/Base/ModMemoryOptimize.vb`（316 行）：调用 `EmptyWorkingSet` 整理指定进程的工作集，可选按模式（`MemoryOptimizeMode`）批量处理，以及「优化后关闭指定进程」（`MemoryOptimizeKillEnabled` / `MemoryOptimizeKill`）。
- `Pages/PageOther/PageOtherTest.xaml` / `.xaml.vb`：新增「内存优化」卡片、进程列表（`PanMemoryProcList`）、模式选择（`ComboMemoryMode`）、信息栏（`LabMemoryInfo`）与「高级设置」子面板（`PanMemoryAdvanced`、`CheckMemoryKill`）。
- `Pages/PageSetup/Settings.vb`：注册 3 个新设置项。

### 2. 一键生成服务端（更多 → 小工具）
- 新增 `Modules/Minecraft/ModServerTool.vb`（764 行）：支持**原版服务端**与**整合包转服务端**两条路径（版本列表获取、EULA 处理、目录组装、启动脚本生成）。
- `Pages/PageOther/PageOtherTest.xaml` / `.xaml.vb`：新增「生成服务端」子页（`PanServerGen`、`ComboServerVersion`、`BtnGenVanilla`、`BtnGenModpack`、`TxtServerPack` 等），与小工具首页（`PanToolHome`）互切。

### 3. 实时日志窗口
- 新增 `Modules/Base/LiveLog.vb`（110 行）与 `FrmLiveLog.xaml` / `.xaml.vb`（80 行）：独立窗口实时滚动启动器自身日志。
- `Modules/Base/PclLogger.vb`：重写 `Output(entry)`，在写文件的同时把消息推给 `LiveLog`。
- `Pages/PageSetup/PageSetupSystem.xaml`：调试卡片新增「实时显示日志」勾选（设置项 `SystemLogLive`，变更即生效）。
- `FormMain.xaml.vb`：启动时按 `SystemLogLive` 应用。

### 4. 崩溃分析升级
- `Modules/Minecraft/ModCrash.vb`（+284 行）：为崩溃原因引入**分类**（`GetCategory` / `GetMainCategory` / `GetCategoryName` / `GetCategoryHint`）与**置信度**（`GetConfidence` / `GetConfidenceName`），按置信度排序输出（`SortedReasons`、`FilterReasons`），并把堆栈里的 Mod 类名归一化匹配到实际 jar 文件名（`ResolveModFiles` / `TryGetModFileName` / `NormalizeModName`），使报错直接指向需要处理的 Mod。

### 5. 联机（TerraCotta，更多 → 联机）
- 新增 `Modules/ModTerracotta.vb`（552 行）与 `Pages/PageOther/PageOtherLink.xaml` / `.xaml.vb`（344 行）：节点、房间、玩家名配置与联机入口。
- `FormMain.xaml.vb`：`PageSubType.OtherLink = 3`；`Modules/ModMain.vb`：`FrmOtherLink` 单例。
- `Pages/PageOther/PageOtherLeft.xaml` / `.xaml.vb`：新增导航项 `ItemLink`，并把「第一个可用子页」的判断重构成 `FirstAvaliableSubPage()`（原来硬编码三段 If/Else，新增子页会漏）。
- `Pages/PageSetup/Settings.vb`：`LinkTerracottaPlayer` / `LinkTerracottaNodes` / `LinkTerracottaRoom`、`UiHiddenOtherLink`。

### 6. Java 下载（下载 → Java）
- 新增 `Pages/PageDownload/PageDownloadJava.xaml` / `.xaml.vb`（402 行）：基于 **Adoptium API v3**。
  - `GET /v3/info/available_releases` 取版本列表；因不同代际接口键名不一致，按 `available_ga_releases` → `available_releases` 顺序取值，失败时把截断后的原始响应写进错误提示。
  - `GET /v3/assets/latest/{major}/hotspot?os=windows&architecture={x64|x86}&image_type={jdk|jre}` 取包地址，直接取 `binary.package.link`（zip）并附带 SHA256 校验信息。
  - 可选大版本、JDK/JRE、32/64 位，标注 LTS；并列出本机目录中已下载的 Java。
- `Pages/PageDownload/PageDownloadLeft.xaml` / `.xaml.vb`：新增分组标题「外部资源」与导航项 `ItemJava`（`Tag="8"`）及其刷新按钮；`FormMain.PageSubType.DownloadJava = 8`。
  > 注意：`PageDownloadLeft` 的子页编号必须与 `PanItem.Children` 的下标一致，新项一律追加在末尾，不改动既有编号。

### 7. 皮肤 / 披风下载（下载 → 皮肤）
- 新增 `Pages/PageDownload/PageDownloadSkin.xaml` / `.xaml.vb`（437 行）：两种图片来源。
  - **LittleSkin**：`GET https://littleskin.cn/csl/{玩家名}.json` → `{username, skins:{default|slim}, cape}`，贴图取 `https://littleskin.cn/csl/textures/{hash}`；不存在的玩家返回 HTTP 200 + `{}`，按「查无此人」处理。
  - **正版**：`GET https://api.mojang.com/users/profiles/minecraft/{name}` 取 UUID，再 `GET https://sessionserver.mojang.com/session/minecraft/profile/{uuid}` 解码 `properties[].value`（base64）取 `textures.SKIN` / `textures.CAPE`。该接口对未知档案返回 **204 空响应**，所以此处不能要求 JSON，改为判空并给出针对性提示；玩家名与 UID 两种输入都支持（UID 自动补/去连字符）。
  - 皮肤按 64×64 / 64×32 判宽窄模型，支持分别下载皮肤与披风。
- 导航项 `ItemSkin`（`Tag="9"`）、`FormMain.PageSubType.DownloadSkin = 9`、`Modules/ModMain.vb` 的 `FrmDownloadJava` / `FrmDownloadSkin` 单例。

### 8. 自定义主题色（需赞助解锁）+ 动态视频背景
- `Modules/ModSecret.vb`：
  - 新增 `ThemeApplyHsl()`，为各主题写入 HSL 参数（色相/饱和度/明度偏移/顶栏色相差），使主题切换后配色正确；`ThemeRefresh` 与 `ThemeRefreshMain` 都会调用。
  - 新增 `ThemeCustomRequireSponsor`（分发版恒为 `True`）、`ThemeCustomUnlocked()`、`ThemeCustomUnlockRequest()`：未解锁时自定义主题不可用，卡片内给出「赞助 PCL」（打开 https://meloong.com/afd/a/LTCat）与「我已赞助，解锁」两个入口。解锁为**荣誉制确认**，本分支不做任何收款，赞助全部指向上游。
  - `ThemeCheckAll` 除越界纠正外，还会在**未解锁**时把 `UiLauncherTheme` 从 14 复位为 0（防止导入他机设置后越过门槛）。
  - `ThemeCheckOne` 与 `ThemeUnlock` 保持上游原样（上游在开源分支里就是恒 `True` / 恒 `False` 的空壳，本项目没有绕过任何真实校验）。
- `Pages/PageSetup/PageSetupUI.xaml` / `.xaml.vb`：
  - 自定义主题面板 `PanThemeCustom`（`BtnThemePickColor` 从图片取主色、`BtnThemeResetCustom` 复位）与 `GetImageMainColor()` 主色统计算法；`PanThemeCustomLock` 赞助入口行；`ThemeCustomRefresh()` 同步 `RadioLauncherTheme14.IsEnabled` 与解锁行可见性。
  - **视频背景卡片**（`CardVideo`、`BtnVideoImport` / `BtnVideoFolder` / `BtnVideoClear`、`PanVideoSuit`、`PanVideoDetail`、`CheckVideoLoop`），**不加任何解锁**；`ImgBack.Background` 的类型判断改为 `TypeOf ... Is ImageBrush`，避免与视频背景互相误清；背景的不透明度与模糊两行在有视频时同样显示。
- 新增 `Modules/Base/ModVideoBg.vb`（172 行）：视频背景的装载、适配（`UiBackgroundVideoSuit`）、速度（`...Speed`）、音量（`...Volume`）、循环（`...Loop`）与 `VideoBackgroundApply` / `VideoBackgroundClose`。
- `Pages/PageSetup/Settings.vb`：注册 `UiBackgroundVideo*` 5 项、自定义主题的 `UiLauncherHue/Sat/Light/Delta`，以及 `UiLauncherThemeCustomUnlock`（写注册表，仅本机）。

### 9. 下载源修复（NeoForge 在中国大陆被封锁）
- `Modules/Minecraft/ModDownload.vb`：
  - `DlNeoForgeListOfficialMain` 的两次列表请求改为**先走 `neoforged.forgecdn.net`**，官方 `maven.neoforged.net` 降为 `BackupUrl`（否则连接会挂到 45s 重试耗尽）。
  - 新增 `DlNeoForgeFileGet(Original)`：为安装器 / userdev 等文件给出 BMCLAPI 与 forgecdn 两个镜像，官方地址兜底。
  - Mod/加载器文件的镜像列表补齐 `maven.neoforged.net`、`maven.minecraftforge.net`、`maven.fabricmc.net` 三条替换，并把官方地址追加为最后一个候选。
- `Pages/PageDownload/ModDownloadLib.vb`：两处 NeoForge installer 下载改用 `DlNeoForgeFileGet`。

### 10. 恢复 Minecraft 下载入口
- `Modules/ModSecret.vb`：`RestrictMcDownload` 由 `True` 改为 `False`，重新开放原版与整合包的下载/安装界面（上游在开源分支中主动屏蔽）。
- `FormMain.xaml.vb`：更新日志里把「删除：为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能」替换为「新增：恢复 Minecraft 本体与整合包的下载、安装功能」。
- ⚠️ 这是与上游意图相反的一处改动，风险见 README 的「已知的合规风险」表。

<br />

## 二、更名为 PCL PX

改名只动**用户可见标识**，不动任何内部约定，以免破坏资源打包与已有配置。

| 位置 | 改动 |
| --- | --- |
| `FormMain.xaml:7` | 窗口标题 `Plain Craft Launcher　` → `PCL PX　`（保留全角空格，与标题渲染逻辑一致） |
| `My Project/AssemblyInfo.vb` | `AssemblyTitle` → `PCL PX 启动器`；`AssemblyDescription` → `Minecraft 启动器（基于 Plain Craft Launcher 的第三方修改版）`；`AssemblyCompany` → `Tatsukini`；`AssemblyProduct` → `PCL PX` |
| `Application.xaml.vb:155` | 单例互斥量 `PCL_SingletonMutex` → `PCL_PX_SingletonMutex`（顺带让本分支能与官方 PCL 同时运行）；日志文案改为「已有一个 PCL PX 实例正在运行」；`FindWindow` 只找 `"PCL PX　"`，删掉原先两次官方窗口标题的回退 |
| `Pages/PageOther/PageOtherAbout.xaml` | 第三条目 `Plain Craft Launcher 2` → `PCL PX`，Info 追加「由 Tatsukini 独立二次创作」；新增第 4 行说明：本软件是第三方基于 Plain Craft Launcher 独立二次创作、与官方无关，并指回上游仓库与本文件 |
| `Pages/PageInstance/PageInstanceExport.xaml.vb:569` | 导出整合包时复制出的启动器命名为 `PCL PX.exe` |
| `Modules/Minecraft/ModModpack.vb:782` | 识别整合包内附启动器时，`ProductName` 同时接受 `"PCL PX"` 与 `"Plain Craft Launcher"` |
| `Modules/Base/ModBase.vb:9` | `VersionBaseName` → `PX beta0.0.1` |

**刻意保持不变**（改了会坏）：

- `RootNamespace=PCL`、`AssemblyName=Plain Craft Launcher 2`——所有 `/Plain Craft Launcher 2;component/...` 资源 URI 与 `assembly=Plain Craft Launcher 2` 引用依赖它。
- 配置目录 `Paths.Base & "PCL\"`、临时目录 `%TEMP%\PCL\`——沿用上游路径以复用既有设置，不做数据迁移。
- `AssemblyVersion 2.99.0.0`、`AssemblyCopyright 成都瓜皮龙科技有限公司`——版本号与版权归属不属于本项目可改动的内容，原样保留。
- 关于页首两条（龙腾猫跃 / HerobrineXia）的署名、头像与「赞助 PCL !」→ https://meloong.com/afd/a/LTCat 按钮，以及 `LICENCE` 全文。

<br />

## 三、删除与仓库结构差异

| 项 | 处理 | 原因 |
| --- | --- | --- |
| 「开源版本说明」弹窗 | 移除（原提示 CurseForge Key / 微软 Client ID / 更新 / 主题 / 百宝箱在开源版不可用） | 其中主题一项的描述与本项目不符；自定义主题色的门槛改由设置页内的赞助解锁行直接说明 |
| `最新正式版.zip`（2.6 MB，上游跟踪的官方发布二进制） | 不纳入本仓库 | 不重新分发官方二进制 |
| `patches/`（11 MB，上游逐版本补丁） | 不纳入本仓库 | 属上游发布流程产物，与本分支无关 |
| `.gitmodules` + `MeloongCore` 子模块 | 改为**普通目录内联**（Apache-2.0，`LICENSE.txt` 原样保留，基线 commit `98157d6`） | 让新克隆者不必再 `submodule update` 即可构建 |
| `.github/ISSUE_TEMPLATE/` | 删除（模板把反馈引导到上游仓库与上游讨论区） | 避免把本分支的问题投递给上游开发者 |
| `.github/FUNDING.yml` | 保留，指向上游赞助页 | 与二.3 的赞助署名一致 |
| `.github/workflows/build.yml` | 按本分支调整 | 见下 |
| `PCLCS/PCLCS.csproj` | `net48` → `net481` | 与主工程的 .NET Framework 4.8.1 目标对齐 |
| `PCLCS/bin/{PCLCS,MeloongCore,MeloongCore.Wpf}.dll` | 例外纳入版本控制（`.gitignore` 有对应 `!` 规则） | `vbproj` 以 `HintPath` 引用它们，而非 ProjectReference |
| `MeloongCore/Tests` | 保留 | 上游内容，未改动 |

<br />

## 四、待办 / 已知不足

- **第三方登录（LittleSkin / Yggdrasil）** 尚未接入正版登录链路，目前只在皮肤下载里使用 LittleSkin。
- 本分支不实现 `DownloadLatestPCL`（整合包打包用），该项在上游开源分支即为空实现，导出整合包时不会自动附带最新启动器。
- Adoptium 接口有速率限制，Java 列表刷新过快会失败。

## 五、如何核对

```bash
git clone https://github.com/Meloong-Git/PCL -b main --single-branch --depth 1 PCL-upstream
diff -r -x .git -x obj -x bin -x patches PCL-upstream Tatsukini_PCL_PX
```

本仓库的首个提交即包含上述全部改动，未逐条拆分提交（改动是在本地开发中累积的）。如需按功能拆分的提交历史，请在 issue 中提出。
