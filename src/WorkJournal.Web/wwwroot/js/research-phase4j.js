(() => {
    if (!document.getElementById('supply-status')) return;
    const text = (id, value) => { document.getElementById(id).textContent = value; };
    const get = async path => {
        const r = await fetch(path, { credentials: 'same-origin', signal: AbortSignal.timeout(15000) });
        if (r.status === 401) throw new Error('請重新登入。');
        if (!r.ok) throw new Error('來源與取得計畫暫時無法讀取。');
        return r.json();
    };
    const row = (id, values) => {
        const tr = document.createElement('tr');
        for (const value of values) { const td = document.createElement('td'); td.textContent = String(value); tr.append(td); }
        document.getElementById(id).append(tr); return tr;
    };
    const failure = e => e.name === 'TimeoutError' ? '讀取逾時，請重新整理。' : e.message;
    (async () => { try {
        let sourcePage = 1;
        do {
            const s = await get(`/api/v1/authority/sources?page=${sourcePage}&pageSize=100`);
            for (const item of s.sources.items) row('supply-sources', [item.officialUrl, `${item.publicationEvidenceCapability} / ${item.revisionEvidenceCapability}`, item.accessStatus]);
            text('supply-status', `來源版本 ${s.sources.totalCount} 項；發布快照驗證於 ${s.verifiedAt}。來源雜湊僅識別內容，不代表歷史修訂證明。`);
            if (sourcePage * 100 >= s.sources.totalCount) break;
            sourcePage++;
        } while (sourcePage <= 20);
        let page = 1; const more = document.getElementById('supply-more');
        const load = async () => { more.disabled = true; try {
            const p = await get(`/api/v1/authority/acquisition-plan?page=${page}&pageSize=20`);
            for (const t of p.tasks.items) {
                const tr = row('supply-plan', [t.claimKey, `${t.status} / ${t.attempts}`, t.nextAction, '']);
                const b = document.createElement('button'); b.type = 'button'; b.className = 'btn btn-link'; b.textContent = '查看取得紀錄';
                b.addEventListener('click', async () => { b.disabled = true; try {
                    text('supply-attempts', JSON.stringify({ task: t, history: await get('/api/v1/authority/acquisition-attempts/' + encodeURIComponent(t.taskId)) }, null, 2));
                } catch (e) { text('supply-attempts', failure(e)); } finally { b.disabled = false; } });
                tr.lastChild.append(b);
            }
            more.hidden = page * 20 >= p.tasks.totalCount; page++;
        } catch (e) { text('supply-status', failure(e)); } finally { more.disabled = false; } };
        more.addEventListener('click', load); await load();
        const base = await get('/api/v1/research/eligibility-summary');
        const d = await get('/api/v1/research/datasets/' + encodeURIComponent(base.dataset.datasetSnapshotId) + '/readiness');
        for (const b of d.builds) row('supply-builds', [b.component, [b.factsCount, b.requestedObservations, b.eligibleObservations].map(x => x ?? '未建置').join(' / '), b.approvedResearchSamples, b.replayStatus]);
    } catch (e) { text('supply-status', failure(e)); } })();
})();
