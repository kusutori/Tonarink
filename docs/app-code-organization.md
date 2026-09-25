# Tonarink App 代码组织规范

本文档约定 `Tonarink.App` 的目录和文件拆分方式。目标是在保留现有一级目录风格的前提下，避免大型 Page、Component 和 Hook 文件同时承担渲染、状态转换与副作用协调，并防止通用 `Models` 文件不断吸收只属于单个界面的类型。

## 总体原则

应用继续使用按技术角色划分的一级目录：

```text
Tonarink.App/
  Pages/
  Components/
  Hooks/
  Services/
  Models/
  Utilities/
```

不引入顶级 `Features`、`ViewModels` 或 `State` 目录。大型功能在现有一级目录下使用二级目录组织：

```text
Pages/
  Send/
  Settings/

Components/
  Transfers/
  Dialogs/

Hooks/
  LocalSendNode/
```

拆分的目的不是让每一种类型都拥有独立文件，而是解决以下问题：

1. 一个文件同时包含过多渲染、状态转换和副作用，导致阅读与修改互相干扰；
2. 同一套表现逻辑需要被多个界面或入口复用；
3. 状态转换需要成为可独立检查、测试的纯逻辑；
4. 某段代码与宿主页面的变化原因已经不同。

文件较长本身不是充分理由。如果一段代码结构连续、只服务一个界面，而且拆分后只会增加跳转成本，则继续共置。

## 一级目录职责

### `Pages`

存放可导航页面，以及只服务于这些页面的表现逻辑。

允许包含：

- Page 组件；
- 页面专属 state union；
- 页面专属 action union；
- 页面专属 reducer；
- 页面专属的轻量投影或 workflow；
- 只被该页面使用的小型 UI 片段。

页面状态不是领域逻辑，但也不是与 UI 无关的通用模型。它描述界面可以处于哪些状态以及用户操作如何改变这些状态，因此属于表现层，可以放在对应的 `Pages/<Area>` 目录。

### `Components`

存放可以被 Page 或其他 Component 组合使用的 UI。

组件可以拥有自己的局部状态机。只有当状态机明显增大时，才在相同二级目录中拆出语义化的 workflow 文件。

### `Hooks`

存放可复用的 Reactor Hook，负责状态、生命周期和副作用的组合。

Hook 专属的 action、state 和 reducer 应与 Hook 放在同一个二级目录，而不是进入全局 `Models`。只有最终暴露给多个模块读取的稳定快照模型，才考虑放入 `Models`。

### `Services`

存放不依赖具体控件树的应用服务，包括持久化、系统集成、协调器和平台能力。

Service 不应引用某个 Page 的内部 state/action。页面通过明确的请求、结果或回调与 Service 交互。

### `Models`

仅存放跨多个页面、Hook 或 Service 共享的应用模型。

以下内容不应因为“不生成 UI”就自动进入 `Models`：

- 单个页面的 reducer action；
- 单个覆盖层的 view state；
- 单个 Hook 的内部生命周期 action；
- 只用于一个组件的对话框编辑状态。

`AppModels.cs` 不再作为所有小型类型的默认落点。新类型必须先回答“哪些模块共同使用它”，再决定是否进入 `Models`。

### `Utilities`

存放无状态、无生命周期、无业务所有权的通用辅助函数。需要依赖设置、服务、Hook 或 UI 状态的代码不属于 Utility。

## 二级目录建立条件

满足以下任一条件时，可以建立二级目录：

- 一个功能已经包含至少三个紧密相关的文件；
- Page 或 Component 需要拆出独立状态机；
- 同类页面或组件需要共享表现逻辑；
- 单文件中渲染、reducer 与异步 workflow 已经难以独立阅读；
- 后续的大规模改造会集中影响这一功能，需要先建立清晰边界。

不为单个短文件创建只有一项内容的目录。二级目录必须表达稳定的功能区域，例如 `Send`、`Transfers`、`Dialogs`，而不是 `Misc`、`Common`、`Helpers`。

## 文件命名

避免使用类似 XAML code-behind 的机械命名：

```text
SendPage.State.cs
SendPage.Actions.cs
SendPage.Helpers.cs
```

除非文件确实包含同一 `partial` 类型，否则不使用“宿主文件名 + 任意后缀”表达代码归属。优先使用代码本身的语义：

```text
Pages/
  Send/
    SendPage.cs
    SendTransferWorkflow.cs

Components/
  Transfers/
    IncomingTransferOverlay.cs
    IncomingTransferWorkflow.cs
    OutgoingTransferOverlay.cs

Hooks/
  LocalSendNode/
    UseLocalSendNode.cs
    LocalSendNodeRuntime.cs
```

