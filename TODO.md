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

