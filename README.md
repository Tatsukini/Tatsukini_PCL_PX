# PCL PX

**Plain Craft Launcher (PCL) PX —— 由 Tatsukini 独立二次创作的第三方修改版启动器**

> **第三方声明（《PCL 存储库合理使用指南》二.1）**
> 本软件与本仓库是**第三方基于 Plain Craft Launcher 独立进行二次创作的产物**，与 PCL 官方、开发者龙腾猫跃及其团队**没有任何隶属或授权关系**；它不是官方作品，不是官方的后续版本（不是 PCL 3），也不由官方提供或维护。
> 上游项目：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)　开发者：**龙腾猫跃**（协力开发者：HerobrineXia）
> 想支持作者，请前往上游的赞助页面：**https://meloong.com/afd/a/LTCat**（正式版 PCL 本身完全免费）

| 项目 | 内容 |
| --- | --- |
| 本仓库开发者 | **Tatsukini** |
| 上游基线 | PCL `2.99.0.0` 正式版（commit [`6b9a50df911c9d68b69b24c979efba477fb14b67`](https://github.com/Meloong-Git/PCL)，2026-10-20） |
| 本版本 | `PX beta0.0.1`（内部 VersionCode 仍沿用上游 413/414） |
| 技术栈 | VB.NET + WPF，.NET Framework 4.8.1，x86/AnyCPU |
| 许可 | 遵循上游《PCL 分发有限许可》与《PCL 存储库合理使用指南》，[`LICENCE`](LICENCE) 原文保留、未作修改 |
| 改动披露 | 全部改动逐条列在 [`MODIFICATIONS.md`](MODIFICATIONS.md) |

<br />

## 它是什么

PCL PX 是把上游 PCL 源代码拿来**做加法**的私人分支：不改变原有界面与逻辑的使用习惯，只补自己需要的能力。目前新增：

| 分类 | 新增内容 |
| --- | --- |
| 更多 → 小工具 | **内存优化**（进程工作集整理、可配置自动处理其他进程）、**一键生成服务端**（原版 / 整合包两条路径，含版本选择与输出目录）、**模组报错图形化检查**（节点连线图列出依赖、冲突与缺失前置，可合并崩溃日志点名的模组） |
| 设置 → 启动 | **启动播放音效**（导入限制 5 秒以内，试听 / 移除，音效复制到 `PCL\Sounds\`）、**启动前硬件检测与预警**（内存 / CPU 必选，显卡驱动与磁盘空间可选，阈值可调，预警后仍可继续启动） |
| 版本设置 → 概览 | **升级整合包向导**（先询问是否备份版本文件夹，再换算新内核、按目标版本与所选发行类型匹配每个模组、逐项判断材质包 / 光影包 / 数据包 / 重要数据的兼容性并按选择迁移，支持就地升级或另存为新版本） |
| 下载 | **Java 下载**（Adoptium API，可选版本、JDK/JRE、x86/x64、热列表与 LTS 标注，并列出本机已下载的 Java）、**皮肤下载**（LittleSkin 与正版两条来源，支持玩家名 / UID，皮肤与披风、宽/窄模型） |
| 更多 → 联机 | 启用并接入 **TerraCotta 联机**（节点、房间、玩家配置与入口页面） |
| 设置 → 个性化 | **自定义主题色**（HSL 参数、顶栏色相差、从图片取主色，**需赞助解锁**）、**动态视频背景**（导入 / 适配 / 速度 / 循环 / 音量，免费保留） |
| 稳定性 | **崩溃分析升级**（原因分类 + 置信度 + Mod 文件名归一化定位）、**实时日志窗口**（设置 → 系统 → 实时显示日志） |
| 下载源 | 修复 `maven.neoforged.net` 在中国大陆被封锁导致内核安装卡死：列表与安装器文件均优先走 `neoforged.forgecdn.net` / BMCLAPI 镜像，官方地址降为最后回退 |
| 其他 | 恢复 Minecraft 本体与整合包的下载入口（上游开源分支以 `RestrictMcDownload` 屏蔽）；软件名、窗口标题、单例互斥量、关于页等统一改为 **PCL PX** |

<br />

## 构建

需要 **Visual Studio 2022**（含 `.NET Framework 4.8.1` 目标包与 SDK）或等价的 MSBuild + .NET SDK。

```powershell
# 1) 生成被 vbproj 以 HintPath 引用的 C# 库（仓库已附带结果，可跳过）
dotnet build "PCLCS\PCLCS.csproj" -c Release

# 2) 生成启动器
msbuild "Plain Craft Launcher 2\Plain Craft Launcher 2.vbproj" -t:Build `
  -p:Configuration=Release -p:Platform=AnyCPU
```

产物在 `Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe`。

两点坑：

- 若构建报 `BC30037`（找不到 `csc.exe` 之类的假错误），显式指定 Roslyn：`-p:VbcToolPath="<roslyn>\tasks\net472"`。
- 帮助文档 `Resources\Help.zip` 来自 [LTCatt/PCL2Help](https://github.com/LTCatt/PCL2Help)，仓库里带的是上游快照；CI 会重新拉取。

调试版与正式版的差异由 `BuildType`/`RELEASE` 条件编译控制；`Option Strict Off`，源文件为 UTF-8 无 BOM。

<br />

## 关于页面的署名（指南二.3）

`更多 → 关于` 页面首位仍是 **龙腾猫跃**（附头像与「PCL 的开发者！」说明）及其 **赞助 PCL !** 按钮，其后是 HerobrineXia 的署名；本项目自己的条目位于第三位，已注明「由 Tatsukini 独立二次创作」并指回上游仓库与本文件。`LICENCE` 作为使用指南继续随仓库分发，内容一字未改。

<br />

## 许可与合规说明

- **本项目不重新授权上游代码。** 上游以《PCL 分发有限许可》（见 `LICENCE` 第 3 行的链接）而非 OSI 许可证发布，本分支同样只在该许可与《PCL 存储库合理使用指南》的框架下被使用与分发；对本仓库的任何再分发都必须保持同样的声明与指向。
- **`MeloongCore/` 是上游的独立仓库**（[Meloong-Git/MeloongCore](https://github.com/Meloong-Git/MeloongCore)，**Apache-2.0**，基线 commit `98157d65c7627721c816c02e8be96ac4fe61d615`）。本仓库把它作为普通目录内联（不再是 git submodule）以便直接构建，其 `LICENSE.txt` 原样保留，请勿删除。
- **`Resources/` 与 `PCLCS/bin/` 里的第三方 dll** 各自遵循其上游许可（Newtonsoft.Json、Ookii.Dialogs、NAudio、CacheCow、Imazen.WebP、ThrottleDebounce 等），本仓库不改变它们的许可状态。
- **不含更新功能**：`UpdateCheckByButton` 在上游开源分支里就是空壳，本分支没有让它指向任何第三方下载地址，也不会给使用者推送非官方更新。
- **不收费**：本分支不做任何形式的收款、会员或付费墙。唯一的门槛是「自定义主题色」的荣誉制赞助确认，其赞助入口直接指向 龙腾猫跃 的页面，钱一分也不经过本项目。

### ⚠️ 已知的合规风险（保留现状，但明确标注）

以下三处是**明知与指南存在张力而按当前形态保留**的选择，如实列出，不做隐瞒。若上游开发者（龙腾猫跃）提出要求，本项目会按要求第一时间修改或下架。

| 指南条款 | 现状 | 风险 | 已做的缓解 |
| --- | --- | --- | --- |
| **二.2 名称** | 界面标题、`AssemblyProduct`、互斥量与关于页统一使用简短名 **`PCL PX`** | 指南要求名称「以 Plain Craft Launcher (PCL) 开头，并添加足以表现第三方修改的后缀」。纯 `PCL PX` 可能被认为后缀不足，有被指为混淆官方的风险 | 仓库名、README 首行副标题、关于页说明文字与本文件均完整写明「Plain Craft Launcher (PCL) PX —— 第三方二次创作修改版」，并在关于页放置「与官方无关」的显式声明 |
| **二.5 赞助解锁类似功能** | 本项目**新增**的「自定义主题色」已重新加上赞助解锁门槛：未解锁时该主题不可选中，卡片下方给出「赞助 PCL」（直达 龙腾猫跃 的爱发电页）与「我已赞助，解锁」，属荣誉制确认，本分支不收取任何费用。动态视频背景保持免费可用 | 上游的 13 套主题在本项目里可直接切换——但**上游开源分支本身就没有解锁逻辑**（`ModSecret.vb` 的 `ThemeCheckOne` 恒为 `True`、`ThemeUnlock` 恒为 `False`，均为官方留的空壳），本项目未绕过任何真实校验；只是「多套彩色主题可选」这一表现仍与上游的赞助主题相近 | 界面**主色相仍为蓝色**（默认主题「龙猫蓝」HSL 色相 210），未加入付费墙、会员或任何形式的第三方收款；赞助入口全部指向上游；`ThemeCustomRequireSponsor` 常量使门槛一目了然，便于审查 |
| **恢复 MC 下载** | `RestrictMcDownload = False`，重新开放原版与整合包下载入口 | 上游在开源分支中主动屏蔽此功能；重新开放可能给上游 CDN 造成额外负担，也与上游「为遵守相关法律法规」的表述相悖 | 下载列表与文件优先使用 BMCLAPI / forgecdn 等社区镜像，官方地址作为回退；仅供个人与小范围使用，不用于商业分发 |

另外：本分支**移除了**上游的「开源版本说明」弹窗（原本提示 CurseForge Key / 微软 Client ID / 更新 / 主题 / 百宝箱不可用），**未包含** `patches/` 目录与 `最新正式版.zip`（上游官方发布二进制），并删除了 `.gitmodules` 与 `.github/ISSUE_TEMPLATE`（后者的模板会把 issue 引导到上游仓库）。这些差异同样记录在 [`MODIFICATIONS.md`](MODIFICATIONS.md)。

<br />

## 相关资源

- [PCL 官方下载](https://meloong.com/pcl)：推荐使用免费的上游正式版
- [PCL 上游仓库](https://github.com/Meloong-Git/PCL)　·　[MeloongCore](https://github.com/Meloong-Git/MeloongCore)　·　[帮助文档库](https://github.com/LTCatt/PCL2Help)
- [PCL 功能投票](https://github.com/Meloong-Git/PCL/discussions/2)：上游开发者会优先处理票数高的项目
- 本项目改动披露：[`MODIFICATIONS.md`](MODIFICATIONS.md)
