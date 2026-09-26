# .NET 11 Runtime Async 迁移评估

本文档记录 Tonarink 对 .NET 11 Runtime Async V2 的初步评估。当前阶段只确定启用边界、验证顺序和风险，不修改生产项目配置，也不为了使用新机制而重写现有异步代码。

## 结论

Runtime Async 值得在实验分支继续验证，主 App、桌面 Core 和桌面传输运行时是最有价值的首批目标。不过它仍是 .NET 11 的 preview 功能，而且 Mono 尚不支持，因此不能在仓库级 `Directory.Build.props` 中全局开启。

这次迁移与联合类型迁移不同：主要工作是为合适的项目加入编译特性，而不是修改每一个 `async` / `await`。现有取消语义、`ConfigureAwait`、`Task` / `ValueTask` 选择和异常处理默认保持不变，只有测试或分析器发现真实问题时才调整源码。

启用方式应追加而不是覆盖其他编译特性：

```xml
<PropertyGroup>
  <Features>$(Features);runtime-async=on</Features>
</PropertyGroup>
```

`net11.0` 不再需要额外设置 `EnablePreviewFeatures`。旧的 `DOTNET_RuntimeAsync` 和 `UNSUPPORTED_RuntimeAsync` 环境变量已经移除；需要回退时应删除项目级 feature，或使用项目级 `UseRuntimeAsync=false`。

## 官方能力边界

Runtime Async 让运行时负责异步方法的挂起与恢复，不再由编译器为每个方法生成传统状态机。预期收益包括：

- 更干净的实时调用栈和调试体验；
- 更少的状态机与 continuation 开销；
- JIT 的 tiered async compilation、tail-await 和同步快速路径优化；
- Native AOT 与 ReadyToRun 支持；
- `Task`、`Task<T>`、`ValueTask` 和 `ValueTask<T>` 的常见路径优化。

它不会自动修复不合理的异步 API，也不意味着应把所有 `Task` 改成 `ValueTask`。异常堆栈本来就会经过清理，最明显的诊断改善主要体现在调试器、Profiler 和运行中的实时调用栈。

当前最重要的限制是 Mono 不受支持。仓库里的 Android、iOS、Mac Catalyst 和 MAUI/Hybrid 目标因此必须继续使用传统 async lowering。

## 仓库扫描结果

仓库中的异步代码主要集中在网络传输与主 App：

| 项目 | 异步密度 | 建议 | 原因 |
| --- | ---: | --- | --- |
| `Tonarink.App` | 高（约 64 处） | 第一批启用 | WinUI 前台、激活、发送/接收与剪贴板均包含异步链路，同时需要验证 JIT 与 Native AOT |
| `LocalSendDotNet.Core` | 很高（约 55 处） | 第一批启用，但仅桌面项目 | 网络、HTTP、发现与传输是最可能获得收益的热点；移动项目会重新编译同一批源码，不应继承该开关 |
| `Tonarink.LocalSend` | 中（约 10 处） | 第一批启用，但仅桌面项目 | 桌面运行时有长期监听与传输协调；`Tonarink.LocalSend.Mobile` 会单独编译共享源码 |
| `LocalSendDotNet.Cli` | 中（约 8 处） | 第二批启用 | CoreCLR 控制台宿主，行为简单，适合作为功能与调用栈验证入口 |
| `Tonarink.Web` | 低（约 5 处） | 第二批启用 | ASP.NET Core 使用 CoreCLR，但应先恢复当前 Web 项目自身的正常构建 |
| `Tonarink.Application` | 低（约 4 处） | 暂不启用 | 同一个程序集被 WinUI、Web 和 MAUI/Hybrid 共同引用，启用后会影响 Mono 消费端 |
| `LocalSendDotNet.Core.Mobile` / `Tonarink.LocalSend.Mobile` | 高 | 不启用 | Android/iOS/Mac Catalyst 使用 Mono；这些项目应继续以传统 lowering 编译共享源码 |
| `Tonarink.Hybrid` / Share Extension | 中/低 | 不启用 | MAUI、Android 和 iOS 均处于不支持范围；唯一的 `async void` 还是平台 override，不能机械改签名 |
| `WidgetProvider` / `ExplorerCommand` | 无明显异步热点 | 暂不启用 | 没有足够收益，不应只为统一配置而打开 preview 特性 |

`Tonarink.Blazor.Shared` 同时服务 Web 与 Hybrid，也不应承载全局开关。混合使用 Runtime Async 程序集和传统状态机程序集是允许的，不要求整个进程一次性切换。

