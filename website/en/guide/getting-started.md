# Getting started

Tonarink is a local-network transfer app for Windows 11 and an unofficial implementation of the LocalSend protocol. Sender and receiver normally only need to be connected to the same local network.

## Install

Download the package for your architecture from [GitHub Releases](https://github.com/kusutori/Tonarink/releases/latest). MSIX is recommended for everyday use because Windows Share and File Explorer context-menu integration require package identity.

1. Extract the downloaded MSIX ZIP.
2. Run `Install.ps1` inside it.
3. Start Tonarink and allow access to the local network.

## Send

Open **Send**, choose files, folders, text, or clipboard content, then select a nearby device. Direct addresses, favorites, and link sharing are also available.

## Receive

Tonarink displays incoming requests from nearby devices. Review the sender and content, then accept, reject, or select individual files.

::: info About LocalSend
Tonarink is an unofficial LocalSend protocol implementation and is not affiliated with or endorsed by the LocalSend project. Protocol compatibility enables devices in both ecosystems to communicate.
:::
