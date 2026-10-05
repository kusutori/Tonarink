# Tonarink 命令行

CLI 使用微软 `System.CommandLine` 定义命令树、类型化参数、校验和子命令帮助，同一套命令支持两种入口：

- 集成版：安装包含此功能的 MSIX 后通过应用执行别名运行 `tonarink`，与 GUI 共用设备身份、服务器、设置、收藏和接收历史，无需手动添加 PATH
- 独立版：直接运行 `Tonarink.Cli.exe`，不依赖 WinUI 或主应用，管理自己的后台服务与配置目录，不自动注册 PATH

下面示例中的 `tonarink` 均可替换为 `Tonarink.Cli.exe`，但 `app open` 和界面/Windows 集成相关设置仅适用于集成版。命令级帮助可直接使用 `send --help`、`receive accept --help` 等。

```powershell
tonarink --help
tonarink --version
tonarink status --json
tonarink discover --seconds 5
tonarink send --target "我的手机" .\photo.jpg .\documents
tonarink send --target "我的手机" --text "来自命令行的消息"
Get-Content .\message.txt -Raw | tonarink send --target "我的手机" --text -
```

如果提示找不到命令，请确认安装的是包含 CLI 的新版本，并在 Windows 设置的“应用执行别名”中启用 `tonarink.exe`。此功能不需要修改系统或用户 PATH。

## 运行方式

- `status` 不会启动应用，只报告当前运行状态
- 集成版需要服务时在后台启动主应用；独立版在后台启动无界面的 CLI 宿主，只有需要服务器的命令才开始监听传输端口
- 命令结束后宿主继续运行，直到 `app quit --yes`；独立版后台日志保存在配置目录的 `cli.log`
- `--no-start` 禁止自动启动，适用于只操作已运行应用的脚本
- `app open` 显示主窗口，`server stop` 仅暂停服务器，`app quit --yes` 退出整个应用
- GUI 已运行时，命令复用同一个服务器，不额外监听传输端口
- 相对路径按调用命令时的工作目录解析，不按应用安装目录解析；带空格的路径应加引号
- 使用 `--` 分隔以短横线开头的文件名或位置参数
- Ctrl+C 或 `--timeout 秒数` 取消当前命令；正在执行的 CLI 传输随之取消，主应用不会因此退出
- 本机通信使用当前用户专属、按应用数据目录隔离的命名管道；不同包身份及未打包开发版本不混用服务

所有命令支持 `--json`、`--timeout 秒数`、`--no-start`，全局选项可放在子命令前后。秒数取值为 1–86400。

独立版默认使用 `%LocalAppData%\Tonarink.Cli`，通过全局 `--profile 目录` 可隔离多个配置、身份和后台宿主，不会读写 GUI 的配置。不同宿主不能监听同一端口；与 GUI 同时运行时，可先执行 `settings set port 53318` 再启动独立服务器。

```powershell
.\Tonarink.Cli.exe --profile D:\CliProfile settings set port 53318
.\Tonarink.Cli.exe --profile D:\CliProfile server start
.\Tonarink.Cli.exe --profile D:\CliProfile status --json
.\Tonarink.Cli.exe --profile D:\CliProfile app quit --yes
```

## 设备与发送

```powershell
tonarink devices
tonarink discover --seconds 10 --json
tonarink send --target "设备名称" FILE_OR_FOLDER...
tonarink send --target FINGERPRINT --text "文本"
tonarink send --target https://192.168.1.20:53317 .\file.zip --pin 1234
tonarink send --target http://192.168.1.20:53317 .\file.zip
tonarink cancel TRANSFER_ID
```

目标支持已发现设备的名称、完整指纹、收藏的名称/指纹，以及 IP 地址。名称冲突时必须使用指纹或地址。裸 IP / IP:端口默认使用 HTTPS；HTTP 设备应显式写 `http://`。未指定端口时使用 LocalSend 的 53317，而不是浏览器默认端口。IPv6 地址加方括号，例如 `https://[fe80::1234%12]:53317`。

文件夹会递归发送，保留最外层文件夹名及子目录结构；空文件夹不产生传输项目。同一次传输不接受重复的目标文件名，最多发送 10000 个文件。发送校验和沿用应用设置，可以用 `--no-checksum` 对本次传输关闭。`--text -` 从重定向的标准输入读取 UTF-8 文本；标准输入最多读取 1 MiB 字符，通信 JSON 帧最多 4 MiB。

PIN 命令参数可能出现在本机进程列表或终端历史中，脚本应注意保护敏感信息。

## 接收

```powershell
tonarink receive list --json
tonarink receive accept REQUEST_ID --directory D:\Received
tonarink receive decline REQUEST_ID
tonarink receive watch --json
tonarink receive watch --auto-accept --directory D:\Received --seconds 60
```

