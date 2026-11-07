# TODO

## 待 Reactor 按钮组合内容支持发版后接入

2026-11-07：已在本地框架分支 `codex/button-element-content` 补齐按钮家族和
CheckBox/RadioButton 的 `Element` 内容入口，保留原字符串接口；验证了内容更新、
状态保留、回调与子组件清理，并通过 Tonarink 筛选器实测。尚未提交 issue/PR。

- 本地 App 验证版本保存在 `codex/test-button-element-content`（`e585b3a`）
- main 继续使用 NuGet.org 的 preview.17，类型筛选暂用生成式
  `ContentToggleButtonElement`，不依赖本地包源
- [ ] 后续按贡献流程提交上游 issue/PR，记录链接
- [ ] 修复正式进入 NuGet 后，升级 Reactor，改用 `ToggleButton(HStack(...), ...)`
- [ ] 删除 `Controls/ContentToggleButtonElement.cs`，复查筛选按钮的多选、主题和展开动画

## Reactor preview.17 接入记录

接入日期：2026-11-06。主线的 `Microsoft.UI.Reactor`、
`Microsoft.UI.Reactor.Advanced`、`Microsoft.UI.Reactor.Devtools` 已同步升级为
**0.1.0-preview.17**，使用 NuGet.org 正式发布的包。
发布包标注的源码提交为 `a61657ff404c180464061912b0c02d4a4560492a`。
本地验证分支中的接入代码已按最终 API 移植，不带入本地包源、源码引用和实验 DSL。

