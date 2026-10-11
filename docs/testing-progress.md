# 测试实施记录

更新日期：2026-10-11

目标见 [testing-plan.md](testing-plan.md)。本记录只声明已核验的范围，不代表完整计划已完成。

旧案例的保留、改写、合并或删除逐项记录见 [testing-migration.md](testing-migration.md)。

逐功能、逐 CLI 命令和 App 页面对应的证据及缺口见 [testing-coverage.md](testing-coverage.md)。矩阵是首轮责任审计，不将语法转发或控件宿主成功视为整个产品能力完成。

## 收尾交接（2026-10-11）

按用户要求暂停测试扩展，不标记完整计划完成，也不继续调查非产品需求的控件边界。

- 撤除历史页交换已有两条记录顺序的案例，以及纯原生 ItemsRepeater Move 对照。当前历史功能仅新增、删除、清空；交换已有记录顺序不是产品需求，不再作为缺口或门禁。
- 历史页保留九个有效控件案例，接收页保留十五个案例及无效/重复选择不保存的防御性处理。WinUI 常规套件数量下限由 128 调整为 126。
- 撤回尚未配套测试的设备详情页收藏存储注入改动，包括 `IFavoriteDeviceStore`；设备详情页恢复原实现，不遗留半成品测试接口。
- 旧 TRX、诊断与首次失败记录保留。撤除案例不改写旧报告为通过；最近完整运行仍为 128 执行、126 通过、2 失败。收尾仅做编译检查，不重新运行完整套件，不能声称当前 126 项已重新全套通过。
- 真实输入、生产 App E2E、集成 CLI、最终 MSIX、其余平台及 CI 的未验收范围继续保留，但暂停实施；没有提交、发布或修改生产安装与数据。
- 收尾编译：`dotnet build tests/Tonarink.App.WinUI.Tests/Tonarink.App.WinUI.Tests.csproj -c Release -p:Platform=x64 --no-restore --nologo` 成功，包含主 App 项目引用，0 警告、0 错误；未运行测试。

以下为收尾前的实施记录，不再表示下一步执行安排。

## 恢复实施：Receive / History 真实控件（2026-10-11，历史记录）

目标已恢复，仍保持完整计划范围，不标记完成。本批只使用独立非打包 WinUI 测试宿主、内存设置/历史边界及自有临时文件；没有启动生产网络服务、安装 App、修改正式设置/联系人、启用 Windows 功能或运行发布。

接收页首次编译失败保留在 `artifacts/test-results/receive-page-20261011-201832/controller/`：CsWinRT1032 要求传给投影边界的两个集合表达式显式转换为 `IncomingTransferRequest[]`。修正后真实 12 项均因初始化保存回传失败，报告 `receive-page-fixed-20261011-201926/`。进一步三个自动保存案例的值序列为 `-1 → 原模式`；`receive-selection-stack-20261011-202514/` 的实际栈指向 `CommunityToolkit.WinUI.Controls.Segmented.OnApplyTemplate()`，而不是此前怀疑的 ItemsHost 重建。保留这次根因修正，不将先前猜测当作结论。

生产 `ReceivePage` 现在忽略无效模式及与当前传入设置相同的模式。测试不再把页头从 null 插入导致的夹具布局替换、相同 runtime 值不触发 UseState 重渲染、同一 dispatcher 回合中连续写原生属性而未等待受控值渲染的行为混入生产回归。真实导航宿主、设置更新回调和“初始化不能保存”的断言都保留；每次操作等待实际渲染完成后仍严格要求一次回调。新增三个空选择/模板重新应用/下一次编辑案例，接收页共 15 项通过。

历史页新增十个生产页面案例和一个原生框架对照。只替换 `IReceiveHistoryStore`，不复制 HistoryPage 或绕过命令；菜单/确认按钮通过 native UIA Invoke 执行，外部文件打开/资源管理器启动不执行。通过的九项验证空态到新增/删除/清空、订阅终结、三种路径门控、单项删除只影响目标 ID、清空取消/确认而文件字节不变、真实 ContentDialog 元数据、已知/缺失导航 ID 消费、切换存储后的订阅与命令更新。初始 `history-page-first-20261011-203030/` 六项失败中，回收池控件误计入显示行和未挂载 XamlRoot 的夹具读取已修正；现在只按 ItemsRepeater 的有效索引读取行，不强制 GetOrCreateElement，也不取消虚拟化。

