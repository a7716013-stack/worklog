# Worklog 3.2.2 — Phase 4L

本版延續 3.2.1 共用 ETL 與 Identity：修正登出後舊 Cookie 仍有效的缺口；新增可持久化的受控排程、租約/心跳/取消、三次重試、供應商額度與斷路器、交易日曆綁定、批次中斷恢復；新增證據結案與研究準備投影，以及研究頁面的監控 API。

運算放在 Application 的 EtlScheduler、EtlPipeline 和 Phase4LOperations，CLI 與 API 重用核心。新增唯一唯讀端點 GET /api/v1/etl/operations，沿用 ApiResearchRead、ReadPolicy、no-store；既有六個 ETL API 不變。Web 只讀取、呈現資料，未加入新公式。舊 Api→Web hosting/Identity/EF service 相依仍為既有過渡架構，沒有宣稱全部抽離。

本機驗證：完整回歸 25 個步驟通過；最終 API/SQL 421 項、Production 設定 Identity fixture 127 項通過。排程 fixture 覆蓋並行互斥、過期接管/舊租約拒絕、持久化退避/額度、斷路器半開 probe、取消、commit 後中斷與第三次嘗試恢復、損壞回執/批次拒絕；既有 schema/quarantine/replay/PIT/revocation/Trend sample/formula/paper/legacy 測試沿用。瀏覽器畫面測試結果另記於 engineering；fixture 不代表真實研究成功。

真實 ETL 重用兩個既有 TWSE 封存，21 筆事實（20 價格、1 法人），不新增網路取得。13 source versions、43 tasks、23 attempts、20 尚未結案 claims 保留。結案清單明確列出優先序、證據需求、下一步、未指派角色、review/closure receipt 與失效影響；無自動購買、授權、核准或人工證據替代。

研究仍為 1 筆觀測、0 PIT 合格、0 核准資料集/樣本，REAL_BASELINE_V1 NOT_RUN、門檻 UNRESOLVED、NOT_PROMOTED、Phase5 LOCKED。Candidate-v1、19 features、25/15/15/20/15/10 權重、原 FormulaDigest、七個 PIT gates、六個獨立構面核准與 TrendSample-v1 不變。已核准但不足量時仍為 INSUFFICIENT_SAMPLE。

正式排程沒有啟用；排程 worker 僅能由受控 CLI 明確執行，檔案式多 worker 協調要求共用持久化磁碟。監控是發布快照，不能用來宣稱即時供應商健康。正式 A/B 會員與真實 OAuth 驗收缺專用測試帳號，保留 BLOCKED；安全驗收詳 SecurityAcceptance-3.2.2.md。

本機入口 Start-WorkJournal3220.ps1，http://localhost:5208；研究監控 /ResearchValidation。發布採最終 head CI 全綠後 fast-forward main，再部署相同 SHA，確認四組件版本/SHA、readiness 與匿名保護端點。原始碼 commit 產生版本綁定研究報告；最後 report-only commit 與部署 health 綁定發布 SHA，兩者區分記錄。

交付：Audit、SecurityAcceptance、SchedulerDesign、APIContractChange、EtlOperationsReport、ProviderHealthReport、AuthorityClosureReport、PITEligibilityReport、DatasetReadiness、ModelValidationStatus、implementation、engineering（皆 3.2.2）。後續先取得歷史發布時間與修訂留存證據、指定審查負責人與隔離正式測試帳號，再評估排程執行環境；不得直接啟用排程或解鎖研究。