| 上游改动 | 主线接入结果 |
| --- | --- |
| [#1363 动态 ItemsHost](https://github.com/microsoft/microsoft-ui-reactor/pull/1363) | 恢复自动 SettingsExpander wrapper 的 Items/Content 协调，删除 SettingsExpanderItems 与手写 Customize |
| [#1365 x86 窗口入口点](https://github.com/microsoft/microsoft-ui-reactor/pull/1365) | 已随依赖升级包含；尚未进行本轮 x86 运行验证，不据此宣称完整 x86 支持 |
| [#1370 ResourceBuilder 重载](https://github.com/microsoft/microsoft-ui-reactor/pull/1370) | PageHeader 的 FontWeight/Thickness 和 AppShell 的导航边距改用链式资源赋值，保留原数值 |
| [#1372 文件选择器](https://github.com/microsoft/microsoft-ui-reactor/pull/1372)、[#1394 WASDK 选择器](https://github.com/microsoft/microsoft-ui-reactor/pull/1394) | 统一使用 PickMultipleFilesAsync / PickFolderAsync，删除 StoragePicker；需要 StorageFile 时在边界按路径转换 |
| [#1389 ContentDialog 主题](https://github.com/microsoft/microsoft-ui-reactor/pull/1389) | 普通对话框使用框架默认主题继承；删除 Themed 补丁和仅为传主题存在的参数 |
| [#1403 Jump List 删除反馈与 AOT](https://github.com/microsoft/microsoft-ui-reactor/pull/1403) | 改用 Reactor 统一打包/非打包实现，消费 UpdateAsync / ClearAsync 的事务删除反馈，放开设置开关 |
| [#1303 标题栏主题](https://github.com/microsoft/microsoft-ui-reactor/pull/1303) | ShellTitleBar 使用 PreferredTheme；保留 NativeSystemMenuTheme.Apply |
| [#1415 页面/覆盖层连通动画](https://github.com/microsoft/microsoft-ui-reactor/pull/1415) | 移植已实测的导航接入，删除 DeviceConnectedAnimation；页面动画由应用配置，保留接收覆盖层退出淡出 |

### 跳转列表删除语义

- 删除了 `jump-list-removed.txt` 永久黑名单的读写逻辑；不迁移其中的旧删除记录
- 根据最终 API，Windows 在提交更新后会消耗移除记录，因此将事务反馈记录为已有
  收藏/历史条目的 `JumpListRemoved` 元数据，随各自存储保存，不另建持久化层
- 移除跳转列表项目不会删除收藏设备或接收历史；编辑/重新收藏设备会重新允许显示，
  新接收记录使用新 ID，自然重新参与列表
- 非打包进程的 AppUserModelID 从应用程序集名称获取，在创建窗口前设置
- 保留 ShowJumpList 设置与串行刷新保护；关闭时清空列表，旧排队刷新不能重新填入
- 设置说明的六种语言均已去掉 MSIX 限制

### 待用户实测

本轮已通过 x64 Release、Debug 编译（均为 0 警告、0 错误），以及非打包 Native AOT 发布编译。
Native AOT 验证产物位于 `artifacts/reactor-preview17/win-x64`，未进行本轮 UI 实测或安装 MSIX。

本轮以编译确认为主，不新增覆盖率测试；以下视觉与 Windows Shell 行为仍需实际运行确认：

- [ ] SettingsExpander 动态增删、展开/滚动，以及反复展开多个 ComboBox 后候选不重复
- [ ] 浅色/深色与系统不一致时，对话框首次打开、再次打开和切换主题的实际显示
- [ ] 多文件/文件夹选择、取消、保存目录选择，以及 picker owner 窗口
- [ ] Jump List 在打包/非打包环境的收藏、历史深度链接和图标；移除后刷新/重启不重新出现，
  重新编辑收藏后允许显示；关闭设置后保持清空
- [ ] 设备详情首次导航及返回、发送覆盖层反复打开/取消、接收文件卡片展开/收起连通动画，
  接收覆盖层退出淡出
- [ ] x86 ReactorWindow 创建（单独验证，不影响本轮 x64 接入）

### 仅讨论，不作为待合并功能

[#1390 模板 DSL 讨论](https://github.com/microsoft/microsoft-ui-reactor/issues/1390) 和
[#1125 代码模板探索](https://github.com/microsoft/microsoft-ui-reactor/pull/1125)
不等于上游已承诺提供 DSL。历史类型筛选现已改为原生 ToggleButton 组合，
删除 TokenizingTextBox、代码模板及其包依赖，不再需要这部分模板接入。

## .NET SDK 安全修复接入记录

2026-11-06 Native AOT 还原报告 `Microsoft.DotNet.ILCompiler 11.0.0-rc.1.26425.128`
存在 [GHSA-p3jp-9v83-76w9](https://github.com/advisories/GHSA-p3jp-9v83-76w9)。
当时仅在本地验证命令传入 `-p:WarningsNotAsErrors=NU1903`，保留审计警告以完成 Reactor
AOT 编译确认；仓库未添加安全警告抑制配置，产物未发布。

- [x] 2026-11-07：`global.json`、CI/发布流程统一升级至本机已安装的
  `11.0.100-rc.3.26522.148`，相关 .NET/F# 包同步升级
- 该版本对应的 `Microsoft.DotNet.ILCompiler 11.0.0-rc.3.26522.148` 已列入上述公告的修复版本
- [ ] 发布前正常执行 Native AOT 发布及安全审计；本轮仅做编译确认，不运行测试或发布产物

## Windows App SDK 通知兼容代码已移除，待实测

2026-11-07：Windows App SDK 从 `2.5.4-experimental` 升级至
`2.6.6-experimental`，WinUI 与 InteractiveExperiences 组件版本同步。
主 App 继续按需引用组件，不引入完整 SDK 的 AI/ML 载荷。

官方在 [2.6.4 发布说明](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-2-0#version-264)
列出了 `VersionInfo_MissingInsightsResourceDll` 修复；
[2.6.6 实验版](https://github.com/microsoft/WindowsAppSDK/releases/tag/v2.6.6-exp)
继承 2.6.5 稳定版修复。此前的跟踪项为
[#6774](https://github.com/microsoft/WindowsAppSDK/issues/6774)。
修复允许组件部署缺少聚合 Runtime 资源 DLL，不要求继续手动携带它。

- 保留 Runtime 显式版本对齐：Reactor preview.17 仍间接依赖 Runtime 2.2.0，
  此处统一至 2.6.6-experimental，不再通过它定位 MSIX 或提取资源
- [x] 删除 `_PrepareWinAppRuntimeInsightsResource`、相关属性/内容项和 `tools/Extract-ZipEntry.ps1`
- [ ] 用户实测普通非打包、portable、AOT、MSIX 和 AOT + MSIX 的通知注册、显示和点击激活
- 本轮仅编译确认，不将官方修复说明或编译结果视为运行验证

### 同轮依赖维护与编译范围

- 已更新 FSharp.Core、SourceLink、System.CommandLine、bUnit、Blazor/Extensions、MAUI、Fluent UI 及 MSTest.Sdk
- 正式版依赖只追踪正式版；已有预览依赖继续追踪预览版。Reactor、社区控件、Win2D、LottieGen、二维码库和 FsToolkit 当前版本未变
- 主 App x64 Release、Core/CLI 解决方案（含纯托管测试项目）、F# Core、Web 和 Blazor 测试项目已编译通过，均为 0 警告、0 错误，未执行测试
- 已核对主 App 的 Base/Foundation/InteractiveExperiences/Runtime/WinUI 均解析为 2.6.6-experimental；Release 输出不再手动携带 Insights.Resource DLL
- [ ] MAUI/Android/iOS/MacCatalyst 编译：当前 .NET 11 SDK 未安装移动端工作负载，本轮不下载或安装，保留未验证说明

## 优化托盘面板动画曲线

托盘面板目前使用与 DWM 刷新节拍同步的窗口位移动画，并在屏幕下方完成
XAML 预渲染后再上浮。现有上浮动画使用三次方减速曲线，下沉动画使用二次方
加速曲线，窗口层级、Mica 渲染和双向动画均已正常工作。

当前仍未采用 Windows 11 系统面板的官方动画参数或对应 API，
因此这一项仍需保留。

后续如果能够获得或可靠复现 Windows 11 操作中心等系统面板的具体动画参数：

- 用对应的 Fluent 缓动曲线替换当前近似曲线。
- 对齐系统动画的上浮、下沉持续时间和位移节奏。
- 验证 60 Hz、120 Hz 等不同刷新率及不同 DPI 下的视觉一致性。
