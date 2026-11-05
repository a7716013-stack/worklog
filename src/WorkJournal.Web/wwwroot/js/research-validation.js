(() => {
    const status = document.getElementById('validation-status');
    if (!status) return;
    const names = {Trend:'趨勢',Momentum:'動能',VolumePrice:'量價',Institutional:'法人',RelativeStrength:'相對強度',RiskQuality:'風險品質'};
    const get = async path => {
        const r = await fetch(path, {credentials:'same-origin',signal:AbortSignal.timeout(15000)});
        if (r.status === 401) throw new Error('請重新登入後查看研究資格。');
        if (!r.ok) throw new Error('研究資格報告暫時無法讀取，請稍後再試。');
        return r.json();
    };
    (async () => {
        try {
            const s = await get('/api/v1/research/eligibility-summary');
            const p = await get('/api/v1/research/runs/REAL_BASELINE_V1/provenance');
            status.textContent = `真實資料 ${s.dataset.rawRealRows} 筆；PIT 合格 ${s.dataset.eligibleObservations} 筆。${s.status === 'BLOCKED' ? '尚未具備研究資格。' : '已具備受控研究資格。'}真實 baseline：${p.researchStatus === 'NOT_RUN' ? '尚未執行' : p.researchStatus}。`;
            const body = document.getElementById('validation-components');
            for (const c of s.components) {
                const tr = document.createElement('tr');
                for (const value of [names[c.evidence] || c.evidence,c.weight,c.datasetStatus === 'APPROVED' ? '已核准' : '待補證據',c.baselineStatus === 'NOT_RUN' ? '尚未執行' : c.baselineStatus]) {
                    const td = document.createElement('td');td.textContent = String(value);tr.append(td);
                }
                body.append(tr);
            }
            const gaps = document.getElementById('validation-gaps');
            for (const reason of s.dataset.blockingReasons) {const li=document.createElement('li');li.textContent=reason;gaps.append(li);}
            document.getElementById('validation-provenance').textContent = JSON.stringify(p,null,2);
        } catch (e) {status.textContent=e.name==='TimeoutError'?'讀取逾時，請重新整理再試。':e.message;}
    })();
})();
