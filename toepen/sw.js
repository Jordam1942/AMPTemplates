// Offline support: app files are cached on install; Google Fonts are cached
// the first time they load. Everything else (such as the live
// game connections) goes straight to the network.
var CACHE = 'toepen-v35';
var FILES = [
  './', 'index.html', 'scorebord.html', 'spelen.html', 'handleiding.html', 'saves.js', 'prefs.js', 'sfx.js', 'engine.js', 'net.js', 'ring.js', 'peer.js', 'manifest.webmanifest',
  'icons/icon.svg', 'icons/icon-192.png', 'icons/icon-512.png',
  'vendor/qrcode.js', 'vendor/jsqr.js', 'vendor/peerjs.min.js'
];
var FONT_HOSTS = ['fonts.googleapis.com', 'fonts.gstatic.com'];

self.addEventListener('install', function (e) {
  e.waitUntil(caches.open(CACHE).then(function (c) { return c.addAll(FILES); }));
  self.skipWaiting();
});

self.addEventListener('activate', function (e) {
  e.waitUntil(caches.keys().then(function (keys) {
    return Promise.all(keys.filter(function (k) { return k !== CACHE; }).map(function (k) { return caches.delete(k); }));
  }));
  self.clients.claim();
});

// Network first so updates arrive; fall back to the cache when offline.
self.addEventListener('fetch', function (e) {
  if (e.request.method !== 'GET') return;
  var url = new URL(e.request.url), own = url.origin === self.location.origin;
  if (!own && FONT_HOSTS.indexOf(url.hostname) < 0) return;
  e.respondWith(
    // Own files skip the browser's HTTP cache, so a new version shows up right away.
    (own ? fetch(url.href, { cache: 'no-cache' }) : fetch(e.request)).then(function (res) {
      var copy = res.clone();
      caches.open(CACHE).then(function (c) { c.put(e.request, copy); });
      return res;
    }).catch(function () {
      return caches.match(e.request, { ignoreSearch: true });
    })
  );
});
