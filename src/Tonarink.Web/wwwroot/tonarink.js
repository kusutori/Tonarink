window.tonarink = {
    readClipboard: () => navigator.clipboard.readText(),
    writeClipboard: text => navigator.clipboard.writeText(text),
    requestNotifications: () => !('Notification' in window) ? 'unsupported' : Notification.requestPermission(),
    notify: async (title, body) => {
        if (!('Notification' in window) || Notification.permission !== 'granted') return;
        const registration = await navigator.serviceWorker?.ready;
        if (registration) await registration.showNotification(title, { body, icon: '/icons/tonarink.svg' });
        else new Notification(title, { body });
    }
};
