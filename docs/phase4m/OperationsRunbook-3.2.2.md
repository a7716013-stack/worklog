# 受控操作與回復
所有寫入在隔離 fixture 或經明確核准的既有 root 執行；本次沒有啟用正式排程。

1. `dotnet run --project tests/ApiContractChecks -c Release -- --sql`；`dotnet run --project tests/AuthCalendarChecks -c Release -- --production`。
2. CLI `--phase4m-review <phase4i> <phase4j> <authority-resolution-root> <protected-roster> <command> <new-receipt>`。命令為 ReviewCommand；先 ASSIGN 指派不同 Operator/Reviewer，接著指定既有 ReviewDigest 執行 REVIEW 或附理由 REJECT/REVOKE。每次 ExpectedDigest 必須等於 queue HeadDigest。root/roster 限操作人員 OS 存取，勿放在網站根目錄。
3. 發布 `--phase4m-export <phase4i> <phase4j> <etl> <scheduler> <authority-resolution-root> <source-sha> <new-output>`，重新驗證 scoped claims 與回執，隨網站部署唯讀快照。撤銷後需重新輸出 Phase4J/Phase4I，所有舊核准須重審。網站快照非即時。
4. `--phase4m-tick <root> <job-id> <Disabled|Dry-run|Approved-enabled> <checklist-json> --controlled`。Disabled/Dry-run 不做 IO；Approved-enabled 僅在正式授權、共享儲存、多 worker recovery、provider permission、backup/restore、secrets、alerts、rollback 全部已驗收後使用。沿用 4L 租約/heartbeat/retry/circuit/recovery，不另建 scheduler。
5. 備份前停止所有 worker：`--phase4m-backup <offline-root> <new-backup> <new-receipt>`；還原到另一個新目錄，用同一命令複製，再以既有 CLI replay 及 workbench export 驗證。byte digest 一致不等於正式環境已復原。不得覆寫或刪除現有 store。
6. 告警來自發布快照，SOURCE_INVALIDATED、LEASE_EXPIRED、FAILED/QUARANTINED 需人工處理；尚未配置正式外部通知，不宣稱即時告警已驗收。
7. 回滾：停 worker，保留新 receipts，部署之前核對 SHA 的既有 3.2.2 Phase4L 套件；不回滾資料庫、不刪除證據、不解除撤銷。核對 system/health、readiness、匿名 MVC/API。必要時先評估舊版能否識別新快照；舊套件保留其原快照。
8. `deploy/Verify-Phase4M.ps1 -BaseUrl <url> -ExpectedSha <release-sha> -Output <report>` 為匿名 GET smoke；真實 A/B 另依 SecurityAcceptance 人工驗收。
