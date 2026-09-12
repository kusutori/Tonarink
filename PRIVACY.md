# Tonarink Privacy Policy

Effective date: September 12, 2026

Tonarink is an open-source, unofficial implementation of the LocalSend protocol.
This policy describes the Windows application distributed from this repository.

## Summary

Tonarink does not require an account and does not include advertising, analytics,
or developer-operated telemetry. Transfers are made directly between devices on
the local network. Tonarink does not upload transferred content to a server
operated by the project.

## Data used for local-network transfers

To discover and communicate with compatible devices, Tonarink exposes and
processes local-network information such as device name, device type, IP address,
port, protocol version, and device fingerprint. Devices participating in a
transfer process the names, sizes, types, and contents of the items selected by
the user. Web sharing and web receiving temporarily expose a local web endpoint
to devices that can reach the computer over the network. Optional PIN and
verification features can be used to confirm or restrict a connection.

This information is exchanged with devices selected by the user or with devices
on the same reachable network; it is not sent to a Tonarink-operated cloud
service. Use Tonarink only on networks and with devices that you trust.

## Data stored on the device

Depending on enabled features, Tonarink stores application settings, favorite
devices, recently used addresses, optional receive history, and diagnostic logs
in the application's local data directory. Received files are written to the
folder selected by the user. Windows may also provide files to Tonarink when the
user invokes its Share target or File Explorer integration.

Diagnostic logs can contain timestamps, error details, device names, local IP
addresses, file names, and file paths. Review a log before attaching it to a
public issue. Logs are shared with project maintainers only when the user chooses
to upload or send them.

## Operating-system features

Tonarink can use the Windows clipboard, notifications, startup tasks, system
tray, File Explorer integration, and local-network capabilities when the user
invokes or enables the relevant feature. Windows and any third-party software
used to open received files are governed by their own privacy policies.

## Retention and deletion

Local settings, history, favorites, and logs remain on the device until the user
clears them, deletes the application data, or uninstalls the packaged app. Files
already received remain in their destination folder until the user deletes them.

## Changes and contact

Material changes to this policy will be committed to the public repository. For
privacy questions, open an issue at
https://github.com/kusutori/Tonarink/issues. Do not include private files, PINs,
or unreviewed logs in a public issue.
