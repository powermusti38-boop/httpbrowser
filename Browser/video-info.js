(() => {
  if (location.href === 'about:blank' || window.__httpBrowserVideoInfo === document) return;
  window.__httpBrowserVideoInfo = document;
  const watched = new WeakSet();
  function preload(video) {
    video.preload = 'auto';
    // Some sites expose their hls.js instance. Only adjust the instance that
    // actually owns this video. Do not reload or replace the page's source.
    const player = window.hls;
    if (player?.media === video && player.config && Number.isFinite(video.duration)) {
      const remaining = Math.max(30, video.duration - video.currentTime + 5);
      player.config.maxBufferLength = remaining;
      player.config.maxMaxBufferLength = remaining;
      player.startLoad?.(-1);
    }
  }
  function report() {
    const videos = [...document.querySelectorAll('video')];
    for (const video of videos) {
      if (!watched.has(video)) {
        watched.add(video); video.preload = 'auto';
        video.addEventListener('pause', () => preload(video));
      }
    }
    const visible = videos.filter(v => { const r=v.getBoundingClientRect(); return r.width>20 && r.height>20 && getComputedStyle(v).visibility!=='hidden'; });
    const video = visible.sort((a,b) => Number(!b.paused)-Number(!a.paused) || b.clientWidth*b.clientHeight-a.clientWidth*a.clientHeight)[0];
    if (!video) { chrome.webview.postMessage({type:'videoInfo',visible:false}); return; }
    let ahead = 0;
    for(let i=0;i<video.buffered.length;i++)
      if(video.buffered.start(i)<=video.currentTime+.25 && video.buffered.end(i)>=video.currentTime)
        ahead=Math.max(ahead,video.buffered.end(i)-video.currentTime);
    const r=video.getBoundingClientRect();
    chrome.webview.postMessage({type:'videoInfo',visible:true,name:(navigator.mediaSession?.metadata?.title || video.getAttribute('title') || document.title || location.hostname).slice(0,160),
      current:Number.isFinite(video.currentTime)?video.currentTime:0,duration:Number.isFinite(video.duration)?video.duration:null,
      ahead,playing:!video.paused,area:r.width*r.height});
  }
  setInterval(report, 750);
  report();
})();
