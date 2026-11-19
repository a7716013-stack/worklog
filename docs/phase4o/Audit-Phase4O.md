# Phase 4O Architecture Audit — 3.2.4
基準 GitHub main f7d42cd68ba06c0b330e2bdfe5a20d40b509140e / tree 552309bfdcb975639829b7fed4c86f56501b8f05。本機Git blobs與遠端一致（允許BOM/換行）。已讀AGENTS、Phase4O任務、Phase4N報告與既有Provider/ETL/Scheduler/Acquisition/Authority/PIT/Research/API原始碼與測試。
OfficialDataProviderAdapter.Catalog已有TWSE/TPEx/FinMind候選，TWSE Price/Institutional與TPEx Price解析；FinMind ETL契約與權利未核准。現有EtlPipeline以request+raw digest建不可變batch及lineage；EtlScheduler已有lease、recovery、quota、heartbeat與取消。保留上述核心，不另建排程。
Phase4I/4J/4M/4N提供20 claims、43 tasks/packets、review/revoke、PIT與資料核准。Phase4N八部署Gate仍0/8，會員正式驗收Blocked；研究NOT_RUN。487 API/SQL、127 Identity、15/15 CI為前版結果，本次重跑。
缺口：來源權利尚未分取得/儲存/研究/再發布；擷取快照缺跨版本觀測歷程與明確decision time；品質缺日曆/公司行為context及跨供應商差異投影。新增Application中的補充模型，復用既有archive、ETL與核准資料集，不新增資料表/Migration。
Bronze沿用SourceArchive；Silver沿用EtlBatch/HistoricalFact；Gold只引用既有批准receipt，不建立新的核准系統。UI與API只讀受控匯出，無原始body/secret/本機路徑公開端點。
本次Git/push/merge/deploy由使用者明確授權；沒有日期/股票池/來源權利核准，不執行外部下載。實際新增來源與真實資料均0；既有來源不是本次新增成果。依PR1到PR8順序實作與驗證。

<!-- binding -->
Source commit: 4357d1a07e5bbb67c256081925cfdedd737235ad
Parent Phase4N digest: C93F4253A044CC62C57579A236DF63F652B2F3D4BA56E8CEB8648288B1083675
Data contracts: Phase4O-v1 / HistoricalNormalized-v1 / TrendSample-v1. FormulaDigest: 81C788B359568B2B058EC92EDFE2F30A3593D7C2A23B3BE8C126BA026D06CBFB
