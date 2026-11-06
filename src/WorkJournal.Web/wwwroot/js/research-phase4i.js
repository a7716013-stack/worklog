(() => {
    if(!document.getElementById('validation-counts'))return;
    const text=(id,value)=>{document.getElementById(id).textContent=value;};
    const get=async path=>{const r=await fetch(path,{credentials:'same-origin',signal:AbortSignal.timeout(15000)});if(r.status===401)throw new Error('請重新登入。');if(!r.ok)throw new Error('研究報告暫時無法讀取。');return r.json();};
    const failure=e=>e.name==='TimeoutError'?'讀取逾時，請重新整理。':e.message;
    const row=(id,values)=>{const tr=document.createElement('tr');for(const value of values){const td=document.createElement('td');td.textContent=String(value);tr.append(td);}document.getElementById(id).append(tr);return tr;};
    (async()=>{try{
        const [summary,replay,queue]=await Promise.all([get('/api/v1/research/runs/REAL_BASELINE_V1/validation-summary'),get('/api/v1/research/runs/REAL_BASELINE_V1/replay-status'),get('/api/v1/authority/closure-queue?pageSize=200')]);
        const base=await get('/api/v1/research/eligibility-summary');const dataset=encodeURIComponent(base.dataset.datasetSnapshotId);
        const d=await get(`/api/v1/research/datasets/${dataset}/readiness`);const c=d.counts;
        text('validation-counts',`來源資料 ${c.totalFacts} 筆：價格 ${c.priceFacts}、法人 ${c.institutionalFacts}、基準 ${c.benchmarkFacts}。完全重複 ${c.duplicateFacts} 筆、額外修訂 ${c.additionalRevisions} 筆。研究觀測 ${c.requestedObservations} 筆，PIT 合格 ${c.eligibleObservations} 筆，核准研究樣本 ${c.approvedResearchSamples} 筆。`);
        const states={NOT_RUN:'尚未執行',INSUFFICIENT_SAMPLE:'樣本不足',COMPLETED:'已完成研究'};const p=summary.trend.policy;
        text('validation-trend',`Trend 25 分研究：${states[summary.trend.status]||summary.trend.status}。各開發／驗證區間與持有期至少 ${p.minimumDates} 個有效日期、每日 ${p.minimumInstrumentsPerDate} 檔股票、${p.minimumOutcomePairs} 組結果配對；達門檻不代表有預測能力。OOS 須獨立選模及驗證。`);
        text('validation-replay',`發布報告完整性已校驗；證據查核時間：${replay.replay.verifiedAt}。Baseline 重播：${replay.replay.baselineStatus==='NOT_RUN'?'尚未執行':'已於發布時驗證'}。`);
        for(const item of queue.claims.items){const tr=row('validation-claims',['',item.applicability==='UNPROVEN_FOR_HISTORICAL_SCOPE'?'尚未證明指定歷史範圍':'已支持指定範圍',item.claim.currentStatus]);const b=document.createElement('button');b.type='button';b.className='btn btn-link';b.textContent=item.id;b.addEventListener('click',async()=>{b.disabled=true;try{text('validation-claim-detail',JSON.stringify(await get('/api/v1/authority/claims/'+encodeURIComponent(item.id)+'/evidence'),null,2));}catch(e){text('validation-claim-detail',failure(e));}finally{b.disabled=false;}});tr.firstChild.append(b);}
        let page=1;const more=document.getElementById('validation-pit-more');
        const load=async()=>{more.disabled=true;try{const r=await get(`/api/v1/research/datasets/${dataset}/coverage?page=${page}&pageSize=20`);for(const o of r.observations.items)row('validation-pit',[`${o.symbol} / ${o.tradeDate}`,o.availableAt??'未證明',o.gates.map(g=>`${g.gate}: ${g.status}`).join('；')]);more.hidden=page*20>=r.observations.totalCount;page++;}catch(e){text('validation-counts',failure(e));}finally{more.disabled=false;}};more.addEventListener('click',load);await load();
    }catch(e){text('validation-counts',failure(e));}})();
})();
