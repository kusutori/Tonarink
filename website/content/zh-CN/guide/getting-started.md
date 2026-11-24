# 开始使用

Tonarink is a local-network transfer app for Windows 11 and an unofficial implementation of the LocalSend protocol. Sender and receiver normally only need to be connected to the same local network.

## 安装

前往 [GitHub Releases](https://github.com/kusutori/Tonarink/releases/latest) 下载适合设备架构的安装包。日常使用推荐 MSIX，因为 Windows 分享菜单、文件资源管理器右键菜单等功能依赖打包身份。 MSIX is recommended for everyday use because Windows Share and File Explorer context-menu integration require package identity.

1. 解压下载的 MSIX ZIP。
2. 运行其中的 `Install.ps1`。
3. 启动 Tonarink，并允许应用访问本地网络。

## 发送内容

Open **Send**, choose files, folders, text, or clipboard content, then select a nearby device. Direct addresses, favorites, and link sharing are also available.

## 接收内容

Tonarink 会显示来自附近设备的请求。确认设备与文件信息后，可以选择接受、拒绝或只接收部分文件。 Review the sender and content, then accept, reject, or select individual files.

:::info 关于 LocalSend
Tonarink 是 LocalSend 协议的非官方兼容实现，与 LocalSend 项目不存在隶属或背书关系。协议兼容让两个生态中的设备可以互相通信。 Protocol compatibility enables devices in both ecosystems to communicate.
:::
