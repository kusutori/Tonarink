# 联合类型与 Core Preview 迁移计划

本文档定义 Tonarink 在 .NET 11 / C# Preview 实验分支上的联合类型改造路线。目标不是把所有 `enum`、异常和状态对象机械地替换为联合类型，而是利用联合类型消除无效状态、让分支穷尽检查生效，并为 Core 的下一版公共 API 建立清晰的成功与失败模型。

整个迁移分为三个阶段：

1. 先在主应用内部改造适合使用联合类型的状态与操作；
2. 再对 Core 进行一次明确的破坏性改造，并发布新的 preview 版本；
3. 最后让主应用切换到全新的 Core API，集中完成跨层迁移。

## 基本原则

### 适合使用联合类型

- 一组封闭且互斥的状态，并且不同状态携带不同数据；
- 当前依赖 `enum` 加多个可空字段表达的状态；
- reducer 的 action 集合；
- 可预期、调用方可以处理的业务结果；
- 需要由编译器协助检查所有分支的流程。

例如，`Idle` 不应该携带活动传输，而 `Sending` 必须携带设备、进度和取消句柄。联合类型可以在类型层面表达这一约束，而不是依赖运行时约定。

### 继续使用 `enum`

- 不携带额外数据的简单选项，例如排序方式、保存策略、设备类型；
- 位标志；
- 协议、持久化文件或公开 JSON 中需要长期稳定的标量值；
- 高频进度事件中的轻量状态，例如 `TransferState`；
- 联合类型不能显著减少无效状态或分支复杂度的场景。

### 继续使用异常

联合类型不应成为“永不抛异常”的借口。以下错误仍然使用异常：

- 参数或调用契约错误；
- 对象生命周期错误和不可能状态；
- I/O、网络栈或框架产生的未预期异常；
- `OperationCanceledException` 及标准取消语义；
- 无法在当前抽象层恢复的内部故障。

可预期的业务分支，例如对端拒绝、需要 PIN、PIN 错误和设备忙，应优先成为结果联合的 case，而不是依靠调用方捕获异常完成正常控制流。

### 边界约束

- 不直接把语言级联合类型暴露为协议 DTO 或持久化 JSON；协议层使用稳定 DTO，并在边界处显式映射。
- 在 Core 公共 API 中使用联合类型前，必须验证 XML 文档、NuGet 消费体验、反射、序列化和 Native AOT 行为。
- 标准联合类型的值类型 case 可能发生装箱；进度回调等高频路径保持轻量结构，除非基准测试证明改造有收益。
- 每次迁移都应减少状态数量或分支复杂度。仅仅为了使用新语法而改写代码不算收益。

## 第一阶段：改造主应用

### 目标

在不改变 Core 公共 API 的前提下，先让主应用内部形成稳定的联合类型使用规范，并验证 Reactor、WinUI 3、Native AOT 与新语法的组合。

### 已完成的试点

当前实验分支已经覆盖了几类典型场景：

- `ShareTargetItem`：文件系统项目与文本分享项目；
- `JumpListActivation`：收藏设备与历史记录激活；
- `AppNotificationActivation`：不同通知动作及各自负载；
- 收藏设备对话框的创建、编辑和待处理操作；
- `TrayDropStatus`：托盘拖放反馈的不同状态；
- `TransferUiState`：发送页空闲与活动传输状态；
- `DeviceResolution`：设备解析成功与失败结果。

这些试点用于确认三件事：case 建模是否自然、模式匹配是否比原条件分支更清晰，以及 JIT/Native AOT 构建是否保持正常。

### 后续优先级

#### 1. `UseLocalSendNode`

将它作为第一阶段的重点：

- 保留当前职责边界，不借机重新设计整个 LocalSend 生命周期；
- 使用标准 Reactor 自定义 Hook 结构；
- 将 reducer action 定义为联合类型；
- 初期可以保留单个 `AppRuntimeState` record，先解决 action 与副作用组织问题；
- reducer 保持纯函数，启动、停止和重启等异步副作用留在 Hook 的 effect 中；
- 等 action union 稳定后，再判断 runtime state 是否值得拆成 `Stopped`、`Starting`、`Running`、`Faulted` 等 case。

