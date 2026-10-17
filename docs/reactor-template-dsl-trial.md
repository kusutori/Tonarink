# 模板 DSL 本地试验

- Reactor：`D:/dotnet/reactor-templates`，分支 `feat/declarative-data-template`
- App：`D:/dotnet/Tonarink-templates`，分支 `try-reactor-templates`
- 基线：Reactor `e168195`，Tonarink `ded6940`
- 本地包：`0.1.0-preview.16-templates.2`，不得发布到正式渠道
- 不混入 ContentDialog 主题 PR、player Source 试验或原工作树的未提交改动

`HistoryFileTypePicker.PickerState` 使用 `Templates.Data(() => HStack(...))`
声明布局，通过 `.Bind(依赖属性, Binding)` 调用原生 `SetBinding`，
FileTypeToken 的 AOT 绑定元数据不变。Reactor 工厂直接调用，原生控件使用
`using Xaml = Microsoft.UI.Xaml.Controls`，例如 `Xaml.FontIcon.GlyphProperty`。
同一个 owner.Template 供 token 和候选两处复用，每次模板实例化生成独立控件。
PickerState 存活期间模板稳定；组件卸载时释放 owner，临时 Unloaded 不销毁模板。

```powershell
dotnet build src/Tonarink.App/Tonarink.App.csproj -c Debug -p:Platform=x64 -p:OutputPath=bin/x64/Debug-templates-v2/ -p:RestoreAdditionalProjectSources=D:/dotnet/Tonarink-templates/artifacts/reactor-templates/packages
```

试用入口：接收 → 接收历史 → 筛选 → 文件类型。检查候选和已选 token
的图标及文本、连续添加/删除、关闭重开面板、切换页面和主题。
先退出其他 Tonarink 实例，避免单实例机制把激活转发到旧进程。

这次按要求以编译为验证范围，用户已确认原型可以运行并认可第二版写法；未运行完整测试套件，
也未验证本版 AOT 或 MSIX。实验版不支持模板内组件、Hooks 或外层状态驱动
结构变化，仅使用静态 Element 布局和原生 Binding。

验证结果：Reactor Release 和 app x64 Debug 均编译成功，最终增量构建零警告、
零错误。App 冷构建出现已有 SearchingDevices.json 的 LottieGen LT0027 警告。
两份 API 索引已同步生成。本分支作为讨论用原型，不用于发布产品包或提交实现 PR。

第一版可用快照已本地提交：Reactor `e3129aa3`、app `eb57f96`。
第二版只改进命名和绑定写法，模板生命周期不变。
第二版编译成功，零警告、零错误。原输出目录被正在运行的第一版进程锁定，
因此保留该进程，使用独立输出目录生成第二版，没有强制结束应用。

可执行文件：
`D:/dotnet/Tonarink-templates/src/Tonarink.App/bin/x64/Debug-templates-v2/Tonarink.exe`
