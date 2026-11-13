(() => {
  if (location.href === 'about:blank') return;
  if (window.__streamBrowserBridge?.document === document || !window.chrome?.webview) return;
  const bridge = window.__streamBrowserBridge = {};
  bridge.document = document;
  // randomUUID is available only in secure contexts; direct HTTP pages still
  // provide getRandomValues and need the same document/video tracking.
  const randomId = () => [...crypto.getRandomValues(new Uint8Array(16))].map(b=>b.toString(16).padStart(2,'0')).join('');
  const doc = randomId(), videos = new Map(), ids = new WeakMap(), encrypted = new WeakSet();
  const stopped = new Set(), blockedUrls = new Set(), blockedPrefixes = new Set(), activeFetches = new Map(), xhrs = new Map(), lastSource = new WeakMap();
  let hovered = null, hoverStamp = 0, host = null, shadow = null, box = null, toolbar = null;
  let candidates = [], handoffToken = '', busy = false, singleStopped = false;
  const absolute = u => { try { return new URL(u, location.href).href; } catch { return ''; } };
  const mediaUrl = u => /\.(m3u8|mp4|m4s|ts|aac|m4a)(?:[?#]|$)/i.test(u);
  const sourceOf = v => v.hasAttribute('src') ? v.src : (v.currentSrc || v.querySelector('source')?.src || '');
  const isBlocked = u => blockedUrls.has(u) || [...blockedPrefixes].some(p => u.startsWith(p)) || (singleStopped && mediaUrl(u));
  const send = (type, data = {}) => chrome.webview.postMessage({ type, doc, frameUrl: location.href, ...data });
  function id(v) {
    if (!ids.has(v)) { const value = randomId(); ids.set(v, value); videos.set(value, v);
      v.addEventListener('encrypted', () => { encrypted.add(v); if (hovered === v) inspect(v); });
      v.addEventListener('play', () => { if (stopped.has(v)) release(v); });
    }
    return ids.get(v);
  }
  function inspect(v) {
    if (stopped.has(v)) return;
    lastSource.set(v,sourceOf(v));
    send('hover', { id: id(v), src: sourceOf(v),
      currentTime: Number.isFinite(v.currentTime) ? v.currentTime : 0,
      duration: Number.isFinite(v.duration) ? v.duration : null, encrypted: encrypted.has(v) || !!v.mediaKeys });
  }
  function ensureOverlay() {
    if (host?.isConnected) return;
    host = document.createElement('div'); host.setAttribute('data-streambrowser-overlay', '');
    host.style.cssText = 'all:initial;position:fixed;inset:0;pointer-events:none;z-index:2147483647';
    shadow = host.attachShadow({ mode: 'closed' });
    const style = document.createElement('style');
    style.textContent = `*{box-sizing:border-box} .box{position:fixed;border:3px solid #e8b44b;border-radius:8px;pointer-events:none}
      .toolbar{position:absolute;top:10px;right:10px;max-width:calc(100% - 20px);display:flex;flex-wrap:wrap;gap:6px;align-items:center;padding:9px;background:#13232ff5;border-radius:8px;color:white;font:13px system-ui;pointer-events:auto;box-shadow:0 3px 15px #0008}
      button,select{font:13px system-ui;border:0;border-radius:5px;padding:8px;max-width:270px}button{background:#54dd8c;color:#092117;font-weight:700;cursor:pointer}button:disabled{background:#718b81;cursor:default}select{background:#f3f8f5;color:#10231b}`;
    box = document.createElement('div'); box.className = 'box'; toolbar = document.createElement('div'); toolbar.className = 'toolbar';
    box.append(toolbar); shadow.append(style, box); document.documentElement.append(host);
  }
  function position() {
    if (!hovered?.isConnected) { if (host) host.style.display = 'none'; return; }
    const r = hovered.getBoundingClientRect();
    if (r.width < 30 || r.height < 20) { if (host) host.style.display = 'none'; return; }
    ensureOverlay(); host.style.display = ''; Object.assign(box.style, { left: r.left+'px', top: r.top+'px', width:r.width+'px',height:r.height+'px' });
  }
  function show(message, ready = false) {
    ensureOverlay(); box.style.borderColor = ready ? '#3ee480' : '#e8b44b'; toolbar.replaceChildren();
    const label = document.createElement('span'); label.textContent = message; toolbar.append(label);
    if (ready) {
      let select;
      if (candidates.length > 1) {
        select = document.createElement('select');
        const placeholder = document.createElement('option'); placeholder.textContent = 'Stream auswählen …'; placeholder.value = ''; select.append(placeholder);
        candidates.forEach(c => { const opt = document.createElement('option'); opt.value=c.url; opt.textContent=c.label; opt.title=c.url; select.append(opt); });
        toolbar.append(select);
      }
      const button = document.createElement('button'); button.textContent = 'In VLC öffnen'; button.disabled = !!select;
      select?.addEventListener('change', () => button.disabled = !select.value);
      button.addEventListener('click', e => {
        e.preventDefault(); e.stopPropagation(); if (busy || !hovered) return;
        if(sourceOf(hovered)!==lastSource.get(hovered)){candidates=[];handoffToken='';show('Stream wird geprüft …');inspect(hovered);return;}
        const selected = select ? select.value : candidates[0]?.url; if (!selected) return;
        busy = true; button.disabled = true; label.textContent = 'VLC wird gestartet …';
        send('handoff', { id:id(hovered), url:selected, token:handoffToken,
          currentTime:Number.isFinite(hovered.currentTime)?hovered.currentTime:0 });
      }); toolbar.append(button);
    }
    position();
  }
  function videoAt(e) {
    const direct=e.composedPath().find(n=>n instanceof HTMLVideoElement); if(direct)return direct;
    const target=e.composedPath().find(n=>n instanceof Element);
    return [...document.querySelectorAll('video')].filter(v=>{
      const r=v.getBoundingClientRect(),style=getComputedStyle(v);
      if(r.width<30||r.height<20||e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom||style.visibility==='hidden'||Number(style.opacity)===0)return false;
      // Player controls commonly cover the video. Accept an overlay belonging to
      // the same compact player container, rather than unrelated page popovers.
      for(let p=v.parentElement;p;p=p.parentElement){
        const bounds=p.getBoundingClientRect();
        if(bounds.width*bounds.height>r.width*r.height*2.2)break;
        if(p.contains(target))return true;
      }
      return false;
    }).sort((a,b)=>a.clientWidth*a.clientHeight-b.clientWidth*b.clientHeight)[0];
  }
  document.addEventListener('pointermove', e => {
    if (e.composedPath().includes(host)) return;
    const v = videoAt(e);
    if (v && v !== hovered) { hovered=v; candidates=[]; busy=false; show('Stream wird geprüft …'); inspect(v); hoverStamp=Date.now(); }
    else if (v && Date.now()-hoverStamp>4000 && !busy) { inspect(v); hoverStamp=Date.now(); }
    else if (!v && !busy) { hovered=null; if(host)host.style.display='none'; }
  }, true);
  window.addEventListener('scroll', position, true); window.addEventListener('resize', position);
  function release(v) {
    v.pause(); v.removeAttribute('src'); v.srcObject=null;
    v.querySelectorAll('source').forEach(s => s.removeAttribute('src'));
    try { v.load(); } catch {}
  }
  chrome.webview.addEventListener('message', e => {
    const m=e.data; if (!m || m.doc!==doc) return;
    if (m.type==='result' && hovered && id(hovered)===m.id && !stopped.has(hovered)) {
      candidates=m.candidates||[]; handoffToken=m.token||''; busy=false;
      show(m.message, candidates.length>0);
    }
    if (m.type==='stop') {
      const v=videos.get(m.id); if(!v) return;
      m.urls.forEach(u=>blockedUrls.add(u)); m.prefixes.forEach(u=>blockedPrefixes.add(u));
      singleStopped=document.querySelectorAll('video').length===1;
      stopped.add(v); release(v);
      for (const [controller,url] of activeFetches) if(isBlocked(url))controller.abort();
      for (const [xhr,url] of xhrs) if(isBlocked(url))xhr.abort();
      busy=false; if(hovered===v)show('Spielt in VLC · Rückkehr über „Im Browser neu laden“');
      send('stopped',{id:m.id,paused:v.paused,source:v.currentSrc||v.getAttribute('src')||''});
    }
  });
  const originalFetch = window.fetch;
  window.fetch = function(input, options) {
    const url=absolute(typeof input==='string'||input instanceof URL ? String(input):input.url);
    if(isBlocked(url))return Promise.reject(new DOMException('Stream spielt in VLC','AbortError'));
    // A mixed-content/CORS failure never reaches the response hook. Remember
    // manifest attempts too; the host still verifies every candidate before VLC.
    if(/\.m3u8(?:[?#]|$)/i.test(url))send('network',{url,contentType:''});
    const controller=new AbortController(), signal=options?.signal || (input instanceof Request ? input.signal:null);
    if(signal?.aborted)controller.abort(); else signal?.addEventListener('abort',()=>controller.abort(),{once:true});
    activeFetches.set(controller,url);
    return originalFetch.call(this,input,{...options,signal:controller.signal}).then(response=>{
      const type=response.headers.get('content-type')||'';
      if(/mpegurl|video\//i.test(type)||/\.m3u8(?:[?#]|$)/i.test(url))send('network',{url:response.url||url,contentType:type});
      if(mediaUrl(url)||/mpegurl|video\//i.test(type))setTimeout(()=>activeFetches.delete(controller),120000);
      else activeFetches.delete(controller);
      return response;
    },error=>{activeFetches.delete(controller);throw error;});
  };
  const open=XMLHttpRequest.prototype.open, xhrSend=XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open=function(method,url,...rest){this.__sbUrl=absolute(url);return open.call(this,method,url,...rest);};
  XMLHttpRequest.prototype.send=function(...args){
    const url=this.__sbUrl||''; if(isBlocked(url)){this.abort();return;}
    if(/\.m3u8(?:[?#]|$)/i.test(url))send('network',{url,contentType:''});
    xhrs.set(this,url); this.addEventListener('loadend',()=>{
      xhrs.delete(this); const type=this.getResponseHeader('content-type')||'';
      if(/mpegurl|video\//i.test(type)||/\.m3u8(?:[?#]|$)/i.test(url))send('network',{url:this.responseURL||url,contentType:type});
    },{once:true});return xhrSend.apply(this,args);
  };
  function watch() {
    document.querySelectorAll('video').forEach(v=>{id(v); if(stopped.has(v) && (v.hasAttribute('src')||v.srcObject))release(v);});
    for(const [key,v]of videos)if(!v.isConnected)videos.delete(key);
    if(hovered && !busy && !stopped.has(hovered) && sourceOf(hovered)!==lastSource.get(hovered)) {
      candidates=[];handoffToken='';show('Stream wird geprüft …');inspect(hovered);
    }
    position();
  }
  new MutationObserver(watch).observe(document,{childList:true,subtree:true,attributes:true,attributeFilter:['src']});
  bridge.inspect = v => {hovered=v;show('Stream wird geprüft …');inspect(v);};
  bridge.snapshot = () => ({doc,videos:[...videos].map(([key,v])=>({id:key,src:v.currentSrc||v.src,paused:v.paused,stopped:stopped.has(v)})),candidates,busy,tokenReady:!!handoffToken,hovered:hovered?id(hovered):null,overlayVisible:host?.isConnected&&host.style.display!=='none'});
  bridge.activate = url => {const b=toolbar?.querySelector('button'),s=toolbar?.querySelector('select');if(s){s.value=url;s.dispatchEvent(new Event('change'));}b?.click();};
  send('hello'); if(document.readyState!=='loading')watch();else document.addEventListener('DOMContentLoaded',()=>{send('hello');watch();},{once:true});
})();
