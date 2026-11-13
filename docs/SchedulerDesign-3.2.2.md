# Phase 4L 排程與恢復

核心為 `WorkJournal.Application/Research/EtlScheduler.cs`，CLI、API 投影重用既有 `EtlPipeline`、Provider Adapter 與品質檢查。沒有新增 production HostedService，也沒有修改資料庫或啟用正式排程。

`ScheduledJobDefinition` 綁定 request、原始資料身分、UTC 到期時間、交易日與顯式版本化交易日曆 digest。日曆必須包含該交易日；不以星期一至五推測交易所開市。一次定義對應一次可重放批次，下一個交易日須建立新的明確定義。預先取得的封存資料由既有 Phase 4J acquisition 管理，本排程不另建網路下載器。

多 worker 必須共用支援排他 FileShare.None 的持久化檔案系統。跨主機獨立磁碟不支援協調，不能將此版本直接當成分散式資料庫租約服務。短期全域鎖保護租約、狀態與額度更新；處理資料時釋放全域鎖，保留既有每工作 ETL 排他鎖。租約 2 分鐘、心跳 20 秒，租約 token 與單調遞增回執 revision 防止舊 worker 更新狀態。回執串連 previous digest，以 Flush(true) 後原子 rename 發布。

到期恢復先檢查原 ETL 完成回執與 immutable batch；重新驗證原始資料、轉換結果、品質與 lineage。若批次已寫入而結果回執缺失，補寫標記 RECOVERED_VERIFIED_COMMIT 的回執，不重複載入。持有原 ETL 鎖時不接管提交；digest 不符直接阻塞。固定 idempotency key、不可覆寫 batch 與排他鎖共同保障只提交一次。

每個工作最多 3 次派送；暫時性 timeout、quota、provider failure 採 30/60 秒退避。schema、內容或 digest 錯誤不自動重試。每供應商每小時最多 20 次派送、同時一件，連續三筆暫時錯誤開啟 5 分鐘 circuit；冷卻後只允許一個 probe。取得前先記額度，崩潰可能保守地消耗額度，不能突破上限。既有網路 acquisition 自身的額度限制仍有效。

取消為持久化請求，待處理工作立即取消，執行中由心跳取消 token 傳至既有 ETL。若取消前批次已完成，保留真實成功回執，不能假裝回滾已提交資料。資料封存成功不授予 authority、PIT、dataset approval 或 promotion。

受控命令：`--etl-schedule-register <root> <definition> <raw> <new-receipt>`、`--etl-schedule-tick <root> <id> --controlled`、`--etl-schedule-cancel <root> <id>`、`--etl-operations-export <root> <new-snapshot>`。請先使用隔離 root 和 fixture；沒有定時自動啟動或正式環境預設啟用。

監控為受控匯出快照，包含排程狀態與告警；不宣稱供應商即時可用性。API 不公開執行、取消、核准或原始封存路徑。
