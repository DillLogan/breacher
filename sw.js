// Breach Point: retired offline cache. The old HTML version installed a service worker that served every file cache-first,
// so browsers kept loading the first Unity build they ever saw. This replacement deletes all of its caches, unregisters itself
// and reloads open tabs, so the live build loads straight from GitHub Pages from now on.
self.addEventListener('install', e => self.skipWaiting());
self.addEventListener('activate', e => {
  e.waitUntil((async () => {
    for (const k of await caches.keys()) await caches.delete(k);
    await self.registration.unregister();
    for (const c of await self.clients.matchAll({ type: 'window' })) c.navigate(c.url);
  })());
});
