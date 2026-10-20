# 按行为重新审计测试

日期：2026-10-20。现有套件的功能审计与本轮必要改写已完成；执行结果、未执行环境和待确认约定分别列出。不是全功能覆盖声明，也不是所有平台验收通过声明。

本次工作取代旧“完整测试体系建设计划”的扩张顺序。保留 MSTest/MTP 和有效案例，不按覆盖率、测试数量或逐成员清单补齐测试。不修改生产行为来满足过时断言，不将上游控件的任意边界变成 Tonarink 的产品需求。

## 判定标准

每组案例必须回答：

1. **行为**：从哪个公开/业务入口操作，用户或调用方能观察到什么？纯逻辑的输出、文件字节、协议响应、退出码同样是可观察结果，不要求全部升级为 UI 自动化。
2. **反例**：输入/初态是否与目标结果不同？如果目标实现缺失、短路或处理错对象，断言是否会失败？关键回归用少量定向变异或修复前后对照确认，不给每项测试再建一层验证工程。
3. **重构**：只改变内部实现，行为正确时测试是否仍成立？不随意锁死引用相等、缓存结构、私有字段、控件像素尺寸、英文诊断句子。

分类为保留、改写、合并／删除、待确认。“待确认”必须列出具体的产品约定疑问；**尚未读完的文件不算已审计，也不以待确认掩盖未完成工作**。测试宿主、生产 App、JIT、AOT、MSIX、操作系统分别登记验证边界。

## 设置与初始重点

| 功能与案例 | 分类及处理 | 行为 / 错误实现反例 / 重构边界 |
| --- | --- | --- |
| 设置草稿 `SettingsInputDraftTests` | 改写首项；保留输入校验、合并和 rebase 案例 | 使用非默认保存值初始化所有可编辑字段；默认/空草稿将失败；不再验证 record 的 `with` 是否复制对象，也不要求网络列表保持同一引用；生产页接线另由下表新增案例验证 |
| Expander `AsyncUiMethod_RemainsOnRealDispatcherAfterAwait` | 删除 | 只检查 UITestMethod/dispatcher 基础设施，不证明 App 功能；真实控件测试本来就依赖此基础设施，失败不能计为独立产品功能 |
| Expander 动态子按钮 | 改写为原生 ToggleSwitch 事件 → 当前视觉树中的子按钮启用 → 实际 Invoke 回调 → 再禁用 | 不再直接调用捕获的 UseState setter；每次从当前宿主重新找控件；保留组件身份断言仅用于已有“重建导致状态丢失”的回归，不把此夹具当作生产设置页或真实鼠标输入验证 |
| 预览覆盖路径 `FilePreviewContentTests` | 保留有效/无效路径、显式保存、失焦不保存和删除；删除固定宽度/内边距断言，以 AutomationId 找动作 | 排版数值相同不能证明图标画布对齐；按钮调整宽度、排列顺序不应破坏功能检查；输入原生 TextBox 并 Invoke 原生按钮，不能替代生产设置页和 MSIX 验证 |
| CLI 校验和策略 `ReceiveChecksumProcessTests` | 改写，保留真实 TLS、字节、历史、清理和正常接收对照；移除英文子串与过时的统一 HTTP 400 预期 | 默认拒绝坏摘要，命令/持久配置允许跳过摘要，但都不能跳过长度检查；正常摘要始终可收；拒绝时 HTTP 非成功、CLI 失败且无文件/历史。精确 Core 错误分类由 Core 结构化结果测试负责，不从翻译文本推断原因 |
| 测试进程 UTF-8 管道 | 修复测试辅助设施；保留退出码、独立 stdout/stderr、超时及自有进程清理案例 | 子进程只回显原始输入字节，强制父控制台 ASCII；旧管道把汉字/emoji 变成问号时新检查失败。明确 UTF-8 后应逐字保留换行与空白，不允许放宽为“非空输出” |
| 路径安全 `SafeFileTargetTests` | 保留，已随 Core 重跑 | 穿越/绝对路径、同名与预留重名、符号链接逃逸均对应真实数据风险；用实际临时文件和独立预期结果，不需要 GUI；总是拒绝路径会被正常命名对照发现。链接能力缺失必须明确报告，不能静默通过 |
| CLI 命令树 `CommandTreeTests` | 保留接线检查，已重跑；不计为业务功能完成 | 正确命令只转发一次，非法参数不转发，help 不执行业务；错误命令映射/重复执行应失败；不测试 System.CommandLine 内部数据结构，不由 DataRow 数量推导命令覆盖 |
| 设置页 E2E `SettingsRegressionTests` | 保留；本轮仅编译驱动，未运行 Sandbox | 测前建立非默认草稿及非零滚动；往返后重新观察当前 App，不读旧控件；通知两个子控件均跟随开关。精确产物 hash/包/guest 归属，不接管用户既有 Sandbox；实际是 AOT loose development package，不是最终 MSIX 安装验收 |

