# .NET 11 Runtime Async

Tonarink 的 .NET 11 项目统一使用 CoreCLR，不维护 Mono 运行时兼容版本。Runtime Async 因此按项目直接启用，不再进行 CoreCLR/Mono 双轨拆分。

## 配置

包含异步链路或生成异步代码的产品项目使用：

```xml
<Features>$(Features);runtime-async=on</Features>
```

Android、iOS 和 Mac Catalyst 项目同时显式固定为 CoreCLR：

```xml
<UseMonoRuntime>false</UseMonoRuntime>
```

当前启用范围：

- `LocalSendDotNet.Core` 与 `LocalSendDotNet.Core.Mobile`；
- `Tonarink.LocalSend` 与 `Tonarink.LocalSend.Mobile`；
- `Tonarink.Application`；
- `Tonarink.App`；
- `Tonarink.Blazor.Shared` 与 `Tonarink.Web`；
- `Tonarink.Hybrid` 与 iOS Share Extension；
- `LocalSendDotNet.Cli`。

Widget 和 Explorer Command 当前没有异步热点，不为了配置统一而启用。

## 改造原则

Runtime Async 是编译器与运行时协作的实现变化，不要求重写现有 `async` / `await`。本次迁移不批量修改 `Task` / `ValueTask`、`ConfigureAwait`、取消、异常传播或 `TaskCompletionSource` 语义；只有测试发现真实问题时才调整业务代码。

Native AOT 是独立的发布模式。它与 CoreCLR 默认运行时不是同一个概念，但 Runtime Async 支持 Native AOT，因此主 App 的 JIT 和 Native AOT 都需要验证。

## 验收标准

- 核心、Application、LocalSend、Interop 和 UI 相关测试通过；
- 主 App Release/JIT 构建并启动成功；
- 主 App win-x64 Native AOT 发布成功；
- 移动共享程序集能够编译；
- 安装对应平台 SDK 后，Android、iOS 和 Mac Catalyst 分别完成一次构建和基本传输冒烟测试；
- 发送、接收、发现、取消、后台激活和关闭流程没有行为回归。

## 当前验证结果

- 主 App Release/x64 构建通过，0 警告、0 错误；
- 主 App 已通过 `winapp run` 启动；
- 主 App win-x64 Native AOT 发布通过；
- Core、Mobile Core、Application、LocalSend、Interop 与 Blazor Shared 共 94 项测试通过；
- Mobile Core 与 Mobile LocalSend 的 `net11.0` 目标构建通过；
- Android 构建尚未执行到编译阶段，本机缺少 Android API 37 SDK（`XA5207`）；
- Web/Hybrid 仍受现有 Blazor Shared 编译错误阻挡：两个已移除的 PIN 异常引用和三个 `BL0016`，与 Runtime Async 无关。

## 参考资料

- [.NET 11 Runtime Async（Microsoft Learn）](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/runtime#runtime-async)
- [Runtime Async tracking issue](https://github.com/dotnet/runtime/issues/109632)
- [.NET 11 中的 .NET MAUI 新功能](https://github.com/dotnet/docs-maui/blob/main/docs/whats-new/dotnet-11.md)
- [.NET for Android 构建属性：UseMonoRuntime](https://github.com/dotnet/android/blob/main/Documentation/docs-mobile/building-apps/build-properties.md#usemonoruntime)
