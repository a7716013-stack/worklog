(() => {
    const host = document.getElementById('phase4m-readiness');
    if (!host) return;
    host.style.overflowWrap='anywhere';
    const labels = {'Source':'來源證據','Authority':'歷史適用性','PIT':'時間點資格','Dataset Approval':'資料集核准','Sample Sufficiency':'樣本充足度','Baseline':'基準研究'};
    async function get(path) {
        const response = await fetch(path, {credentials:'same-origin',cache:'no-store',signal:AbortSignal.timeout(15000)});
        if (!response.ok) throw new Error(response.status === 401 ? '請重新登入。' : response.status === 403 ? '此功能僅供授權管理者使用。' : '準備度資料暫時無法取得。');
        return response.json();
    }
    function node(tag,text) {const el=document.createElement(tag);el.textContent=text;return el;}
    function rows(title,items) {
        const section=node('section','');section.className='panel mt-3';section.append(node('h3',title));
        const list=node('ul','');for(const item of items)list.append(node('li',item));section.append(list);return section;
    }
    (async () => {
        try {
            const r=await get('/api/v1/operations/readiness');host.replaceChildren();
            host.append(node('h2','資料與研究準備度'),node('p',`版本 ${r.version} · Phase ${r.phase} · 發布快照 ${r.exportedAt}。正式排程：${r.production.mode}；不是即時監控。`));
            host.append(rows('準備流程',r.trend.stages.map(s=>`${labels[s.name] || s.name}：${s.status}${s.reasons.length?' — '+s.reasons.join('、'):''}`)));
            host.append(rows('正式排程啟用前待驗收',r.production.checks.map(c=>`${c.code}：${c.status}`)));
            host.append(node('p',`樣本要求：每 fold 的訓練與驗證各至少 ${r.trend.policy.minimumDates} 個有效日期，每日至少 max(${r.trend.policy.minimumInstrumentsPerDate}, quantiles) 檔股票，至少 ${r.trend.policy.minimumOutcomePairs} 組結果。OOS 獨立驗證；模型未升級。`));
            host.append(rows('待處理事項',r.alerts.map(a=>`${a.code}：${a.nextAction}`)));
            if(host.dataset.admin==='true') {
                const button=node('button','載入證據審查佇列');button.type='button';button.className='btn btn-light';host.append(button);
                button.addEventListener('click',async()=>{
                    button.disabled=true;
                    try {const q=await get('/api/v1/authority/workbench?pageSize=200');host.append(rows('審查工作（寫入使用受控操作流程）',q.items.map(i=>`${i.claimKey}｜${i.status}｜取得人：${i.operator}｜審查人：${i.reviewer}｜${i.reason || '尚未完成範圍與證據審查'}`)));button.remove();}
                    catch(e){button.disabled=false;button.textContent=e.message;}
                });
            }
        }catch(e){host.replaceChildren(node('p',e.message));}
    })();
    window.addEventListener('pageshow',e=>{if(e.persisted)location.reload();});
})();
