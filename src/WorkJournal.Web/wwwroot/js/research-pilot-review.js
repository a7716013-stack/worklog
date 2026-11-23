(() => {
    const host = document.getElementById('phase4r-review');
    if (!host || host.dataset.admin !== 'true') return;
    const el = (tag, text) => { const e = document.createElement(tag); e.textContent = text; return e; };
    function section(title, rows) {
        const s = el('section', ''); s.className = 'panel mt-3'; s.append(el('h3', title));
        const list = el('ul', ''); rows.forEach(x => list.append(el('li', x))); s.append(list); return s;
    }
    let generation = 0; host.style.overflowWrap = 'anywhere';
    async function refresh() {
        const current = ++generation; host.replaceChildren(el('p', '正在載入 Phase 4R 審查資料…'));
        try {
            const response = await fetch('/api/v1/data/pilot/review', { credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(15000) });
            if (!response.ok) throw new Error(response.status === 401 ? '工作階段已結束，請重新登入。' : response.status === 403 ? '試行審查僅供管理者使用。' : response.status === 429 ? '讀取次數過多，請稍後重試。' : '審查快照暫時無法讀取。');
            const r = await response.json(); if (generation !== current) return;
            host.replaceChildren(el('h2', 'Phase 4R · 首批候選 Pilot'), el('p', `${r.version} · ${r.exportedAt} · 僅供審查，未核准，不得執行取得。`));
            host.append(section('三項就緒狀態', [`取得準備：${r.readiness.acquisitionReady}`, `資料集合格：${r.readiness.datasetEligible}`, `研究準備：${r.readiness.researchReady}`, ...r.readiness.blockingReasons]));
            host.append(section('權利判定', r.rights.purposes.map(x => `${x.purpose}：${x.status}；${x.restriction}`)));
            host.append(section('替代來源相容性（未接入）', r.rights.alternatives.flatMap(x => [`${x.source}：${x.status}`, x.license, ...x.gaps])));
            const p = r.proposal, s = p.scope;
            host.append(section('候選範圍：未核准', [`${p.instrumentCount} 檔 × ${p.monthCount} 個歷史月份；${p.uniqueRequests} 項唯一請求；含重試最多 ${p.maximumAttempts} 次。`,
                `候選股票：${s.instruments.join('、')}；歷史身分尚待驗證。`, `候選期間：${s.historicalStart} ～ ${s.historicalEnd}；交易日數未知。`,
                `候選執行窗口：${s.forwardStart} ～ ${s.forwardEnd}（UTC）；操作人未指派。`,
                `回應上限 ${s.maximumResponseBytes} bytes；儲存上限 ${s.storageLimitBytes} bytes；保留 ${s.requests[0].minimumRetentionDays} 日，均待審查。`,
                `Scope Digest：${s.id}`, ...p.requiredConfirmations]));
            host.append(section('隔離環境驗收（實際證據尚缺）', [...r.environment.controls, ...r.environment.boundaries].map(x => `${x.code}：${x.status}；${x.requiredEvidence}`)));
            host.append(section('單次執行同意', [r.runConsent ? '請依既有信任與撤銷規則重新驗證收據。' : '未提供單次執行同意。開發及 Git 上傳授權不包含下載。', ...r.acquisition.blockingReasons]));
            host.append(section('取得歷程與原始快照／重播', [`真實取得 ${r.captureVerification.realRuns} 次；本版新增 ${r.captureVerification.newFacts} 筆；重播 ${r.captureVerification.replayStatus}。`,
                'ObservedAt／RetrievedAt 不代表原始 PublishedAt。', ...r.captureVerification.requirements.map(x => `${x.code}：${x.status}`)]));
            host.append(section('歷史 Authority 覆蓋', r.historical.map(x => `${x.domain}：${x.status}；既有範圍 ${x.supported}/${x.required}，不代表本次候選範圍。`)));
            host.append(section('PIT 與 Trend', [`既有資料 ${r.pit.priorRawFacts} 筆；新增 ${r.pit.newRawFacts}；候選分母 ${r.pit.proposedScopeDenominator ?? '未知'}。`,
                ...r.pit.sevenGates.map(x => `${x.gate}：既有觀測 ${x.verified}/${x.inspected}；${x.status}`),
                `PIT 合格 ${r.pit.pitEligibleObservations}；核准資料集 ${r.pit.approvedDatasetCount}。`,
                `${r.samplePolicy.version}：每 Fold 至少 ${r.samplePolicy.minimumDates} 日、每日 max(${r.samplePolicy.minimumInstrumentsPerDate}, quantiles) 檔及 ${r.samplePolicy.minimumOutcomePairs} 組結果；另需獨立 OOS。`,
                `Baseline ${r.baseline}；OOS ${r.oos}；${r.promotion}；Phase 5 ${r.phase5}`]));
            host.append(section('正式 ETL 驗收', [...r.production.map(x => `${x.code}：${x.status}`), r.googleAcceptance, `排程 ${r.schedulerMode}；本版僅本機與 Git，未部署網站。`]));
        } catch (e) { if (generation === current) host.replaceChildren(el('p', e.message)); }
    }
    refresh(); document.addEventListener('visibilitychange', () => { if (document.hidden) { generation++; host.replaceChildren(); } else refresh(); });
    window.addEventListener('pageshow', e => { if (e.persisted) refresh(); });
})();
