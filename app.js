/* ============================================================
   墨水大作战 InkTurf — 介绍页交互
   纯原生 JS：滚动显现 / 数字计数 / 导航指示条 / 视频接线
   ============================================================ */
(function () {
  'use strict';

  var reduceMotion = window.matchMedia &&
    window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  /* ---------- 滚动显现（错落 pop） ---------- */
  function setupReveal() {
    var items = Array.prototype.slice.call(document.querySelectorAll('.reveal'));
    if (!items.length) return;

    // 同一容器内兄弟节点错落延迟（0/70/140/210ms 循环）
    var groups = new Map();
    items.forEach(function (el) {
      if (el.hasAttribute('data-d')) return;
      var key = el.parentNode || document.body;
      var idx = groups.get(key) || 0;
      groups.set(key, idx + 1);
      el.style.setProperty('--d', (idx % 4) * 70 + 'ms');
    });

    if (reduceMotion || !('IntersectionObserver' in window)) {
      items.forEach(function (el) { el.classList.add('in'); });
      return;
    }

    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (en.isIntersecting) {
          en.target.classList.add('in');
          io.unobserve(en.target);
        }
      });
    }, { threshold: 0.15, rootMargin: '0px 0px -6% 0px' });

    items.forEach(function (el) { io.observe(el); });
  }

  /* ---------- 数字带计数（cubic ease-out） ---------- */
  function countUp(el, target, dur) {
    var start = null;
    function step(ts) {
      if (start === null) start = ts;
      var t = Math.min(1, (ts - start) / dur);
      var e = 1 - Math.pow(1 - t, 3);
      el.textContent = Math.round(target * e);
      if (t < 1) requestAnimationFrame(step);
      else el.textContent = target;
    }
    requestAnimationFrame(step);
  }

  function setupBand() {
    var band = document.querySelector('.band');
    if (!band) return;
    var nums = Array.prototype.slice.call(band.querySelectorAll('[data-count]'));

    function fill() {
      nums.forEach(function (el) {
        el.textContent = el.getAttribute('data-count');
      });
    }

    if (reduceMotion || !('IntersectionObserver' in window)) { fill(); return; }

    var done = false;
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (en.isIntersecting && !done) {
          done = true;
          nums.forEach(function (el, i) {
            setTimeout(function () {
              countUp(el, parseInt(el.getAttribute('data-count'), 10), 900);
            }, i * 110);
          });
          io.disconnect();
        }
      });
    }, { threshold: 0.4 });
    io.observe(band);
  }

  /* ---------- 导航指示条 + 当前区块高亮 ---------- */
  function setupNav() {
    var nav = document.getElementById('nav');
    var ind = document.getElementById('navInd');
    if (!nav || !ind) return;
    var links = Array.prototype.slice.call(nav.querySelectorAll('a[data-nav]'));
    var sections = links
      .map(function (a) { return document.querySelector(a.getAttribute('href')); })
      .filter(Boolean);

    function moveInd(link) {
      if (!link) { ind.style.opacity = '0'; return; }
      ind.style.opacity = '1';
      ind.style.width = link.offsetWidth + 'px';
      ind.style.transform = 'translateX(' + link.offsetLeft + 'px)';
    }

    function setActive(link) {
      links.forEach(function (a) { a.classList.toggle('on', a === link); });
      moveInd(link);
    }

    if ('IntersectionObserver' in window && sections.length) {
      var io = new IntersectionObserver(function (entries) {
        entries.forEach(function (en) {
          if (en.isIntersecting) {
            var link = nav.querySelector('a[href="#' + en.target.id + '"]');
            if (link) setActive(link);
          }
        });
      }, { threshold: 0.25, rootMargin: '-20% 0px -55% 0px' });
      sections.forEach(function (s) { io.observe(s); });
    }

    links.forEach(function (a) {
      a.addEventListener('click', function () { setActive(a); });
    });

    window.addEventListener('resize', function () {
      var cur = nav.querySelector('a.on');
      if (cur) moveInd(cur);
    });
  }

  /* ---------- Hero 视频 ----------
     宣传视频已落地（AV1 主源 + H.264 兜底）：
     点击黄色播放键 → 隐藏按钮、显示原生控制条并播放。 */
  function setupVideo() {
    var video = document.getElementById('heroVideo');
    var btn = document.getElementById('playBtn');
    if (!video || !btn) return;

    btn.addEventListener('click', function () {
      btn.hidden = true;
      video.controls = true;
      var p = video.play();
      if (p && p.catch) p.catch(function () {});
    });
  }

  /* ---------- 启动 ---------- */
  function init() {
    setupReveal();
    setupBand();
    setupNav();
    setupVideo();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
