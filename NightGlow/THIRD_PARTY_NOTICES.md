# Third-party notices / 第三方组件和许可证

NightGlow 本身以 MIT 许可证发布（见同目录的 `LICENSE`）。下面列出 NightGlow 用到或随程序分发的第三方代码。
只列出 NightGlow 实际编译进去或随程序分发的部分；alien-tools 里 NightGlow 没有用到的组件放在最后单独说明。

## 编译进 NightGlow 的代码

| 组件 | 用在哪里 | 许可证 | 来源 |
|---|---|---|---|
| alien-tools（AlienRGB_SDK、alienfan-SDK_v2） | `nightglow_core.dll`：灯光 HID 协议、BIOS 风扇/电源接口 | MIT，Copyright (c) 2020 Rik Lain（见仓库根目录 `LICENSE`） | https://github.com/T-Troll/alien-tools |
| Gurjot95 AlienFX-SDK | alien-tools 说明它的底层灯光 SDK 基于这个项目 | **未声明许可证**：上游 SDK 的低层部分源自 Gurjot95/AlienFX-SDK，该仓库没有许可证文件（2026-11-27 查询 GitHub API 返回无许可证）。NightGlow 通过 alien-tools（MIT）使用这部分代码；如原作者有异议，会移除相关代码。 | https://github.com/Gurjot95/AlienFX-SDK |

## 随程序分发的运行库（自包含发布时）

| 组件 | 许可证 | 来源 |
|---|---|---|
| .NET 运行时 | MIT | https://github.com/dotnet/runtime |
| Windows App SDK（WinUI 3） | Microsoft 软件许可条款（NuGet 包内 `license.txt`，允许随应用再分发运行库） | https://github.com/microsoft/WindowsAppSDK |
| CommunityToolkit.Mvvm | MIT | https://github.com/CommunityToolkit/dotnet |

`Microsoft.Windows.SDK.BuildTools` 只在编译时使用，不随程序分发。

## alien-tools 里 NightGlow 没有用到的第三方组件

以下组件只在上游的 `alienrgb-gui` / `alienrgb-cli` 里使用，NightGlow 没有编译它们；列在这里方便以后扩展时核对：

| 组件 | 上游用途 | 许可证 |
|---|---|---|
| Kiss FFT | 频谱分析（声音灯效） | BSD-3-Clause（GitHub 识别为 NOASSERTION，需看原文） |
| Tnbuig Spectrum-Analyzer-15.6.11 | 频谱分析器 | **未确认**：仓库没有许可证文件 |
| Bryal DXGCap | 屏幕取色（环境光灯效） | MIT |
| Kalbert312 alienfx-cli | alienrgb-cli 的高层 API | MIT |

## 图标和素材

NightGlow 的图标（月牙和光晕）由 `tools/make-icon.ps1` 用代码绘制，是原创素材。程序里没有使用 Dell / Alienware 的商标、图标、图片或音效，也没有使用厂商控制软件的代码或素材。
