(() => {
    const host=document.getElementById('phase4n-operations');
    if(!host || host.dataset.admin!=='true')return;
    host.style.overflowWrap='anywhere';
    const el=(tag,text)=>{const n=document.createElement(tag);n.textContent=text;return n;};
    const list=(title,items)=>{const s=el('section','');s.className='panel mt-3';s.append(el('h3',title));const ul=el('ul','');items.forEach(x=>ul.append(el('li',x)));s.append(ul);return s;};
    let generation=0;
    async function get(path){const r=await fetch(path,{credentials:'same-origin',cache:'no-store',signal:AbortSignal.timeout(15000)});if(!r.ok)throw new Error(r.status===401?'工作階段已結束，請重新登入。':r.status===403?'此資料僅供管理者使用。':'管理資料暫時無法讀取。');return r.json();}
    async function refresh(){const current=++generation;host.replaceChildren(el('p','正在讀取管理資料…'));
        try {
            const [r,p]=await Promise.all([get('/api/v1/operations/dashboard'),get('/api/v1/authority/resolution-packets?pageSize=200')]);
            if(current!==generation)return;
            host.replaceChildren(el('h2','營運驗收與人工處理'),el('p',`${r.version} · ${r.evidenceClass} · ${r.exportedAt}`));
            host.append(el('p','Verified Live：指定時間的線上驗證；Verified at Export：匯出快照；Fixture：隔離測試；Blocked：缺少證據；Not Run：尚未執行。此頁不是即時監控。'));
            host.append(el('p',`正式排程：${r.production.mode}；部署 Gate：${r.production.checks.filter(c=>c.status==='Verified').length}/8；PIT 合格：${r.counts.eligibleObservations}；核准樣本：${r.counts.approvedResearchSamples}`));
            host.append(list('部署驗收證據',r.deploymentEvidence.map(e=>`${e.code} · ${e.result} · ${e.requiredNextAction}`)));
            host.append(list('正式會員驗收',r.productionAcceptance.map(e=>`${e.code} · ${e.result} · ${e.requiredNextAction}`)));
            host.append(list('資料集準備',r.datasets.map(d=>`${d.component} · ${d.researchStatus} · ${d.blockingReasons.join(' / ')}`)));
            host.append(el('p',`Baseline：${r.baseline.status}；執行：${r.baseline.executionStatus}；OOS：${r.baseline.oosStatus}；${r.baseline.promotion}；Phase 5 ${r.baseline.phase5}`));
            host.append(list('Authority 阻塞',r.baseline.blockingReasons));
            host.append(el('h3',`證據審查封包／人工工作（${p.totalCount}）`));
            for(const x of p.items){const d=el('details','');d.append(el('summary',`${x.priority}. ${x.claimId} · ${x.rightsStatus} · ${x.assignedReviewer || '尚未指派審查者'}`));
                d.append(list('範圍與來源',[`${x.coverageStart} — ${x.coverageEnd} · ${x.instruments.join(', ')}`,`Subject：${x.subject}`,`版本：${x.sourceVersions.join(', ')}`,`Raw：${x.rawDigests.join(', ')}`]));
                d.append(list('時間與審查證據',[`原始發布：${x.originalPublicationEvidence}`,`可用時間證明：${x.availableAtEvidence}`,`修訂：${x.revisionEvidence}`,`審查回執：${x.existingReviewReceipts.join(', ') || '尚無'}`,`撤銷：${x.revocationImpact.join(' / ') || '匯出時無撤銷影響'}`]));
                d.append(list('阻塞與下一步',[...x.blockingReasons,x.requiredNextAction]));host.append(d);}
        }catch(e){if(current===generation)host.replaceChildren(el('p',e.message));}
    }
    refresh();
    document.addEventListener('visibilitychange',()=>{if(document.hidden){generation++;host.replaceChildren();}else refresh();});
    window.addEventListener('pageshow',e=>{if(e.persisted)refresh();});
})();