### 重点代码路径

首轮验证应覆盖以下真实热点，而不是只验证项目能编译：

- `LocalSendNode` 的启动、发现、发送、接收与释放；
- `V2Server`、`V2HttpClient` 与 multicast discovery；
- `LocalSendRuntime` 的长期监听和取消；
- `SendPage`、`IncomingTransferOverlay` 与 `UseLocalSendNode`；
- Share Target、通知、People Suggestions 和托盘发送；
- `IAsyncEnumerable` 设备/传输事件流；
- `ValueTask` 流读取与 `IAsyncDisposable` 路径。

现有大量 `ConfigureAwait(false)` 属于线程上下文语义，不因 Runtime Async 而删除。UI 层显式恢复上下文的 `ConfigureAwait(true)` 同样继续保留。

## 初步冒烟结果

使用 .NET 11 RC1 SDK，通过命令行临时传入 `Features=runtime-async=on`，得到以下结果：

- `LocalSendDotNet.Core` Release 构建通过，0 警告；
- `Tonarink.App` win-x64 Native AOT 发布通过；
- `Tonarink.Web` 的项目图构建未通过，但错误来自已有的 Blazor Shared 迁移遗留：仍引用已删除的 PIN 异常，以及三个现存的 `BL0016` JS interop 分析器错误，与 Runtime Async 无关。

这只能证明当前编译器、CoreCLR 和 Native AOT 能处理主链路，不能代替运行时传输、UI、打包和性能验证。

## 建议实施顺序

### 第一阶段：桌面传输层

1. 只在 `LocalSendDotNet.Core.csproj` 和 `Tonarink.LocalSend.csproj` 中启用；
2. 不修改对应 Mobile 项目的配置，确认共享源码仍能由 Mono 目标正常编译；
3. 运行 Core、Interop 与 LocalSend 测试；
4. 重新执行 NuGet pack 和仓库外 Native AOT consumer smoke test；
5. 对比传输吞吐、分配量、程序集大小和实时调用栈。

### 第二阶段：主 App

1. 在 `Tonarink.App.csproj` 中单独启用；
2. 验证 Debug/JIT、Release/JIT、win-x64 与 win-arm64 Native AOT；
3. 验证普通 MSIX、AOT MSIX、Share Target、通知、托盘和 Widget；
4. 重点复测快速导航、后台激活、PIN、取消和关闭时的异步生命周期；
5. 使用现有性能基线比较启动时间、导航帧、内存与传输期间 CPU。

### 第三阶段：宿主与工具

1. 为 CLI 启用，并把它作为长传输、取消和 stack trace 的简单复现宿主；
2. 修复 Web 当前与 Runtime Async 无关的构建问题后，再为 `Tonarink.Web` 启用；
3. 不主动迁移 `Tonarink.Application`、Blazor Shared、Mobile 与 Hybrid，直到 Mono 官方支持或仓库为 CoreCLR/Mono 产出独立程序集。

## 不应顺手进行的改造

- 不批量把 `Task` 改成 `ValueTask`；
- 不删除 `ConfigureAwait(false)`；
- 不因状态机实现变化而修改取消、异常或 `TaskCompletionSource` 语义；
- 不把 feature 放进全局 `Directory.Build.props`；
- 不为了“全项目一致”给没有异步热点的 Widget/Explorer 项目启用；
- 不在同一提交中处理 Web 的旧异常迁移、JS interop 分析器错误和 Runtime Async 开关。

## 验收标准

正式保留该特性前至少需要满足：

- Core、Application、LocalSend、Interop 和相关 UI 测试全部通过；
- Core 的 `net11.0`、移动 Core 的 Mono 目标均能独立构建；
- 主 App 的 JIT、Native AOT、普通 MSIX 与 AOT MSIX 均可运行；
- 发送、接收、发现、Web 分享、Share Target、通知和托盘行为无回归；
- 与现有性能基线相比没有明显退化，并记录至少一项可验证收益；
- 遇到问题时可以按项目关闭，而不影响移动端和其他宿主。

## 参考资料

- [.NET 11 Runtime Async（Microsoft Learn）](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-11/runtime#runtime-async)
- [.NET 11 Preview 3 Runtime release notes](https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/preview3/runtime.md)
- [.NET 11 Preview 7 Runtime release notes](https://github.com/dotnet/core/blob/main/release-notes/11.0/preview/preview7/runtime.md)
- [Runtime Async tracking issue](https://github.com/dotnet/runtime/issues/109632)
