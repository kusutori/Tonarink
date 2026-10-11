# 文件资源管理器“发送到”集成

未打包版可在设置的“通用”分组开启“文件资源管理器‘发送到’菜单”。默认关闭，仅影响当前用户，不需要管理员权限。MSIX 版禁用此开关，本次不修改包权限或文件虚拟化配置。

开启后，在资源管理器中选中文件或文件夹，右键选择“发送到 → Tonarink”；Windows 11 通常需要先选择“显示更多选项”。应用会打开发送页，并将所选项目加入现有发送列表，用户再选择接收设备，不会自动发送。支持多选及文件夹，连续调用按顺序导入。

## 快捷方式

- 位置：当前用户的 `Environment.SpecialFolder.SendTo`，也可在资源管理器地址栏输入 `shell:sendto` 打开
- 文件名：`Tonarink.lnk`
- 目标：当前运行的 `Tonarink.exe`
- 参数：`app open --`，由资源管理器追加所选项目路径
- 图标：应用自带的 `Assets/AppIcon.ico`，缺失时使用可执行文件图标

应用使用原生 `IShellLinkW` / `IPersistFile` 创建快捷方式，不依赖 PowerShell、WScript 或动态 COM 包装。快捷方式描述中记录归属标记；关闭开关只删除应用自己创建的快捷方式，不覆盖或删除用户已有的同名快捷方式。

启用状态保存在应用设置中，每次启动会同步快捷方式。移动便携版后，需要从新位置启动一次应用以更新目标；移除便携版前可先关闭此开关。

## 命令行入口

```powershell
& .\Tonarink.exe app open -- "C:\Photos\photo.jpg" "C:\Documents"
& .\Tonarink.exe settings set send-to-menu true
& .\Tonarink.exe settings set send-to-menu false
```

路径按独立参数传入，包含空格时应加引号；相对路径按调用者工作目录解析。独立的 `tonarink-cli` 没有 GUI，不提供 `app open` 或此快捷方式设置。

MSIX 的 AppData 文件写入虚拟化可能使新建的 SendTo 快捷方式对资源管理器不可见，因此暂不支持打包版；未来若加入支持，需要另行评估包权限和商店提交要求。集成版 `app open` 的文件参数处理本身不受此限制。
