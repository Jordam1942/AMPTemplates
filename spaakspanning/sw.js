// Spaakspanning: offline gebruik. De app zelf: eerst netwerk (updates komen direct binnen), anders uit de cache.
// Lettertype en stijl-bibliotheek: uit de cache, op de achtergrond bijgewerkt.
const CACHE = 'spaakspanning-v1';
const CORE = ['./', './index.html', './manifest.webmanifest', './icons/icon-192.png', './icons/icon-512.png', './icons/apple-touch-icon.png'];
self.addEventListener('install', (e) => { e.waitUntil(caches.open(CACHE).then((c) => c.addAll(CORE)).then(() => self.skipWaiting())); });
self.addEventListener('activate', (e) => { e.waitUntil(caches.keys().then((ks) => Promise.all(ks.filter((k) => k !== CACHE).map((k) => caches.delete(k)))).then(() => self.clients.claim())); });
self.addEventListener('fetch', (e) => {
  const req = e.request; if (req.method !== 'GET') return;
  const url = new URL(req.url);
  if (url.searchParams.has('nocache')) return; // versiecontrole van de app: altijd echt netwerk
  const same = url.origin === self.location.origin;
  if (same && (req.mode === 'navigate' || url.pathname.endsWith('/index.html') || url.pathname.endsWith('/'))) {
    e.respondWith(fetch(req).then((r) => { const cp = r.clone(); caches.open(CACHE).then((c) => c.put('./index.html', cp)); return r; })
      .catch(() => caches.match('./index.html').then((r) => r || caches.match('./'))));
    return;
  }
  const cdn = /(^|\.)cdn\.tailwindcss\.com$|fonts\.(googleapis|gstatic)\.com$/.test(url.hostname);
  if (same || cdn) {
    e.respondWith(caches.match(req).then((hit) => { const net = fetch(req).then((r) => { if (r && (r.ok || r.type === 'opaque')) { const cp = r.clone(); caches.open(CACHE).then((c) => c.put(req, cp)); } return r; }).catch(() => hit); return hit || net; }));
  }
});
