(() => {
    const host=document.getElementById('phase4o-data');if(!host||host.dataset.admin!=='true')return;
    const node=(tag,text)=>{const e=document.createElement(tag);e.textContent=text;return e;};
    const list=(title,items)=>{const s=node('section','');s.className='panel mt-3';s.append(node('h3',title));const ul=node('ul','');items.forEach(i=>ul.append(node('li',i)));s.append(ul);return s;};
    host.style.overflowWrap='anywhere';let generation=0;
    async function read(path){const r=await fetch(path,{credentials:'same-origin',cache:'no-store',signal:AbortSignal.timeout(15000)});if(!r.ok)throw new Error(r.status===401?'工作階段已結束，請重新登入。':r.status===403?'資料營運僅供管理者使用。':'資料營運快照暫時無法讀取。');return r.json();}
    async function refresh(){const current=++generation;host.replaceChildren(node('p','正在讀取資料營運…'));
        try{const [r,h]=await Promise.all([read('/api/v1/data/operations'),read('/api/v1/data/captures?pageSize=200')]);if(current!==generation)return;
            host.replaceChildren(node('h2','真實資料取得與向前 PIT 擷取'),node('p',`${r.version} · ${r.evidenceClass} · ${r.exportedAt}`));
            host.append(node('p','Live：當時線上驗證；Snapshot：受控匯出；Fixture：隔離測試；Blocked：尚缺核准或證據。此頁不會觸發下載。'));
            const c=r.capture;host.append(list('本次實際成果',[`新增來源：${c.newRealSources}；新增真實資料：${c.newRealRows}`,`新增歷史發布／修訂證據：${c.newHistoricalPublicationProofs}／${c.newRevisionProofs}`,`新增 PIT 合格／核准資料集：${c.newPitEligible}／${c.newApprovedDatasets}`,`正式 Gate 解除：${c.productionGatesReleased}；排程：${r.schedulerMode}`,`Baseline：${r.baselineStatus}；${r.promotion}；Phase 5 ${r.phase5}`]));
            for(const p of r.providers)host.append(list(`${p.capabilities.provider} · ${p.healthStatus} · ${p.evidenceClass}`,[p.datasetContract,p.coverage,p.rateLimit,...p.rights.map(x=>`${x.purpose}：${x.status} — ${x.requiredAction}`),p.publicationPolicy,p.revisionPolicy]));
            host.append(list('歷史取得 Pilot',[r.acquisition.status,`建議 ${r.acquisition.suggestedMonths} 個月；實際日期：${r.acquisition.start||'待核准'} — ${r.acquisition.end||'待核准'}；股票池：${r.acquisition.instruments.join(', ')||'待核准'}`,`沿用 ${r.acquisition.existingTaskIds.length} 項工作／審查封包`,...r.acquisition.requiredApprovals]));
            host.append(list('資料分層',r.warehouse.map(x=>`${x.layer} · ${x.status} · ${x.storageModel} · ${x.admission}`)));
            host.append(list('品質與跨來源差異',[r.qualityStatus,r.reconciliationStatus,'來源差異保留，禁止覆寫；品質通過不代表 Authority 或 PIT 通過。']));
            host.append(node('h3',`擷取歷程（${h.totalCount}）`));
            if(h.totalCount===0)host.append(node('p','尚無本次真實擷取快照；Fixture 結果不計入。'));
            for(const x of h.items)host.append(list(`版本 ${x.versionSequence} / 觀測 ${x.sequence}`,[x.rawDigest,`ObservedAt：${x.observedAt}`,`RetrievedAt：${x.retrievedAt}`,`決策時點：${x.decisionAt}`,`來源發布聲明：${x.publication.claimedPublishedAt||'UNKNOWN'}；${x.publication.status}`]));
            host.append(list('待處理外部條件',c.blockingReasons));
        }catch(e){if(current===generation)host.replaceChildren(node('p',e.message));}}
    refresh();document.addEventListener('visibilitychange',()=>{if(document.hidden){generation++;host.replaceChildren();}else refresh();});window.addEventListener('pageshow',e=>{if(e.persisted)refresh();});
})();
