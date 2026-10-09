// Toepen has moved to a new address. This service worker only cleans up the old app and removes itself.
self.addEventListener('install', function () { self.skipWaiting(); });
self.addEventListener('activate', function (e) {
  e.waitUntil(caches.keys().then(function (keys) {
    return Promise.all(keys.filter(function (k) { return /^toepen-v/.test(k); }).map(function (k) { return caches.delete(k); }));
  }).then(function () { return self.registration.unregister(); }));
});
// Nothing is cached any more: every request goes to the network.
