(function () {
  'use strict';
  var root = document.documentElement;
  var theme = document.getElementById('theme-toggle');
  theme.addEventListener('click', function () {
    var current = root.getAttribute('data-theme') || (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    var next = current === 'dark' ? 'light' : 'dark';
    root.setAttribute('data-theme', next);
    try { localStorage.setItem('toucan-theme', next); } catch (e) {}
  });
  var header = document.getElementById('site-header');
  function onScroll() { header.classList.toggle('scrolled', window.scrollY > 8); }
  window.addEventListener('scroll', onScroll, { passive: true });
  onScroll();

  var search = document.getElementById('release-search');
  var releases = Array.from(document.querySelectorAll('.release'));
  var index = Array.from(document.querySelectorAll('#release-index a'));
  var status = document.getElementById('search-status');
  var expand = document.getElementById('expand-all');
  var texts = releases.map(function (release) { return release.textContent.toLowerCase(); });
  document.getElementById('release-tools').hidden = false;
  function updateExpand() {
    var visible = releases.filter(function (release) { return !release.hidden; });
    var allOpen = visible.length > 0 && visible.every(function (release) { return release.querySelector('details').open; });
    expand.textContent = allOpen ? 'Collapse all' : 'Expand all';
    expand.disabled = visible.length === 0;
  }
  function filter() {
    var query = search.value.trim().toLowerCase();
    var terms = query.split(/\s+/).filter(Boolean);
    var count = 0;
    releases.forEach(function (release, i) {
      var matched = terms.every(function (term) { return texts[i].includes(term); });
      release.hidden = !matched;
      index[i].hidden = !matched;
      if (matched) count++;
      if (query) release.querySelector('details').open = matched;
    });
    status.textContent = query ? count + ' matching ' + (count === 1 ? 'release' : 'releases') : '';
    document.getElementById('no-results').hidden = count > 0;
    updateExpand();
  }
  search.addEventListener('input', filter);
  document.getElementById('clear-search').addEventListener('click', function () { search.value = ''; filter(); search.focus(); });
  expand.addEventListener('click', function () {
    var open = expand.textContent === 'Expand all';
    releases.forEach(function (release) { if (!release.hidden) release.querySelector('details').open = open; });
    updateExpand();
  });
  releases.forEach(function (release) { release.querySelector('details').addEventListener('toggle', updateExpand); });
  function followHash() {
    var target = releases.find(function (release) { return '#' + release.id === location.hash; });
    if (!target) return;
    if (target.hidden) { search.value = ''; filter(); }
    target.querySelector('details').open = true;
    target.scrollIntoView();
    index.forEach(function (link) {
      if (link.hash === location.hash) link.setAttribute('aria-current', 'location');
      else link.removeAttribute('aria-current');
    });
  }
  window.addEventListener('hashchange', followHash);
  followHash();
})();
