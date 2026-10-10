# MTP 故障审计宿主

此项目只用于 `Tonarink.CommandLine.Tests` 的 `MtpControllerTests`，不加入 solution 的独立测试列表，也不计为产品功能测试。构建引用和复制目标让父套件使用当前构建的真实 MSTest/MTP 模块及依赖，不使用预装工具或另一套解析/报告框架。

故障包括断言失败、测试宿主自终止、配合取消的无限等待、不配合取消的无限等待，以及全部跳过；父套件另用非匹配过滤器验证零测试和最低执行数量。执行需由父套件提供标准 MSTest `--test-parameter probeLease=<绝对路径>`，内容为本次临时目录中的 nonce。没有租约时测试不会执行故障操作。

父套件验证真实退出码、TRX 结果与附件、MTP 启停诊断、nonce 和租约释放。MTP 控制器与测试宿主是不同进程，不依靠按名称查找或杀死数字 PID。自终止使用当前进程 Kill，而非可能触发系统崩溃对话框的 FailFast。

配合取消的等待使用 TestContext.CancellationToken，必须在 MTP 的 3 秒全局超时后非零结束，并留下 nonce/PID 对应的取消证据和内部 session 取消日志；父控制器 25 秒上限不能代替它。不配合取消的等待单独验证外层 10 秒硬限时及自有进程树清理。后者不能被报告为 MTP 自行结束，实际 deadline 另记在 controller.json。

子宿主的故意失败 TRX 作为 `probe.trx.txt` 附件保留，防止 dotnet test 或 CI 将它合并为产品套件结果。诊断附件保存在父套件结果中；第一次失败不覆盖。这不替代 WinUI/WinApp 控制器或最终 App 包的负向验证。
