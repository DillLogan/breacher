// Breach Point offline cache. Bump VERSION whenever index.html changes so phones pick up the update.
const VERSION='bp-v8';
const PRECACHE=[
 "./",
 "index.html",
 "manifest.webmanifest",
 "icon-192.png",
 "icon-512.png",
 "apple-touch-icon.png",
 "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/postprocessing/EffectComposer.js",
 "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/postprocessing/RenderPass.js",
 "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/postprocessing/ShaderPass.js",
 "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/postprocessing/UnrealBloomPass.js",
 "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/shaders/CopyShader.js",
 "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/shaders/FXAAShader.js",
 "https://cdn.jsdelivr.net/npm/three@0.128.0/examples/js/shaders/LuminosityHighPassShader.js",
 "https://cdnjs.cloudflare.com/ajax/libs/three.js/r128/three.min.js",
 "https://fonts.googleapis.com/css2?family=Barlow:wght@400;500;600;700&family=Barlow+Condensed:wght@500;600;700&family=IBM+Plex+Mono:wght@500&display=swap",
 "https://cdn.jsdelivr.net/npm/cannon-es@0.20.0/dist/cannon-es.js"
];
self.addEventListener('install',e=>{e.waitUntil(caches.open(VERSION).then(c=>Promise.all(PRECACHE.map(u=>fetch(u,{mode:u.startsWith('http')?'cors':'same-origin'}).then(res=>{if(res.ok)return c.put(u,res);}).catch(()=>{})))).then(()=>self.skipWaiting()));});
self.addEventListener('activate',e=>{e.waitUntil(caches.keys().then(ks=>Promise.all(ks.filter(k=>k!==VERSION).map(k=>caches.delete(k)))).then(()=>self.clients.claim()));});
self.addEventListener('fetch',e=>{const r=e.request;if(r.method!=='GET')return;const u=new URL(r.url);
  // the page itself: network first so updates arrive, cache as the fallback when offline
  if(r.mode==='navigate'){e.respondWith(fetch(r).then(res=>{const cp=res.clone();caches.open(VERSION).then(c=>c.put('index.html',cp));return res;}).catch(()=>caches.match('index.html')));return;}
  // everything else (libraries, fonts, icons): cache first
  e.respondWith(caches.match(r,{ignoreVary:true}).then(hit=>hit||caches.match(r.url,{ignoreVary:true})).then(hit=>hit||fetch(r).then(res=>{if(res&&(res.ok||res.type==='opaque')){const cp=res.clone();caches.open(VERSION).then(c=>c.put(r,cp));}return res;})));});
