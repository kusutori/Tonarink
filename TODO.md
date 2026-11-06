# TODO

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
不等于上游已承诺提供 DSL。当前 `HistoryFileTypePicker` 保持原生代码模板、
稳定模板实例和 `SetCompiledBinding`，不移植 `try-reactor-templates` 的实验 DSL。

## 发布前升级 .NET SDK 安全修复

2026-11-06 Native AOT 还原报告 `Microsoft.DotNet.ILCompiler 11.0.0-rc.1.26425.128`
存在 [GHSA-p3jp-9v83-76w9](https://github.com/advisories/GHSA-p3jp-9v83-76w9)。
本轮仅在本地验证命令传入 `-p:WarningsNotAsErrors=NU1903`，保留审计警告以完成 Reactor
AOT 编译确认；仓库未添加安全警告抑制配置，产物未发布。

- [ ] 发布前将 `global.json` 及相关 .NET 依赖升级至包含修复的版本，正常安全门禁下重新编译发布

## 升级 Windows App SDK 后移除通知兼容代码

当前 Windows App SDK 2.5.1 的自包含输出仍可能缺少
`Microsoft.WindowsAppRuntime.Insights.Resource.dll`，导致非打包应用注册系统通知失败。
项目暂时会从 Runtime MSIX 中提取并携带该 DLL。

最初的修复 [WindowsAppSDK #6725](https://github.com/microsoft/WindowsAppSDK/pull/6725)
已包含在 2.5.1 中，但该版本仍可复现此问题，微软已重新跟踪为
[WindowsAppSDK #6774](https://github.com/microsoft/WindowsAppSDK/issues/6774)。该问题目前已标记为
`Status: Pending release`，等待后续稳定版本发布修复。

升级到真正修复 #6774 的稳定版本后：

- 删除 `Tonarink.App.csproj` 中的 `Microsoft.WindowsAppSDK.Runtime` 显式引用。
- 删除 `Directory.Packages.props` 中对应的中央包版本。
- 删除 `_PrepareWinAppRuntimeInsightsResource` 及相关 MSBuild 属性和内容项。
- 删除 `tools/Extract-ZipEntry.ps1`。
- 验证普通非打包、portable、AOT、MSIX 和 AOT + MSIX 版本均可发送测试通知。

## 优化托盘面板动画曲线

托盘面板目前使用与 DWM 刷新节拍同步的窗口位移动画，并在屏幕下方完成
XAML 预渲染后再上浮。现有上浮动画使用三次方减速曲线，下沉动画使用二次方
加速曲线，窗口层级、Mica 渲染和双向动画均已正常工作。

Windows App SDK 2.5.1 未公开 Windows 11 系统面板的具体动画参数或对应 API，
因此这一项仍需保留。

后续如果能够获得或可靠复现 Windows 11 操作中心等系统面板的具体动画参数：

- 用对应的 Fluent 缓动曲线替换当前近似曲线。
- 对齐系统动画的上浮、下沉持续时间和位移节奏。
- 验证 60 Hz、120 Hz 等不同刷新率及不同 DPI 下的视觉一致性。
