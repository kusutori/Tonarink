# 模板 DSL 本地试验

- Reactor：`D:/dotnet/reactor-templates`，分支 `feat/declarative-data-template`
- App：`D:/dotnet/Tonarink-templates`，分支 `try-reactor-templates`
- 基线：Reactor `e168195`，Tonarink `ded6940`
- 本地包：`0.1.0-preview.16-templates.1`，不得发布到正式渠道
- 不混入 ContentDialog 主题 PR、player Source 试验或原工作树的未提交改动

`HistoryFileTypePicker.PickerState` 使用 `Templates.Data(() => HStack(...))`
声明布局，原生 `SetBinding` 和 FileTypeToken 的 AOT 绑定元数据不变。
同一个 owner.Template 供 token 和候选两处复用，每次模板实例化生成独立控件。
PickerState 存活期间模板稳定；组件卸载时释放 owner，临时 Unloaded 不销毁模板。

```powershell
dotnet build src/Tonarink.App/Tonarink.App.csproj -c Debug -p:Platform=x64 -p:RestoreAdditionalProjectSources=D:/dotnet/Tonarink-templates/artifacts/reactor-templates/packages
```

试用入口：接收 → 接收历史 → 筛选 → 文件类型。检查候选和已选 token
的图标及文本、连续添加/删除、关闭重开面板、切换页面和主题。
先退出其他 Tonarink 实例，避免单实例机制把激活转发到旧进程。

这次按要求以编译为验证范围，实际交互由用户确认；未运行完整测试套件，
也未验证本版 AOT 或 MSIX。实验版不支持模板内组件、Hooks 或外层状态驱动
结构变化，仅使用静态 Element 布局和原生 Binding。

验证结果：Reactor Release 和 app x64 Debug 均编译成功，最终增量构建零警告、
零错误。App 冷构建出现已有 SearchingDevices.json 的 LottieGen LT0027 警告。
两份 API 索引已同步生成；没有发布 issue、PR 或推送实验分支。

可执行文件：
`D:/dotnet/Tonarink-templates/src/Tonarink.App/bin/x64/Debug/net11.0-windows10.0.22621.0/Tonarink.exe`