## 其余现有功能组

表内反例是测试应能发现的错误，不代表本轮逐项运行了变异。实际做过的反向验证见证据表。“保留”也不代表绝无误报风险或整个功能已经覆盖。

| 功能 / 对应测试 | 分类及处理 | 行为 / 错误反例 / 重构边界 |
| --- | --- | --- |
| Core 协议 `ProtocolTests` | 改写 JSON 语义断言；删除只调用框架的坏 JSON 案例 | 真实协议序列化器的字段、非 ASCII、可选字段、64 位大小和设备类型；字段缺失/截断应失败；不依赖 JSON 排版或转义。坏 IPC JSON 仍从 CLI 协议入口检查 |
| Core 结果构造 `TransferOutcomeTests` | 删除 | 只构造 record 再 switch/比较引用，不执行收发；有价值的 outcome 分支由真实节点/PIN/取消/完整性案例验证，不计语言/编译器保证为产品功能 |
| 身份/TLS `IdentityTests`、`TransportReliabilityTests.Register…` | 保留并补强注册 | 保存身份重载稳定、损坏/错误身份拒绝；伪造 register 403、不入库，随后正确身份能注册；始终拒绝所有请求不能通过；不依赖翻译后的诊断句子 |
| 收发 `NodeIntegrationTests` | 保留，改写大流检查 | 真正 TLS 文本/文件、部分接收；32 MiB 非零流逐块验字节，同大小零文件不能通过；不再宣称检查了内部“不缓存整个源”的内存策略 |
| 完整性/并发 `TransportReliabilityTests` | 保留 | 错摘要不发布文件、无临时残留；错令牌后正确令牌仍可传；并发各文件内容正确；只报成功或总是拒绝都会失败；不约束内部上传实现，保留结构化失败码约定 |
| 元数据限额、超时释放名额：同上 | 改写，增加正常后续请求 | 超限 prepare 必须 413，不能拿用户决策超时冒充限额；小元数据的大文件仍可传。单名额会话超时后同节点实际接收成功，漏释放会失败；不只读失败标记 |
| 取消/停止 `IncomingCancellationTests` 与节点集成案例 | 保留 | 已提交结果不被晚到取消改写；在实际进度/临时文件出现后取消，要求清理、通知对端、恢复传输；不能在未开始时取消来冒充中途取消；内部状态机案例不单独计为端到端通过 |
| 广播/设备 `BroadcastLifecycleTests`、`ReliabilityTests` | 保留，改写过期事件 | 实际订阅者收到 Removed、新设备仍保留；只改字典不发事件会失败；真实满队列/断订阅/结束不悬挂且不丢业务事件；不测试 Channel 库本身 |
| 地址规则、目录、流适配和限值：`NetworkAddressPatternsTests`、`ReliabilityTests` | 保留 | 独立输入/期望检查允许与拒绝、相对路径和流内容；常量返回会被相反对照发现；不锁死集合/适配器结构 |
| 端口/启动 `ReliabilityTests` | 改写 | UDP 冲突后真正 HTTPS info 可访问；TCP 冲突释放后同节点可访问；预取消启动可重试；删除依赖 RSA 耗时的 50 ms 猜测，缩小名称，不再声称检查了启动中途清理 |
| 网卡刷新 `DiscoveryMembership…`：同上 | 缩小名称；零地址回退策略待确认 | 只刻画实际 discovery 的 membership 记录，不证明 LAN 包能收到；不作为网络切换 E2E 证据；具体约定疑问见下文 |
| PIN `PinSecurityTests` | 保留 | 缺 PIN 不计错、错误累积/成功重置、锁定/到期、来源隔离及并发不丢计数；同一节点真实成功收发为对照；不从英文分类，不重启后冒称锁定恢复。重复轮次是压力检查，不是更多功能覆盖 |
| Web `WebShareTests` | 保留，删除 JS 私有函数名/固定英文浏览器名 | 启用前后状态、真实下载/上传字节、审批/PIN、pending 清理，接收 Web 类型与文件元数据；重命名页面函数或修改翻译不应破坏流程测试；不等于执行了浏览器 JS |
| 公共兼容 `PublicApiBaselineTests` | 保留一份，删除重复反射成员清单 | 约束库调用者可见的 API，不计作收发功能；不盲目更新 hash 让测试变绿；内部正确重构不改变公共 API |
| Application 设置/导入 `TonarinkAppStateTests` | 保留，移除乐观缓存/异常引用断言 | 非默认恢复、规范保存、失败传播、后续恢复、并发最终值不丢；导入项目清理只删自有临时文件、保留仍持有文件；不要求失败时 UI 一定显示未保存值或异常同一对象 |
| Application 业务 `LocalSendRuntimeTests`、`DeviceSnapshotTests` | 保留 | TLS 收发、PIN/部分/自动接收、重启后配置、快照枚举期间旧设备仍可用；先暂停枚举再发送，排除没进竞争区的假通过；只控制平台/枚举边界，不替换发送功能 |
| 语言 `AppLanguagesTests`、`LanguageTests`、`PluralMessageTests` | 保留，补六语言显式索引；缩小复数名称 | 各索引不回退成英文、未知回退、await 后翻译及文化恢复；不锁死 CultureInfo 引用；复数资产案例明确仅英/中，不代表其他翻译正确 |
| CLI 派发/配置 `CommandBehaviorTests`、`StandaloneSettingsTests` | 保留，更正 clear 的名称 | 正确 action/选项、错误响应、规范值、调用方目录、互斥列表、PIN 隐藏；spy 只证明接线，不宣称它证明了不删文件；值转换有独立合法/非法对照，不约束字典实现 |
| IPC `Ipc/ProtocolTests` | 保留并补独立 wire 对照 | 小端 UTF-8 JSON、多帧不串读、非法/截断拒绝、超限不写半帧、profile 隔离；独立构造输入并直接解析输出，防 writer/reader 同错仍 round-trip；删除“证明分配前拒绝”的过度命名 |
| IPC/背压 `HostPipeTests`、`ResponseQueueTests` | 保留，队列按值比较 | 真实 pipe 并发响应不串、断连/超时只结束所属工作、其他客户端可用、结果先于退出；真正满队列/未读大帧，区别 idle 与写阻塞；不把 runtime spy 算作 GUI 或真实接收 |
| CLI 生命周期 `StandaloneProcessTests` | 保留 | 当前构建可执行文件、自有 profile；只读不启动、启动竞争、持久化、停止释放端口、崩溃后身份恢复；不采用用户进程，不自动替换 host 掩盖崩溃；实际状态/文件/端口判断 |
| CLI 业务 `TransferProcessTests` | 保留，删除固定英文浏览器名和 JSON 顺序依赖 | 实际字节、文本原样呈现不静默落盘、审批拒绝、目录、历史删除不删文件、收藏重启后能发送、真实 Web 请求；不只检查 exit 0；保留记录按 JSON 语义比较 |
| CLI 中途取消/PIN/watch：`ActiveUploadProcessTests`、`PinSecurityProcessTests`、`WatchBackpressureProcessTests` | 保留 | 在真实 TLS 字节/接收进度后暂停再取消/停止/重启，同 host 仍能传、无半文件；已锁来源不被其他来源成功解锁；watch 读/不读不损坏其他业务；不以瞬间取消或 readiness sleep 造前提 |
| CLI 分发 `NativeArtifactTests` | 保留，环境单列 | 指定包 hash/架构/平台产物，help/version 无副作用；已安装旧版不能顶替；JIT 通过不冒称 Native AOT 或 NuGet/winget 验收 |
| 生产设置页：新增 `SettingsPageBehaviorTests` | 补真实接线 | 原生输入后未提交，UIA 点保存/启动才提交规范值且保留其他设置；交替打开并更新主题/语言不积累选项，等待真实 DropDownOpened 而非只数关闭的项目；临时移除草稿应用已实测失败；只替换持久化/服务器边界，不宣称启动了实际服务 |
| 接收页/网卡页 `ReceivePageTests`、`NetworkInterfacesPageTests` | 保留 | 服务状态控制动作；自动保存模式维护相关设置；草稿/失焦保存、增删规则、互斥模式；原生事件进入生产组件；属性赋值不称鼠标自动化，InfoBadge 不称任务栏系统徽标 |
| 历史 `HistoryPageTests` | 保留并补焦点结果 | 菜单/对话框确认、只删指定记录、不删文件、导航显示真实信息、换 store 不调用旧源；移除另一行后验证当前行键盘焦点，不只比较 Button 引用；没加入已撤销的“交换索引”需求 |
| 设备卡 `SendDevicesCardTests` | 保留，删除框架 effect/重复 Dispose 断言 | 当前设备资料/收藏/状态和回调指向最新设备；生产头像 mount/unmount 锚点回调仍检查；不单测 Reactor effect 次数当 App 功能；spy 有调用不代表实际文件发出 |
| 发送源/解析 `SelectedSendItemTests`、`SendDeviceResolverTests` | 保留并改写 | 重命名/撤销/重做、文件夹读取后真正发送并验字节，不只读 Path；Windows StorageFile/位图也实际发送；信任、双协议超时/取消、后续恢复观察真实连接/设备，不锁死 InnerException 形状 |
| Blazor `PageBehaviorTests` | 保留 | 生产 Razor 路由、incoming 链接、设置事件保存/外部状态、文本入队、同传输更新、宿主资产；只替代平台/JS 测量边界；不证明 WebView/鼠标/CSS 或完整网络收发 |
| Portable `PortableServerTests` | 保留，补文本 preview 与 info 语义断言 | 真正 portable 实现的 TLS 文本/PIN/部分/并发锁；观察准确文本、info alias/fingerprint，不搜子串；不以桌面 Core 同 API 通过替代 portable 实现，不称移动设备 UI 测试 |
| 控制器 `SuiteProcessTests`、原 `MtpControllerTests` | 合并／删除后者 | 保留实际 `Invoke-TestProcess.ps1` 的通过/失败/崩溃/空套件/全跳过/硬超时；删除重复验证 MTP 自身规则的夹具和专用取消 probe；故意失败子 TRX 仅作附件，不算产品案例 |
| 入口与 CI `Invoke-Tests.ps1`、工作流 | 同步调整 | 最低发现数是防误漏 guard，不是质量指标；按有意删改同步。Core NuGet 发布用当前 Core 入口及报告，避免旧整解决方案命令误跑需外部产物的套件；不改成忽略失败 |