`receive list` 返回待处理请求，其中 `id` 用于接受/拒绝，`transferId` 用于取消。接受请求时接收全部项目，默认使用应用的保存目录及校验和设置；`--directory`、`--no-checksum` 仅覆盖本次接收，不更改设置。

`receive watch` 持续报告新请求；只有传入 `--auto-accept` 才会额外自动接受。应用已有的“自动保存”策略仍然生效，其自动处理的请求不会变成 CLI 待处理请求。监听期间不因待处理请求自动显示主窗口。未指定 `--seconds` 时，监听持续到 Ctrl+C 或超时。

## 服务器与应用

```powershell
tonarink server start
tonarink server stop
tonarink server restart
tonarink app open
tonarink app open --favorite FINGERPRINT
tonarink app open --history HISTORY_ID
tonarink app quit --yes
```

重启服务器会中断正在进行的传输。`app quit` 也会结束图形界面和所有服务，因此必须显式传入 `--yes`。

集成版 `app open --favorite` 跳转到发送页，打开收藏列表并优先显示该设备；`app open --history` 跳转到接收历史并打开记录详情，两者不能同时使用。Windows 跳转列表也使用这组命令，旧版跳转列表参数仍兼容。

切换这两个目标时会先关闭此前打开的收藏/历史对话框；若其他编辑对话框正在打开，则返回退出码 4，需先关闭它，命令不会丢弃未保存的编辑内容。

## 设置

```powershell
tonarink settings list
tonarink settings get alias
tonarink settings set alias "书房电脑" --restart
tonarink settings set download-directory D:\Received
tonarink settings set tray-click panel
tonarink settings set preview-tool QuickLook
tonarink settings set network-allowlist "192.168.1.*,10.0.0.*" --restart
```

设置通过与 GUI 相同的存储保存并同步到界面。设备名称、端口、加密、网络筛选、PIN 等服务器配置需要重启服务器；可以附加 `--restart` 一并应用。涉及 Windows 自启动的修改沿用系统权限限制。

独立版只支持服务器、传输和存储相关设置，不包含主题、托盘、系统通知和预览等 GUI 设置。运行 `settings list` 获取当前宿主支持的键；独立版 `language` 支持 `zh-CN` / `en-US`，用于浏览器分享页面。

| 设置键 | 可用值 |
| --- | --- |
| `alias`, `device-model` | 设备名称、设备型号文本；名称不能为空 |
| `device-type` | `Desktop`, `Mobile`, `Server`, `Headless`, `Web` |
| `download-directory` | 保存目录，可使用相对路径及环境变量 |
| `theme` | `system`, `light`, `dark` |
| `language` | `system`, `zh-CN`, `en-US` |
| `auto-save` | `off`, `favorites`, `on` |
| `tray-click` | `panel`, `window` |
| `notification-action` | `file`, `folder` |
| `preview-tool` | `PowerToysPeek`, `QuickLook` |
| `preview-overrides` | 逗号分隔的工具名；空字符串清除覆盖配置 |
| `powertoys-path`, `quicklook-path` | 存在的程序路径；空字符串恢复自动检测 |
| `port` | 1–65535 |
| `discovery-timeout` | 1–60000 毫秒 |
| `multicast` | IPv4 组播地址 |
| `network-allowlist`, `network-blocklist` | 逗号分隔的 IPv4 地址模式；空字符串清除；设置一种会清除另一种 |
| `receive-pin` | PIN 文本，最多 32 字符；启用 PIN 时不能为空 |
| 以下布尔键 | `true` / `false`，也支持 `on` / `off`、`1` / `0` |

布尔键：`minimize-to-tray`、`start-with-windows`、`windows-share-favorites`、`notifications`、`keep-send-items`、`send-checksums`、`receive-checksums`、`app-wide-drop`、`file-preview`、`receive-history`、`pin-enabled`、`encryption`、`explorer-menu`。

`settings list/get` 默认隐藏 PIN；确实需要读取时显式加 `--show-secrets`。开启 PIN 时，先设置 `receive-pin`，再设置 `pin-enabled true`。

## 收藏与历史

```powershell
tonarink favorites list --json
tonarink favorites add "我的手机"
tonarink favorites remove FINGERPRINT
tonarink history list --json
tonarink history remove HISTORY_ID
tonarink history clear --yes
```

添加收藏时先连接设备并保存其指纹。使用收藏地址时再次检查指纹，避免地址被其他设备占用。接收完成后按应用设置记录历史；移除历史记录不会删除已保存的文件。

## 浏览器分享

```powershell
tonarink web send .\photo.jpg --auto-accept --pin 1234
tonarink web receive --auto-accept
tonarink web status --json
tonarink web accept SESSION_ID
tonarink web decline SESSION_ID
tonarink web stop
```

