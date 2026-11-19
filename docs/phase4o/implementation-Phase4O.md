# Worklog 3.2.4 — Phase 4O
按Audit→Provider Registry→Historical Pilot→Forward Capture→Warehouse→Quality/Reconciliation→Trend Pilot→Data Operations順序完成；實際檔案與測試見engineering與Source SHA綁定。
擴充既有TWSE/TPEx/FinMind候選provider policy，四用途rights分離；沒有新增外部供應商，FinMind既有app credentials不構成新ETL授權。Pilot建議三個月但日期/股票池維持null/empty待核准，引用既有43 tasks及packet，不杜撰scope。實際新增真實資料0。
ForwardCaptureStore新增immutable observation/version/revision chain與idempotent replay、source-time claims、decision time、retention及可信精確scope權利檢查。ProcessFixture重用EtlScheduler/EtlProductionGate/EtlPipeline，quality quarantine先於Silver dispatch。沒有重寫Scheduler、Identity、Authority/PIT或研究公式。
CaptureQuality重用EtlQuality並補充calendar/corporate-action context、decision time及fact integrity；Reconcile保留雙方差異，不覆寫來源。日曆/身分/公司行為核准仍用既有模型，Bronze/Silver/Gold為既有儲存的邏輯分層，無新資料表/Migration。
Candidate-v1、19 features、25/15/15/20/15/10、原FormulaDigest、七PIT、六獨立核准與TrendSample-v1保留。每fold Train/Validation至少20有效日，每日max(10,quantiles)股票、200 outcome pairs，OOS獨立；沿用既有bias/feature-outcome/version-binding測試，新增capture cutoff與revision leakage測試。不建立未核准Trend dataset，REAL_BASELINE_V1 NOT_RUN，NOT_PROMOTED，Phase5 LOCKED。
正式環境A/B/OAuth、共享持久儲存、多worker recovery、provider permission、備份還原、secrets、alerts、rollback與明確enable仍Blocked；部署不解除這些Gate。使用者授權本次Git與網站部署，但未授權資料購買、登入供應商、大量下載或正式DB變更。

受控CLI：
`--phase4o-fixture-capture <root> <plan> <input> <new-report>` 僅Synthetic，Plan.SourceCodeSha須匹配編譯SHA；沒有網路取得。
`--phase4o-replay <root> <plan> <new-report>` 驗證既有歷程；`--phase4o-quality <root> <plan> <idempotency-key> <context> <new-report>` 產生綁定原capture/context的品質回執。
`--phase4o-export <phase4i> <phase4j> <phase4m> <phase4n> <source-sha> <new-output>` 驗證前版資料綁定後輸出本次blocked/zero-real快照；不把fixture root匯入正式報表。真實capture與現場驗收具備後，需受控審查和重新匯出，不能手改數字。

最終七項實際成果：新增來源0、真實資料0、合格歷史發布/修訂證據0、PIT合格觀測0、核准研究資料集0、正式Gate解除0/8。外部依賴為精確pilot scope核准、各用途來源權利、歷史證據、隔離帳號與八Gate實際環境驗收。

<!-- binding -->
Source commit: f7d42cd68ba06c0b330e2bdfe5a20d40b509140e
Parent Phase4N digest: C93F4253A044CC62C57579A236DF63F652B2F3D4BA56E8CEB8648288B1083675
Data contracts: Phase4O-v1 / HistoricalNormalized-v1 / TrendSample-v1. FormulaDigest: 81C788B359568B2B058EC92EDFE2F30A3593D7C2A23B3BE8C126BA026D06CBFB
