window.tonarink = {
    readClipboard: () => navigator.clipboard.readText(),
    writeClipboard: text => navigator.clipboard.writeText(text),
    requestNotifications: () => 'unsupported',
    notify: () => {}
};
