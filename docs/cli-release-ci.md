# 独立 CLI 发布

工作流为 `.github/workflows/release-cli.yml`，只处理 `src/Tonarink.Cli` 的独立分发，不更改主 App 的执行别名或 MSIX 发布流程。

| 路线 | 包名 / 安装命令 | 运行方式 |
| --- | --- | --- |
| NuGet | `dotnet tool install --global tonarink-cli` | Windows、Linux、macOS，各自 x64 / ARM64，Native AOT；安装需要 .NET SDK 10+ |
| winget | `winget install --id kusutori.tonarink-cli --exact` | Windows x64 / ARM64，Native AOT，自包含 portable |
| GitHub Release | 下载对应 RID 的便携包 | Windows ZIP；Linux / macOS `.tar.gz`，不需要 .NET SDK |

两者的命令均为 `tonarink-cli`，与主 App 的 `tonarink` 不冲突。不要同时安装两条路线，以免同名命令的 PATH 优先级造成混淆。

NuGet 和便携包复用同一架构的一次 Native AOT 编译输出，不依赖 .NET 11 / ASP.NET Core 共享运行时。NuGet 采用 [官方 RID-specific tools](https://learn.microsoft.com/zh-cn/dotnet/core/tools/rid-specific-tools) 机制：一个顶级选择器包引用六个 RID 子包，SDK 安装时自动选取原生程序；不分发 `any` 包，不保留 JIT 回退。支持 `win-x64`、`win-arm64`、`linux-x64`、`linux-arm64`、`osx-x64`、`osx-arm64`，其他系统/架构没有匹配包。

| RID | CI runner | 便携附件 | 运行检查 |
| --- | --- | --- | --- |
| win-x64 | windows-latest | ZIP | 安装与运行 |
| win-arm64 | windows-latest | ZIP | 交叉编译，仅检查 PE 格式与架构 |
| linux-x64 | ubuntu-22.04 | tar.gz | 安装与运行 |
| linux-arm64 | ubuntu-22.04-arm | tar.gz | 安装与运行 |
| osx-x64 | macos-15-intel | tar.gz | 安装与运行 |
| osx-arm64 | macos-15 | tar.gz | 安装与运行 |

Native AOT 不跨操作系统编译。Linux 固定 Ubuntu 22.04（glibc 2.35）构建基线，runner 安装 `clang`、`zlib1g-dev`，产物需要用户系统提供 ICU、OpenSSL、zlib，不覆盖 Alpine/musl。macOS 使用 runner 自带的 Xcode 工具链，在 macOS 15 检查，不承诺更早版本；当前没有 Developer ID 签名或公证，下载后的 Gatekeeper 限制需按系统策略处理。没有新增 Homebrew、apt 等分发渠道。

## GitHub 配置

沿用仓库的 `release` environment。确保其保护规则允许 `cli-v*` 标签部署，以及允许 Actions 使用 OIDC。Secrets 可放在该 environment 或仓库层级：

| Secret | 内容 | 用途 |
| --- | --- | --- |
| `NUGET_USER` | nuget.org 的用户名，不是邮箱、API key 或 GitHub 用户名 | `NuGet/login@v1` 的可信发布登录；已有同名配置可复用 |
| `WINGET_TOKEN` | GitHub **classic PAT**，仅需 `public_repo` 权限 | WingetCreate 创建/更新个人的 winget-pkgs fork，并提交清单 PR |

`GITHUB_TOKEN` 由 Actions 自动提供，无需手动配置。便携 EXE / ZIP 不需要 MSIX 签名证书，也不使用主 App 的签名 secrets。不需要长期保存 `NUGET_API_KEY`，NuGet 发布时通过 OIDC 获取短期 key。

不要把 PAT 发到聊天、提交到仓库或写进命令参数，直接在 GitHub Secrets 中配置。工作流把它映射为 `WINGET_CREATE_GITHUB_TOKEN`，不通过 WingetCreate 的 `--token` 参数传递。参见 [WingetCreate 的 token 要求](https://github.com/microsoft/winget-create/blob/main/doc/token.md)；fine-grained PAT 当前不受它支持。

### nuget.org Trusted Publishing

除了 Secret，还要在 nuget.org 添加一条 [Trusted Publishing policy](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)，现有 Core 的 policy 不会自动授权不同工作流：

- Repository owner：`kusutori`
- Repository：`Tonarink`
- Workflow file：`release-cli.yml`（仅文件名）
- Environment：`release`
- 包 ID 范围：`tonarink-cli` 和 `tonarink-cli.*` 两条规则；若使用精确 ID，则同时授权入口包以及 `.win-x64`、`.win-arm64`、`.linux-x64`、`.linux-arm64`、`.osx-x64`、`.osx-arm64` 六个子包
- 允许发布新包和新版本，首次发布需要“新包”权限

填写的 owner / repository / environment 必须与实际工作流完全匹配。只给该 CLI 包及其 RID 子包的发布权限，不要扩大到其他包。原先仅授权入口包或 Windows 子包的策略需要补上 Linux/macOS 子包，否则发布会因未授权的新包失败；Secrets 不变。

## 构建与发布

CLI 的版本取自 `src/Tonarink.Cli/Tonarink.Cli.csproj` 中的 `<Version>`，标签必须为 `cli-v<Version>`，例如 `cli-v1.1.0`。CLI 的版本独立于主 App 和 Core。

先提交 CI、文档和包元数据，再通过 Actions 手动运行 `release-cli`。默认 `publish=false`：只构建和校验，上传 artifacts，不发布 NuGet、GitHub Release 或 winget PR，也不需要 secrets。

准备发布时，在提交对应版本之后创建并推送标签：

```powershell
git tag -a cli-v1.1.0 -m "tonarink-cli 1.1.0"
git push origin cli-v1.1.0
```

标签触发的工作流会：

1. 检查标签与 CLI 项目版本一致
2. 构建 NuGet 顶级 RID 选择器包，包含平台映射及文档，不含实现 DLL
3. 在对应系统分别编译六个 RID 的 Native AOT，一次编译同时生成对应 NuGet 子包和 portable 包；检查 PE / ELF / Mach-O 格式及架构，并比较 NuGet 与便携包内的程序哈希
4. 除交叉编译的 Windows ARM64 外，在每个原生 runner 上通过 .NET SDK 10 从隔离本地源安装主包，运行中英文帮助、后台 IPC、设置读写、HTTPS 服务启停及宿主退出
5. 根据 Windows ZIP 的真实 SHA256 生成多文件 winget 清单，使用微软的 1.12.0 JSON schemas 校验，并生成全部发布附件的 `SHA256SUMS.txt`
6. 创建 CLI 专用的 GitHub Release，通过可信发布先上传六个 NuGet RID 子包，最后上传顶级包，保证用户安装时对应的架构包可用
7. 对正式版使用 WingetCreate 自动向 `microsoft/winget-pkgs` 提交 PR，合并后才进入 winget 源

手动触发并选择 `publish=true` 时，也必须选择匹配的现有 `cli-v...` 标签；从分支运行只能做不发布的构建。手动发布可以设置 `submit_winget=false` 暂不提交社区 PR。带 `-preview.1` 等后缀的版本发布为 prerelease，不生成或提交 winget 清单；安装这样的 NuGet 版本时使用 `--prerelease` 或指定 `--version`。

CLI Release 使用 `latest=false`，不会覆盖主 App 的 Latest 下载入口。附件包括：

```text
tonarink-cli.<version>.nupkg
tonarink-cli.win-x64.<version>.nupkg
tonarink-cli.win-arm64.<version>.nupkg
tonarink-cli.linux-x64.<version>.nupkg
tonarink-cli.linux-arm64.<version>.nupkg
tonarink-cli.osx-x64.<version>.nupkg
tonarink-cli.osx-arm64.<version>.nupkg
tonarink-cli-<version>-win-x64.zip
tonarink-cli-<version>-win-arm64.zip
tonarink-cli-<version>-linux-x64.tar.gz
tonarink-cli-<version>-linux-arm64.tar.gz
tonarink-cli-<version>-osx-x64.tar.gz
tonarink-cli-<version>-osx-arm64.tar.gz
tonarink-cli-winget-manifests.zip       # 仅正式版
SHA256SUMS.txt
```

便携包根目录为 `tonarink-cli.exe`（Windows）或 `tonarink-cli`（Unix）、`README.md`、`LICENSE`、`NOTICE`，不分发 PDB / DBG / dSYM、MSIX 或 .NET 运行时文件。Unix 使用 tar 保留可执行位。内部 .NET 程序集仍叫 `Tonarink.Cli`，避免改变共用命令库的程序集友元关系；外部命令名和分发名均为 `tonarink-cli`。

## winget portable 清单

包标识为 `kusutori.tonarink-cli`，清单位于 `manifests/k/kusutori/tonarink-cli/<version>`。使用 `InstallerType: zip`、`NestedInstallerType: portable`，嵌套文件为 `tonarink-cli.exe`，`PortableCommandAlias: tonarink-cli`。portable 不支持在清单中指定 `Scope`，安装时可加 `--scope user` 选择不需要管理员权限的用户安装。清单里的 `Commands` 只是命令元数据，实际命令路径由 portable alias 注册；不需要 MSIX 签名。

GitHub Release 托管二进制，winget-pkgs 只存清单。第一次提交可能需要接受微软 CLA、人工审核或按社区要求调整元数据；自动生成 PR 不代表马上可安装。未签名 EXE 仍可能受到 SmartScreen / 本机策略检查。

## 重试与维护

- GitHub Release 先建为 draft，完整上传后公开，避免社区清单引用不完整附件
- 公开附件不会覆盖；重试时检查文件哈希一致，若代码或打包内容改变，必须发布新版本
- NuGet 使用 `--skip-duplicate`，同一版本不会覆盖；有缺陷的包应发布新版本
- winget 提交前检查已合并版本和自己创建的同名未关闭 PR，避免重复提交
- 若只有 NuGet / winget 发布步骤失败，修复凭据后使用 Actions 的 **Re-run failed jobs**，复用原构建产物；不要全量重新构建同一公开版本
- 构建 SDK 当前固定为 `11.0.100-rc.1.26425.128`，跟随仓库 `global.json` 升级；Native AOT 工具的运行不需要该 SDK 或其共享运行时，NuGet 安装需要 .NET SDK 10+ 支持 RID 工具
- WingetCreate 固定为 `1.12.13.0` 并校验官方附件 SHA256；它自身需要 .NET 9，因此仅在提交清单的 CI job 安装此 SDK，与 AOT CLI 用户的运行时要求无关

普通 `ci.yml` 也使用相同的六平台矩阵，构建选择器包与 AOT 子包、实际安装并运行原生 CLI（Windows ARM64 仅格式和架构检查），防止只在发布标签时才发现工具包不能安装。`Verify-CliTool.ps1` 是隔离 profile 下的真实命令检查，没有添加单元测试用例。

本地生成 portable / 清单：

```powershell
dotnet pack src/Tonarink.Cli/Tonarink.Cli.csproj -c Release -o artifacts/cli/native -p:CreateRidSpecificToolPackages=false
./tools/Build-CliPortable.ps1 -RuntimeIdentifier win-x64 -Version 1.1.0 -OutputDirectory artifacts/cli/native -PackTool
./tools/Build-CliPortable.ps1 -RuntimeIdentifier win-arm64 -Version 1.1.0 -OutputDirectory artifacts/cli/native -PackTool
./tools/New-CliWingetManifest.ps1 -Version 1.1.0 -AssetDirectory artifacts/cli/native -OutputDirectory artifacts/cli/winget
winget validate --manifest artifacts/cli/winget/manifests/k/kusutori/tonarink-cli/1.1.0 --disable-interactivity
```

上面的命令在 Windows 运行；Linux / macOS 在对应系统上用相同脚本传入本机 RID。Windows 跨架构 Native AOT 构建需要 Visual Studio 的对应 C++ 工具链和 Windows SDK。脚本使用唯一临时目录并保留用于诊断，不删除用户文件；若输出便携包已存在会拒绝覆盖，请使用新的输出目录。

`-PackTool` 使用 SDK 的 `dotnet pack -r <RID>` 生成 `DotnetToolRidPackage`，然后从同一个 PublishDir 创建 portable ZIP / tar.gz。省略该开关则仅生成 portable 包。构建选择器时的 `CreateRidSpecificToolPackages=false` 只关闭当前 SDK 11 自动编排 RID 编译的步骤，不清除平台映射，也不会生成 JIT 实现；六个子包由矩阵分别构建。
