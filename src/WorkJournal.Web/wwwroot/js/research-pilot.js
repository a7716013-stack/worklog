(() => {
    const host = document.getElementById('phase4p-pilot');
    if (!host || host.dataset.admin !== 'true') return;
    const node = (tag, text) => { const e = document.createElement(tag); e.textContent = text; return e; };
    const section = (title, items) => { const s = node('section', ''); s.className = 'panel mt-3'; s.append(node('h3', title)); const ul = node('ul', ''); items.forEach(x => ul.append(node('li', x))); s.append(ul); return s; };
    host.style.overflowWrap = 'anywhere'; let generation = 0;
    async function refresh() {
        const current = ++generation; host.replaceChildren(node('p', '正在載入研究資料中心…'));
        try {
            const response = await fetch('/api/v1/data/pilot', { credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(15000) });
            if (!response.ok) throw new Error(response.status === 401 ? '工作階段已結束，請重新登入。' : response.status === 403 ? '研究資料中心僅供管理者使用。' : '研究資料快照暫時無法讀取。');
            const r = await response.json(); if (generation !== current) return;
            host.replaceChildren(node('h2', '研究資料中心 · Trend 試行'), node('p', `${r.version} · ${r.evidenceClass} · ${r.exportedAt}`));
            host.append(node('p', '以下為受控匯出的資料準備狀態，並非即時下載結果；隔離測試不計入真實成果。'));
            const labels = { approvedProviderRights: '已核准來源用途權利', approvedPilotScopes: '已核准取得範圍', realAcquisitionRuns: '真實取得執行', newRawSnapshots: '新增原始快照', newNormalizedFacts: '新增標準化資料', historicalEvidenceAccepted: '歷史證據接受', pitEligibleObservations: 'PIT 合格觀測', approvedDatasets: '核准資料集', productionGatesReleased: '正式驗收關卡解除' };
            host.append(section('本次實際成果', Object.entries(labels).map(([k, v]) => `${v}：${r.counts[k]}`)));
            host.append(section('取得範圍', [`${r.scope.status}；建議 ${r.scope.suggestedInstruments} 檔、${r.scope.suggestedTradingDays} 個交易日，仍須另含特徵回看與結果期間。`, `核准日期：${r.scope.historicalStart || '尚未核准'} ～ ${r.scope.historicalEnd || '尚未核准'}`, `核准股票池：${r.scope.instruments.join('、') || '尚未核准'}`, '核准範圍與各用途權利尚未完備時，不會執行資料取得。']));
            for (const p of r.providers) host.append(section(p.capabilities.provider + ' 來源權利', p.rights.map(x => `${x.purpose}：${x.status} — ${x.requiredAction}`)));
            const names = { HistoricalSecurityMaster: '歷史股票身分與股票池', TradingCalendar: '交易日曆', CorporateActions: '公司行為', FeatureOutcome: '特徵與結果', TrendSample: 'Trend 樣本', QualityReconciliation: '品質與跨來源比對', ProductionAcceptance: '正式環境驗收' };
            for (const x of r.coverage) host.append(section(names[x.name] || x.name, [`${x.status}；已驗證：${x.verifiedCount ?? '未知'}`, ...x.blockers]));
            host.append(section('擷取歷程與原始快照', r.runs.length ? r.runs.map(x => `${x.status} · ${x.evidenceClass} · ${x.facts} 筆 · ${x.captureDigest || '尚無快照'}`) : ['尚無本次真實擷取；無原始快照中繼資料。']));
            host.append(section('研究限制', [`${r.samplePolicy.version}：Train／Validation 各至少 ${r.samplePolicy.minimumDates} 個有效日，每日至少 max(${r.samplePolicy.minimumInstrumentsPerDate}, quantiles) 檔，至少 ${r.samplePolicy.minimumOutcomePairs} 組結果。`, `正式排程：${r.schedulerMode}；Baseline：${r.baseline}；${r.promotion}；Phase 5 ${r.phase5}`, 'PIT 與資料集核准仍各自獨立；OOS 需另外凍結選擇。', ...r.blockers]));
        } catch (e) { if (generation === current) host.replaceChildren(node('p', e.message)); }
    }
    refresh(); document.addEventListener('visibilitychange', () => { if (document.hidden) { generation++; host.replaceChildren(); } else refresh(); }); window.addEventListener('pageshow', e => { if (e.persisted) refresh(); });
})();