## 待确认约定与未验证边界

- **保存失败后 UI 是否必须回滚？** 已查 Application 代码和仓库文档，未找到对外的回滚/保留草稿承诺。不从当前乐观缓存反推需求；只断言错误上报、没有伪装持久化、后续保存可恢复。
- **刷新时暂时无地址，应保留旧 membership 还是立即断开？** `src/LocalSendDotNet.Core/docs/core-api.md` 只承诺变化后重绑/重试，未定义零地址瞬态策略。现有 membership 案例保留为内部诊断性刻画，待约定明确再决定；不能称“真实网络切换仍可发现”。
- **启动中途取消清理**有生命周期约定，但旧 50 ms 无法保证在哪个阶段取消。本轮改成确定的预取消/重试并正确命名；中途部分启动清理仍无确定性检查，不为补这一处增加生产测试钩子或时间猜测。
- **不新增外观/关键帧检查**：固定按钮尺寸、图标 JSON 存在等不证明视觉效果；继续人工实际查看。测试里已不把 hover 不删除称作证明开盖动画。
- **本轮未运行**：SystemInput 一项、Sandbox 生产 App AOT E2E、最终 MSIX、Native AOT CLI 分发、Linux/macOS、Android/iOS 设备。App E2E 驱动仅编译；宿主通过没有贴上上述环境标签，也没为测试接管用户的 App/Sandbox。
- WinUI 宿主与 App 同用实验版 Windows App SDK；生成元数据 CS8305 在宿主项目内明确选择接受实验 API。已有两条 MSTest 资源 PRI263 警告仍保留，没有统一静音全部警告。
- 实验 F# Core 没有现有独立套件，本轮不为它新增迁移/覆盖承诺。其余边界也不自动变成“补齐覆盖率”任务；以后围绕真实改动与回归增加最小案例。

