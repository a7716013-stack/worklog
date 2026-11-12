(() => {
    const status = document.getElementById('etl-status');
    if (!status) return;
    const body = document.getElementById('etl-jobs'), detail = document.getElementById('etl-detail'), more = document.getElementById('etl-more');
    const get = async path => {
        const r = await fetch(path, {credentials:'same-origin', cache:'no-store', signal:AbortSignal.timeout(15000)});
        if (r.status === 401 || r.status === 403) { body.replaceChildren(); detail.textContent = ''; throw new Error('請重新登入後查看資料匯入紀錄。'); }
        if (!r.ok) throw new Error('資料匯入紀錄暫時無法讀取，請稍後再試。');
        return r.json();
    };
    let page = 1;
    const load = async () => {
        more.disabled = true;
        try {
            const result = await get(`/api/v1/etl/jobs?page=${page}&pageSize=20`);
            for (const job of result.items) {
                const tr = document.createElement('tr');
                for (const value of [`${job.request.provider} / ${job.request.dataset}${job.synthetic ? '（測試）' : ''}`,job.status,`${job.factsCount} / ${job.loadedFacts}`]) {
                    const td = document.createElement('td'); td.textContent = String(value); tr.append(td);
                }
                const td = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-light'; button.textContent = '品質與來源';
                button.addEventListener('click', async () => {
                    button.disabled = true;
                    try {
                        const id = encodeURIComponent(job.jobId);
                        const [record, quality] = await Promise.all([get(`/api/v1/etl/jobs/${id}`),get(`/api/v1/etl/jobs/${id}/quality`)]);
                        const lineage = record.lineage ? await get(`/api/v1/etl/batches/${id}/lineage`) : null;
                        detail.textContent = JSON.stringify({stages:record.stages, quality, lineage, authority:record.authorityStatus,pit:record.pitStatus,approval:record.datasetApproval,research:record.researchStatus},null,2);
                    } catch (e) { detail.textContent = e.message; } finally { button.disabled = false; }
                });
                td.append(button); tr.append(td); body.append(tr);
            }
            more.hidden = page * 20 >= result.totalCount; page++;
        } catch (e) { status.textContent = e.message; } finally { more.disabled = false; }
    };
    more.addEventListener('click', load);
    (async () => {
        try {
            const [health, providers] = await Promise.all([get('/api/v1/etl/health'),get('/api/v1/data/providers')]);
            status.textContent = `已驗證發布快照：${health.verifiedAt}；真實工作 ${health.realJobs}，測試工作 ${health.testJobs}，失敗 ${health.failedJobs}，隔離 ${health.quarantinedJobs}。此處不代表供應商即時健康狀態。`;
            for (const p of providers.items) {const li = document.createElement('li');li.textContent = `${p.provider}：${p.status}；${p.datasets.join(' / ') || '尚未啟用'}`;document.getElementById('etl-providers').append(li);}
            await load();
        } catch (e) { status.textContent = e.message; }
    })();
    window.addEventListener('pageshow', e => {if(e.persisted) location.reload();});
})();
