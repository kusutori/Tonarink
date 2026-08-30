const cacheName = "tonarink-shell-v1";
const shell = ["/", "/manifest.webmanifest", "/icons/tonarink.svg", "/tonarink.js"];
self.addEventListener("install", event => event.waitUntil(caches.open(cacheName).then(cache => cache.addAll(shell)).catch(() => {})));
self.addEventListener("activate", event => event.waitUntil(caches.keys().then(keys => Promise.all(keys.filter(key => key !== cacheName).map(key => caches.delete(key))))));
self.addEventListener("fetch", event => {
    if (event.request.method !== "GET" || new URL(event.request.url).origin !== self.location.origin)
        return;
    event.respondWith(fetch(event.request).catch(() => caches.match(event.request).then(response => response || caches.match("/"))));
});
self.addEventListener("notificationclick", event => {
    event.notification.close();
    event.waitUntil(clients.matchAll({ type: "window", includeUncontrolled: true }).then(windows => {
        const existing = windows[0];
        return existing ? existing.focus() : clients.openWindow("/");
    }));
});