仍失败的重排有可重复的运行证据：`history-sized-viewport-20261011-203520/` 使用 1000×640 宿主，repeater 实际 928×515、数据两项，而显示索引为 `+,-`、第一项仍显示旧内容。`history-native-move-20261011-203706/` 使用原生 ItemsRepeater、普通 DataTemplate 和 `ObservableCollection<string>.Move`，完全不经过 Reactor，仍显示 `first,null`。这只证明故障能够在原生路径复现，不足以宣布已定位到 WinUI、CsWinRT 或某个版本的具体内部原因；不能归因于 HistoryPage 的自定义工厂。微软[ItemsRepeater 数据源说明](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/items-repeater#configure-the-data-source)说明 INotifyCollectionChanged 支持 Move，IObservableVector 没有 Move 动作，投影使用哪条路径仍需进一步核验。未引入列表整体重建、未屏蔽或忽略这两条失败。

完整统一入口结果：`artifacts/test-results/receive-history-complete-win-20261011-203850/winui/`，128 执行 / 126 通过 / 2 失败 / 0 跳过；MTP 和控制器均退出 2、输出排空，外层脚本正确非零失败。TRX 的类别数量为 Expander 2、FilePreviewContent 1、FilePreviewSettings 15、History 11、NetworkInterfaces 4、PluralMessage 6、Receive 15、SelectedSendItem 16、SendDeviceResolver 32、SendDevicesCard 6、SettingsInputDraft 20。该次命令使用数量下限 122，随后按实际清单将入口下限改为 128；不把旧 96 项报告当作当前工作区的数量或通过状态。

被测 App DLL SHA-256 `07FC34083417D8BA6FF41A8D7C435A383D6E6F2AC879150375F01AF082BA0D62`，WinUI 测试 DLL `68A30F698A6223E558979C744353629CF8E23DD428965DABC7BDB0B26504FD45`；ReceivePage 源码 `57C1DB7A9F9BEB8FAEAAD51AEBE206636D6A7C316E02CE8F8DB43673523D1425`、Receive 用例 `7F29FF0809E1FB619632F57BA4949E02CB7D79F23AF75AFEEEEED8FC0C1D16B2`。首次失败与中间报告均保留。本批尚不能认定 History 重排、完整 WinUI、真实用户输入、生产 App 导航、集成 CLI、最终 MSIX 或其他 OS/架构验收通过；后续继续定位 Move 运行时路径并推进其余计划缺口。

## 暂停交接点（2026-10-10，历史记录）

按用户要求收尾，测试建设目标保持暂停，不标记整体完成。本次只核对现有文件、TRX、控制器结果及进程状态，更新交接记录；没有启动新测试、构建、CI 或发布。

- 实际仓库为 `D:\dotnet\Tonarink`；旧 `D:\dotnet\LocalSendDotNet` 不是当前源码根目录
- 核对时有 75 项已跟踪变更和 24 条未跟踪文件/目录记录；全部保留，未提交、回滚或清理。未跟踪目录可能含多个文件，以上不是文件总数
- 未发现 Windows 测试用的 dotnet、CLI 或 WSL 进程；`wsl --list --running --quiet` 未列出运行中的发行版。未关闭用户终端，也没有为查看报告重新启动 WSL
- Windows/已归档 WSL Linux：Core 各 95、Application 各 26、CommandLine 各 186、Mobile 各 5 项通过，均零失败、零未执行；报告见 `pin-atomic-shared-complete-{win,linux}/`
- Windows PIN 修复后的 AOT tool、portable 各 58 项通过，均零失败、零未执行；两份控制器退出 0、输出排空，实际/预期 EXE 哈希一致。tool 于北京时间 20:21 完成，portable 于 20:29 完成，报告见 `cli-pin-atomic-distribution-win/win-x64/{tool,portable}/cli/`
- Linux 同版 AOT 分发报告未归档到当前 Windows 结果目录，本次没有核验其终态；不能将之前“已启动”写成已完成。新 JIT 55 项全套也尚无核验记录
- WinUI 最近已核验全套仍为 96 项；此报告早于下述 Receive/History 在途修改，不证明当前 App 工作区全套通过

在途代码与恢复顺序：

1. `ReceivePageTests.cs` 已写入，设计共 12 个参数化案例，但 `receive-page-first-win/` 结果目录不存在；先构建并取得专项实际结果，再登记覆盖。`ReceivePage` 已改为使用传入设置及更新页头，不再直接读取全局配置
2. `HistoryPage`、`HistoryPageProps`、`ReceiveHistoryStore` 及新增 `IReceiveHistoryStore.cs` 已加入可隔离存储依赖；生产默认仍委托原持久化实现。尚无 History 页面专项用例或验收结果，不能视为已验证重构
3. 上述专项完成后重新运行完整 WinUI，再检查 Linux 原始 AOT 分发报告并归档；随后补当前 Windows/Linux JIT 55 项全套，避免旧产物报告替代最新代码验证
4. 生产 App E2E、真实输入、集成 CLI、最终 AOT MSIX、macOS/ARM64 和 CI 实际执行仍未验收，继续按计划矩阵推进

`git diff --check` 本次退出 0；仅有 Git 的 LF/CRLF 转换提醒。它不包含编译或运行验证。计划文档已补充排版、可访问性与真实输入的证明边界；现有首次失败证据和产物均未删除。

### 按功能归档的本地 Git 提交

随后按用户要求将暂停点的修改按功能提交。本次没有恢复测试建设目标、重跑套件或推送远端；提交本身不改变上述验收状态。各提交组成同一依赖链，框架及项目配置先归档，具体模块和方案注册随后归档，不声明每个中间提交均已单独构建或测试。

| 提交 | 功能范围 |
| --- | --- |
| `a437889` | MSTest/MTP 配置、项目基础、隔离辅助设施及控制器审计 |
| `f3fa56e` | Core/portable 传输取消、可靠广播、待处理快照及原子 PIN 锁定回归 |
| `f1efb3a` | 共享业务设备完整快照与运行时用例归并 |
| `cf3c411` | CLI IPC 生命周期、背压、退出、文本输出、地址选择及真实进程用例 |
| `73f8d60` | Blazor 组件语言上下文及 bUnit 行为验证 |
| `c39f559` | 真实 WinUI 控件宿主、发送解析与设备卡片终结清理 |
| `37244e0` | 生产 AutomationId 与隔离 App E2E 控制器，实际用户流程尚未验收 |
| `f1080f6` | Receive/History 依赖隔离与 Receive 新案例，在途且未验收 |
| `3168761` | 方案注册、CI 测试入口及 Native CLI 分发验证接线 |

本计划、迁移清单、覆盖矩阵和本交接记录另随最后一个文档提交归档；以 `git log` 中 `docs: record test strategy, migration and paused handoff` 定位。运行报告和构建产物仍保留在忽略目录中，不加入源码提交。

## 已完成的迁移

| 原套件 | 处理 | 保留与替换说明 |
| --- | --- | --- |
| Core | MSTest | 保留协议、身份、真实网络、传输可靠性、安全路径、Web 分享及 API 基线；缺少环境时显式报告，不静默返回成功 |
| Application | MSTest | 保留状态、设置、资源及文件导入案例 |
| LocalSend 适配 | 合入 Application | 5 个实际业务收发案例保留，独立旧项目移除 |
| App.Settings | 合入 App.WinUI | 引用生产 App，保留草稿与预览覆盖逻辑，修正过时选择/删除断言；删除源码链接替身及 4 个结构/动画源码检查 |
| Core.Mobile | MSTest | 4 个真实 portable TLS/收发案例保留；这不是手机设备或 MAUI UI 测试 |
| Blazor.Shared | MSTest + bUnit | 原 4 个 Razor 文本结构检查改为实际生产组件渲染、更新与事件案例；CSS 引用检查保留为明确的宿主资产契约，不计为 UI 自动化 |
| Interop | 移除伪功能套件 | 原案例只读取文档标题；人工官方客户端记录仍在 `src/LocalSendDotNet.Core/docs/interop-matrix.md`，未声明自动互通通过 |

仓库测试项目统一使用 `MSTest.Sdk` 4.4.1，`global.json` 已启用 MTP runner；原 xUnit、runner、VSTest-only SDK、coverlet collector 的直接配置已移除。WinUI 自宿主能引用生产组件并运行异步 dispatcher；其他打包模型和完整 4.5 支持矩阵仍需单独核验。

## 实际运行证据

以下结果均来自本机 Windows x64、Release 配置；没有据此推断 Linux、macOS、ARM64 或最终 App MSIX 通过。

| 套件 | 结果 | TRX 所在目录 |
| --- | --- | --- |
| Core（含 TLS/Web 跨 IP 与并发 PIN 回归） | 95 通过、0 跳过；当前 PIN 原子判定修复 | `artifacts/test-results/pin-atomic-shared-complete-win/core/` |
| Application（含 LocalSend 与设备快照回归） | 26 通过、0 跳过；重新构建当前 Core 引用 | `artifacts/test-results/pin-atomic-shared-complete-win/application/` |
| CommandLine（含真实 MTP、调度函数与 IPC 生命周期） | 186 通过、0 跳过；重新构建当前生产引用，保留最终响应断连回归 | `artifacts/test-results/pin-atomic-shared-complete-win/commandline/` |
| Core.Mobile portable 传输 | 5 通过、0 跳过；含并发 PIN 原子判定回归 | `artifacts/test-results/pin-atomic-shared-complete-win/mobile/` |
| Blazor 组件及宿主资产 | 8 通过、0 跳过；包括语言切换及后台更新回归 | `artifacts/test-results/blazor-culture-scope-win/` |
| WinUI 控件、设置、发送辅助逻辑与网络接口页 | 96 通过、0 跳过；重新构建 PIN 修复后的主 App 引用，不包含 SystemInput 或主 App 导航 E2E | `artifacts/test-results/pin-atomic-app-reference-win/winui/` |
| Windows 当前 PIN 修复 AOT tool 与 portable | 两组各 58 通过、0 跳过；控制器退出 0、输出排空，实际/预期原生 EXE 哈希一致 | `artifacts/test-results/cli-pin-atomic-distribution-win/win-x64/{tool,portable}/cli/` |
| Windows 上一版 JIT CLI 完整范围 | 54 通过、0 跳过；实际 peer watch 与退出修复，不证明随后 PIN 修复 | `artifacts/test-results/cli-quit-delivery-jit-complete-win/cli/` |
| Windows/Linux 上一版 tool 与 portable AOT CLI | 四组各 57 通过、0 跳过；退出修复及真实 peer watch，不证明随后 PIN 修复 | `artifacts/test-results/cli-quit-delivery-distribution-{win,linux}/{win,linux}-x64/{tool,portable}/cli/` |
| Windows 新 NuGet AOT CLI（有界输出载荷） | 55 通过、0 跳过；本地 RID 包实际安装执行 | `artifacts/test-results/cli-bounded-output-distribution-win/win-x64/tool/cli/` |
| Windows 新 portable AOT CLI（同一有界输出载荷） | 55 通过、0 跳过；实际解压执行，与 tool 原生 EXE 哈希一致 | `artifacts/test-results/cli-bounded-output-distribution-win/win-x64/portable/cli/` |
| Linux 上一版 JIT CLI 完整范围 | 54 通过、0 跳过；退出修复后的载荷，不证明随后 PIN 修复 | `artifacts/test-results/cli-quit-delivery-jit-complete-linux/cli/` |
| Linux 新 NuGet / portable AOT CLI（有界输出载荷） | 两组各 55 通过、0 跳过；实际安装与解压，同一原生 EXE 哈希 | `artifacts/test-results/cli-bounded-output-distribution-linux/linux-x64/{tool,portable}/cli/` |
| Windows JIT CLI 上一版完整范围 | 52 通过、0 跳过；新总预算约 8 分 46 秒，尚不证明随后停止/释放修复 | `artifacts/test-results/cli-active-lifecycle-complete-win/cli/` |
| 此前 NuGet win-x64 AOT CLI（广播与显式地址修复载荷） | 50 通过、0 跳过；不证明本批 Core 取消修复 | `artifacts/test-results/cli-active-upload-distribution-win/win-x64/tool/cli/` |
| 此前 portable win-x64 AOT CLI（广播与显式地址修复载荷） | 50 通过、0 跳过；不证明本批 Core 取消修复 | `artifacts/test-results/cli-active-upload-distribution-win/win-x64/portable/cli/` |
| 新 NuGet win-x64 AOT CLI（停止/释放与取消意图修复载荷） | 55 通过、0 跳过；实际安装本地 RID 包 | `artifacts/test-results/cli-active-shutdown-distribution-win/win-x64/tool/cli/` |
| 新 portable win-x64 AOT CLI（同一修复载荷） | 55 通过、0 跳过；实际解压执行 | `artifacts/test-results/cli-active-shutdown-distribution-win/win-x64/portable/cli/` |

CLI 普通进程套件现包含 55 个案例，Native AOT 分发档额外执行 3 个哈希、原生格式/架构、版本与中英文帮助案例；新 PIN 案例与各载荷的实际执行范围见下文。此前两个隔离宿主验证了文件/文件夹、Unicode、PIN、拒绝、接收 watch、收藏/历史持久化、NDJSON 进度、待处理取消/命令超时、浏览器分享批准/拒绝、活动浏览器上传取消/停止/重启、双向实际 CLI 上传取消/停止/重启及显式新端口，以及宿主并发启动、崩溃恢复和真实 peer 未读 watch 背压。文本、摘要策略、可靠广播和各版取消修复的执行范围分别登记在下文；旧数量报告只证明对应扩展前范围，不能替代之后新产物的验证。CLI 完整矩阵仍缺多项/并发活动操作、集成 CLI 等分支。

分发验证只从本次构建的本地 NuGet 源安装到临时 tool 目录，并解压本次 portable；`run.json` 记录实际可执行文件路径、SHA256、版本、RID、SDK 和 OS。业务断言已从 `Verify-CliTool.ps1` 移入 MSTest，脚本保留安装、解压、启动器检查及调度。Windows ARM64 在现有 x64 runner 上仍只构建，未伪称原生运行。

Core 初始覆盖率基线约 82% 行覆盖，来自 `artifacts/test-results/baseline/core/` 的 cobertura；它不是全仓库、App 或 Native AOT 覆盖率，也不是最终门槛。

共享状态新增 5 个实际行为案例：并发初始化只加载一次、加载失败后重试、取消排队初始化不影响持有者、保存失败传播并可恢复、串行保存快照且保留最新合并设置。保存失败案例按当前共享状态的乐观更新契约断言，未把内存已更新误认为持久化已成功。

## 实际 IPC 与传输取消回归

`HostPipeTests` 新增 12 个实际 NamedPipe 案例，使用生产 Host、协议和命令树，运行时边界替身只控制等待与取消：六个并发请求隔离（两种 host mode）、断连取消、超时响应、Dispose 关闭活动业务/半截读、坏长度/坏 JSON/截断、并发语言隔离、解析错误不调用运行时、退出响应先于宿主退出。集成 mode 的边界案例不是实际 GUI 集成 CLI 验证。

首轮 9 通过、3 失败，见 `artifacts/test-results/ipc-real-pipes-first/`。其中超时案例揭示生产问题：业务处理生成退出码 130，但共享取消 token 同时关闭响应管道，客户端收到 EOF。已分开业务与连接取消，最后响应另有 10 秒写入上限；断连/宿主退出仍向业务传播取消。另两项为测试的坏帧写入时机和实际中文资源措辞校正，未改生产解析语义。12 项定向复验及当前命令套件 136 项通过。

CLI 新增 4 条真实进程流程：

- receiver 待处理取消，未知 ID 不影响当前请求、重复取消返回 3，sender 收到拒绝 4，没有文件或历史
- 发送命令 3 秒超时返回 130、接收请求消失、两端宿主仍 Running，并在相同宿主上再次正常收发
- 浏览器上传的错误/缺失 PIN、错误及重复 token，CLI 接受后的字节/哈希、单份文件与单条历史
- 浏览器请求拒绝和停止 web receive 后，新请求被拒绝，但 LocalSend 服务继续运行

首轮 3 通过、1 失败，见 `artifacts/test-results/cli-cancel-web-first/`：超时发送后 CLI 缓存仍显示已失效请求。Core 到达流只发布新增，没有移除通知；新增只读 `GetPendingIncomingTransfers()` 快照，并让独立运行时按“先取缓存、后取 Core 快照”的顺序核对，避免误删同时到达的新请求。公共 API 是明确的加法变更，已记录 XML/API 文档及规范表面哈希 `DE870C4D4753DC96816E01C798F9444D3AE0DBC637260556114B2EBFD77E411D`；未改协议 DTO 或发布包版本。

Core 增加 4 个参数化快照案例（拒绝、HTTP 断连、停止、决策超时），既有正常上传案例增加接受后快照为空的断言。首轮停止案例误期待必定 403，实际返回 408；停止同时取消等待与拒绝决定，两种终止分支都合法，测试要求 403/408、无接受会话、无文件和空快照，没有更改业务停止契约。首轮报告保留于 `artifacts/test-results/core-pending-snapshot-first/`。修改后 Core 69、Application 24、CommandLine 136、JIT CLI 20 项均通过且无跳过；跨平台 Release 方案构建及格式核验通过。

`Verify-CliTool.ps1` 可传 `-ResultsDirectory`，在重新验证新产物时保留旧运行证据，不将之前 19 项的报告覆盖为新产物结果。

本轮包含 IPC/待处理快照修复的 AOT 产物位于 `artifacts/test-artifacts/cli-pending-sync-win-x64/`，selector 与 RID 包均来自本次本地构建。NuGet 本地安装载荷及 portable 解压载荷各执行 23 项，全部通过、无跳过；两个 `run.json` 的实际/预期 EXE SHA256 均为 `3D4006D89214F0C558395D3780A007092BFF9DA7471BA26B958913EEA8AB86D5`，归档 ZIP SHA256 为 `8735644DF48EFCE2FBE1A7DBE8A230001962677A6CFAC5A6A3E74F7B8CD204A6`，安装 SDK 为 10.0.400。修复前两份 19 项报告仍保留于 `artifacts/test-results/cli-distribution/win-x64/`，不能用其替代最新产物证据。此次仅本地构建和临时 tool 安装，未推送 NuGet、GitHub Release 或 winget。

## 独立宿主与活动上传新增回归

新增 6 条真实 CLI 进程案例：6 个首次命令并发复用同一 owner 并拒绝竞争 daemon；杀死测试自己创建并持有句柄的 daemon 后，TCP 端口可重新独占绑定，自动启动恢复配置及身份；sender 按自身进度 ID 取消等待请求；receiver 在浏览器上传已经报告实际字节进度时分别取消、停止、重启。后三项要求临时文件、待处理请求和历史均清理，再用相同宿主成功收发新文件，避免只验证取消输出。

上传使用真实 HTTP 请求，声明 8 MiB、提交 64 KiB 后按取消 token 阻塞；不用固定睡眠猜测活动状态，也不分配整个大文件。进度观察直接读取被测 CLI 的 NDJSON，没有复制生产解析器。清理及故障操作只作用于测试自己的进程、profile 和临时端口。

首次宿主 2 项通过，报告为 `artifacts/test-results/cli-owner-fault-first/`。活动取消首轮 1 通过、3 失败（`cli-active-cancel-first/`）：断言错误地期待 `Receiving`，Core 的实际契约为 `direction=Receive`、`state=Transferring`；只校正断言，未改生产语义。校正后 4 项及 Windows 全套 26 项通过，无跳过；首轮失败报告保留。

## Linux 首轮运行与监听器回归

在已有 WSL2 Arch Linux x64 中运行，不改全局 SDK 或 NuGet 配置。匹配的 .NET SDK `11.0.100-rc.1.26425.128` 下载到自有 `/var/tmp/tonarink-tests.3IgGtX/`，按微软发布元数据验证 SHA512 后解压；profile、包缓存和测试目录均独立。最初使用 `/tmp`，WSL shutdown 后其 tmpfs 内容消失，因此改用持久的 `/var/tmp`。SDK 首次使用生成了一个 localhost 开发证书，经创建时间及证书元数据核对后仅将该证书移到自有诊断目录保留，未清空用户证书库；后续禁用 SDK 自动生成开发证书。

源代码快照包含当前工作区的已跟踪及非忽略未跟踪文件，不冒充已提交版本。以下关键文件与 Windows SHA256 相同：`CliHost.cs` 为 `2FCC39FAD5D2FCB974F72F07CA58C0260C6E95D79661064EA15A30588BEDD1DF`；`HostPipeTests.cs` 为 `596E79268830FAF99630F0E9BAB08BBCD3FE3CE0299ABB950D6E6F91B88005A9`。使用相同生产 SDK、MTP 参数及套件，不将 WSL Arch 等同于 Ubuntu/macOS/ARM64 CI 或原生 AOT 分发。

首轮 Core 69、Application 24 通过；CommandLine 136 项中 135 通过、1 失败。截断请求关闭后，下一条正常请求被 Unix socket reset，定向复验再次失败。新增连续 32 次截断与正常连接交替的回归，在旧实现上也失败。三份失败报告保留于 `artifacts/test-results/linux-wsl-owner-active/` 的 `commandline/`、`ipc-corrupt-confirmation/` 和 `ipc-rapid-before-fix/`。

根因是请求处理可能同步完成并释放最后一个 NamedPipeServerStream，而下一实例尚未创建；Unix 共享监听 socket 随最后引用释放，排队连接随之重置。这与 [该 SDK 对应的 .NET Unix NamedPipe 实现](https://github.com/dotnet/runtime/blob/v11.0.0-rc.1.26425.128/src/libraries/System.IO.Pipes/src/System/IO/Pipes/NamedPipeServerStream.Unix.cs) 的共享引用生命周期一致。生产监听循环现在先创建下一实例再分派当前请求，退出时释放等待实例，保留 CurrentUserOnly。未加客户端业务重试掩盖失败。

修改后 Windows 和 Linux 的 CommandLine 各 137 项通过、0 跳过；Linux 的 CLI 26 项及 portable Mobile 4 项通过。Linux 报告已复制到 Windows 工作区 `artifacts/test-results/linux-wsl-owner-active/`，未覆盖原始失败报告。

额外执行 Blazor 时，首轮 7 通过、1 失败；定向复验和资源诊断再次失败。`SimplifiedChinese` 已保存，直接 ResourceManager 查找中文标题成功，但实际组件标题仍为英文。生产基类此前只在初始化或状态变化回调中设置文化，异步排队的渲染可能继承不同的执行上下文；[微软同步上下文文档](https://learn.microsoft.com/en-us/aspnet/core/blazor/components/synchronization-context) 也明确提示外部事件与文化上下文风险。现在在每次资源查找时按当前选择设置文化，并在 finally 恢复原文化，不设置进程全局默认值。测试继续通过生产 Fluent 事件保存语言，另从后台更新无关设置、切回英文，验证同一组件的刷新，不在测试中直接调用生产文化方法来强制成功。Windows 与 Linux 的 8 项各通过、无跳过；Linux 的 `blazor/`、`blazor-language-confirmation/`、`blazor-language-diagnostics/` 失败报告均保留，修复报告为 `blazor-fixed/`。这不是浏览器或 MAUI 设备 E2E。

| Linux WSL Arch x64、Release | 通过/失败/跳过 | 报告子目录 |
| --- | --- | --- |
| Core | 69 / 0 / 0 | `core/` |
| Application | 24 / 0 / 0 | `application/` |
| CommandLine（监听器修复后） | 137 / 0 / 0 | `commandline-fixed/` |
| Core.Mobile portable | 4 / 0 / 0 | `mobile/` |
| 独立 CLI JIT 进程 | 26 / 0 / 0 | `cli/` |
| Blazor（资源查找文化修复后） | 8 / 0 / 0 | `blazor-fixed/` |

包含监听器修复的新 Windows AOT 产物为 `artifacts/test-artifacts/cli-listener-lifetime-win-x64/`，selector、RID 包和 portable 均本地新建。安装载荷及解压载荷各 29 项通过，0 失败、0 跳过，验证脚本退出 0；EXE SHA256 为 `8C7681011E37205BC1C3ED4A819484837513F83C15C8298668389314A9134245`，ZIP SHA256 为 `A84B2194F3E2C16AEA1A1816F96DD0DEB219AF65DC37149FA8720CE229B09EC7`，安装 SDK 10.0.400。修复前 29 项报告保留于 `cli-owner-active-distribution/`，不充当最新代码证据。随后继续完成下面的 Linux AOT 和辅助器负向验证。

## CLI 目录、历史和已知设备业务扩展

新增 4 条真实进程案例，全部使用两个独立 profile 的生产 CLI，不用运行时替身：

- `RelativeReceiveDirectoryUsesCallerWorkingDirectoryWithoutChangingSavedSettings`：相对覆盖目录按接受命令的 caller cwd 解析，验证 Unicode 文件字节、结果路径、历史大小/路径；默认接收目录不落盘，宿主退出重载后保存设置仍未改变
- `InvalidReceiveDirectoryAndUnknownDecisionLeaveOfferAvailableForValidAcceptance`：文件占用目录位置时返回 1，未知接受/拒绝 ID 返回 3；不创建未知请求目录、不破坏障碍文件、不决定真实请求，随后可接受同一请求；重复接受/拒绝返回 3 且只写一条历史
- `HistoryRemovePersistsOnlySelectedDeletionAndNeverDeletesReceivedFiles`：接收两项，未知/重复历史 ID 返回 3，单项删除后另一记录原样保留；宿主重载后删除仍生效，两份已收文件内容不变
- `DevicesListsDirectlyProbedPeerIdentityAndCanSendUsingItsFingerprint`：通过真实直连探测添加设备，列表包含该节点的名称、指纹、Headless 类型、地址与协议版本；删除收藏后仍能按已知指纹完成传输。没有把这一案例称为真实多播发现

首轮定向 4 项为 3 通过、1 失败，保留 `artifacts/test-results/cli-destination-history-devices-first/`；设备断言误设 Desktop，而生产 StandaloneSettings 的默认值是 Headless，已修正测试假设，没有改动生产配置。之后 Windows 当前 JIT CLI 完整 30 项通过、0 失败、0 跳过，报告 `artifacts/test-results/cli-business-expansion-win/cli/`，其中四项业务案例各自都有 Passed 结果，非仅最低数量门槛通过。测试源文件 Windows/Linux 快照 SHA256 均为 `E28385451192CDB3DBDB69F8B5BB61971386D6BBDFEE8625EA2E7BBFFE50DBDB`。

普通脚本与 CI 最低执行数量同步为 30，原生档为 33。JIT 完整串行耗时 4 分 36 秒，已将 CLI 套件总 deadline 从 5 分钟调为 8 分钟，为新增案例和 CI 波动留出余量；四条新案例各自的 60 秒 timeout、子进程 deadline 与资源清理限制保持不变，不增加业务重试。

Linux 分发调用首轮因外层 WSL shell 的 PATH 传递未让 PowerShell 找到 `dotnet` 而退出 1，发生于 MSTest 启动前；没有生成通过的 TRX。改为 `wsl -- env` 显式传入私有 SDK 和运行环境后，继续使用原本已构建的相同 AOT 包，不重建载荷。安装工具与 portable 各 33 项通过、0 失败、0 跳过，验证脚本退出 0；报告和实际路径/哈希/环境已复制到 `artifacts/test-results/linux-cli-business/linux-cli-business-distribution-fixed-env/linux-x64/{tool,portable}/`。两者 ELF SHA256 仍为 `9D8C787751A744196F11C76D475EA97B530F50F894EBB3025AC5482F71049EC9`。没有补 chmod 或重跑失败的业务案例。

Windows 也重新用 SDK 10.0.400 从原本的本地包临时安装并解压 portable，两份载荷各 33 项通过，验证脚本退出 0。EXE SHA256 仍为 `8C7681011E37205BC1C3ED4A819484837513F83C15C8298668389314A9134245`，未重建、替换或发布包。Linux 私有工作区的生产 JIT CLI 重新构建成功后，完整 30 项也通过，报告已复制到 Windows。逐一读取六份 TRX，确认四条新增业务案例均存在且 Passed，各套件无失败和跳过：

| 环境/形态 | 通过/失败/跳过 | Windows 工作区报告目录 |
| --- | --- | --- |
| Windows x64 JIT | 30 / 0 / 0 | `artifacts/test-results/cli-business-expansion-win/cli/` |
| Windows x64 AOT tool | 33 / 0 / 0 | `artifacts/test-results/cli-business-distribution-win/win-x64/tool/cli/` |
| Windows x64 AOT portable | 33 / 0 / 0 | `artifacts/test-results/cli-business-distribution-win/win-x64/portable/cli/` |
| WSL Arch x64 JIT | 30 / 0 / 0 | `artifacts/test-results/linux-cli-business/linux-cli-business-jit/cli/` |
| WSL Arch x64 AOT tool | 33 / 0 / 0 | `artifacts/test-results/linux-cli-business/linux-cli-business-distribution-fixed-env/linux-x64/tool/cli/` |
| WSL Arch x64 AOT portable | 33 / 0 / 0 | `artifacts/test-results/linux-cli-business/linux-cli-business-distribution-fixed-env/linux-x64/portable/cli/` |

本轮只扩充测试与入口门槛、文档，没有修改生产 CLI/Core。格式验证通过，当前六轮完成后没有重启任何仍在运行的测试。结果不替代 macOS/ARM64、Ubuntu CI、主 App 集成入口或整体计划验收。

该轮审计还确认文本接收的可用性缺口：Core 会直接接受带预览的单项文本，不写 `message.txt`，当时 CLI 只输出元数据而未输出文本内容。该发现随后由下面的真实内容回归复现并修复；该轮 30/33 项报告不证明文本内容正确。

## CLI 文本内容回归

首先新增 literal/stdin 与空/非空文本的 4 条实际进程案例，旧实现全部失败：传输已经 Completed，但接收响应缺少 `text` 字段。首轮失败保留于 `artifacts/test-results/cli-text-before-fix/`，不是未生成文件导致的测试误判。

生产 CLI 在 incoming 和单项文本完成接收的 `data.text` 中提供内容，空字符串与缺失字段分开；仅接受单项 `text/*` 且有 preview 的内容，不将图片 preview、多项文件或无 preview 的文件当成文本。终端中英文标签与原始内容由客户端格式化，IPC/NDJSON 的 message 仍只放元数据，避免大文本重复占用 4 MiB 帧。失败、拒绝和取消不输出已接收内容。没有更改 Core 公共 API、文本不落盘或不写文件历史的契约。

普通进程套件新增 11 条实际行为案例：

- literal/stdin 各自的空/非空文本（4 条），逐字符比较引号、emoji、CRLF/LF、首尾空白及 UTF-8 字节数
- 60 万汉字 stdin（1 条），完整通过真实传输与 IPC，要求 message 不重复内容
- 拒绝/receiver 取消（2 条），请求清理、无接收文件/历史、无接受内容输出
- 英文/中文终端（2 条），完整内容写 stdout，不进入 stderr
- 普通/自动接受 watch（2 条），incoming 只报告一次；仅 auto-accept 输出 completed transfer，完整内容一致

旧文件/文件夹案例另增加不含 `text` 字段的断言。专项中间 8 项通过报告保留于 `cli-text-fixed-targeted/`；最终 11 项通过于 `cli-text-final-targeted/`，不以中间版本报告替代最终产物验证。命令契约另增加单项文本、空文本、图片 preview、无 preview、多项共 5 条边界案例；它们使用运行时边界替身，不冒充真实网络案例。Windows/Linux 各完整 148 项通过，5 条新投影结果均已逐项核对。

Windows 和 WSL Arch x64 都从最终源码新建 AOT 载荷，同时生成 RID NuGet 与 portable；只从本地源临时安装指定包。Windows 先构建控制器再用 `Verify-CliTool.ps1 -NoBuild` 执行，避免与 JIT 进程测试并行时覆盖被测 DLL。Linux probe 的标准路径缺少 assets 曾使 `--no-restore` 构建报 NETSDK1004；只还原并构建测试 fixture 后继续，无失败测试被重试到绿色。

| 环境/形态 | 通过/失败/跳过 | Windows 工作区报告目录 |
| --- | --- | --- |
| Windows x64 CommandLine | 148 / 0 / 0 | `artifacts/test-results/cli-text-classification-win/commandline/` |
| WSL Arch x64 CommandLine | 148 / 0 / 0 | `artifacts/test-results/linux-cli-text/linux-cli-text-classification/commandline/` |
| Windows x64 JIT | 41 / 0 / 0 | `artifacts/test-results/cli-text-full-win/cli/` |
| Windows x64 AOT tool | 44 / 0 / 0 | `artifacts/test-results/cli-text-distribution-win/win-x64/tool/cli/` |
| Windows x64 AOT portable | 44 / 0 / 0 | `artifacts/test-results/cli-text-distribution-win/win-x64/portable/cli/` |
| WSL Arch x64 JIT | 41 / 0 / 0 | `artifacts/test-results/linux-cli-text/linux-cli-text-jit/cli/` |
| WSL Arch x64 AOT tool | 44 / 0 / 0 | `artifacts/test-results/linux-cli-text/linux-cli-text-distribution/linux-x64/tool/cli/` |
| WSL Arch x64 AOT portable | 44 / 0 / 0 | `artifacts/test-results/linux-cli-text/linux-cli-text-distribution/linux-x64/portable/cli/` |

逐一读取六份 CLI TRX，确认 11 条新增文本结果均存在且 Passed；两种 native 形态的 `run.json` 实际哈希均与包内预期哈希一致。Linux portable 原始解压即可执行，未补 chmod。脚本/CI 最低数量更新为 CommandLine 148、普通 CLI 41、原生 CLI 44，CLI 总 deadline 仍为 8 分钟；没有增加失败业务重试。

新产物位于 `artifacts/test-artifacts/cli-text-win-x64/` 与 `cli-text-linux-x64/`：

| 产物 | SHA256 |
| --- | --- |
| Windows 两种形态共有 EXE | `EF20C5D943A00057F140492D0DB3C5D1E613B1259343919A67DB7D15D8199A09` |
| Windows portable ZIP | `0557DDBAB4538015614D638DF70040A59C7140ABEEE9A5272ED1E2DB5C3074EC` |
| Windows RID nupkg | `6A832494A975E506D0D7D574C2B7539963CF711B6A2E46498FB9959B44DF8BF7` |
| Windows selector nupkg | `F9D59F3CEC405439316BCCD942DAB790052FD966C53CFFA5209E5F09FBE90FD3` |
| Linux 两种形态共有 ELF | `1AA8FFAD05614B642230E8787E2D2185B29EC6DE8BBD85A87DB0C01A51212758` |
| Linux portable tar.gz | `B409D9E80FD7D1739700A455B4F6E1D71C7A9F2B949A583E1741975E3755B88C` |
| Linux RID nupkg | `3C063592E0619E26DFE422452306C8C7128CCD79BFD66E31F60242489508E632` |
| Linux selector nupkg | `4190B3959FDD9F1D9EB0B1E9D66FCF1676EA91C6529DD7C23B95335612749949` |

Windows/Linux 同步源码 SHA256 已核对：`CliClient.cs` 为 `67DDF2569D4E33FC67D4BF80CE1DC7E6A1D70DE14CE93A2F91A0DFF37DBCAE4F`，`CliProtocol.cs` 为 `D20642137202B76633002FFF944FB6F380095D4E742538A6EBC9E1FFD75FE218`，`CliTransfers.cs` 为 `EB23A8CE703C96E10E013F9087163C8230BDFD3F55BE383CBC310B43A0DDCE13`，文本进程案例为 `F2DEC77764DC44E0B04469216658F4168255D0880459DC13AB70947A61C65AF0`，命令行为案例为 `E79DE6FB93DDD238069B3A21FD3A99CBD792500784F5149A53A4C26D83159C99`。仍使用本地版本 `1.1.0-preview.1`，没有发布、推送或覆盖生产安装；完整 P3 和其余平台仍未完成。

## 接收摘要策略与可靠广播生命周期

`ReceiveChecksumProcessTests.ReceiveChecksumPolicyControlsRealUploadWithoutDisablingLengthChecks` 新增四个真实进程场景：默认校验拒绝错误 SHA-256；`receive accept --no-checksum` 接受相同错误摘要但不修改保存设置；保存 `receive-checksums=false` 后动态关闭校验；带 `--no-checksum` 的长度不符上传仍被拒绝。使用自有客户端证书，通过真实 TLS 向隔离 CLI 的标准 prepare/upload 端点上传，并按 CLI status 的 receiver 指纹固定验证服务器证书；不调用 Core 内部客户端或绕过 CLI 接受命令。

每项断言 HTTP 状态、CLI 退出码与失败原因、完整文件字节、历史数量和大小、pending/临时文件清理、settings get 与实际保存文件，以及同一宿主随后接受正确摘要。命令级 override 和持久设置分开验证，skip 不是禁用长度检查。

首轮 Linux 四项通过；Windows 四项都在测试客户端 Schannel 获取凭证时失败，尚未进入上传。报告分别保留于 `artifacts/test-results/linux-cli-checksum/linux-cli-checksum-first/` 和 `cli-checksum-first/`。测试证书改为与生产身份相同的导出/载入 PKCS#12 路径后，Windows 四项通过，报告 `cli-checksum-certificate-loaded/`；没有修改产品 TLS/摘要规则。最终案例 Windows/Linux SHA256 均为 `600C2B68727E94430DB95F167F8384E0F0DB1126553EC4537E2289BFCB8B0A32`，格式核验通过。

完整 JIT 扩展轮 Windows/Linux 各 45 项通过、无失败/跳过，报告为 `cli-checksum-full-win/cli/` 与 `linux-cli-checksum/linux-cli-checksum-full/cli/`。AOT 继续使用上一节文本修复载荷，未替换内容：两边 tool 及 portable 各 48 项通过；Windows 为 `cli-checksum-distribution-win/win-x64/{tool,portable}/cli/`，Linux 为 `linux-cli-broadcast/linux-cli-checksum-distribution/linux-x64/{tool,portable}/cli/`。最低数量同步为普通 45、原生 48，总 deadline 保持 8 分钟。这些载荷早于下面的广播修复，不能充当最新 Core 的原生证据。

`BroadcastLifecycleTests` 首轮三项为 1 通过、2 失败（`core-broadcast-lifecycle-first/`）：解除已满订阅仅移除字典条目，已捕获频道的 publisher 仍阻塞；Complete 唤醒 publisher 后抛出 ChannelClosedException。失败任务由测试自己的取消 token 有上限地结束，没有留下永久等待任务。

生产 `BroadcastHub` 现在在订阅 finally 中关闭自己的 writer，可靠发布跳过关闭频道并继续给剩余订阅者发送；调用者取消仍传播。第四项验证慢订阅退出后其他订阅仍收到事件。四项覆盖取消发布保留缓冲、解除订阅释放背压/新订阅可用、剩余订阅继续收到事件，以及完成/排空/晚订阅。专项四项和完整 Core Windows/Linux 各 73 项通过，无失败/跳过；报告为 `core-broadcast-lifecycle-fixed/`、`core-broadcast-full-win/` 与 `linux-cli-checksum/linux-core-broadcast-full/`。

Core 修复先用独立 `--artifacts-path` 构建测试及生产引用，未覆盖仍在执行的 CLI 二进制；公共 API 哈希仍由完整 Core 套件校验，公开表面未变。广播源码 SHA256 为 `BC8B04AB4A84F7BC797956DE6360FD2A0DCA7457D1567229568D1C665789CB66`，四项案例为 `FF948E742801F08B5575D767AB2A110FE76C6CF46F00DEA4CFE9F29608C649B6`；Core 最低数量更新为 73。这是生产广播实现的验证，不等于全部公共 Node 订阅/状态流组合已覆盖。

包含广播修复的新 Windows/Linux AOT RID/portable 和 selector 已构建并保留到 `artifacts/test-artifacts/cli-broadcast-win-x64/` 与 `cli-broadcast-linux-x64/`。四种真实原生运行各 48 项通过、0 跳过，报告为 `artifacts/test-results/cli-broadcast-distribution-win/win-x64/{tool,portable}/cli/` 与 `linux-cli-broadcast/linux-cli-broadcast-distribution/linux-x64/{tool,portable}/cli/`；run.json 的实际载荷哈希与期望包载荷一致。

| 最新广播修复产物 | Windows x64 SHA256 | Linux x64 SHA256 |
| --- | --- | --- |
| tool/portable 共用原生载荷 | `3A66436E7FBD1360D287252B74ED9E4D36260FA7D671001E89608CE477C0BFBB` | `B81615DFA3C6FBCC21FE07DD6BD3A4D39F86F4AA17C6F725889BD9A20851CDFA` |
| portable ZIP / tar.gz | `82327499160D9E8A3300FC82AEFD7F97224E75AEA64873816F149AEDC5892281` | `9355AE3DE86F99340BBB69CD5D37B2039A93F1C61D32D372CC944E2BCA7C5651` |
| RID nupkg | `67881FC6DF8367718A54D284880C47E7848961D83D7834ACF642EBAE0E1DAA1A` | `59D0D4B80EBA1E3D8D24C72F9694E903C6A77A5A9FE42FCE9A6A7C9B121D5ECF` |
| 本地 selector nupkg | `CF63D4EFE30D46EC7C3168C6DAB5E685D8371028F5C123839B41CF54976EE676` | `246CAD5DDB4C20D1F0E791E0E61473F160C113BB0D8E65D520BC1A5512820890` |

普通 JIT 最新复验 Windows/Linux 各 45 项通过、0 跳过，报告为 `cli-broadcast-jit-win/cli/` 和 `linux-cli-broadcast/linux-cli-broadcast-jit/cli/`。selector 打包后原输出没有 apphost，此前两次普通调用在 Resolve-Path 阶段失败，没有启动测试或产生 TRX；显式以 `PackAsTool=false, PublishAot=false, UseAppHost=true` 重建本地 JIT 入口后才执行这一轮，没有回退到预装工具。JIT 只是测试形态，分发仍为 AOT。

共享业务复验不能报告为最新 Core 全绿：早先使用 `BuildProjectReferences=false` 的 Windows 24 项报告中，测试目录 Core DLL 与当前生产输出哈希不一致。完整重建引用后确认二者 SHA256 同为 `97AF53E064C1DAA67C88C6BB7A4B1D0D1DEE0FBBFDBEC0F3D613E97D245903E0`，新一轮为 23 通过、1 失败。`SendWithoutRequiredPinReturnsOutcomeAndSucceedsWithPin` 首次 PIN 结果正确，第二次发送在 HTTPS 路径出现“目标设备已离线”，随后 HTTP fallback 报 ResponseEnded；报告 `broadcast-application-current-win/application/`。生产 `ReplaceDevices` 的 clear/填充与发送查询并发这一方向需要独立回归和修复；不因以前 24 项绿色宣布解决。未发布、提交、签名或安装到生产 App 身份。

## 真实 MTP 故障与报告审计

新增 `tools/testing/Tonarink.MtpProbe`，与仓库相同的 MSTest.Sdk 4.4.1 / MTP 2.4.1，由 CommandLine 套件构建引用和完整依赖复制；不是产品测试套件，也不计 fixture 自己的案例为覆盖数量。标准 MSTest test parameter 传入本次 nonce 租约，故障宿主无租约不执行破坏操作。父套件八项验证成功、真实断言失败、真实宿主自终止、配合取消的等待、不配合取消的等待、非匹配过滤器零测试、最低执行数量未满足、全部跳过的 strict 策略。

首轮 Windows 为 2 通过、5 失败（`mtp-controller-first-win/`）：MSTest 调整了工作目录，nonce 查找未进入故障；改用标准 test parameter 指定绝对租约路径。第二轮 Windows/Linux 各 2 通过、5 失败（`mtp-controller-lease-win/`、Linux `linux-mtp-controller-lease/`）：错误地把 MTP controller PID 当成实际 test host PID。已用 nonce、MTP 实际启动/退出日志和独占租约释放验证宿主，不重新查找或杀死数字 PID。

第三轮 Windows/Linux 各 6 通过、1 失败（`mtp-controller-host-win/`、Linux `linux-mtp-controller-host/`）：不配合取消的无限等待不会因 `--timeout 3s` 立即退出，最终命中父控制器 25 秒硬限时。原失败保留，拆成两个独立场景：配合取消使用 TestContext.CancellationToken，必须在 MTP 内非零完成，父控制器 cap 不得代替；不配合取消单独验证 10 秒外层硬限时、自有进程树退出及租约释放，并在 controller.json 明确记录 hardDeadline，不伪称 MTP 自行结束。

Windows 专项八项通过，完整 CommandLine Windows/Linux 各 156 项通过、0 跳过；初次完整报告为 `mtp-controller-full-win/` 和复制来的 `linux-mtp-controller/linux-mtp-controller-full/`。之后强化取消因果断言：实际测试体写出含 nonce/PID 的 `probe-cancelled.txt`，同时要求 MTP 日志确认 session 以取消退出 3 结束；仅命令参数中存在 timeout 字样不算证据。通过统一入口再次完整运行，两边各 156 项通过、0 跳过，最新报告为 `mtp-controller-cancellation-win/commandline/` 与复制来的 `linux-mtp-controller/linux-mtp-controller-cancellation/commandline/`。最终父案例 SHA256 为 `B9017D7F10920510E31865C55D9B88444B779C393D356E82A45FDCB5304595DC`，fixture 为 `FD8A5ACA4958FA9C4DA8C11E22824CA81A88591ADC747E93611DA7CEF65DA82F`；跨 OS 源码一致，格式核验通过。

真实子 TRX、stdout/stderr、身份与哈希、同步诊断均作为 MSTest 附件归档；故意失败的子 TRX 改为 `.trx.txt` 附件，避免被 dotnet test/CI 合并成顶层测试结果。断言失败退出 2，strict 零测试及全跳过退出 8，显式最低数量退出 9；崩溃和配合取消均非零。Windows/Linux 配合取消时内部 session 已以 3 结束，但报告通道随后产生 broken-pipe，外层 MTP 返回 7；保留原始诊断，不规范化为 0，也不称为报告完整的正常结束。最低数量更新为 156。

上述八项审计只针对普通 MTP 测试模块与自有进程辅助器；P0 尚未完成。后续统一入口硬限时见下一节，WinUI/WinApp 控制器故障/附件、交互 runner 和最终 App 包仍需验证，不能用普通 MTP 成功代替。

## 设备缓存快照与统一入口硬限时

`DeviceSnapshotTests` 控制生产 `ReplaceDevices` 的枚举边界，发送仍访问两个实际 HTTPS runtime 的真实证书身份。两个行为为：新快照尚在枚举时发送仍使用旧完整快照；枚举抛错不清空已经发布的设备。快照发布后，继续使用该设备发送文件，断言双方 Completed 和实际落盘内容。生产方法仅增加限定测试程序集的 internal 可见性，不新增公共 API，也没有复制缓存实现或绕过实际网络。

第一版测试错误地用运行时类型断言 C# union case，失败保留在 `application-snapshot-first-win/`；改为 case 模式断言后，旧生产实现两项均准确复现“目标设备已离线”，报告 `application-snapshot-repro-win/`。生产改为构建完整字典后原子发布，缓存与业务状态的发布串行化；地址发送直接使用已探测的 peer，不再经过可能被另一个刷新替换的缓存重新查找。

第一次修复验证中的文件与空活动列表断言不符合现有单文本预览及准备进度契约，失败保留在 `application-snapshot-fixed-win/`。回归改用实际二进制文件并断言没有完成的传输，不修改生产文本/进度语义。最终 Windows/Linux 专项各 2 项、共享业务全套各 26 项通过且无跳过，报告 `application-snapshot-file-win/`、`application-snapshot-full-win/application/`；Linux 已复制至 `linux-snapshot-controller/linux-application-snapshot-{file,full}/`。

两端源码 SHA256 一致：生产 runtime 为 `F7712C61A829AC6F760186A35D6ACE7D177D22E6F82C5A606796C3FB4A153290`，回归案例为 `8B41AD8A54513FB722883CE5681FFBE6180F84D49B3D7EDE2B73AF585E70076D`。Windows 测试目录实际 LocalSend DLL 与生产输出同为 `E7E0ABA19EDEEE3E78480A5E9C704CBF810EBC949F93F08F291B042BD9D2529B`，Core DLL 同为 `97AF53E064C1DAA67C88C6BB7A4B1D0D1DEE0FBBFDBEC0F3D613E97D245903E0`。最低 Application 数量更新为 26。这证明清空/填充竞态已被回归捕获和修复，不据此声称此前每一份时序失败具有相同根因。

`tools/testing/Invoke-TestProcess.ps1` 是薄的自有进程调度函数，统一入口使用它运行实际 `dotnet test`，不是新的断言框架。MTP 协作取消外另设 360 秒硬限时（CLI 540 秒），清理仅使用实际启动的 Process 对象及其进程树，最多等待退出和管道排空各 10 秒。结果保留 native exit code、控制器 outcome、stdout/stderr 和时间；硬限时返回 124，控制器/清理异常返回 125，不规范化为成功。

`SuiteProcessTests` 的 6 项 MSTest 用同一个生产函数启动实际 MTP：成功、断言失败、崩溃、不响应取消、零案例、全跳过。不配合取消的案例必须由函数自身的 10 秒硬限时结束，外部审计的更长 cap 不得代替；nonce/宿主 PID 由真实 MTP 日志与文件核对，结束后实际租约可独占打开。真实子 TRX 作为 `.trx.txt` 附件归档，避免预期故障污染父套件统计。

首轮 Windows 5 通过、1 失败（`suite-controller-first-win/`）：成功用例本身通过，但给所有场景一律 3 秒 MTP 预算导致报告路径结束失败，退出 7 被正确保留。普通场景改为 15 秒 MTP 预算，不配合取消仍为 3 秒；专项 Windows/Linux 各 6 项通过、无跳过（`suite-controller-budget-win/`、复制后的 `linux-snapshot-controller/linux-suite-controller-budget/`）。之后普通场景外层预算设为 25 秒，以留出平台退出/报告余量；不配合取消仍固定 10 秒。最终父案例源码为 `1928307A5534F5ABED56D325DA50F762C1A8B08F2678C9985F9EB81312D7711B`，生产调度函数为 `2844B80A8ACC7E1556D020AFCA3B59D711B651B8EC9CB64C579B53EE9AA3F471`；两端一致。

首次统一入口使用 `-NoBuild` 时撞到默认输出目录的旧 Core 测试：Windows 仅 72 项且两项失败，Linux 仅 69 项；入口正确非零终止，未执行其余套件。报告 `snapshot-suite-controller-full-win/core/` 与 `linux-snapshot-suite-controller-full/core/` 保留，不认定新源码复发。Windows 测试目录 Core DLL SHA256 为 `6A3B933DD6D3D7293CC197D7D2AF79ABB25BE765F8001FF5BA8D918B275CB0BD`，与生产不符；完整重建默认输出后恢复与生产一致。此前 73 项报告来自独立 artifacts 构建，不能假设默认 bin 也已更新。

完整重建 Core 默认输出后，统一入口 Windows 为 Core 73、Application 26、CommandLine 162 全部通过（`snapshot-suite-controller-current-win/{core,application,commandline}/`）；Linux Core 73、Application 26 通过，CommandLine 为 161 通过、1 失败（复制在 `linux-snapshot-controller/linux-snapshot-suite-controller-current/`）。失败发生于旧 MTP 审计的不响应取消场景：最初控制器的 Exited 已触发，但立即独占打开子宿主的租约仍报 sharing violation；当时真实诊断记录 host PID 814，稍后只读进程清单确认其已不存在，没有接管或按 PID 终止任何进程。

增加共用 `ProbeLease`：以实际独占打开租约为结束条件，最多等待 5 秒，仍被占用即失败；不改退出码或重跑业务案例。控制器退出只证明控制器本身，不假定其所有子进程已经释放句柄。旧 MTP 和新调度函数审计均使用此条件。最终 Windows/Linux 完整 CommandLine 各 162 通过、0 跳过，报告 `suite-controller-lease-win/commandline/` 与 `linux-snapshot-controller/linux-suite-controller-lease/commandline/`；Linux 首次失败报告保留。最新父案例 SHA256 分别为 `E9C44AA0B04F99D0B23B7A17F45768165B3F75D8E36DD05DC82439536F25F89F`（MTP）与 `55E90E4B4AD87FCA6D877CA46D6914C9A18A4EF9ABC849423BE3B4A1AB6B7480`（调度函数）；租约辅助为 `0D1E3D215A6913E6FD3FF631C0F52AF5731769C851FCB812AE00971C12A480C6`。最终最低 CommandLine 数量为 162。

另通过统一入口完整引用构建并运行 Windows WinUI 控件套件，38 通过、0 跳过（`suite-controller-winui-current/winui/`）。这是非打包、自宿主控件验证，不含 SystemInput，不代替生产 App E2E 或最终 MSIX。调度函数的故障矩阵仍只在普通 MTP 模块执行，不能据此宣布 WinUI 崩溃/挂起路径审计完成。

CI 的活动测试步骤已改为调用统一入口，避免散落的最低数量/超时参数；YAML 解析、PowerShell 语法与新增 C# 格式检查通过，但尚未在 GitHub runner 实际执行。普通 MTP 全套通过不代替 Windows App 控制器、交互环境、最终产物与快速/完整/发布三档验收。

## Linux AOT 分发与实际安装验证

在同一 WSL Arch x64 环境中，按 [微软二进制归档说明](https://learn.microsoft.com/en-us/powershell/scripting/install/alternate-install-methods) 将 PowerShell 7.6.6 解压到自有目录；下载 SHA256 与官方 release asset digest 一致，值为 `DDBC4A2D113BBD46D283CFEDCBCD117A70CAEFD7673F41F2B4E0000BADF103BC`。从 [.NET 10 发布元数据](https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json) 获取 SDK 10.0.400 的 Linux x64 归档和 SHA512，验证后加入私有 SDK 安装。不安装全局 tool、不修改 PATH 配置文件或系统软件包；已有 clang/gcc/zlib 头文件可用于本机原生编译。

直接运行仓库 `Build-CliPortable.ps1 -PackTool`，一次 AOT 编译同时生成 RID NuGet 与 tar.gz；selector 也在本地生成。首次 `Verify-CliTool.ps1` 已成功用指定 SDK 10.0.400 安装并运行 launcher，但载荷扫描误报 0 项并退出 1，尚未启动 MSTest。原因是 Unix 的 `.store` 为隐藏目录，默认 Get-ChildItem 不递归进入；加 `-Force` 后能在同一个安装目录找到且只找到一份载荷。诊断保留于 `artifacts/test-results/linux-aot-distribution-first/directory-scan.txt`，明确不是一份通过的 TRX。

只修正扫描并再次临时安装，没有重建或替换被测 AOT 包。工具载荷和原始 tar.gz 解压后的 portable 各执行 29 项：全部通过、0 失败、0 跳过，脚本退出 0。portable 解压后模式为 755，验证过程中没有补 chmod。结果与实际路径/哈希/SDK/架构记录位于 `artifacts/test-results/linux-aot-distribution-fixed/linux-x64/{tool,portable}/`。

本次本地产物已保留到 `artifacts/test-artifacts/cli-listener-lifetime-linux-x64/`，SHA256：

| 产物 | SHA256 |
| --- | --- |
| 两个分发形态共有的 ELF x64 载荷 | `9D8C787751A744196F11C76D475EA97B530F50F894EBB3025AC5482F71049EC9` |
| portable tar.gz | `FE704FF73D2EC1FBAD42910E449EE6CB2E4C9B7AFD507EBA937ADB5F11936A06` |
| linux-x64 RID nupkg | `7D14D67C68FFF3747803612A4EAEDF338E23D2CF54F470AA70F5B108EAF360B6` |
| selector nupkg | `A8B5DF1091B2993E7AC5F64156EFFCE24F3312D8D046A0D9C3DD4B21DEF8F2CE` |

上述证明本机 Arch x64 的原生运行及当前脚本，不替代 Ubuntu GitHub runner、macOS、ARM64 或较旧 glibc 环境的验证，也未上传这些包到分发源。

## 进程辅助器负向验证

新增普通 SDK fixture `Tonarink.TestSupport.ProcessProbe`，只生成测试自己启动的成功/非零退出/自终止/等待子进程，不是生产 CLI、测试解析 DSL 或另一个测试框架。CommandLine 套件增加 6 条 MSTest：stdout/stderr 分离、退出 17 原样返回、异常终止不得成功、hang 到期后实际退出、输出观察器异常传播且另一个进程仍存活、观察器的 OperationCanceledException 不得误报成 deadline。fixture 通过项目引用构建，复制真实 GetTargetPath 相邻运行文件，支持独立 artifacts 输出路径；不拿公开旧产物作替身。

首轮 Windows 5 通过、1 失败，报告 `artifacts/test-results/process-runner-negative-first/`：ProcessRunner 无条件把观察器取消转成 TimeoutException，虽然 15 秒 deadline 并未到期。已仅在自己的 deadline token 确实取消时转换超时，其余错误原样传播并清理自有子进程。观察器异常也会在等待退出时及时被观察，避免子进程因 stdout 无人读取而阻塞。

增加独立存活进程断言后，Linux 出现 142 通过、1 失败，保留 `linux-process-runner-witness/`：辅助 fixture 通过重新查找 PID 和比较精确 StartTime ticks 来判断身份，未能可靠停止 witness，finally 等待超时。已移除 PID 重新查找，使用启动者提供的实际 Process 对象和真实 Exited 事件；身份 nonce 仍由子进程 stdout 核验。入口观察发生在开始读 stdout 之前，避免早输出先于身份记录。cleanup 不按进程名或重新查找的 PID 终止其他进程。

修改后 Windows 与 Linux 完整 CommandLine 各 143 项通过、0 跳过；Linux 从独立 `--artifacts-path` 构建 fixture，未依赖复制来的 Windows bin/obj。最新 Windows 报告为 `artifacts/test-results/process-runner-entry-order-win/`；Linux 各次报告已复制至 `artifacts/test-results/linux-process-runner/`，最新子目录为 `linux-process-runner-entry-order/`，首次 witness 失败仍保留。各次失败和复验分别归档，不以最后绿色覆盖首轮失败。此前 Windows 转换 glob 的构建错误 MSB3030 也已修正为实际 TargetPath 目录中的文件枚举；包含 fixture 的 14 项跨平台 solution 在 Windows 标准输出路径及 Linux 独立 artifacts 路径均构建通过。这些是进程辅助器的负向验证，**不代表 MTP、WinUI 或 WinApp 控制器的整体 crash/hang/报告审计已经完成**。

## 已发现的问题与验证约束

- 实际 CLI 进程验证发现 Windows 后台宿主继承调用者管道，前台退出后 stdout/stderr 仍未 EOF；已调整独立宿主的 Windows 启动方式，生命周期及 AOT 工具套件通过
- 初次命令契约测试误把库解析退出码 `1` 当作外部 CLI 退出码 `2`；初次文件选择案例误把生产 `CliException` 的使用错误退出码 `2` 当作 `1`，均按生产契约修正，而非改业务代码迎合测试
- bUnit 首轮缺少 interactive renderer 设置，导航断言也忽略了 Fluent 的桌面/移动两份导航；修正宿主与断言后通过，首轮失败 TRX 保留
- 实际 WinUI 调用 UIA Invoke 会排队，测试改为条件等待回调和重新渲染，不靠固定延时或重复点击
- 初次 native 打包与 JIT 进程测试并行，遇到被测 DLL 锁定而失败；已等进程测试结束后重新构建。后续运行应先构建产物，再启动外部进程测试，不在测试期间覆盖它
- WinUI 构建仍有 MSTest 适配器卫星资源的 PRI263 提示；本机运行通过不等于所有语言/打包配置已验证
- 零测试的严格策略已实际验证为非零退出；完整控制器的故意失败、崩溃及 hang 负向审计尚未补齐
- 加入 Core.Mobile 的源项目后，方案默认构建会要求移动 workload；本机缺少 Android 37 平台而失败。已增加显式 `PortableTransportOnly=true` 范围，跨平台 CI 的 restore/format/build 及 Mobile 测试入口只选 net11.0；手机产品默认目标未改变。本机该范围 restore、Release 方案构建、格式检查通过，未安装额外 SDK
- Application 扩充后两次全套复跑分别出现部分接收等待超时、重启后地址发送的 HTTP 响应提前结束；两份失败 TRX 分别保留在 `artifacts/test-results/final-logic/application/` 和 `artifacts/test-results/final-logic-diagnostics/application/`。已让等待请求同时观察发送方错误，并补充有上限的运行日志。随后全套 24 例及两项定向诊断的 5 轮运行通过，但未复现根因，**不能据此认定间歇性问题已修复**；这是后续稳定性验收的未解决项，CI 未增加失败重试
- CLI 辅助宿主在退出或锁所有权验证失败时保留自有 profile 和日志，不再删除可能仍被占用的目录；无效产物路径在分配临时目录前报错
- CLI 普通进程复跑中，接收 watch 的业务步骤完成，但 `TestCleanup` 立即删除目录出现 Windows 占用错误，首轮结果为 15 通过、1 失败，保留于 `artifacts/test-results/final-cli/`。已让自有目录的异步清理有上限地等待文件系统占用释放，超过 10 秒仍抛错；这是资源释放条件等待，不是重试业务案例或吞掉清理失败。修改后全套 16 例（含清理）通过，结果保留于 `artifacts/test-results/cli-cleanup-verification/`
- 新增 AutomationId 的 `.Set(...)` 写法触发 `REACTOR_POOL_001` 编译错误；改用声明式修饰器后编译通过，但真实控件断言发现动画组件外层 ID 没有到达内部 Button。首轮运行 37 通过、1 失败，见 `artifacts/test-results/automation-id-lifecycle/winui/`。已让 Add/Verify/Delete 接受可选 ID 并在原生 Button 上设置；输入测试不再覆盖生产 ID。重渲染和禁用状态断言加入后，38 例通过，未以源码检查替代实际控件检查

## 主 App E2E 与实际输入进展

`Tonarink.App.E2E.Tests` 已建立为普通 MSTest 外部控制器，不引用生产 App 源码或调用组件事件。当前编写了两个生产用户流程：三轮设置导航往返保持名称草稿和滚动位置；六个 Expander 展开/收起及通知开关让下拉框、测试按钮即时同步。**这两条流程尚未在生产 App 中执行成功**，不能计入通过数量或发布门禁。

控制器要求显式当前 Native AOT layout、完整 manifest、合并的 `resources.pri` 和预期 EXE SHA256。启动前核验 PE/CLR header 和原生架构；启动后计划核验 guest epoch、实际 EXE 哈希、PID/启动时间、包身份/版本及 development-mode。每个 UI 命令指定 `--on sandbox` 并重新检查 epoch；拒绝接管已有 Sandbox，不退回本机生产 App。控制器及格式检查已通过，guest 启动、真实页面断言、guest 证据归档和清理路径仍需在合适环境实际验收。

此前 `NativeAot=true`、`TonarinkPackaged=true` 的 x64 发布成功，EXE 位于 `artifacts/test-artifacts/app-aot-win-x64-current/Tonarink.exe`，SHA256 为 `9ACC79406A4924AD998EEE9142F2C6EA00CD4414A6BDA64E95E7FD7F309AEAB9`。目录名中的 current 不代表始终对应最新工作区：该 App 布局早于本轮 IPC/Core 修复，不能替代之后版本的包验证。补入源 manifest 的 architecture/full-trust 信息及发布 assets 后用于启动前检查；未修改正式包身份、未安装、未签名或发版。该布局已有 `resources.pri`，不能使用仅适用于含 `Tonarink.pri` 的 unpackaged 发布流程再次合并；首次调用 `New-AotMsixResourcesPri.ps1` 正确拒绝缺少 `Tonarink.pri` 的布局，没有生成替代 PRI。

负向运行记录：

- 未指定产物时，发现 2 例、0 通过、2 失败、0 跳过，MTP 返回非零；`artifacts/test-results/app-e2e-missing-artifact/`
- 指定本次产物及正确哈希后，PE/架构/哈希与 WinApp 0.7.0 检查通过，随后因本机没有 `WindowsSandbox.exe` 而失败；发现 2 例、0 通过、2 失败、0 跳过，`artifacts/test-results/app-e2e-sandbox-prerequisite/appe2e/`。没有执行 `winapp run`，正式 App 和用户数据未被触碰
- `SystemInput` 自宿主试验能通过 WinApp 读取实际 UIA 树和 disabled 状态，但真实 click/focus 因 `foreground_not_target` 被工具拒绝；两个失败结果保留于 `winapp-input-pilot/`、`winapp-input-click/`。未关闭前台保护，也未用 UIA Invoke 或直接事件回调替代鼠标悬停/键盘输入

常规 WinUI 与 `WindowsInput` 现在分开调度；前者 38 通过不包含后者。`AppE2E` 也单独调度，部署模式明确为 **loose-development-package**，不能替代真实签名 MSIX 安装/升级或 Store 产物门禁。

## 双 CLI 活动上传取消与显式地址回归

新增 `ActiveUploadProcessTests` 两项真实进程案例，两端都是独立 profile 的生产 CLI，而不是浏览器伪造发送方或 mock 运行时：

- `SenderCancelsActualCliUploadAndBothHostsRecover`：通过仅转发加密 TCP 字节的自有 relay，在一个连接转发 256 KiB 后暂停；不终止 TLS、不解析或替代 LocalSend 协议，其他控制连接仍可通行。两端实际 progress 都必须为 `Transferring` 且 `0 < bytesTransferred < 8 MiB`，接收端必须存在 `.part-*` 文件，才允许发送 `cancel`。验证两端退出码 130/Cancelled、没有已完成项、重复取消返回 3、无临时/目标文件或接收历史、源文件哈希不变，最后用同一对未重启宿主完整发送该文件并核对 SHA-256 和单项历史。
- `ExplicitAddressUsesNewPortInsteadOfCachedEndpoint`：先真实发送到旧端口，再在同一接收 profile 改端口并重启服务，保留证书身份；显式指定新地址发送第二个文件，核对内容、两项历史及设备列表保留新旧地址。

首轮取消流程本身成功，但随后直连恢复失败，见 `artifacts/test-results/cli-active-upload-first-win/`。第二项在旧实现独立复现相同连接被拒绝，见 `cli-endpoint-repro-win/`。原因是 `AddKnownDeviceAsync` 返回合并后的地址集合，CLI 随后选用了排在前面的旧端口；新地址的 probe/register 成功并不保证上传使用它。修复在共用 CLI 命令实现中保留注册与缓存更新，但将本次显式/收藏地址解析结果约束到刚验证的 endpoint；不改变 Core 的多地址保留策略。

修复后 Windows/Linux JIT 专项各 2 项通过，报告为 `cli-active-upload-fixed-win/` 与私有 Linux 工作区的 `linux-cli-active-upload-fixed/`。随后强化断言和清理：核对两端 totalBytes、双方重复取消及空 completed items，并观察所有自有 relay 连接和两个 CLI 调用。普通 CLI 最低数量更新为 47，Native 分发档为 50；旧 45/48 项结果不能证明新范围或此次地址修复。

共享命令套件针对修复后的实现在 Windows/Linux 全套各 162 项通过，报告为 `cli-active-upload-commandline-win/commandline/` 与私有 Linux 工作区的 `linux-cli-active-upload-commandline/commandline/`。普通 MTP 控制器的成功仍不能替代 App、WinUI 控制器或打包集成入口。

源码 SHA-256：`CliTransfers.cs` 为 `AEDFA8CA30868CB3D93D40E6D3141318458FAE2C198D131030730AAF86C3629C`；47/50 项全套使用的活动上传案例为 `E7317E9253C20D1633225D807B0A58CFA32F174E9CD2EB45A9153873D2D23D2C`；不透明 relay 为 `3EB0E05CB9EE30F70C65ACDFA63FFE080EDC7CF7A1DDE8ACDE61EB2CA414738B`。

新的本地 AOT RID 包、portable 和 selector 位于 `artifacts/test-artifacts/cli-active-upload-{win,linux}-x64/`，版本仍为 `1.1.0-preview.1`，未上传或发布。原生载荷分别为 Windows `FEB437B531517F0BAA640D5906B9D016DF0BDB3CD7C5CE44E2809640DEF24BC4`、Linux `B024065BF89FE741B2E2FA236D4876A7031B61FBDB459B6FCD340D81A1ADD7E4`；两边实际 tool 安装载荷与 portable 解压载荷均与本地 RID 包一致。

四个 Native 分发全套均为 50 项通过、0 失败/跳过：Windows 报告为 `cli-active-upload-distribution-win/win-x64/{tool,portable}/cli/`，Linux 报告已复制到 `linux-cli-active-upload/linux-cli-active-upload-distribution/linux-x64/{tool,portable}/cli/`。Windows 两轮约 6 分 14 秒、6 分 13 秒，Linux 两轮约 5 分 32 秒、5 分 30 秒；遵循原有限时，没有重试获得绿色。首次失败、JIT 修复轮与此次 AOT 结果分开保存。

| 产物 | Windows SHA-256 | Linux SHA-256 |
| --- | --- | --- |
| portable archive | `58C41A232FB014BD0BC4B4CA95D784C64E713BF7BDF0958643549EC23CC580FE` | `C2554DBCE6BA12D0169130283729D227B0D076D53DF128B82F6CFE3C5EA01EC1` |
| RID nupkg | `F9FE3808FD257CA11A8BACCEDCFE42435CBC61D1F8FD2B570C7435C3202506AB` | `97DBE480000985092A441FB0A75D21383E8995009C34DBFE21E27EE546A0ACDD` |
| selector nupkg | `AFC2BC4806DD1173C418CB7382C6FB1556FA14968B14B35A462FF4DEFCB4019B` | `D1A82739931F58AC3A09C1821059AA077ACB5AAF541CD3D9EABF46C06FC175D2` |

强化后的 JIT 完整套件在 Windows/Linux 各 47 项通过、0 跳过，报告为 `cli-active-upload-full-win/cli/` 与 `linux-cli-active-upload/linux-cli-active-upload-full/cli/`；该轮同时执行其他隔离套件，本机耗时分别约 7 分 42 秒和 6 分 38 秒，仍遵循原 8 分钟平台 deadline 与外层 9 分钟硬限时，没有为获得绿色提高限时或重跑失败案例。Windows 的共用命令 DLL 在实际 JIT 目录与测试依赖中哈希一致（`EC79DF659283A0AE9FCEA7B4CC86568D29F13979469D47D393954DAECBDC4E44`），Core 载荷为已验证的广播修复版本（`97AF53E064C1DAA67C88C6BB7A4B1D0D1DEE0FBBFDBEC0F3D613E97D245903E0`）。Linux 定向、完整 JIT 及命令套件报告已复制到 `artifacts/test-results/linux-cli-active-upload/`，保留各自 controller/TRX 和产物路径。

全套结束后进一步强化取消案例：活动 send/accept/cancel、重复取消以及取消后的状态、列表、历史、设置和恢复发送都加上 `--no-start`；这样后台宿主异常退出不能靠命令自动拉起新宿主获得绿色。最终案例源码哈希为 `227489FCABA4794B15363D55876F3484EF717795C5C193454FAB155D6F2327DC`。只重新构建测试控制器，未替换生产载荷；针对两条案例在 Windows/Linux 的 JIT、已安装 AOT tool、已解压 portable 六种形态分别执行，全部各 2 项通过、0 跳过。报告为 `cli-active-upload-no-start-win/{jit,tool,portable}/` 和 `linux-cli-active-upload/linux-cli-active-upload-no-start/{jit,tool,portable}/`。这六个定向报告证明最终 `--no-start` 断言，全套报告证明此前未改动的其余案例，不能混称为最后一次运行又执行了 47/50 项。

本批不声明双 CLI 接收方活动取消、活动上传停止/重启、macOS/ARM64、生产 GUI 集成 CLI 或最终 MSIX 通过，相关缺口继续保留。

## 双 CLI 活动上传生命周期与 Core 调用方取消

本批将 `ActiveUploadProcessTests` 的发送方取消扩展为六种活动上传操作：发送方/接收方分别执行 `cancel`、`server stop`、`server restart`，另保留显式新端口回归。取消前两端都已报告 `Transferring` 且字节数大于零、未达到总量，接收目录已有部分文件；原有不透明 TCP relay 只阻塞上传载荷，不解析 TLS。操作后要求两端在 5 秒内返回 130/Cancelled、没有完成项或历史残留，并在同一后台宿主恢复完整收发。恢复命令使用 `--no-start`，禁止自动拉起宿主掩盖崩溃。

首轮 7 项为 4 通过、3 失败，报告保留在 `artifacts/test-results/cli-active-lifecycle-repro-win/`。接收方主动取消或重启时，发送方报告连接关闭失败；停止接收方还使 relay 的后续连接被拒绝。生产修复先通知发送方，再取消本地上传；停止服务也在关闭 HTTP 前通知已接受的发送方。协议与浏览器入口显式区分是否有取消回调，不根据设备名称或类型猜测浏览器的虚构端口。relay 仅将目标停止后的 ConnectionRefused 视为已关闭的自有连接，其余异常及业务断言不放宽。这版定向测试 Windows/Linux 各 7 项通过、0 跳过，但下面追加的 Core 修复之后仍需重新验证最终 CLI 载荷。

Core 新增双向调用方令牌取消案例：生产节点通过真实 TLS 上传一个由测试控制读取暂停的源流，确认两端进度和部分文件后取消 `SendAsync` 或 `AcceptAsync` 的调用方令牌。调用方必须收到 OperationCanceledException，另一端必须得到 Cancelled；还检查源流释放、部分文件与待处理清理、节点仍运行，以及同一节点后续完整文件的 SHA-256。它不替代 CLI 的真实 TCP relay 或浏览器上传案例。

最初的接收结果类型断言不适用于 C# union 的包装类型，已改为匹配具体分支并核对取消项为空；这个断言错误不是生产问题的证明。用修正后的分支断言重新执行旧发送取消顺序，发送方案例仍失败，证据为 `core-caller-upload-cancel-original-observed-win/`，不能将其归因于断言 API。最终调整发送方调用令牌的处理：在关闭上传前发送有界的取消通知；独立观察任务在 SendAsync 的 finally 中取消并等待结束，不遗留后台任务。接收方调用令牌同样先通知；成功、故障、按 ID 取消和节点停止仍保留各自结果分支。

修复后的共享套件在 Windows/Linux 分别执行 Core 75、Application 26、CommandLine 162、Mobile portable 4 项，均通过、0 跳过；报告为 `cli-active-lifecycle-shared-{win,linux}/`。随后加强 Core 案例的失败清理，取消并观察请求订阅、初始收发与恢复收发的全部自有任务，确保断言失败也停止节点。最终 Core 源码两平台全套各 75 项通过，报告为 `cli-active-lifecycle-final-core-{win,linux}/core/`。

最终载荷 Linux JIT 全套 52 项通过、0 跳过，耗时约 7 分 28 秒，报告为 `cli-active-lifecycle-full-linux/cli/`。Windows 的同范围首次全套在原 8 分钟套件期限处中止，48 项通过、0 断言失败，控制器确实非零退出；报告保留在 `cli-active-lifecycle-full-win/cli/`，不能计为全套成功。与 Linux 报告比较，缺少 empty-folder 及三条 Web 流程；六条活动操作全部已经执行通过，所有已完成案例均在原单例期限内。基于扩展后的串行工作量，将 CLI 的套件/外层总限时改为 10/11 分钟，保留原单例 deadline、最少发现数量和严格退出规则；其余套件不变。新预算完整 Windows 运行 52 项通过、0 跳过，控制器正常退出并排空输出，耗时约 8 分 46 秒，报告为 `cli-active-lifecycle-complete-win/cli/`。该结果仍只证明此次停止/释放修复前的 JIT 载荷。

普通 CLI 最低发现数量现为 52，Native 档为 55；这些是当前发现要求，不表示新的原生载荷已经通过。此前 47/50 项报告只证明此前范围与载荷。本批未重建或运行最终 App MSIX，也未证明新版本 AOT tool/portable、macOS、ARM64 或真实 CI runner 通过。

新的本地 Linux x64 AOT RID 包、portable 和 selector 已构建并复制到 `artifacts/test-artifacts/cli-active-lifecycle-linux-x64/`，版本保持 `1.1.0-preview.1`，未上传或发布。本地指定源的实际 tool 安装及英文帮助成功，但 tool 全套 55 项为 53 通过、2 失败：sender stop/restart 的接收方未在原期限内取消。报告为 `cli-active-lifecycle-distribution-linux/linux-x64/tool/cli/`；验证入口正确非零终止，portable 全套没有执行。portable archive、RID 包和 selector 的 SHA-256 分别为 `007D784FA75A97E3FE163F3738184DD223B073E22348C239E9F1A8DC2CAB885C`、`D501D40DCE98F23EEA1C96C59FC2D5B52AE8D5FEBAA05FB732DD4DC22EE7A0CA`、`563A586DC7332F791731E7E5A5A8B911B329AF31BA836FD9EC7A72F52CBAF0CF`。随后修复及新载荷证据见下一节，不覆盖此失败记录。

本次 Windows JIT 的 Core DLL 为 `BBDA2C1ED552332FE9DC877A5815A80E16582FD00E804691D397EF00095F91B4`，与实际 Core 测试目录一致；共用命令 DLL 为 `0A5ABC938D95B9B0D22965D73013A0E8A473B21C36E88CFF6D359B847B55B05B`。Linux 对应 DLL 为 `FDA07AFCC3089D86F0AB17748C1FE67C32A352F6437E22F3FEF5646D3AA04869` 和 `D5B47BEFB0785C4AC19AF368CDA37603BFF78D40773E44EFC60523F8583B5C7A`。JIT apphost 的哈希相同不证明托管载荷相同，因此分别保留这些 DLL 身份。

源码 SHA-256：`LocalSendNode.cs` 为 `26085B05E3CEC38647195F249E76588E12B705C6C1111010BCBC17D3D314A499`；最终 Core 案例为 `A5661C6618143B4E5B9377750DC55139EA541D2828D97BBFDE0D578EF9E37FF3`；CLI 活动上传案例为 `BD55EF5E41F610EE2F07E89268E872CF79FAF880C6FAD1BC75D4A445D2EBF3C4`；relay 为 `1F5C576956D7D13AD50F1874984245F7F7EA567E6950442B534DF209C3ED8147`。

## AOT 活动停止/释放与接收取消意图回归

在同一已安装 Linux Native 载荷上只增加诊断输出，7 项复验为 5 通过、2 失败（`cli-active-lifecycle-native-diagnostics-linux/`）。发送方 stop/restart 返回 130/Cancelled，接收方仍等待，直到测试 finally 关闭自有 relay 才报告 EOF/Failed。没有放宽 5 秒业务 deadline，也没有改成接受接收失败。

停止顺序现在保留已接受发送会话的 endpoint、指纹与取消来源，在关闭生命周期和释放身份之前发送有界取消通知。收到对端 `/cancel` 的发送会话记录来源，结束上传后不向正在停止的对端再次回发取消；本地调用方和按 ID 主动取消仍发送通知。发送体因已声明的取消而关闭，不再误记为普通网络失败。所有发送 token 都已链接节点生命周期，删除重复枚举已可能释放的 CTS 的取消操作。

Core 新增 4 个真实 TLS 案例，分别从 sender/receiver 停止或 Dispose：确认实际上传进度与部分文件、双端 Cancelled、源流释放、待处理/部分文件清理，保留未停止的 peer，在同一 identity 目录与端口重建停止方并完成 SHA-256 一致的恢复收发。首轮夹具错误对已 Dispose 的节点再次 Stop，清理异常遮住了结果；修正夹具后仍有两条接收方 EOF/receive_failed，报告为 `core-active-shutdown-cleanup-corrected-win/`，这是独立的生产问题证据。

接收会话现在在通知 peer 前记录取消意图，并在提交接收失败时与终态提交使用同一把锁。只有已声明取消后发生的 body 失败转成 Cancelled；已经提交的 Completed/Failed 不被改写。追加 4 个单元案例证明保留部分完成项、没有提前关闭 body、未取消的摘要错误保留 code/message/item，以及后续取消不能覆盖已有成功/失败。

取消意图首轮仍暴露回发通知的等待竞争：`core-active-shutdown-intent-fixed-win/` 为 5 通过、1 超时；`core-active-shutdown-intent-diagnostics-win/` 为 3 通过、1 超时。日志明确显示 receiver 已 Disposed、源流已释放，但 sender 仍等待回发 `/cancel`。取消来源修复后原 6 项定向全部通过（`core-active-shutdown-no-echo-win/`），意图单元 4 项全部通过（`core-cancel-intent-unit-compiled-win/`）；没有加长单例期限。

统一入口 Windows 和 WSL Arch Linux 的 Core 83、Application 26、CommandLine 162、Mobile portable 4 项分别全部通过、0 跳过，报告为 `cli-active-shutdown-shared-{win,linux}/`。Linux 新 AOT 载荷 7 项活动操作通过、0 跳过（`cli-active-shutdown-fixed-native-linux/`）；其后本地 tool/portable 各 55 项全套通过，耗时约 6 分 22 秒/6 分 20 秒。Windows 新 Native tool/portable 各 55 项全套通过，耗时约 7 分 13 秒/7 分 6 秒。全部控制器正常退出，报告为 `cli-active-shutdown-distribution-{win,linux}/{win,linux}-x64/{tool,portable}/cli/`，不覆盖上述旧载荷失败报告。

同一当前实现的 Linux JIT 完整 52 项通过、0 跳过，约 7 分 37 秒（`cli-active-shutdown-jit-linux/cli/`）；Windows JIT 活动上传专项 7 项通过、0 跳过（`cli-active-shutdown-jit-targeted-win/`），完整 52 项在 `cli-active-shutdown-jit-complete-win/cli/` 通过、0 跳过，约 8 分 44 秒。两套完整运行控制器退出 0，输出排空。测试构建使用 `BuildProjectReferences=false`，核对生产 DLL 未变化，避免运行中的 JIT 进程使用被重新构建的载荷。Windows 被测 Core/CommandLine DLL SHA-256 分别为 `0B8B81F186DBA527133BC30C83D89380F4CA715ADCC5AB15A6EBC33D1ECCA8B4`、`C093B2FE4A3BECD395D914A8F3C29FD52642796C432340D638A98579693F9C91`；JIT apphost 的哈希不能单独证明托管载荷身份。

本地验证仍使用版本 `1.1.0-preview.1`，未上传 NuGet、GitHub Release 或 winget。以下产物已保存到 `artifacts/test-artifacts/cli-active-shutdown-fixed-{win,linux}-x64/`，两种安装方式的 `run.json` 实际 executable SHA-256 均匹配各自预期值；没有用公开旧包或缓存替代当前构建。

| 产物 SHA-256 | Windows x64 | Linux x64 |
| --- | --- | --- |
| 实际 Native executable（tool 与 portable 相同） | `5735AB3E84CD3655FF3B337B32EA0543316E32521F74F53AB74B4AB98B4CE9B7` | `B032301681DFA4DFE3C747CE8E55B1126D414A6D81BC7723F56DD17F8BD134C4` |
| portable archive | `7421157424A1E61A273A4E9A900441A62344943A21597E3DC514342EB477DE0D` | `073761BCBD8F9908A17731921913A605A230D671AB2AA33B46AA5BC435D35070` |
| RID NuGet 包 | `3DF0EBDA8F14763C212275EB883F03F4C702BBFB175A7A5C910056A7CA572EA7` | `2B3321E3A6AAC3C9A9741E09079614F3CAFCDF66151CC7B73904CBA2E56F4EFB` |
| selector NuGet 包 | `7DB25EC55311A3EAD7B07EEA615CBD6F8CF09BDDFB7679AEA1B6A72C18BBFE74` | `B0C59A0F67D2D7822CAD4EC7274F7FE5AE49715ECA6B66E373049D1549CFB276` |

该验证不证明 macOS/ARM64、Ubuntu CI、集成 GUI CLI 或 App 最终 MSIX 通过。

Windows/Linux 源码哈希一致：`LocalSendNode.cs` 为 `ED441843316622DB1FD73BD0FF403FA56CF3EF240011AFB428EF29E692B15D36`，`IncomingSession.cs` 为 `6BAB24079303859C864B5CE5AF143925BB3758D60740221E16DA9B0B6AA0DE38`，`OutgoingSession.cs` 为 `E15C459F9698DFF006CA60BFD303BB642F52846782386A8FAE1395501E7AF540`。Core 活动回归为 `FE6407FDFD05CF1DEF4E67A7EB5BEB1F2489E1CDBA803862D44F4F1DCFE9BCF7`，意图单元为 `A0A9C0D4845F6A7748C9815D6C2217B2E5C878F48729C7683EC21D7CEA032692`。内部变更未改变公开 API、协议 DTO 或发布版本。

## App 发送辅助逻辑与真实 Windows 文件接口

新增 `SelectedSendItemTests` 16 项：重命名/撤销/重做保持原始身份与实际源文件、UTF-8 字节大小、协议叶名称、空/不存在/取消文件夹、嵌套目录与文件内容。两个 `[UITestMethod]` 调用真实 StorageFile 与 Windows DataPackage/RandomAccessStream，再通过生产节点实际 TLS 重复发送，验证 Windows stream factory 的读取与释放、移除源文件后的 SourceIo，以及图片字节不变。不修改 OS 剪贴板，不调用 Core 内部流工厂，也不复制生产实现。

专项 16 项通过（`app-selected-send-first-runtime-win/`），全部非 SystemInput 案例 54 项通过（`app-selected-send-complete-win/winui/`）。停止/释放修复后重新完整引用构建，54 项再次通过、0 跳过（`app-selected-send-shutdown-core-win/winui/`）。初期编译访问 Core internal API 与 CsWinRT 接口集合表达式的问题分别通过生产公开发送入口、明确 SendItem 数组修正；没有新增 Core 友元或移除 WinRT 检查。案例源码 SHA-256 为 `5B20B0387F4CAC8279C225E07D199A73E935A8E9FB4737F8C8AD0A85E4AC42E0`。

这批是生产发送辅助逻辑、Windows API 与控件宿主验证，不是生产 Send 页面选择器/剪贴板入口的用户自动操作，也没有补齐系统输入、集成 CLI、最终 App MSIX 或平台矩阵。最低发现数量为 Core 83、WinUI 54；完整计划继续保留未验收项。

## IPC 未读大响应与宿主退出

`HostPipeTests.UnreadLargeResponseDoesNotBlockOtherClientsOrHostShutdown` 在独立/集成 mode 各一条，使用生产 CliHost、命令树和实际 OS NamedPipe。只有 ICliRuntime 边界替身控制返回内容：请求提交 2 MiB 值后读取实际响应的 4 字节 header，声明长度为 2,097,195；不读 body，验证同一 listener 的另一个 status 请求成功，再关闭自有 host。

退出后在三秒内排空已缓冲 body 并收到 EOF/reset，且实际 body 少于声明长度。Windows 缓冲 body 为 0，Linux 为 219,264 字节；不是完整响应已写完后才停止。新客户端连接超时、无报告错误，证明写侧背压不阻塞其他连接，宿主停止能释放未完成的大响应。

专项 Windows/Linux 各 2 项通过（`ipc-write-backpressure-first-{win,linux}/`），当时完整 CommandLine 各 164 项通过、0 跳过（`ipc-write-backpressure-complete-{win,linux}/commandline/`）。本批没有改生产 CliHost。mode 替身不等于生产 GUI 集成；不关闭 host 的最终交付 deadline 和响应队列上限仍分别需要行为证据。随后补充的写出断连见下一节，不以宿主关闭案例替代。

## IPC 写出断连与请求准入期限

追加四条实际 OS NamedPipe 案例，Windows/Linux 专项各 4 项通过、0 跳过（`ipc-disconnect-admission-first-{win,linux}/`），完整 CommandLine 各 168 项通过、0 跳过（`ipc-disconnect-admission-complete-{win,linux}/commandline/`）。

- 两条独立/集成 mode 案例通过生产 `receive watch` 发出超过 1 MiB 的合法 incoming 响应，客户端只读 header、不读 body；运行时下一次读取进入受控等待后，仅关闭该客户端。三秒内观察到工作取消、watch scope 仅释放一次，真实自有 Node 与 host listener 继续运行，另一个 status 请求成功，无提交和错误报告。
- 两条半个 header/半个 body 案例保持客户端连接，不关闭 host，不触发 caller cancellation；在十五秒外层期限内由生产十秒请求准入超时产生 EOF/reset，未执行业务，超时前后其他客户端都可请求。等待的是实际关闭，不使用固定 sleep。

边界替身仅提供合成 incoming 元数据和可取消读取；自有实际 Node 用临时 identity、下载目录、非默认端口与空发现白名单启动。上述超大元数据不声称来自真实 peer 或生产 GUI，scope 释放也不替代完整 GUI 生命周期验证。生产 CliHost 未修改；无宿主关闭的最终响应交付超时、响应队列上限仍未完成。

`HostPipeTests.cs` SHA-256 为 `4F7220CA721EFD5999156AB9F50487293435D790568B58BF4B2EE94900D4C11E`，Windows/Linux 文件相同。入口最低发现数量更新到 CommandLine 168；生产依赖未重建，之前 Native 分发身份保持不变。

## 生产网络接口页的 WinUI 回归

新增 `NetworkInterfacesPageTests` 四条 `[UITestMethod]`，对实际生产页面、LocaleProvider、Toolkit/动画资源和真实 native controls 进行挂载与协调，不复制页面代码。分别从白名单/黑名单起步，覆盖：

- 切换模式时移交规则且保持互斥；复用同一对 CheckBox；关闭模式后移除规则和 Add 按钮；保留设备名称与其他设置
- 输入规则草稿不提交；无关设置刷新不替换 TextBox、不覆盖草稿；程序化焦点离开只提交一次且保留最新无关设置；Add/Remove 调用 native UIA provider 后条目即时更新，删除最后规则关闭名单

专项 4 项通过、0 跳过（`app-network-page-first-win/`），全部非 SystemInput WinUI 58 项通过、0 跳过（`app-network-page-complete-win/winui/`）。测试只读取实际网络接口信息，设置回调使用自有内存状态，不写 AppSettingsStore、不改系统网络配置。CheckBox 原生属性事件、程序化 Focus 和 UIA Invoke 验证控件接线，不宣称真实鼠标键盘输入、主 App 页面导航或最终 MSIX 通过。

案例源码 SHA-256 为 `3938725AC8043FFF6065B936E5FAAFC64180793754B328DCE9B43D34D3055E22`；完整入口最低发现数量为 Core 83、Application 26、CommandLine 168、CLI 52（Native 55）、WinUI 58。`Invoke-Tests.ps1` SHA-256 为 `19C884539A9DAF4ADC70E108502C9A57F1D708C2B179CE93ECE0EFBC7C09D662`。本轮未改生产实现；格式检查与 diff whitespace 检查通过。

## 发送设备解析、超时与主动取消回归

`SendDeviceResolverTests` 新增 32 条生产辅助逻辑及真实网络案例，不是发送页 UI 自动化。纯解析覆盖 IPv4/IPv6、显式端口、默认端口格式化、IPv6 scope 与往返；拒绝空值、主机名、URL、路径/查询/fragment/用户信息及无效端口。真实隔离节点覆盖 HTTP/HTTPS 与首选协议四种组合、注册验证后的身份、旧指纹不匹配时双方不注册且正确指纹可恢复，以及不可达地址不损坏已有可信设备。

首次 25 项运行 17 通过、8 失败，报告保留于 `artifacts/test-results/app-device-resolver-first-win/`。其中两项发现 `IPAddress.TryParse` 接受带端口的括号 IPv6 后丢弃显式端口；五项发现 URI 解析丢弃附加部分后错误接受输入；一项发现 HttpClient 请求超时的 TaskCanceledException 被当作主动取消直接抛出，而不是向页面返回探测失败。

生产 `SendDeviceResolver` 改用 `IPEndPoint.TryParse` 解析端点，避免先将带端口的括号 IPv6 作为裸 IP 解析；保留裸 IP 默认端口与 IPv6 scope。`ResolveAsync` 增加可选调用方 cancellation token 并传递至探测和注册：非调用方取消且包含 TimeoutException 的请求 deadline 可尝试另一协议；真正取消继续抛出，不吞为离线结果。现有页面调用保持兼容；没有据此宣称页面卸载时已接入取消令牌。

新增三个无效 IPv6 端口边界和四个 timeout/cancellation 案例后，专项 32 项全部通过、0 跳过（`app-device-resolver-fixed-win/`）。deadline 案例使用实际接受 TCP 但不发送 HTTP/TLS 数据的自有监听器，要求两次协议连接、失败结果、空注册以及同一 sender 随后正常探测；主动取消分别在已取消令牌及 HTTP/HTTPS 实际连接后触发，要求不打开回退连接、不注册设备并可恢复。不使用固定 sleep 或直接抛替身异常模拟超时。

完整生产项目引用重新构建，随后统一入口执行非 SystemInput 套件 90 项全部通过、0 跳过（`app-device-resolver-complete-win/winui/`，约 25 秒，控制器退出 0）。格式核验通过；构建仍有两条既有 MSTest satellite PRI263 警告，不声称无警告。原始 8 项失败未覆盖。本次只修改 App 内部辅助实现；Core/CLI 生产源码未改，不用此前 CLI Native 结果证明 App AOT 载荷。

身份记录：生产解析文件 SHA-256 `0AC9ED27049AB8FB61123BD89B28BFE73BAFA35BD850D410B7EB7C1CE683D297`，案例文件 `DD6139A4AD9E610BE7302781A3BFC89DC0106977801FAB764BB0EDCD2CC9C577`；主 App 输出与 WinUI 宿主实际引用的 `Tonarink.dll` 均为 `E88DBC58E196612BD955396EB2AE78BB66E739740ECD2F74D93459A85FEEF27B`。入口 WinUI 最低发现数量更新为 90，脚本 SHA-256 `5E6703A5CE29C677F50A2CFA84AE1FDD251D46FE072D2C40E73FAB7F1B405D95`。这些仅证明当前 Windows x64 Release JIT 行为，不证明真实 App 导航、OS 输入、最终 AOT MSIX 或其他架构；旧 App 载荷尚需重新构建和验证。

## 生产发送设备卡片与终结清理

新增 `SendDevicesCardTests` 六条 `[UITestMethod]`，使用实际 `SendDevicesCard`、`DeviceIdentityCard`、动画按钮、MenuFlyout、LocaleProvider 和 WinUI 控件，传入自有运行状态/收藏快照及内存回调，不挂载主 App Shell，不读取或改写正式 AppSettings/Favorite/History 存储：

- 全部七种节点生命周期状态、正在探测和正在发送时，设备按钮及地址/收藏/分享/刷新/多接收开关的 IsEnabled 即时更新；复用原 native controls，单纯刷新不调用操作；native Invoke/Toggle 到达生产回调
- 窄/宽布局两条案例：按 fingerprint 更新、重排、移除和增加设备，保留仍在列表的 native 按钮；名称/型号/IP 末段编号刷新；同一按钮的选择与详情回调传入最新设备快照；移除观察真实 Unloaded
- 收藏快照变化后，展示名称、标记及 native MenuFlyout 的 Favorite/Unfavorite/Verify 操作更新，回调不保留已删除的收藏对象；只验证 UI 接线，不宣称收藏文件持久化
- 空列表、发现降级、启动失败及恢复时替换设备内容、保留工具操作，故障禁用相关动作而恢复后重新可用
- 自有 Host 的 effect 清理和生产卡片的 ElementChanged(null) 通知在终结 Dispose 时执行，重复 Dispose 不重复通知

首次五项全部失败于测试等待条件，见 `send-devices-card-first-win/`；`send-devices-card-mount-diagnostic-win/` 真实控件树确认四个普通工具 Button 加一个 ToggleButton，初始夹具误等待九个普通 Button。改为按实际操作名称等待后，`send-devices-card-observed-win/` 为一通过、四失败：状态条件已为 true 导致在新 render 前断言、移除后 XamlRoot 属性仍有值、菜单未完成关闭即再次打开。改用实际 render 版本、原生 Unloaded 和 Flyout Opened/Closed 事件后，五项通过（`send-devices-card-lifecycle-win/`）。这些是夹具与等待条件修正，不把它们描述为生产功能缺陷；旧失败报告均保留。

补充终结清理案例后，`send-devices-card-final-win/` 六项中五通过、一失败：Host.Dispose 已执行 effect cleanup，但生产卡片仅靠 native OnUnmount modifier 释放引用，ElementChanged(null) 未执行。生产 `DeviceIdentityCard` 增加组件 effect 清理，与 native OnUnmount 共用按当前元素引用保护的 ReleaseCard，避免终结路径漏清理或重复通知；没有修改 Reactor 依赖或关闭分析器。Expander、预览内容、网络页和新设备卡片夹具均显式 Dispose 自有 Host，不再假定 Unloaded 等于终结释放。尚未据此证明所有 App 组件的终结路径都已审计。

生产 App 项目引用重新构建后，专项六项通过、0 跳过（`send-devices-card-cleanup-fixed-win/`）；完整非 SystemInput 套件 96 项通过、0 跳过（`send-devices-card-cleanup-complete-win/winui/`，约 25 秒，控制器退出 0、输出排空）。当前增量 Release 构建和范围内格式核验通过。此前 95 项全套及失败诊断分别保留于 `send-devices-card-complete-win/`、`send-devices-card-owned-cleanup-win/` 和上述失败目录。

生产卡片源码 SHA-256 为 `CE6A13A154CA55E471AA82950A73D7FC323E8BFE539031B2C9804C0E94C85F58`，新增案例文件为 `108194CB662E8E8EA9E7CCED5C9DA37DFD67D9B5ADEBE0673825803885EBBA38`；主 App 输出与宿主实际引用的 `Tonarink.dll` 均为 `577A0D8034C8C8F157C8B9F7DF4A441E5DDCFF1DC8AA642F529D006977E982C6`。入口 WinUI 最低发现数量为 96，脚本 SHA-256 `CBC71549815090BBE73FEAF787A2FAE6BBE46C732B2D3B3B4974ECB51CD89933`。native Invoke/Toggle、程序化菜单打开和 Unloaded 不冒充 OS 鼠标键盘输入；仍未验收生产 App 导航、设备详情页全局收藏行为或最终 MSIX。Core/CLI 生产源码未改；最新 App AOT 载荷仍需重新构建及独立验证。

## IPC 最终响应交付期限

新增 `FinalDeliveryDeadlineClosesOnlyTheUnreadConnectionWhileHostStaysUsable` 的独立/集成 mode 两条案例，使用生产 Host、命令树和真实 OS NamedPipe。保存运行时边界替身返回约 2 MiB 的合法响应；客户端只读取四字节 header，保持连接且不读 body，不设置业务 `--timeout`，也不关闭宿主。另一个 status 请求在背压期间成功。

首轮 Windows/Linux 各 0 通过、2 失败，分别保留 `artifacts/test-results/ipc-final-delivery-first-{win,linux}/`。失败发生在夹具等待保存方法收到的 token 于业务完成后取消；不是生产交付超时失败。[System.CommandLine 2.0.12 的调用实现](https://github.com/dotnet/command-line-api/blob/v2.0.12/src/System.CommandLine/Invocation/InvocationPipeline.cs) 在 invocation 内创建并释放 linked source，不能用已完成 action 的 token 观察之后的管道期限。

最终案例移除该观察器，明确保持十二秒未读条件，跨过生产十秒交付预算，然后要求三秒内到达 EOF/reset 且读取量小于声明 body。这里的十二秒是维持故障条件的定时注入，不是等待组件/业务就绪的 sleep，也没有为了测试更改生产计时器。随后断言保存仅提交一次、没有调用 Quit、listener 仍存活、另一个 status 成功且没有错误报告。Windows body 已丢弃，Linux 剩余缓冲 219264 字节，均小于声明的 2097195 字节。

专项两系统各 2 项通过、0 跳过（`ipc-final-delivery-fixed-{win,linux}/`）；统一入口完整 CommandLine 两系统各 170 项通过、0 跳过（`ipc-final-delivery-complete-{win,linux}/commandline/`）。Windows 控制器约 123.18 秒、Linux 约 82.51 秒，均退出 0、`streamsDrained=true`；已逐项读取新增案例结果和 TRX counters。Linux 为自有 WSL Arch x64 环境，不替代 Ubuntu CI、macOS 或 ARM64。

本批仅修改案例、最低发现数量和证据文档，未修改生产 Host 或 CLI 依赖。`HostPipeTests.cs` SHA-256 为 `044CB405C34E2E4A3F8A41D60E1A7ABCDCD6F2DB03E28EEF509320C3C565B816`，`Invoke-Tests.ps1` 为 `2990C72866BC1E070B62C19EBABF2A1701D8E0BBC2BE0B95EEBB77D4C47DEE07`，Windows/Linux 源文件一致；Windows 被测 CommandLine DLL 仍为 `C093B2FE4A3BECD395D914A8F3C29FD52642796C432340D638A98579693F9C91`。定向格式及 diff whitespace 核验通过。

这些案例只证明业务结束后的最终交付期限，不证明持续输出的队列上限、真实 GUI 集成入口或当前 Native 产物的新增案例执行。持续 watch 的未读输出与队列资源界限仍是后续责任。

## IPC 持续输出的有界队列与逐帧期限

新增八条 `ResponseQueueTests` 和六条真实 NamedPipe Host 案例（独立/集成 mode 各三个场景），共十四条：

- 队列达到生产默认容量 16 后，业务写入异步等待；消费一个记录后继续，FIFO、对象和最终结果不丢失
- 调用方/连接取消解除待写，未接受的记录不发布；仅调用方取消时连接仍能接受下一条写入；完成队列解除待写、已入队记录可排空，重复 Complete 安全
- 已取消令牌即使空队列也不能发布；10000 次满队列进度回调均立即拒绝、不增加队列长度，最终结果仍等待并送达；释放容量后进度可恢复
- 未读 watch：64 条合成合法大帧元数据，第一帧只读 header，客户端保持连接、业务无期限、宿主不关闭；实际写入到期后 scope 仅释放一次，部分 body 后 EOF/reset，Node/listener 保留，其他 status 请求前后可用
- 正常读取：64 条 incoming 按原 ID/文件顺序全部送达，无重复或遗漏，然后恰好一个成功 final，scope 释放
- 正常空闲：无 incoming 的十二秒 watch 超过十秒帧期限，仍成功返回 final；不能将写入期限误用为整个连接的闲置期限

首轮旧生产实现四项为 2 通过、2 失败（`ipc-bounded-watch-first-win/`）：两条正常读取通过，两条未读 watch 十五秒内未释放 scope。该失败发生在真实管道与生产 watch，不是直接抛替身异常。运行时只提供合成 incoming 数据；真实 Node 使用独立身份/下载目录、非默认端口和空发现白名单，不声称是实际 peer 发出的超大文件名。

修复：新增生产 `CliResponseQueue`，每连接最多排队 16 条响应；业务 incoming/transfer/final 使用异步有界写入且不丢弃。同步 IProgress 只尝试入队，满队列时跳过中间采样，避免阻塞 Core 或积累 fire-and-forget 待写任务。每个实际响应帧另有十秒写出预算，失效只取消本连接、待写和业务；空闲 reader 没有该计时器。Host 的 finally 完成队列、取消连接并观察写入/断连任务，再释放 cancellation source。业务超时的 final 130 仍只受连接期限约束，既有回归继续通过。

共享命令树只把输出回调改为异步委托，并分开同步进度；没有新增命令 DSL。App 激活入口也适配相同签名。首次 App 引用构建发现遗漏的同步回调，失败说明保留 `ipc-bounded-watch-app-reference-win/build-error.txt`，发生于测试启动前；修正后实际重建及 WinUI 96 项通过、0 跳过（`ipc-bounded-watch-app-reference-fixed-win/winui/`），不据此宣称 GUI 集成 CLI 通过。

十四条扩展前的专项 12 项在 Windows/Linux 各通过（`ipc-bounded-watch-fixed-{win,linux}/`）；中间完整各 182 项通过（`ipc-bounded-watch-complete-{win,linux}/commandline/`）。追加正常空闲保护后，最终完整各 184 项通过、0 跳过（`ipc-bounded-watch-final-{win,linux}/commandline/`），首次失败没有覆盖。Windows/Linux 新 JIT CLI 全套各 52 项通过（`ipc-bounded-watch-jit-complete-{win,linux}/cli/`），控制器退出 0、输出排空。两系统新 Native tool/portable 四组各 55 项也已完成，无跳过，控制器正常退出；Linux 报告已复制归档到本机同名结果目录。

当前源码身份：Host `2C471F45DF6818129537225648CA70C76B3821952C5E73373C2D5154A22C687B`，响应队列 `C62B133F038BAF845775B9CE0B027EC0D43A5E284B9D46A1053924C94395F957`，Host 案例 `CE53630F5C8D297A550989C57AF2B78A8D1CF4A7910103638B6A1C018E4EE6E5`，队列案例 `02235F10340F6A1ACEAB7C4A48D38A1B4D59061F094AEBE295449DCB80A7B0E0`。App 与控件宿主的生产 DLL 同为 `BEE71C1D608C60137899BA6A319A975AE60B6F31A03C595DF7ABEC564F2C1533`。格式检查已修正三处 helper 换行并再次通过；最终 184 报告使用格式修正及新增空闲案例后的源码。

新本地包版本仍为 1.1.0-preview.1，未发布：

| 身份 | Windows x64 | Linux x64 |
| --- | --- | --- |
| portable archive | `3AC8C0A6076A1FEB4E3DFE08FBB99D22058E7D788A532D7A244AD4919FD7C2BA` | `F21617B8339EA1997814A31BF212D6A62C22163657A4237F5EF765E3A2D83EA7` |
| RID NuGet | `C2113FB3BD28E4744FA2B014FC77699E82105E0780B0FFC41562EE894230A896` | `EFAE4AD8ED13A05A0CF8E3DD3A41BB1036A03129F405B05EE383F457BEAA15C6` |
| selector NuGet | `F672378D6FD26111D962BFA32FBE0138B6B535E40005DF7E62DBBE8AAAB9F036` | `51B8EA864BD2E1ABAC923403342A08822020022CD4D74AC16CEB0FEFCEA307D7` |
| native executable | `2A8D638BBD86BD427475102E2BE8BC6DDE15B4DDC2A76A79A739986E881B73BF` | `1DB2B792C2A84EE51ED4A22B4C5EB3C067BFD538614AF75EA12DC814C99109B0` |

两系统包位于 `artifacts/test-artifacts/cli-bounded-output-{win,linux}-x64/`。本地安装 tool 和解压 portable 四组均已实际执行 55 项、0 跳过，报告为 `cli-bounded-output-distribution-{win,linux}/{win,linux}-x64/{tool,portable}/cli/`。各组 `run.json` 的实际/预期原生 EXE 哈希相符，Windows 同为 `2A8D638BBD86BD427475102E2BE8BC6DDE15B4DDC2A76A79A739986E881B73BF`，Linux 同为 `1DB2B792C2A84EE51ED4A22B4C5EB3C067BFD538614AF75EA12DC814C99109B0`。selector 首次漏传 `CreateRidSpecificToolPackages=false` 导致常规输出复制与正在运行的自有 JIT CLI 发生文件锁冲突，构建退出 1；改为明确的 pointer-only、`--no-build --no-restore` 打包，不终止被测进程、不覆盖已生成的 RID 与 portable。JIT 实际引用的 CommandLine DLL 保持 `716634A09949D32C282D6055E98706D9C654A5CCCD5913E136E3AA256F038254`；CI 配置库输出哈希不同，不混称同一份载荷。

上述队列证据是每连接的记录数量和取消契约，不是整个宿主的内存/连接数量上限，也不证明 Native 超大真实 peer 帧、GUI 集成入口或最终 MSIX。部署结果以各实际完成的报告为准；此前 Native 55 项报告不替代本次修改。

## 真实 peer 的 Native watch 背压与退出断连竞态

`WatchBackpressureProcessTests` 新增两条真实双 CLI 进程案例：通过实际 TLS 发送 60 万汉字，接收方产生 3,600,436 字节 IPC incoming 帧。未读分支只读取 header，不设置业务超时，保持连接至少十二秒；其间另一客户端查询同一宿主成功。排空时要求 body 不完整并收到 EOF/reset，待处理请求仍在，随后在同一对节点手动接受并逐字符核验原文。正常读取分支接收完整帧、正常接受，watch 保持十二秒后收到唯一成功最终响应，防止把正常空闲误判为写入超时。原始管道仅用于公共线协议操作，不替代生产命令、宿主或传输实现。

旧 Native 的未读分支复现完整帧继续写出，报告 `cli-real-watch-old-native-win/`；有界队列/逐帧期限载荷的专项 Windows/Linux 各两项通过，报告 `cli-real-watch-new-native-{win,linux}/`。首轮完整 Windows tool、Linux tool/portable 各 57 项通过；Windows portable 为 55 通过、2 失败，报告 `cli-real-watch-complete-win/portable/cli/`，没有覆盖。

其中一项是十二秒夹具的 `Task.Delay` 在 Windows 提早约一毫秒唤醒，已用单调时钟循环确保实际未读间隔，不改变生产十秒交付期限。另一项在成功 `app quit` 后十五秒仍持有 profile 锁；诊断重跑同样失败（`cli-owned-crash-cleanup-diagnostic-win-1/`），退出后的只读 status 仍显示原宿主 Running，并非单纯文件锁释放稍慢。清理等待仍为十五秒，未放宽期限或无条件重试。

新增 `ClientClosingImmediatelyAfterFinalResultCannotDiscardAcceptedQuit` 的独立/集成 mode 两条 Host 案例，每条进行 32 次真实管道交换；客户端读到成功最终响应后立即关闭。旧实现首轮一通过、一失败（`ipc-quit-close-first-win/`），发现断连取消与 Invoke/队列收尾竞争，已接受的退出可能没有执行。

生产 `CliHost` 现在保留已成功写出的最终响应，观察连接任务完成后执行一次退出；失败或部分帧不触发退出。`CliProtocol` 不再对无缓冲的 `PipeStream` 做冗余 Flush，避免客户端已读完并关闭后，Flush 的状态检查把成功写出误判为失败；其他 Stream 仍 Flush。该行为依据 [.NET PipeStream 实现](https://github.com/dotnet/runtime/blob/main/src/libraries/System.IO.Pipes/src/System/IO/Pipes/PipeStream.cs)。修复后专项三项通过，完整 CommandLine Windows/Linux 各 186 项通过、0 跳过，报告 `ipc-quit-close-fixed-win/`、`ipc-quit-close-complete-{win,linux}/commandline/`。

本地重新构建退出修复后的 RID/tool 和 portable，版本仍为 `1.1.0-preview.1`，未发布。Windows/Linux tool/portable 四组各 57 项通过，控制器退出 0 且输出排空；Windows 两组约 7 分 55 秒/7 分 56 秒，Linux 约 7 分 3 秒/7 分 1 秒。结果位于 `cli-quit-delivery-distribution-{win,linux}/{win,linux}-x64/{tool,portable}/cli/`，两种安装形式的可执行文件哈希在各系统一致。最新 JIT 两组各 54 项通过、0 跳过（`cli-quit-delivery-jit-complete-{win,linux}/cli/`），约 9 分 42 秒/8 分 28 秒；没有改变十分钟平台与十一分钟外层限时。重建主 App 引用后的 WinUI 96 项通过（`cli-quit-delivery-app-reference-win/winui/`）。上述 Host 集成 mode 和控件宿主不等于生产 GUI 集成 CLI。

Windows/Linux JIT CLI 的实际命令依赖 DLL SHA-256 分别为 `6898E69388578DD8B251C8F04B4A61EE737831D36139F6C56ED39C80F5BA611A` 和 `062186EE701DC7916F9A2A14097112824D322C13146A366D54D61E7AE2393AA5`；Linux 在发现 CLI 目录仍有旧 DLL 后重新构建，再开始验证，不用 apphost 哈希替代托管载荷身份。Windows App 与 WinUI 的命令 DLL 均为 `7337A4ED19608A9FF888B1EBDFD0FA3D57202F2535DE324F8412D59402D4D01B`，生产 App DLL 与控件宿主引用均为 `BEE71C1D608C60137899BA6A319A975AE60B6F31A03C595DF7ABEC564F2C1533`。Host/Protocol 源码 SHA-256 为 `A334B2312C7412F5C8D3CBC6CB06E44E2C094078CD5AF7838DDCB7BAC2F86D15`、`42E6BED259EFBB6D872CD7AE700C10481BC1045734322C037A47B559C9571EE5`。

| 当前退出修复载荷 | Windows x64 | Linux x64 |
| --- | --- | --- |
| 原生 EXE / ELF SHA-256 | `47EE7E25C69B1220E2CAF628926AFA0C52B07E8E58DC66805BF842260EC88223` | `C1664ADCC0E69CA3EDD89529AF51E837812BF241B00BA7C92F29D789452103D9` |
| portable archive | `0E7527C3B8A9F83064D6BB63B47E388E050F522DEC6A914C012733A7F40034AF` | `EC59546B5CE60DF5E11B9904A8B7EF42FF7F2F2E4CAE7079EB5CA245530DBC6E` |
| RID NuGet | `19AA05D729CC4550BF570F8F38BF7846C032F46FBFBEAE00A2DCA4EDC80E8689` | `74B09039794CB4298F78BC0A9C7AD64562A5A645C80896173887F1B3701A1D7E` |
| selector NuGet | `4BAE72FA5C30ED642E36001842C95A426EE12309F9EA907313E508E31A525699` | `F7F88894A32332303ABAAE2A865B27DE26B00446E8B21A6A7B0F668B2F9BC24A` |

这批补齐两条真实 peer 背压/空闲案例及两条退出断连案例，不证明多连接整体内存上限、生产 GUI 集成或其他 OS/架构。此前 52/55 项报告只证明扩展前范围；当前普通 CLI 为 54 项、Native 分发为 57 项。

## Core PIN 连续错误、锁定恢复与 Web 上传/下载

`PinSecurityTests` 新增六条实际网络案例，LocalSend TLS、Web 下载和 Web 上传各有成功重置与锁定恢复两个分支。缺失 PIN 四次不计入错误次数；两次错误后合法操作清零，再两次错误仍可成功。第三次错误返回未授权，此后的正确/错误/缺失 PIN 均被限流；配置的两秒锁定到期后，在同一节点恢复真实传输。检查待接收项、落盘副作用、节点状态和最终 UTF-8 内容；TLS 文本不产生目标文件，Web 上传只保留应有的一或两份完整文件。Web Receive PIN 与 LocalSend ReceivePin 设置为不同值，验证两者不能相互替代。

首轮四项为三通过、一失败，诊断报告保存在 `core-pin-first-win/` 与 `core-pin-diagnostic-win/`：测试错误地使用运行时类型断言检查 `SendOutcome` 联合类型的容器。改为生产联合类型分支模式断言后四项通过，无须修改 Core 生产实现；加入 Web 上传后六项专项通过（`core-pin-web-receive-first-win/`）。扩展前完整 Core Windows/Linux 各 87 项通过，最新完整各 89 项通过、0 跳过，报告 `core-pin-complete-{win,linux}/core/` 和 `core-pin-web-receive-complete-{win,linux}/core/`。

六项版本源码 SHA-256 为 `922F632CE2C4F31DA44C5CC37156C42684863BD97DBB600FC42D1DA1F511D176`，两系统一致；首次失败保留，不算生产 PIN 缺陷。

随后新增 `BrowserPinLockIsScopedToRemoteAddressAndSurvivesOtherClientsSuccess`：真实 Socket 分别绑定 `127.0.0.1` 和 `127.0.0.2`，第一地址锁定后第二地址仍能下载原文；第二地址的成功不会解除第一地址锁定。服务端快照必须只有 `127.0.0.2` 的授权会话，无未授权项或本地落盘。三十秒锁定仅用于保证隔离断言不因自然到期失效，不取代已有两秒到期恢复案例。Windows 七项专项通过（`core-pin-ip-isolation-first-win/`），加入服务端 IP 断言后完整 Core Windows/Linux 各 90 项通过、0 跳过（`core-pin-ip-isolation-complete-{win,linux}/core/`）。

七项版本的入口 Core 最低发现数量为 90。对应 PIN 案例与入口 SHA-256 为 `C3CCB628B569281F53C5CABF2B9247D08E7D7BAD3DE15A46A277EEC615AE0077`、`2491CE8ADC5EEB19C841186A19874DE7120E0FF1EF1A7F962ADC2899AB7AF4D2`，格式检查通过。该版本仅验证 Web 下载跨 IP；随后 TLS/Web 上传与并发扩展见下一节，不能将两个版本的报告混为一次执行。

## PIN 跨 IP、并发计数与实际 CLI 产物回归

`PinSecurityTests` 现为十二项，新增 TLS/Web 上传跨 IP 隔离、TLS 并发错误计数及 Web 上传/下载两种并发计数。真实 Socket 绑定不同源 IP；TLS 两端故意使用同一客户端证书，服务端证书通过实际指纹校验。锁定按 IP 隔离，其他 IP 成功不会解除原锁；成功走正常接受/上传或下载流程并检查 UTF-8 字节，不以 HTTP 成功替代最终内容。

TLS 在同一节点使用 24 个新源 IP，各同时提交八次错误 PIN，要求严格三次 401、五次 429，随后正确 PIN 仍返回 429、无待处理项和文件。Web 上传/下载各使用 16 个源 IP 验证相同规则，并让另一个 IP 在同一服务上完成真实操作。原 Core 并发案例首轮八次均为 401（`core-pin-concurrent-first-win/`）；Mobile 实际生产发送也出现八次 PIN 错误而非三次（`mobile-pin-concurrent-first-win/`）。这是生产读取、递增、写回不原子的缺陷，并非断言措辞问题。

Core 与 Mobile 的 `V2Server.CheckPin` 改为同一短锁下完成读取、到期恢复、成功清零、递增和锁定；锁内没有网络 I/O 或用户回调。保留缺失 PIN 不计错、第三次仍返回未授权、后续限流及到期恢复的现有契约。Mobile 的连接任务表仍使用 ConcurrentDictionary，没有误删其并发支持。

Mobile 保留 16 轮真实收发节点。修复后第一次运行在后续节点的发现广播阶段触发 25 秒夹具限时，报告 `mobile-pin-concurrent-fixed-win/`；每轮启动的广播固定等待 100 ms + 500 ms + 2 s，原预算不足以覆盖 16 轮。现为案例内 75 秒、MSTest 90 秒，未减少轮次或放宽 PIN 断言；完整五项通过，报告 `mobile-pin-atomic-complete-win/`。Web 两项首轮中上传误断言已完成请求仍留在快照，按生产完成后移除契约改正；一通过一失败及修正后两项通过的报告分别保留在 `core-web-pin-concurrent-{first,fixed}-win/`，没有修改生产 Web 判定。

当前统一入口 Core 最低数量 95、Mobile 5、CLI 普通档 55，Native 档额外三项共 58。Windows 与 WSL Linux 当前 Core 各 95 项、Application 各 26 项、CommandLine 各 186 项、Mobile 各 5 项通过，均零跳过；报告 `pin-atomic-shared-complete-{win,linux}/{core,application,commandline,mobile}/`，各控制器退出码 0 且输出排空。Linux 原始报告已复制到 Windows 同名结果根目录。Windows 重建生产 App 引用后 WinUI 96 项通过，报告 `pin-atomic-app-reference-win/winui/`。新 CLI 产物全套以各自完成报告为准，不能从上述通过推断完成。

`PinSecurityProcessTests.ConcurrentIncorrectPinsLockTheRealCliReceiverWithoutPublishingFiles` 在指定实际 CLI 载荷上启动独立 profile，通过生产 settings 命令启用 PIN/自动接收。十二轮真实源 IP 各八次并发错误请求，严格三次 401、五次 429；正确/缺失 PIN 都保持锁定，接收、历史及文件为空。另一 IP 完成真实 TLS 上传，文件字节、唯一历史记录与同一节点身份正确，原 IP 仍锁定。测试控制器不引用 Core，不替换后台宿主；同一案例供 JIT、tool 与 portable 共用。

旧 Windows AOT EXE SHA-256 `47EE7E25C69B1220E2CAF628926AFA0C52B07E8E58DC66805BF842260EC88223` 的首轮出现四次 401，失败报告 `cli-pin-concurrent-old-aot-win/`。新 Windows AOT EXE `668D1E2293FD3D499DB915388FB4D2CAC5AE499A74CE12B3BAC3D27ACE5A0DF8` 单项通过，报告 `cli-pin-concurrent-fixed-aot-win/`。这不是新 58 项全套的通过声明。

新本地载荷在 `artifacts/test-artifacts/cli-pin-atomic-{win,linux}-x64/`，Linux AOT EXE 为 `C5A7975BFD96B6B9D185D39E87CAC3CF4FB1313A1143952D44F07ADDD2239C03`。Windows/Linux 的实际 tool 安装与 portable 解压全套此前已启动。收尾时核验 Windows 两组各 58 项通过、0 跳过，控制器退出 0 且输出排空，实际/预期 EXE 哈希均为 `668D1E2293FD3D499DB915388FB4D2CAC5AE499A74CE12B3BAC3D27ACE5A0DF8`；报告为 `cli-pin-atomic-distribution-win/win-x64/{tool,portable}/cli/`。Linux 终态本次未核验，报告也未复制到当前 Windows 目录，恢复时先检查原始报告，不假定仍在运行或已通过。没有发布新版本，也没有修改生产 App 安装或默认 profile。

当前源码 SHA-256：Core/Mobile `V2Server.cs` 分别为 `52394DBE389C4798AC503FC7C6149BD68D66D93A2017AC65C27CCBB995A411B4`、`539828B24A96751F6AE51968B39B6F74A6D8EE00595828DB1A4C08A113B9EB94`；Core PIN、Mobile 和 CLI PIN 案例分别为 `6A582C9EB0CF30DD90FEA6C0CC1ECEA9DE067C7B3F30D4019E10DBBC61CEA32B`、`F687DE8F28D0689B0826C9E1B11B9AF5DEC1BCAD1F7C01AFAC70228DFE9D8698`、`D153D0BFE07823436AE2660BF5A2593336B0FCFE5D4282FB6C20C9CBB4353190`；入口为 `02DEF87A7C5A20F6F815CF2F6A6C8A62CA590A8138E1E182F980008CFF5B8CDB`。三套修改的 whitespace 检查通过。浏览器真实 UI、Mobile 跨 IP/锁定恢复全矩阵、混合正确/错误并发、全部平台及最终 App MSIX 仍是缺口。

## 本地入口

```powershell
# 逻辑/协议与共享业务，可按套件选择
./tools/Invoke-Tests.ps1 -Suite Core,Application,CommandLine,Mobile,Blazor

# 跨平台方案构建，不要求 Android/iOS workload
dotnet restore LocalSendDotNet.Core.slnx -p:PortableTransportOnly=true
dotnet build LocalSendDotNet.Core.slnx -c Release --no-restore -p:PortableTransportOnly=true

# 真实 WinUI 窗口，需要 Windows 交互桌面
./tools/Invoke-Tests.ps1 -Suite WinUI

# 真实鼠标/键盘输入，需要可用的前台交互桌面；不包含在上面的 WinUI 套件中
./tools/Invoke-Tests.ps1 -Suite WindowsInput

# 生产 Native AOT layout，严格使用新 Sandbox；哈希应由本次构建产物提供
./tools/Invoke-Tests.ps1 -Suite AppE2E -AppLayout <current-layout> `
  -AppExpectedSha256 <build-executable-sha256>

# 已构建的 JIT CLI，显式传入本次可执行文件
./tools/Invoke-Tests.ps1 -Suite Cli -CliExecutable ./src/Tonarink.Cli/bin/Release/net11.0/Tonarink.Cli.exe

# 本次本地 AOT RID 包 + portable；安装 SDK 可以为 .NET 10
./tools/Verify-CliTool.ps1 -AssetDirectory ./artifacts/test-artifacts/cli-text-win-x64 `
  -Version 1.1.0-preview.1 -InstallerSdkVersion 10.0.400 -RuntimeIdentifier win-x64 `
  -ResultsDirectory artifacts/test-results/cli-text-distribution-new-run
```

原生验证要求 `DOTNET_ROOT` 指向选定 SDK 安装。直接调用 `dotnet test` 时，普通 CLI 档需排除 `TestCategory=Distribution` 并设置 `TONARINK_CLI_TEST_EXECUTABLE`；原生档还需设置 RID、版本和包内可执行文件的预期 SHA256。使用脚本可自动传递并恢复这些环境变量。

命令套件的调度函数审计需要 PowerShell 7；默认调用 PATH 中的 `pwsh`，私有安装可通过 `TONARINK_TEST_PWSH` 指定完整可执行文件路径。`-NoBuild` 只使用已有输出，不会检查源码是否已重新构建；实施时先构建完整生产引用并核对被测 DLL，不能拿独立 artifacts 输出的结果推断默认 bin 已更新。多 Suite 在 PowerShell 内使用数组调用；从外部 `pwsh -File` 调用时，不把逗号字符串当作数组，默认不传 Suite 即运行 Core、Application、CommandLine。

每套入口的控制器记录和 stdout/stderr 位于 `<suite>/controller/`；外层限时不会吞掉 MTP 的原始失败码。正常套件为 MTP 5 分钟、外层 6 分钟；当前 54/57 项串行 CLI 为 10/11 分钟，旧 8 分钟中止的证据保留。退出与管道清理各另有 10 秒上限。必需环境或资源释放失败均非零结束；未执行的套件不能计为通过。

App E2E 需要 WinApp 0.7.0+ 及已配置的 Windows Sandbox/交互客户端；本测试不会启用 Windows 功能、重启系统或接管已有 VM。直接运行控制器需设置 `TONARINK_APP_TEST_LAYOUT`、`TONARINK_APP_EXPECTED_SHA256`，可用 `TONARINK_WINAPP_EXECUTABLE` 指定工具。证据写入 TRX 附件；测试只卸载本次 guest 开发注册，不停止 Sandbox。下一次全新运行前，应由环境所有者保存证据并同意销毁旧 guest，或在全新 disposable runner 上执行。Sandbox 限制和命令作用域参见 [WinApp 官方指南](https://github.com/microsoft/winappCli/blob/main/docs/sandbox-execution.md)。

## CI 状态和剩余范围

`ci.yml` 已配置跨平台 Core/Application/CommandLine/Mobile、CLI 进程、Blazor 组件、Windows WinUI 真实控件及 TRX 归档；CLI 原生 CI 与发布 native 作业复用同一 MSTest 产物验证，并归档结果。这些工作流尚未在 GitHub runner 实际执行，不声称 CI 或交互桌面门禁已经验收。

仍需继续完成：

- Core/共享业务逐功能矩阵审计与缺口补齐，而不是仅迁移已有案例
- 全 CLI 命令行为、同时取消/多项活动操作、IPC 多连接资源组合、完整 web receive 失败组合；已验证的真实 peer watch 未读到期/正常空闲、退出断连竞态、双向活动操作及每连接队列上限不替代这些缺口
- 在隔离交互环境中实际验收新 App E2E 控制器、生产 AutomationId 与两条回归流程，继续覆盖全部页面和 Windows 平台服务
- 生产 App 集成 CLI 与独立 CLI 的共同契约执行
- 隔离环境中的实际 AOT MSIX、Store/Widgets 身份变体与最终包验证
- macOS/ARM64 原生运行、Ubuntu/Windows hosted runner、交互桌面及最终发布门禁真实执行；本机 WSL Linux JIT/AOT 结果不替代这些范围
- 完整功能覆盖矩阵、失败诊断附件、控制器负向审计、快速/完整/发布三档收敛

未运行的环境不计为通过。未改动发布版本、未上传或提交发布，也未安装到开发者的生产 App 身份下。