`Workflow` 适用于包含 state、action 和纯 reducer 的小型表现状态机。若文件只有数据类型而没有状态转换，可以使用更具体的 `State` 或 `Models` 名称，但不要为了形式把一个紧密状态机拆成多个小文件。

## 命名空间

命名空间必须与目录位置一致，以保持 ReSharper、IDE 导航和代码审查结果稳定：

```text
Pages/Send/SendPage.cs
namespace Tonarink.Pages.Send;

Components/Transfers/IncomingTransferOverlay.cs
namespace Tonarink.Components.Transfers;

Hooks/LocalSendNode/UseLocalSendNode.cs
namespace Tonarink.Hooks.LocalSendNode;
```

移动文件时，应在同一提交中更新命名空间和引用。不要通过保留旧命名空间来规避调用点修改，也不要在项目中建立与目录不对应的临时命名空间。

## 状态机拆分方式

页面或组件的状态机使用独立顶级类型，不使用 `partial Page` 隐藏实现：

```csharp
union SendTransferState(...);

union SendTransferAction(...);

static class SendTransferReducer
{
    public static SendTransferState Reduce(
        SendTransferState state,
        SendTransferAction action) => action switch
    {
        // Exhaustive state transitions.
    };
}
```

Page 负责建立 Hook、dispatch action 和渲染：

```csharp
var (transfer, dispatchTransfer) =
    UseReducer<SendTransferState, SendTransferAction>(
        SendTransferReducer.Reduce,
        SendTransferState.Initial);
```

状态机不得访问控件实例、窗口句柄或 Hook。异步 I/O 仍由 Page、Hook 或 Service 执行，完成后再 dispatch 结果 action。这样 reducer 保持纯函数，也不会退化为另一种 code-behind。

## 复用范围与放置位置

| 使用范围 | 放置位置 |
| --- | --- |
| 单个 Page，逻辑简单 | 与 Page 共置在同一文件 |
| 单个 Page，状态机复杂 | `Pages/<Area>` 中的语义化 workflow 文件 |
| 单个可复用 Component | `Components/<Area>`，必要时附带 workflow |
| 多个 App 页面或组件共享 | App 内的 Hook、Service 或共享 Model |
| WinUI、MAUI、Widget 等多个前端共享 | `Tonarink.Application` |
| LocalSend 协议、传输和公开能力 | `LocalSendDotNet.Core` |

“可以复用”不意味着立刻上移。只有出现真实的第二个调用方，或者逻辑本身明确属于更低层抽象时，才移动到共享位置。

## Tonarink 当前拆分目标

在 Core 0.3 preview 改造前，只整理即将承受大量 API 迁移的区域：

```text
Pages/
  Send/
    SendPage.cs
    SendTransferWorkflow.cs

Components/
  Transfers/
    IncomingTransferOverlay.cs
    IncomingTransferWorkflow.cs
    OutgoingTransferOverlay.cs

Hooks/
  LocalSendNode/
    UseLocalSendNode.cs
    LocalSendNodeRuntime.cs
```

建议职责如下：

- `SendTransferWorkflow.cs`：`TransferUiState`、`SendTransferAction`、overlay update 与纯 reducer；
- `IncomingTransferWorkflow.cs`：接收覆盖层 state、action 与纯 reducer；
- `LocalSendNodeRuntime.cs`：`AppRuntimeState`、`AppRuntimeAction` 与纯 reducer；
- UI 文件继续负责 Hook 调用、控件树和异步调用；
- `OutgoingTransferViewState` 根据实际共享范围放在 `Components/Transfers` 或稳定的共享模型文件中，不继续堆入通用 `AppModels.cs`。

本轮不搬动设置、历史记录、网络页面及其他小型文件。更广泛的目录整理只能在出现真实维护问题时逐步进行。

## 与 Core 改造的执行顺序

代码拆分优先于 Core 破坏性改造，但只执行上述受控范围。

原因：

1. Core 0.3 会同时修改发送、接收、PIN 和失败处理，这些正是当前几个最大文件中的状态机；
2. 先把纯 reducer、UI 和异步调用分开，可以显著缩小 Core 迁移时每个提交的修改范围；
3. 如果先改 Core，再移动文件，API 变化与目录变化会混在同一批 diff 中，难以审查和定位回归；
4. 反过来进行全项目重排也会产生大量无功能价值的 churn，因此只整理 Core 迁移会直接触及的代码。

推荐顺序：

```text
受控拆分发送/接收/节点生命周期
  -> 验证行为与构建
  -> Core 0.3 preview 破坏性改造
  -> 主 App 迁移到 Core 0.3
  -> 按实际需要继续整理其他功能
```

拆分提交必须是纯结构改动，不新增功能、不改变状态语义。完成后应继续通过 Debug、Release 与 Native AOT 构建，保证下一阶段从清晰、稳定的目录边界开始。
