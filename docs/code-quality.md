# 编译期代码规范

规则入口是根目录 `.editorconfig` 和 `Directory.Build.props`，适用于源码、工具和测试项目；`tests/Directory.Build.props` 已导入根配置。新增规范应修改这里，不只依赖口头约定。

## 执行方式

使用 .NET SDK 内置的 Roslyn/.NET 分析器，不额外安装 StyleCop 或重复引用 NetAnalyzers 包。SDK 版本由 `global.json` 固定，`AnalysisLevel` 固定为 `11.0`；升级 SDK 时显式审查分析级别，而不是使用 `latest` 自动漂移。

- `EnableNETAnalyzers`、`RunAnalyzersDuringBuild`：启用构建分析
- `EnforceCodeStyleInBuild`：将配置为 warning/error 的代码风格规则纳入构建
- `TreatWarningsAsErrors`：延续原有设置，C# 编译器与 Roslyn 分析器警告使普通 `dotnet build` 失败；不等于将所有 MSBuild/外部打包工具警告也升级为错误
- 不使用整类禁用或 `AnalysisMode=All`，保留 SDK 原有默认检查，只额外收紧明确选定的规则

因此，这些规则既在编辑器提示，也会在现有 CI 的构建步骤中执行，不需要单独的分析器工作流。

## 首批强制规则

| 规则 | 要求 |
| --- | --- |
| IDE0161 | 使用文件范围命名空间 |
| IDE0065 | using 指令放在命名空间外 |
| IDE0036 | 统一修饰符顺序，具体顺序以 `.editorconfig` 为准 |
| IDE1006 | 接口使用 `IPascalCase` 命名 |
| CA2012 | 正确消费 ValueTask，避免明显的丢弃、重复消费等误用 |
| CA2018 | Buffer.BlockCopy 的 count 使用字节数 |
| CA2021 | 避免不兼容的 Enumerable.Cast/OfType 类型转换 |
| CA2200 | 重新抛出异常时保留原始堆栈，不使用 `throw exception` |
| CA2208 | 参数异常的构造方式和参数名必须正确 |
| CA2241 | 格式字符串与参数数量匹配 |
| CA2242 | 正确判断 NaN，不直接与 NaN 比较相等 |
| CS8509 | switch 表达式保持穷尽；继续仅豁免未命名枚举数值的 CS8524 |

可空引用、未等待的 Task、平台适用性、Reactor 可访问性等已有编译/分析器检查继续生效。不要通过新增 `_` 分支掩盖可穷尽类型遗漏；不过内置规则无法禁止一切无意义的兜底分支，这仍需审查。

## 编辑器与格式化

统一 UTF-8、空格缩进、文件末尾换行；C# 使用 4 空格，JSON/YAML 和多数项目配置使用 2 空格，现有三个 Windows 宿主项目文件保留 4 空格。Markdown 保留行尾空格以允许硬换行。

不统一已有混合换行符，不强制 var/显式类型、表达式体、所有分支加大括号、异步方法后缀或私有字段前缀。空白排版和 using 排序使用 SDK 自带的 `dotnet format` 整理并校验；未将 IDE0055 升级为构建错误。

本地与 CI 共用 `tools/Format-Code.ps1`，SDK 由 `global.json` 选择：

```powershell
# 整理 Core/CLI（包含 portable 测试）和 Web；不需要移动平台工作负载
./tools/Format-Code.ps1

# Windows 开发机整理全部活跃 CI 范围
./tools/Format-Code.ps1 -Scope Core,Web,Windows

# 与 CI 一致，只校验、不修改文件
./tools/Format-Code.ps1 -Scope Core,Web,Windows -Verify
```

`Windows` 包含 App、ExplorerCommand 和 WidgetProvider，使用 x64 平台；`Core` 包含共享 Application 测试，因此 Web 不再重复检查该测试项目。移动平台宿主仍在各自暂禁用的 CI 任务中检查，F# 实验项目不在 `dotnet format` 的 C# 检查范围内。

CI 的 `format` 是独立任务，格式不符仍使该任务失败，但不会阻止其他任务编译和测试。跨平台测试矩阵关闭 fail-fast，避免一个平台失败时取消其他平台的诊断。只有测试入口已生成运行元数据后才上传报告，保留真实的编译、格式或测试错误，不再把“测试尚未开始”额外报告为上传失败。

`*.g.cs`、`*.g.i.cs`、`*.generated.cs`、`*.designer.cs` 和 obj/bin 中的 C# 标记为生成代码；不以目录豁免任何手写业务代码，不抑制生成代码的编译器错误。各分析器是否分析生成代码仍由其实现决定。

## 维护原则

### 应用名称与集成身份

- 产品名、发布者和仓库地址统一在 `Directory.Build.props` 定义，通过程序集元数据供 `Tonarink.Application.AppIdentity` 读取；运行时不要重复写名称和 GitHub 地址
- Windows 界面使用 `AppPlatform.DisplayName`：打包版读取包显示名，未打包版使用产品元数据
- App、Explorer 扩展和 Widget 共享 `AppIdentity.SharedDataDirectory`；本轮保持原有 `kusutori/Tonarink` 路径，不迁移或删除已有数据
- `Company`／`Product` 也组成未打包存储身份，后续更名必须单独规划数据迁移，不能只改显示文案
- 协议、启动项、包名、互斥量、事件、管道前缀和快捷方式所有权标记属于持久化或跨进程契约，集中在 `AppIntegrationIds`，不能随显示名或语言改变
- 包清单、COM GUID、资源逻辑名和翻译资源中的品牌文案是各自系统的声明，不做盲目替换；修改相关注册时必须同步检查对应契约

### 后续规则调整

1. 新规则先确认实际诊断的行为与项目适用范围，再开启并编译验证
2. 少量明确问题就地修复；不要为引入规则进行无关功能重构
3. 必要例外使用最小范围抑制并写明原因，不为通过构建关闭整个规则类别
4. 不为“规范统一”破坏 AOT、原生互操作、协议兼容或现有交互行为
5. 分析器不能代替运行验证，也不会自动发现所有 Reactor hook 顺序、主题、布局或动画生命周期问题；当前没有为这些行为编写自定义分析器

本轮验证只编译，不新增或运行测试用例。移动平台 UI 未做设备/工作负载验证；跨平台 Core 的 portable 构建不等同于 Android/iOS 应用构建。

首次落地的编译结果：Core/CLI portable solution、App x64、Web/Blazor、ExplorerCommand、WidgetProvider，以及 WinUI、App E2E、Blazor 测试项目均编译通过。WinUI 测试项目的 MakePRI 仍报告两条 MSTest 卫星资源 PRI263 警告，未将其作为 C# 分析器例外或在本轮屏蔽；其他上述编译为 0 警告、0 错误。规则只发现一处需调整的诊断：WindowsStartup 将内部系统状态误当作方法参数抛异常，已按 CA2208 改为 InvalidOperationException。

参考：[微软代码分析配置](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/configuration-files)、[构建时启用风格分析](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview)、[规则严重性](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/configuration-options)。