## 本轮证据

所有目录相对仓库根目录；均为本机 Windows x64，不能外推为其他系统、Native AOT 或 MSIX 通过。

| 范围 | 结果 | 报告 |
| --- | --- | --- |
| UTF-8 原始字节回显，修复辅助设施之前 | 1 项失败，汉字/emoji 被替换为问号 | `artifacts/test-results/test-audit/utf8-before/` |
| 修复 UTF-8 后的 ProcessRunner | 7 项通过，0 跳过 | `artifacts/test-results/test-audit/process-runner-after/` |
| Core 第一轮 | 90 通过、1 失败：固定英文浏览器名已过时 | `artifacts/test-results/test-audit/core-first-pass/` |
| Core 修订后全套 | 91 通过，0 跳过 | `artifacts/test-results/test-audit/core-revised/` |
| Application 修订后全套 | 30 通过，0 跳过 | `artifacts/test-results/test-audit/application-final/` |
| CommandLine 全套 | 180 通过，0 跳过 | `artifacts/test-results/test-audit/commandline/` |
| 清理专用 probe 后的套件控制器 | 6 通过，0 跳过 | `artifacts/test-results/test-audit/suite-controller-final/` |
| CLI 文本与完整性 | 9 通过，0 跳过；JIT 当前构建 | `artifacts/test-results/test-audit/cli-integrity-text/` |
| CLI 历史/Web/停止，第一轮 | 2 通过、1 失败：同样的固定英文浏览器名 | `artifacts/test-results/test-audit/cli-history-web-lifecycle/` |
| CLI 历史/Web/停止，修订后 | 3 通过，0 跳过；未重跑其余 CLI E2E/分发案例 | `artifacts/test-results/test-audit/cli-history-web-lifecycle-revised/` |
| 生产设置页接线两项 | 2 通过，0 跳过 | `artifacts/test-results/test-audit/settings-page/` |
| 临时移除生产设置页的草稿应用 | 1 失败：保存的仍是旧名称，而非新草稿 | `artifacts/test-results/test-audit/settings-mutation-no-apply/` |
| 恢复产品代码后 WinUI 非 SystemInput 全套，含原生下拉框展开事件 | 127 通过，0 跳过 | `artifacts/test-results/test-audit/winui-final-native-open/` |
| Blazor 全套 | 8 通过，0 跳过 | `artifacts/test-results/test-audit/blazor/` |
| Portable 服务端全套 | 5 通过，0 跳过；不是设备 UI | `artifacts/test-results/test-audit/portable/` |

反向验证后生产源码已原样恢复，本轮没有改产品行为。另已完成 App E2E 驱动 Release 编译；格式处理限定本轮改动的 C# 文件。原始失败报告保留；旧 `testing-progress.md` 等报告只代表其历史版本，不移作本轮验收证据。数字仅用于描述实际执行，不是验收数量目标。