Reactor 示例中通过抽象基类派生 action 的写法，可以直接由联合类型替代。两者都表达封闭的 action 集合，但联合类型可以省去继承样板，并继续使用穷尽模式匹配。

#### 2. 接收覆盖层

当前接收覆盖层同时管理等待、编辑、接收、完成、失败等状态，适合拆成：

- 一个描述页面阶段的 state union；
- 一个描述用户操作和异步完成事件的 action union；
- 一个纯 reducer；
- 若干只负责 I/O 的 effect。

重点是消除“某个布尔值为 `true` 时另一个字段必须非空”一类隐含约束，而不是一次性重写全部 UI。

#### 3. 发送页

`TransferUiState` 已经证明 state union 可行，下一步是逐步引入 action union，让粘贴、选择内容、开始发送、PIN 重试、取消和结束等状态变化通过 reducer 进入同一个入口。

发送页体量较大，迁移应按行为分批完成，避免把 UI 提取、状态重构和 Core API 迁移混在同一个提交中。

#### 4. 全局审计

搜索以下结构，逐项判断，而不是批量替换：

- `enum` 与多个 nullable payload 同时出现；
- 多个布尔字段共同编码单一状态机；
- `object`、字符串 code 或 tuple 表示不同结果；
- reducer/action 使用抽象基类和大量空派生类型；
- 捕获异常只是为了识别正常业务分支。

### 不在本阶段改造的内容

- 单纯用于触发重渲染的 epoch/counter；
- 普通设置 record 的 `with` 更新；
- 独立且无附加负载的 UI 选项；
- 协议中的设备类型、传输阶段和兼容性枚举；
- 已经简单、清晰且没有无效状态的模型。

### 第一阶段验收条件

- 新增 union 均对应明确的无效状态消除或分支简化；
- reducer 可以脱离 UI 独立测试；
- 不改变 Core 的公开契约；
- 主应用 Debug、Release 和 Native AOT 构建通过；
- 分享、发送、接收、托盘、通知和激活入口的现有行为不回退；
- 形成一套可复用的命名、case 组织和模式匹配风格。

## 第二阶段：Core 破坏性 Preview 改造

### 版本策略

Core 当前版本为 `0.2.0-preview.5`。项目尚未进入 `1.0`，因此按照 SemVer 的 0.x 约定，下一轮破坏性 API 应从以下版本开始：

```text
0.3.0-preview.1
```

后续修订依次使用 `0.3.0-preview.2`、`0.3.0-preview.3`。在 API、文档、AOT 和消费端迁移全部稳定前，不发布 `0.3.0` 正式版。

preview 阶段允许继续调整 case 名称和负载，但每次调整必须写入 changelog。进入 `0.3.0` 后，同一 minor 版本内不再进行无迁移说明的破坏性变化。

### 改造范围

#### 1. 传输结果

当前 `TransferResult`、`TransferState`、可空 `Failure` 与 PIN 异常构成了两套结果通道。目标是让可预期结果通过一个明确的联合返回，例如：

```csharp
public union SendOutcome(
    SendOutcome.Completed,
    SendOutcome.Cancelled,
    SendOutcome.Declined,
    SendOutcome.PinRequired,
    SendOutcome.PinRateLimited,
    SendOutcome.PeerBusy,
    SendOutcome.Failed);
```

具体 case 和负载需要在实现前通过 API 设计评审确定。尤其需要回答：

- 完成结果需要返回哪些文件、字节数和对端信息；
- `PinRequired` 是否以 `InvalidPin` 负载区分首次请求与错误 PIN；
- 本地取消与远端拒绝是否必须区分；
- 哪些失败是稳定的领域错误，哪些只应包装底层异常；
- 发送与接收是否共享一个 outcome，还是分别使用 `SendOutcome` 与 `ReceiveOutcome`。

