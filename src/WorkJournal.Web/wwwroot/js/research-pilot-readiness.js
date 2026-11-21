(() => {
    const host = document.getElementById('phase4q-readiness');
    if (!host || host.dataset.admin !== 'true') return;
    const el = (tag, text) => { const e = document.createElement(tag); e.textContent = text; return e; };
    function section(title, items) {
        const s = el('section', ''); s.className = 'panel mt-3'; s.append(el('h3', title));
        const list = el('ul', ''); items.forEach(x => list.append(el('li', x))); s.append(list); return s;
    }
    let generation = 0; host.style.overflowWrap = 'anywhere';
    async function refresh() {
        const current = ++generation; host.replaceChildren(el('p', '正在載入試行審查與證據覆蓋率…'));
        try {
            const response = await fetch('/api/v1/data/pilot/readiness', { credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(15000) });
            if (!response.ok) throw new Error(response.status === 401 ? '工作階段已結束，請重新登入。' : response.status === 403 ? '試行審查僅供管理者使用。' : response.status === 429 ? '讀取次數過多，請稍後重試。' : '審查快照暫時無法讀取。');
            const r = await response.json(); if (generation !== current) return;
            host.replaceChildren(el('h2', '研究資料中心 · 首次資料試行審查'), el('p', `${r.version} · ${r.exportedAt}`));
            host.append(el('p', '此為受控匯出快照；草案、測試及來源條款筆記都不代表已取得資料授權。'));
            for (const right of r.rights) {
                const s = section(`TWSE ${right.purpose}：${right.status}`, [right.restriction, `獨立審查者：${right.reviewer || '尚未指派'}；授權到期：${right.expiresAt || '尚未核准'}`, `來源版本：${right.sourceVersion}`]);
                right.sources.forEach(url => { const a = el('a', '條款／資料集來源'); a.href = url; a.rel = 'noopener noreferrer'; a.target = '_blank'; a.className = 'me-3'; s.append(a); }); host.append(s);
            }
            const p = r.proposal;
            host.append(section('Pilot 範圍審查：尚未核准', [`${p.provider} / ${p.dataset}；${p.instruments.length} 檔候選普通股（歷史身分尚待驗證）`, `股票：${p.instruments.join('、')}`, `歷史資料：${p.historicalStart} ～ ${p.historicalEnd}；評估：${p.evaluationStart} ～ ${p.evaluationEnd}`, `目標約 ${p.targetEvaluationSessions} 個評估交易日、${p.featureLookback} 日回看、${p.outcomeHorizon} 日結果；交易日數尚待日曆確認。`, `${p.exactRequestCount} 個月份請求；含重試總額 ${p.maxRequests} 次；儲存 ${p.storageLimitBytes} bytes；單次 ${p.maximumResponseBytes} bytes；保留 ${p.retentionDays} 日。`, `執行時間 UTC：${p.forwardStart} ～ ${p.expiresAt}`, '所有股票、日期、容量、權利引用與操作人仍須人工確認。']));
            host.append(section('取得準備度', [r.acquisition.status, ...r.acquisition.blockingReasons]));
            host.append(section('隔離環境驗收', r.isolatedEnvironment.map(c => `${c.code}：${c.status}`)));
            host.append(section('Capture 歷程', r.runs.length ? r.runs.map(x => `${x.status} · ${x.evidenceClass} · ${x.facts} 筆`) : ['本版真實 Capture：0；新增資料：0。ObservedAt 不代表原始 PublishedAt。']));
            host.append(section('歷史證據覆蓋', r.historical.map(h => `${h.domain}：${h.status}；已結案 ${h.supported} / ${h.required}（既有受審範圍）`)));
            const c = r.pit;
            host.append(section('PIT 覆蓋率與資料集', [`累計來源事實 ${c.totalRawFacts} 筆（既有 ${c.priorRawFacts}，本版新增 ${c.newRawFacts}）。`, `既有已檢查觀測 ${c.inspectedObservations}；本次提案範圍的分母：${c.proposedScopeDenominator ?? '未知'}。`, `已驗證歷史 context：${c.verifiedHistoricalContext}；發布證據：${c.publicationEvidenceObservations}；修訂證據：${c.revisionEvidenceObservations}。`, ...c.sevenGates.map(g => `${g.gate}：${g.verified} / ${g.inspected}；${g.status}`), `PIT 合格：${c.pitEligibleObservations}；核准資料集：${c.approvedDatasetCount}`, ...c.blockingReasons]));
            host.append(section('Trend 與正式營運', [r.trendStatus, `${r.samplePolicy.version}：Train／Validation 各至少 ${r.samplePolicy.minimumDates} 日、每日至少 max(${r.samplePolicy.minimumInstrumentsPerDate}, quantiles) 檔、${r.samplePolicy.minimumOutcomePairs} 組結果。`, ...r.production.map(g => `${g.code}：${g.status}`), r.googleAcceptance, `排程 ${r.schedulerMode}；Baseline ${r.baseline}；OOS ${r.oos}；${r.promotion}；Phase 5 ${r.phase5}`]));
        } catch (e) { if (generation === current) host.replaceChildren(el('p', e.message)); }
    }
    refresh(); document.addEventListener('visibilitychange', () => { if (document.hidden) { generation++; host.replaceChildren(); } else refresh(); }); window.addEventListener('pageshow', e => { if (e.persisted) refresh(); });
})();