启动浏览器分享后输出可访问的局域网链接，浏览器分享持续到显式停止或主应用退出。默认需要批准浏览器请求，`--auto-accept` 可关闭逐次批准；`web status` 的 `pendingRequests` 给出待批准的会话 ID。浏览器 HTTPS 分享使用应用的自签名证书，浏览器可能提示证书确认。

## 输出与退出码

普通模式把结果写到 stdout、传输进度及错误写到 stderr。`--json` 把所有响应作为独立的 JSON 行写到 stdout（NDJSON），每行含 `type`、`exitCode`、`message`，有数据时还含 `data`。

- `progress`：传输进度
- `incoming`：接收监听发现的请求
- `transfer`：接收监听中单次传输的结果
- `result`：命令最终结果；脚本应以此行及进程退出码判断命令是否成功

| 退出码 | 含义 |
| --- | --- |
| 0 | 成功 |
| 1 | 执行或传输失败 |
| 2 | 参数或设置值无效 |
| 3 | 服务不可用、设备或请求不存在 |
| 4 | 对方拒绝或繁忙 |
| 5 | 需要 PIN、PIN 错误或尝试次数被限制 |
| 130 | Ctrl+C 取消或超时 |

## 开发调试

项目分工：

| 项目 | 职责 |
| --- | --- |
| `src/Tonarink.CommandLine` | 共用命令树、命令执行、传输、命名管道和输出协议；引用 `System.CommandLine` |
| `src/Tonarink.Cli` | 独立控制台入口、后台节点、配置/收藏/历史存储 |
| `src/Tonarink.App/Cli` | GUI 运行时适配、UI 调度与应用设置映射；主应用只引用共用命令库 |

旧的 `src/LocalSendDotNet.Cli` 试验项目已移除。开发构建可以直接调用主可执行文件，但不会注册 PATH：

```powershell
& .\Tonarink.exe --cli --help
& .\Tonarink.exe --cli status --json
```

CLI 在图形启动和主实例重定向前解析命令；服务命令随后通过命名管道进入 GUI 所持有的节点。JSON 使用源生成序列化，普通 MSIX 和 Native AOT MSIX 共用实现及清单声明。安装与别名激活需使用实际打包版本验证，不能由直接调用未打包 EXE 替代。

`System.CommandLine` 的命令处理器直接读取 `Option<T>` / `Argument<T>` 并调用类型化服务，不再转换成命令名、动作字符串或选项字典。客户端和宿主使用同一命令定义；GUI 的业务启动参数也复用其解析与执行。Windows 提前送达的启动命令仅排队到 UI 挂载后执行，避免冷启动时连接自身。通知/分享协议、`--minimized`、内部后台启动标记及开发工具参数仍由各自的平台启动层处理。

独立版构建与分发：

```powershell
dotnet build src/Tonarink.Cli/Tonarink.Cli.csproj
dotnet publish src/Tonarink.Cli/Tonarink.Cli.csproj -p:PublishProfile=win-x64-aot
# ARM64 使用 win-arm64-aot
```

Native AOT 输出位于 `artifacts/publish/cli/<rid>/`，不需要 .NET 或 Windows App Runtime，也不包含 GUI。分发该目录中的 `Tonarink.Cli.exe`，PDB 仅用于诊断，可不随包分发。项目也支持普通 `dotnet publish -c Release -r win-x64 --self-contained`。

使用 WinApp CLI 验证集成版时，先准备 **不同于正式应用的临时清单身份和别名**，保留 console 子系统、多实例声明及所需资源，避免替换已安装的应用或注册其 COM/Explorer 扩展。临时清单必须命名为 `AppxManifest.xml`：

```powershell
winapp run <构建或发布目录> --manifest <临时目录>\AppxManifest.xml `
  --output-appx-directory <临时布局目录> --with-alias -- --help
# 然后直接运行临时执行别名验证 status、server、send 及重定向
winapp unregister --manifest <临时布局目录>\AppxManifest.xml `
  --output-appx-directory <临时布局目录>
```

别名入口通过 WindowsApps 中的调用路径识别（包含 cmd.exe 的 PATH 查找），不依赖清单给原生 EXE 注入 `--cli`。这样空参数会显示帮助，错误命令也不会误打开 GUI。

本次在 Windows x64 上实际验证了普通开发包和 Native AOT 开发包的执行别名、自动后台启动、设置和服务器控制、NDJSON、超时退出码 130；也验证了打包版向独立 AOT 服务发送文件夹及接收文件的 SHA-256 一致。ARM64 发布配置已提供，未在 ARM64 设备实测。

参考：[System.CommandLine 解析与调用](https://learn.microsoft.com/en-us/dotnet/standard/commandline/how-to-parse-and-invoke)、[WinApp CLI 调试](https://github.com/microsoft/winappCli/blob/main/docs/debugging.md)。
