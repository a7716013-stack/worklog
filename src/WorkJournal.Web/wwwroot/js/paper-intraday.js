(() => {
    'use strict';
    const status = document.getElementById('paper-live-status');
    if (!status) return;
    const refreshButton = document.getElementById('paper-refresh-quotes');
    const note = document.createElement('div'); note.setAttribute('role', 'status'); status.append(note);
    const number = value => value == null ? '—' : new Intl.NumberFormat('zh-TW', {minimumFractionDigits:2, maximumFractionDigits:2}).format(value);
    const stamp = value => value ? new Date(value).toLocaleString('zh-TW', {timeZone:'Asia/Taipei',hour12:false}) : '—';
    let running = false;
    function session() {
        const d = new Date(Date.now()+8*3600000), minutes=d.getUTCHours()*60+d.getUTCMinutes();
        return d.getUTCDay()>0 && d.getUTCDay()<6 && minutes>=540 && minutes<=811;
    }
    async function update(manual = false) {
        if (running || (!manual && (document.hidden || !session()))) return;
        running=true;
        if (refreshButton) {refreshButton.disabled=true;refreshButton.textContent='更新中…';}
        note.textContent='正在查詢最新行情…';
        try {
            const url=new URL(status.dataset.url,window.location.origin);
            if(manual)url.searchParams.set('refresh','true');
            const response=await fetch(url, {cache:'no-store',headers:{Accept:'application/json'},signal:AbortSignal.timeout(25000)});
            if (!response.ok || response.redirected) throw Error('unavailable');
            const data=await response.json();
            document.querySelectorAll('[data-live]').forEach(el=>{
                const value=data[el.dataset.live];
                el.textContent=(el.dataset.prefix||'')+number(value)+(el.dataset.suffix||'');
                if (el.classList.contains('paper-profit') || el.classList.contains('paper-loss')) {el.classList.toggle('paper-profit',value>0);el.classList.toggle('paper-loss',value<0);}
            });
            let mismatch=false;
            document.querySelectorAll('#paper-positions tbody tr[data-symbol]').forEach(row=>{
                const item=data.positions.find(x=>x.symbol===row.dataset.symbol);
                if (!item) {mismatch=true;return;}
                row.querySelectorAll('[data-tick]').forEach(el=>{
                    const key=el.dataset.tick,value=item[key]; el.textContent=number(value)+(key==='returnPercent'?'%':'');
                    if (key==='unrealizedProfitLoss'||key==='returnPercent'){el.classList.toggle('paper-profit',value>0);el.classList.toggle('paper-loss',value<0);}
                });
                row.querySelector('[data-tick-status]').textContent=item.quoteStatus+' · '+(item.quoteTime?stamp(item.quoteTime):item.date||'—');
            });
            note.textContent=mismatch?'持倉已變更，請重新整理頁面。':(data.hasIncompleteValuation?'部分行情不可用，總資產估值不完整。':'估值查詢完成；請以各檔報價時間及狀態為準。');
        } catch {note.textContent='更新失敗，畫面保留上次資料；請勿視為最新即時報價。';}
        finally {running=false;if(refreshButton){refreshButton.disabled=false;refreshButton.textContent='即時更新';}}
    }
    refreshButton?.addEventListener('click',()=>update(true));
    setInterval(()=>update(),15000);
    document.addEventListener('visibilitychange',()=>{if(!document.hidden)update();});
    update();
})();
