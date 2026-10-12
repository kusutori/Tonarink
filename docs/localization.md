# 多语言适配进展

目标：为所有产品项目补齐日语、韩语、法语和西班牙语，保留英语与简体中文。更新日期：2026-10-12。

各产品项目的四种新语言资源及入口已补齐。验证以资源检查、现有命令入口和编译为主，没有新增或运行测试用例。Android 缺少 SDK 平台，用户明确选择不下载、保留未验证说明；Apple 托管编译不等于原生签名、安装或实机验证。以下分别记录实现与验证边界，不把局部编译当作所有平台已实测。

## 已有实现

| 项目 | 当前适配 |
| --- | --- |
| Tonarink.Application | 共享语言选择及地区匹配；旧语言编号 0–2 保持兼容，新增语言使用 3–6；六种语言各 51 条内嵌后端文案 |
| Tonarink.App | 六种语言各 457 条 RESW；设置、收发、设备、历史、托盘、通知等界面，包声明及集成 CLI 设置入口 |
| Tonarink.ExplorerCommand | 读取 App 的 RESW，地区变体匹配到支持的资源目录 |
| Tonarink.WidgetProvider | 正常快照和无快照时的六语言文案 |
| Tonarink.CommandLine | 六种语言各 178 条 RESX，资源内嵌；语言作用域、系统匹配、验证及库生成帮助 |
| Tonarink.Cli | 使用共用命令库；独立浏览器语言设置接受六种明确语言标签 |
| LocalSendDotNet.Core | 浏览器上传、下载状态、PIN 和错误提示；浏览器原生数字与复数格式 |
| LocalSendDotNet.Core.Mobile | 引用相同 Core 源码，继承浏览器文案；net11.0、iOS、MacCatalyst 托管目标已编译 |
| Tonarink.Blazor.Shared | 六种语言各 143 条 RESX；现有文化切换和资源加载逻辑复用，布局同步文档语言 |
| Tonarink.Web | 页面、重连、错误和访问限制提示使用资源；框架语言解析结果经共享规则匹配地区变体，保持 query/cookie/header 顺序 |
| Tonarink.Hybrid | 共享页面、HTML 启动、加载、错误提示，以及文件选择、权限和原生通知均使用资源；Android/Apple/Windows 语言声明已配置 |
| Tonarink.LocalSend | 摘要、通知及业务提示使用共享文案，按设置语言生成；保留项目数量和型号回退标记，切换语言可重新显示已有行，不翻译真实自定义名称 |
| Tonarink.LocalSend.Mobile | 引用相同适配源码及共享资源；net11.0、iOS、MacCatalyst 托管目标已编译 |
| Tonarink.Hybrid.ShareExtension | 系统自带界面跟随 OS；声明六种语言，自定义导入失败反馈使用共享文案，iOS 托管编译通过 |

## 验证记录

- 主 App、ExplorerCommand、WidgetProvider 编译通过，0 警告、0 错误；四种新 App 资源均复制到输出且与源文件哈希相同
- CLI、Core、Blazor.Shared、Web Release 编译通过，0 警告、0 错误
- 最终资源检查：App 457、CommandLine 178、Blazor.Shared 143、Application 51 条，每组英文、中文和四种新语言的资源键及占位符一致；重复项、空值与 JS 语法检查通过
- Hybrid Windows Release 编译通过，0 警告、0 错误；五种非英文 UI 卫星资源均进入输出
- Hybrid iOS 和 ShareExtension 的 Windows 托管构建通过；Hybrid MacCatalyst x64/ARM64 托管构建通过，均 0 警告、0 错误。这不是 Mac 上的原生打包、签名或实机运行
- iOS/MacCatalyst 的 MSBuild BundleResource 均有六份 `.lproj/InfoPlist.strings`，逻辑路径正确；主 App、Hybrid Windows、Apple 主程序和分享扩展的语言声明已核对
- Android `locales_config.xml` 已实际进入 AndroidResource，逻辑路径为 `xml/locales_config.xml`，清单引用已配置；完整编译遇到 XA5207（缺少 Android 37 的 android.jar），用户选择暂不下载 SDK。不得宣称 Android 编译或实机验证通过
- 本地构建 Windows x64 Native AOT CLI，实际 EXE 的六种语言 `--help` 均输出对应帮助并退出 0；调用不启动后台服务，未安装或发布。产物位置：`artifacts/localization/cli-aot-win-x64/`
- 未触发完整测试套件，没有提交、签名、安装或对外发布

## 验证限制与以后可选检查

1. 用户要求不下载 Android SDK；以后有 Android 37 平台时再编译及运行 Android
2. Apple 原生打包、签名、权限对话框和实机效果需在 Mac/iOS 环境验证，不把 Windows 托管构建称为完整原生验证
3. 本次不做动画视觉测试或全部界面的截图比较；长文本、字体回退和母语措辞可后续人工优化

原生配置参考：[MAUI 本地化指南](https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/localization?view=net-maui-10.0)、[Android 应用语言声明](https://developer.android.com/guide/topics/resources/app-languages?hl=en)。Apple 权限说明按 BundleResource 和 `.lproj` 组织；Android 使用 `android:localeConfig` 并显式纳入资源，避免只有清单引用而文件未打包。

CLI 的命令名、设置键、JSON 字段、协议标识和退出码不翻译。底层系统异常与诊断日志保留原始信息，不把它们与用户界面文案混为一谈。商店介绍和社区翻译工作流与应用资源独立，不自动提交商店或发布。
