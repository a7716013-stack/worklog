(() => {
'use strict';
document.querySelectorAll('[data-widget-url]').forEach(async widget => {
 const controller = new AbortController();
 const timeout = setTimeout(() => controller.abort(), 20000);
 try {
  const response = await fetch(widget.dataset.widgetUrl, {credentials:'same-origin', cache:'no-store', signal:controller.signal, redirect:'error'});
  if (!response.ok) throw new Error('unavailable');
  // Same-origin Razor partial; external text is encoded by Razor.
  widget.innerHTML = await response.text();
 } catch {
  const message = document.createElement('p');
  message.className = 'empty-state'; message.textContent = widget.dataset.error;
  widget.replaceChildren(message);
 } finally {
  clearTimeout(timeout); widget.setAttribute('aria-busy','false');
  if (widget.id === 'home-calendar') {
   const count = widget.querySelector('[data-calendar-count]')?.dataset.calendarCount;
   document.getElementById('today-calendar-count').textContent = count || '—';
   document.getElementById('today-calendar-note').textContent = count ? '今日可用行程 · 查看行事曆 →' : '行程摘要暫時無法完整取得';
  }
 }
});
})();
