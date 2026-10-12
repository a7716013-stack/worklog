(() => {
    'use strict';
    const app = document.querySelector('.international-sidebar');
    if (!app) return;
    const button = document.getElementById('international-refresh');
    const status = document.getElementById('international-status');
    const list = app.querySelector('.international-list');
    const cards = [...list.querySelectorAll('[data-symbol]')];
    const number = value => Number(value).toLocaleString('zh-TW', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    const time = value => new Date(value).toLocaleString('zh-TW', { timeZone: 'Asia/Taipei', hour12: false });
    let pending = false, nextRefresh = 0;
    async function load() {
        if (pending) return;
        pending = true; button.disabled = true; list.setAttribute('aria-busy', 'true');
        status.textContent = '正在讀取最新報價…';
        try {
            const response = await fetch(app.dataset.url, { signal: AbortSignal.timeout(25000) });
            if (!response.ok) throw new Error('Unavailable');
            const data = await response.json();
            if (!Array.isArray(data.quotes)) throw new Error('Invalid data');
            let missing = 0, stale = 0;
            for (const card of cards) {
                const quote = data.quotes.find(q => q.symbol === card.dataset.symbol);
                const change = card.querySelector('.international-change');
                const value = card.querySelector('.international-value');
                const when = card.querySelector('.international-time');
                const note = card.querySelector('.international-state');
                change.className = 'international-change';
                if (quote?.value == null) {
                    missing++; value.textContent = '—'; change.textContent = '漲跌暫缺';
                    when.textContent = '尚無有效報價'; note.textContent = '來源暫時無法取得';
                    continue;
                }
                value.textContent = number(quote.value);
                if (quote.change == null || quote.changePercent == null) change.textContent = '漲跌基準暫缺';
                else {
                    const sign = quote.change > 0 ? '+' : quote.change < 0 ? '−' : '';
                    const arrow = quote.change > 0 ? '▲' : quote.change < 0 ? '▼' : '●';
                    change.classList.add(quote.change > 0 ? 'index-up' : quote.change < 0 ? 'index-down' : 'index-flat');
                    change.textContent = `${arrow} ${sign}${number(Math.abs(quote.change))}（${sign}${number(Math.abs(quote.changePercent))}%）`;
                }
                when.textContent = '報價 ' + time(quote.quotedAt);
                note.textContent = quote.isStale ? '更新失敗 · 保留上次報價' : quote.status;
                if (quote.isStale) stale++;
            }
            status.textContent = (missing || stale ? `暫缺 ${missing} 項 · 舊報價 ${stale} 項` : `${cards.length} 項指數 · 最新可用報價`) + ' · 查詢 ' + time(data.checkedAt);
        } catch {
            status.textContent = '更新失敗，請稍後重試；已顯示數字保留原報價時間。';
            cards.forEach(card => card.querySelector('.international-state').textContent = '更新失敗');
        } finally {
            pending = false; nextRefresh = Date.now() + 300000; button.disabled = false; list.setAttribute('aria-busy', 'false');
        }
    }
    button.addEventListener('click', load);
    document.addEventListener('visibilitychange', () => { if (!document.hidden && Date.now() >= nextRefresh) load(); });
    setInterval(() => { if (!document.hidden && Date.now() >= nextRefresh) load(); }, 300000);
    load();
})();
