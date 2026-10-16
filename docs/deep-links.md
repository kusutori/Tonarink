# 应用深度链接

MSIX 包已注册 `tonarink` 协议，可从 Windows 运行对话框、浏览器或其他应用打开以下地址：

| 地址 | 行为 |
| --- | --- |
| `tonarink:///open` | 恢复主窗口，保留当前页面 |
| `tonarink:///receive` | 打开接收页 |
| `tonarink:///send` | 打开发送页 |
| `tonarink:///settings` | 打开设置页 |
| `tonarink:///history` | 打开接收历史，返回接收页 |
| `tonarink:///send?favorite=FINGERPRINT` | 打开收藏设备列表并定位指定设备 |
| `tonarink:///history?id=UUID` | 打开指定接收记录的详情，返回接收页 |
| `tonarink:///receive/web` | 打开网页接收，返回接收页 |
| `tonarink:///send/web` | 打开网页分享，返回发送页 |
| `tonarink:///settings/network-interfaces` | 打开网络接口设置，返回设置页 |

例如：

```powershell
Start-Process 'tonarink:///settings'
```

参数值必须进行 URI 编码。收藏指纹和历史 ID 必须存在于当前应用数据中。
URI 使用无主机的路径形式。未知路由、未知或重复参数、无效 UUID、主机、用户凭据、端口及片段会被拒绝并记录诊断日志。
不开放删除记录、退出应用、启动服务或执行任意文件的深度链接。

## 入口与生命周期

- 外部协议通过 `IProtocolActivatedEventArgs.Uri` 进入 `AppDeepLinks`
- 新跳转列表把 URI 作为 `JumpListItem` 的启动参数，因此通过 Launch 而非 Protocol 激活进入同一个解析器
- 路径、参数与父级返回栈由 Reactor 的 `DeepLinkMap` 链式映射解析，页面仍使用 `AppRoute` 枚举
- CLI 保留 `System.CommandLine`；收藏和历史入口复用同一深链映射后提交 `AppNavigationRequest`
- 托盘菜单也直接提交类型化请求，不调用 Windows 再激活自身
- `AppNavigationService` 缓存冷启动请求，在启动画面结束、Shell 挂载后串行处理
- 运行中激活沿用现有 `AppInstance` 单实例转发，导航沿用现有页面缓存与过渡协调器，不创建新的 Page
- 导航前校验目标并完成已有收藏/历史弹窗的关闭；不强制丢弃其他编辑弹窗
- 应用合成返回栈前保留离开/进入页面生命周期；相同页面再次激活也会更新返回栈并清空前进栈
- CLI 导航遵循取消/超时参数，反馈采用当前 CLI 语言

未打包版不向 Windows 注册协议，但可显式运行 `Tonarink.exe 'tonarink:///settings'`。
独立的 `tonarink-cli` 不提供 GUI 深度链接。打开 Windows 系统设置使用的 `ms-settings:` 协议不受此改动影响。