原则上，发送和接收的调用方需求不同，优先使用两个小而精确的联合，而不是一个包含大量无关 case 的通用结果。

#### 2. 失败模型

评估用带负载的失败 case 替代字符串 code 和可空属性。失败模型至少应做到：

- 调用方不需要解析消息文本；
- UI 可以稳定地映射到本地化文案；
- 未知服务端响应仍有保底 case；
- 原始异常可以作为诊断信息保留，但不作为业务分支的唯一依据。

#### 3. 异常边界

以下现有正常控制流应重点评估并迁移：

- `PinRequiredException`；
- PIN 频率限制；
- 对端拒绝或繁忙；
- 已知的身份验证失败；
- 可恢复的设备解析失败。

参数错误、生命周期错误、取消和不可预期的传输实现故障仍使用异常。Core 不应为了返回 union 而吞掉真实异常。

#### 4. 公共 API 与适配层

需要逐项审查：

- `LocalSendNode.SendAsync`；
- `LocalSendNode.AcceptAsync`；
- `IncomingTransferRequest` 与内部 session completion；
- `TransferResult`、`TransferFailure` 和公开异常；
- `Tonarink.LocalSend.LocalSendRuntime` 的包装契约；
- Core、Mobile 和集成测试中的辅助方法。

协议 DTO 与 HTTP transport 不直接返回 UI-facing union。Transport 先解析为稳定的内部响应，再由 Core 服务层映射为公开 outcome。

### 兼容策略

Core 的这一阶段明确允许破坏性变更，不为旧签名长期维护双轨实现。可以在一个短暂提交中保留 `[Obsolete]` 适配器辅助迁移，但在 `0.3.0-preview.1` 发布前应决定是否删除，避免新 API 从一开始就背负兼容层。

主应用在第二阶段暂不与每个中间提交同步。Core 先在独立测试中达到可消费状态，再发布 preview，防止应用层在 API 摇摆期间反复改写。

### 第二阶段验收条件

- 所有公开 outcome 和 case 都有 XML 文档；
- 正常业务分支不再依赖 PIN 等专用异常；
- transport、domain outcome 和 UI 文案边界清晰；
- Core 单元测试、集成测试和 Mobile 测试覆盖所有 case；
- NuGet 打包、公开 API 检查和 Native AOT smoke test 通过；
- 发布 `0.3.0-preview.1`，并提供从 `0.2.x` 迁移的简短说明。

## 第三阶段：主应用迁移到新 Core

### 目标

让主应用完整采用 Core 0.3 preview 的新结果模型，删除旧异常和旧 `TransferResult` 判断逻辑，并把 Core outcome 转换为应用自己的状态与 action。

仓库内开发仍可以使用 `ProjectReference`，但应用应以 `0.3.0-preview.*` 的公开契约为准；在准备发布时，再验证对应 NuGet 包的真实消费路径，避免只在源码引用下可用。

### 迁移顺序

#### 1. 建立单一映射边界

在应用服务层集中完成：

```text
Core outcome -> application action/result -> UI state
```

页面和纯 UI 组件不应各自解释 Core 的所有 case，也不应直接拼接 Core 的诊断消息。这样可以防止 Core 类型扩散到整个视图树，并让本地化、通知和日志共享同一套语义。

#### 2. 发送链路

依次迁移：

- `LocalSendRuntime`；
- `SendPage`；
- `TraySendCoordinator` 与托盘面板；
- 发送覆盖层；
- PIN 输入和重试流程；
- 发送通知与任务栏进度。

`PinRequired` 应成为 reducer action 或应用结果 case，不再通过跨多层 catch 驱动 UI。

#### 3. 接收链路

依次迁移：

- incoming request coordinator；
- 接收覆盖层与文件选择；
- `ReceiveHistoryStore`；
- 接收完成通知；
- 自动保存和拒绝流程。

