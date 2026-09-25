# TODO

## 升级 Windows App SDK 后移除通知兼容代码

当前 Windows App SDK 2.4.0 的自包含输出可能缺少
`Microsoft.WindowsAppRuntime.Insights.Resource.dll`，导致非打包应用注册系统通知失败。
项目暂时会从 Runtime MSIX 中提取并携带该 DLL。

升级到包含 [WindowsAppSDK #6725](https://github.com/microsoft/WindowsAppSDK/pull/6725)
修复的稳定版本后：

- 删除 `Tonarink.App.csproj` 中的 `Microsoft.WindowsAppSDK.Runtime` 显式引用。
- 删除 `Directory.Packages.props` 中对应的中央包版本。
- 删除 `_PrepareWinAppRuntimeInsightsResource` 及相关 MSBuild 属性和内容项。
- 删除 `tools/Extract-ZipEntry.ps1`。
- 验证普通非打包、portable、AOT、MSIX 和 AOT + MSIX 版本均可发送测试通知。

## 优化托盘面板动画曲线

托盘面板目前使用与 DWM 刷新节拍同步的窗口位移动画，并在屏幕下方完成
XAML 预渲染后再上浮。现有上浮动画使用三次方减速曲线，下沉动画使用二次方
加速曲线，窗口层级、Mica 渲染和双向动画均已正常工作。

后续如果能够获得或可靠复现 Windows 11 操作中心等系统面板的具体动画参数：

- 用对应的 Fluent 缓动曲线替换当前近似曲线。
- 对齐系统动画的上浮、下沉持续时间和位移节奏。
- 验证 60 Hz、120 Hz 等不同刷新率及不同 DPI 下的视觉一致性。
