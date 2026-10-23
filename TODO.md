# TODO

## Reactor 新版发布后的接入清单

状态核对日期：2026-10-23。主线当前使用 `Microsoft.UI.Reactor`、
`Microsoft.UI.Reactor.Advanced`、`Microsoft.UI.Reactor.Devtools` **0.1.0-preview.16**。
下列已合并改动仍在该版本之后；**PR 已合并不等于当前 NuGet 包已包含**。
升级时逐项核对实际发布版本与最终 API，不直接把本地实验分支整体合入主线。

| 上游改动 | 当前状态 | Tonarink 后续动作 |
| --- | --- | --- |
| [#1363 动态 ItemsHost](https://github.com/microsoft/microsoft-ui-reactor/pull/1363) | 评审中 | 改回自动 SettingsExpander wrapper，删除手写协调 |
| [#1365 x86 窗口入口点](https://github.com/microsoft/microsoft-ui-reactor/pull/1365) | 已合并，等待包含它的版本 | 升级后核对 x86 ReactorWindow 创建，不据此宣称完整 x86 支持 |
| [#1370 ResourceBuilder 重载](https://github.com/microsoft/microsoft-ui-reactor/pull/1370) | 已合并，等待包含它的版本 | 用链式资源赋值替代原生资源字典写入 |
| [#1372 文件选择器补全](https://github.com/microsoft/microsoft-ui-reactor/pull/1372)、[#1394 迁移 WASDK 选择器](https://github.com/microsoft/microsoft-ui-reactor/pull/1394) | 均已合并，等待包含它们的版本 | 使用新版 picker helpers，删除自有 StoragePicker |
| [#1389 ContentDialog 主题](https://github.com/microsoft/microsoft-ui-reactor/pull/1389) | 评审中 | 删除主题传递补丁，使用框架传递与默认继承 |
| [#1403 Jump List 删除反馈与 AOT](https://github.com/microsoft/microsoft-ui-reactor/pull/1403) | 评审中 | 统一打包/非打包实现，改用事务返回的删除反馈 |

### 动态 SettingsExpander wrapper（#1363）

- [ ] 升级到包含最终修复的版本后，在 `Controls/ToolkitControls.cs` 中恢复生成器管理
  Items/Content，删除手写 `Customize`、Content 适配重载及不再需要的底层协调引用
- [ ] 删除 `Controls/SettingsExpanderItems.cs`；其中的
  `ObservableCollection<object>` WinRT 暴露标记须确认已由上游 AOT 方案覆盖后再移除
- [ ] 对照当前主线保留已有 UI 行为：动态增删、重排后状态/焦点不丢失，内容属性更新生效；
  反复展开主题、语言、托盘操作下拉框，确认候选不会增加；检查实际 AOT 打包版
- 参考：`dev`、`dev-pr1363-pr1389-validation` 有接入和用户实测记录，但不是当前主线的替代品

### 资源赋值重载（#1370）

- [ ] `Components/Shell/PageHeader.cs` 的 `BreadcrumbBarItemFontWeight`、
  `BreadcrumbBarChevronPadding` 改为 `.Resources(resources => resources.Set(...))`
- [ ] `AppShell.cs` 中导航栏的 `NavigationViewHeaderMargin`、
  `NavigationViewMinimalHeaderMargin` 同样改为链式资源赋值；保留现有数值，不顺带改布局
- 参考：`dev-resource-overloads` 的本地接入；只移植仍适用的资源配置，不恢复旧依赖路径

### 文件选择器（#1372 + #1394）

- [ ] 发送页的多文件、文件夹选择，以及设置页、接收覆盖层的保存目录选择，统一使用
  Reactor 新版 `PickMultipleFilesAsync` / `PickFolderAsync` 和对应 options
- [ ] 按最终 WASDK picker 返回类型处理路径；如后续流程仍需 `StorageFile`，在边界显式转换，
  不继续假设新接口返回旧 `Windows.Storage.Pickers` 的 WinRT 对象
- [ ] 删除 `Services/Platform/StoragePicker.cs` 及相关参数传递、旧 HWND 初始化和无用引用；
  保留选择器文案、起始目录、取消行为与 owner 窗口语义
- 参考：`dev-pr1363-pr1389-validation` 上 `22716db` 已接入并经用户实测；
  主线后续已有页面/Hook 拆分，应按当前调用点移植而非直接覆盖文件

### ContentDialog 主题（#1389）

- [ ] 删除 `Styling/ContentDialogStyleExtensions.cs` 中通过 `.Set` 修改原生控件的 `.Themed`
- [ ] 显式指定主题的调用改为框架 `.RequestedTheme(...)`；与声明处主题一致的普通对话框
  可不再单独传主题，使用默认继承；只为该补丁存在的主题参数一并清理
- [ ] 核对浅色/深色应用与系统主题不一致、未配置主题、显式覆盖以及再次打开对话框的行为；
  不把“控件属性已赋值”当成对话框实际主题已生效
- 参考：`dev-pr1363-pr1389-validation` 已进行与 #1363 的组合接入实测

### 跳转列表（#1403）

- [ ] `Services/Activation/JumpListService.cs` 改用 Reactor 封装，替代主线
  `Windows.UI.StartScreen.JumpList` 路径，支持打包和非打包应用
- [ ] 按最终 API 消费 `UpdateAsync` / `ClearAsync` 返回的 `Task<IReadOnlyList<string>>`
  删除反馈：反馈属于本次更新事务，不能只在更新前调用只读查询再假定后续不会变化
- [ ] 删除当前 `_removedItems`、`RemovedItemsPath`、`RememberRemovedItems`、`WasRemoved`
  及 `jump-list-removed.txt` 永久黑名单逻辑，遵循 Windows 的用户移除语义；
  不把该删除记录误当成收藏设备或历史记录本身，也不再新增平行持久化层
- [ ] 非打包 AppUserModelID 从现有应用身份/程序集信息获取，并在创建窗口前设置；
  不恢复已删除的 `NativeJumpList` 或硬编码应用名
- [ ] 同步更新设置开关的可用条件和六种语言的说明，去掉“仅限 MSIX 安装版”；
  保留新加入的 `ShowJumpList` 设置和并发刷新保护，关闭时清空列表且旧排队刷新不能重新填入
- [ ] 核对收藏设备/历史项深度链接、图标、Windows 移除行为，以及打包/非打包 Native AOT
- 参考：`dev-jump-list-aot-validation`、`dev-pr1363-pr1389-validation` 的 `d410b00`
  已验证旧版接入；**早于本轮事务返回值调整**，不能原样复制它的先查询后更新实现

### 标题栏主题声明式配置

- [ ] 新版包含对应配置后，将 `AppShell.cs` 中
  `window.AppWindow.TitleBar.PreferredTheme` 的命令式赋值迁至 `ShellTitleBar` 的
  `.PreferredTheme(...)`，保留默认/浅色/深色三种语义
- [ ] **保留** `NativeSystemMenuTheme.Apply(...)`，它处理的是另一层原生系统菜单主题
- 参考：`deferred/titlebar-preferred-theme` / `7253e07`；基于当前主线移植，
  不恢复该旧提交中的硬编码标题或整段旧 Shell 实现

### 升级收尾

- [ ] 三个 Reactor 包版本同步升级；所有本地源码引用、本地测试包和临时 NuGet 源仍留在实验分支，
  不作为正式主线依赖
- [ ] 更新 `docs/reactor-native-api-audit.md` 的“等待依赖版本”清单；完成的 TODO 注明实际接入版本
- [ ] 日常改动仍以编译通过为基本要求；本轮针对曾出过问题的 UI/AOT 边界进行定向实测，
  不以追求覆盖率为由扩展测试范围

### 仅讨论，不作为待合并功能

[#1390 模板 DSL 讨论](https://github.com/microsoft/microsoft-ui-reactor/issues/1390) 和
[#1125 代码模板探索](https://github.com/microsoft/microsoft-ui-reactor/pull/1125)
不等于上游已承诺提供 DSL。当前 `HistoryFileTypePicker` 保持原生代码模板、
稳定模板实例和 `SetCompiledBinding`；未来仅在 API 正式发布或出现具体新需求时重新评估，
不等待该讨论来移植 `try-reactor-templates` 的实验 DSL。

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
