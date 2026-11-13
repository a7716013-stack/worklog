# API 變更 3.2.2

保留既有六個 ETL GET 端點及 DTO。新增 `GET /api/v1/etl/operations`：原六個端點只有已完成 ETL 工作、品質與 lineage，無法表達持久化排程、未完成工作或證據結案流程。

回應 `EtlOperationsReport` 包含發布時間、明確快照語意、正式排程關閉旗標、schedule 狀態、provider 歷史統計、closure 工作清單、source invalidation、component readiness 與 alert。排程回執使用 opaque digest，不公開 worker 身分、原始資料、帳號、Cookie、Token 或封存路徑。證據負責角色未指派時明確標示 UNASSIGNED；review receipt 不等於 closure receipt。

沿用 ApiResearchRead、ReadPolicy、no-store；匿名 401，無權限 403，發布檔缺失或驗證失敗 503 DATA_NOT_READY。新端點不接受寫入，也不核准或執行研究。研究頁面以同來源 Cookie、15 秒逾時呼叫 API，401/403 清除顯示並要求重新登入。

所有聚合與治理投影位於 Application，Controller 只組合讀取服務，瀏覽器只顯示資料。公式、19 features、Candidate-v1、七個 PIT gates、六個獨立核准與 TrendSample-v1 不變。相容性須由 OpenAPI compatibility 及既有回歸測試實際確認，結果列於 implementation 與 engineering。
