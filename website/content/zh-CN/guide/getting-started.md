# 开始使用

Tonarink 是一款面向 Windows 11 的局域网传输工具，并兼容 LocalSend 协议。发送方和接收方只需连接到同一个本地网络，通常无需手动配置地址。

## 安装

前往 [GitHub Releases](https://github.com/kusutori/Tonarink/releases/latest) 下载适合设备架构的安装包。日常使用推荐 MSIX，因为 Windows 分享菜单、文件资源管理器右键菜单等功能依赖打包身份。

1. 解压下载的 MSIX ZIP。
2. 运行其中的 `Install.ps1`。
3. 启动 Tonarink，并允许应用访问本地网络。

## 发送内容

打开“发送”页，选择文件、文件夹、文本或剪贴板内容，再选择附近设备。也可以通过地址、收藏夹或链接完成发送。

## 接收内容

Tonarink 会显示来自附近设备的请求。确认设备与文件信息后，可以选择接受、拒绝或只接收部分文件。

::: info 关于 LocalSend
Tonarink 是 LocalSend 协议的非官方兼容实现，与 LocalSend 项目不存在隶属或背书关系。协议兼容让两个生态中的设备可以互相通信。
:::
