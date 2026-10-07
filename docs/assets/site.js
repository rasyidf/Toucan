/* Shared by every page: theme toggle and the header's scrolled state. */
(function () {
  'use strict';
  var root = document.documentElement;
  var theme = document.getElementById('theme-toggle');
  if (theme) theme.addEventListener('click', function () {
    var current = root.getAttribute('data-theme') || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    var next = current === 'dark' ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    try { localStorage.setItem('toucan-theme', next); } catch (e) {}
  });
  var header = document.getElementById('site-header');
  if (!header) return;
  function onScroll() { header.classList.toggle('scrolled', window.scrollY > 8); }
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();
})();