#### 4. 其他消费端

- Widget 状态映射；
- 跳转列表与通知激活后的错误处理；
- 测试 fake/stub；
- MAUI/Hybrid 对 Core 的直接或间接引用。

### 清理工作

完成所有调用点迁移后：

- 删除旧的 `TransferPinRequiredException` 包装；
- 删除旧 `TransferResult.State` 和 nullable `Failure` 分支；
- 删除只为旧 API 存在的 catch；
- 将日志改为记录 outcome case 与结构化负载；
- 更新 README、API 示例、changelog 和发布说明。

### 第三阶段验收条件

- 主应用不再引用 Core 0.2 的结果类型和业务异常；
- 每个 Core outcome 都有明确的应用层处理或显式兜底；
- PIN、拒绝、取消、失败、完成流程均有自动化测试；
- JIT、Native AOT、打包模式和非打包模式验证通过；
- 托盘发送、Share Target、通知操作和前台发送行为一致；
- Core 0.3 preview 可以被仓库外的最小示例项目正常引用。

## 提交与分支策略

为避免难以审查的大提交，三个阶段采用不同粒度：

### 第一阶段

- 每个 union 或 reducer 单独提交；
- 一个提交只处理一种状态机；
- UI 提取与状态迁移尽量分开；
- 每次提交保持主应用可运行。

### 第二阶段

- 先提交 API 设计与测试草稿；
- 再提交 Core 内部实现；
- 随后更新公开 API、文档和版本号；
- 以 `v0.3.0-preview.1` 发布第一个可消费包。

仓库现有发布脚本对应的命令为：

```powershell
./tools/Publish-Release.ps1 -Project Core -Version 0.3.0-preview.1
```

执行前必须先在 `CHANGELOG.md` 中添加同版本标题。脚本会同步 Core 项目和中央包版本、创建提交与 `v0.3.0-preview.1` 标签，并由 NuGet 发布工作流完成打包和推送。

### 第三阶段

- 按发送、接收、托盘、通知和其他消费端分批迁移；
- 在所有调用点迁移前，不混入无关 UI 改版；
- 最后单独提交旧 API 清理和文档更新。

## 风险与控制

| 风险 | 控制方式 |
| --- | --- |
| .NET 11/C# Preview 语法继续变化 | 将实验保留在专用分支；升级 SDK 时先构建最小 union 示例和主应用 |
| 公共 union 影响 NuGet 消费体验 | 发布前使用仓库外示例项目验证引用、模式匹配和 XML 文档 |
| union case 进入 JSON 后造成兼容问题 | 协议和持久化层继续使用显式 DTO/转换器 |
| 值类型 case 装箱增加热路径开销 | 不迁移进度事件；对热点进行基准测试后再决定 |
| 新旧错误通道并存导致重复处理 | 第二阶段先统一 Core 契约，第三阶段按链路迁移并尽快删除旧通道 |
| 迁移同时改变行为，难以定位回归 | 状态重构、UI 改版和功能新增分开提交 |
| preview Core API 频繁摇摆 | 在发布首个 preview 前完成 API 设计评审；之后每次变更记录迁移说明 |

## 完成定义

整个计划完成时，应满足：

- 主应用只在确实改善建模的地方使用联合类型；
- reducer 的 action 集合不再依赖样板式继承层次；
- Core 使用结构化 outcome 表达可预期的传输结果；
- 异常只负责契约错误、取消和不可预期故障；
- 主应用通过单一适配边界消费 Core outcome；
- Core `0.3.0-preview.*` 与主应用在 JIT、Native AOT 和打包场景中均通过验证；
- 文档、示例和迁移说明足以让外部使用者完成升级。

这三个阶段必须按顺序推进。第一阶段用于建立应用侧模式和经验；第二阶段可以专注于 Core 的公共契约；第三阶段再一次性承担真正的跨层破坏性迁移，避免主应用长期处于新旧 API 混用状态。
