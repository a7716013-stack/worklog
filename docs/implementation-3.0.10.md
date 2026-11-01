# Worklog 3.0.10 — Phase 4F-1 實作報告

已閱讀完整 `# Worklog Phase 4F-1.txt`（第 0–100 節及 Final Target State）。本版新增分項資料集核准流程與逐筆來源追溯；**尚未取得第一個已核准真實資料集，未執行真實 baseline**。依第 95 節停止條件，歷史證據不足時保留阻擋，不推定 AvailableAt、歷史 Universe、修訂版本或調整政策。

## 範圍與實作

- `ComponentAuthorityDependencyManifest` 明確列出 Trend／Momentum／VolumePrice 必要 domains、datasets、policies、identities。Institutional 不是必要依賴；benchmark identity 與 benchmark return data 分開。第一輪優先 Trend。
- `AuthorityClaimResult` 包含支持、覆蓋、衝突、review、缺口與逐項修訂摘要。既有 case 及 raw archive 可重播；新 supersede 必須指向當時已核准且涵蓋原範圍的替代案件。3.0.9 歷史封存不回寫。
- 明確交易 session 型別、半開區間歷史識別、Universe snapshot/member、availability/revision policy、corporate action、authority registry 與 raw row locator 契約。日曆缺分母時 Coverage=null，不把平日直接當交易日；身份解析不使用目前代號對應回填。
- TWSE STOCK_DAY parser 保存 response digest、列號、日期、symbol、解析版本及 value digest。`--component-link-price` 產生確切 FactId 與準備資料集識別；未知永久身份使用未解析標記，無法通過核准。原始 revision 及 AvailableAt 保持 null，不把 captured-body checksum 當 revision proof。
- `ComponentDatasetGate` 檢查 request digest、component scope、case/policy IDs、目前 closure、Phase 4D/E 核准、calendar、universe、raw-to-fact lineage、availability、revision 及 price basis。未來修訂由既有 PIT builder 排除；只有其實際選入的 facts 可進入分項 lineage 驗證。
- `ComponentDatasetStore` 只有全部必要關卡通過才寫 immutable receipt，封存 evidence bytes／review／closure／authority 與 manifest。Replay 重建 dataset／coverage／approval 摘要；撤銷只限制未來使用，不刪除歷史。新 baseline 重新驗證目前核准狀態。
- baseline manifest 綁 CodeCommitSha、Candidate-v1、parameters、dataset、universe、benchmark identity、closure、policies 與 price basis；code SHA 必須匹配編譯進 Web assembly 的 SourceRevisionId。新 wrapper 呼叫既有 ResearchEngine／RealBaselineStore，沿用 IC、quantile、turnover 與 Trend feature ablation，不另寫研究數學。完整分數保持 null，OOS 禁止。
- 新增 `v3-component-dataset` 同 SHA CI，保留 Legacy 全部驗收。錯誤與 blocked evaluation 保存 failure artifacts；CLI 不會退回合成資料。

可接受的 parser 目前明確限定 TWSE STOCK_DAY；其他來源必須增加有版本的 parser 與對應測試才能核准。型別與 operator review 並不是自動語意認證；reviewer 必須對官方證據的涵蓋範圍負責。既有 Candidate snapshot 的資料形狀、歷史 Universe 與 benchmark identity invariant 均保留。

## 真實資料執行

沿用 3.0.9 六份 raw evidence，重新核對各 claim；另封存兩份官方規則／服務文件。一般休市處理規則不等於指定期間所有實際休市紀錄；服務分類也不是 STOCK_DAY 每筆資料的歷史發布時間或修訂證據。

- [TWSE 天然災害處理規則](https://www.twse.com.tw/zh/clearing/suspended.html)
- [TWSE 資訊服務說明](https://www.twse.com.tw/zh/products/information/information.html)

20 筆 2026-09 的 2330 原始價量成功連到解析值、Fact checksum 與準備資料集摘要。這次資料集申請仍是 2026-09-30 的 Trend 準備範圍，**不宣稱已建立整個 9 月的完整研究日曆或足夠 lookback**。原始案件綁定 3.0.9 subject，新 facts 需要新的範圍審查，不能沿用舊核准。

核准命令已實際執行並拒絕；未產生 receipt。20 筆均缺歷史 availability／revision 核准，歷史 instrument identity、universe 與 price-basis 也未齊備。`InstrumentCount=0`／`ObservationCount=0` 是核准觀測數，並非原始價格為零；未知分母維持 null。

| 驗收項目 | 結果 |
|---|---|
| Approved Authority Domains | 0 |
| Partially Supported Domains | 6，仍不足以核准新資料集 |
| Blocked Domains | 六類皆阻擋本次申請 |
| Approved Component Dataset Count | 0 |
| FIRST_APPROVED_REAL_COMPONENT_DATASET | NO |
| FIRST_REAL_COMPONENT_BASELINE | NO |
| Real IC／RankIC／Quantile／Ablation | NOT RUN |
| Real OOS | NOT RUN |
| V3 Model Promotion | NOT_PROMOTED |
| V3Threshold | UNRESOLVED |
| Phase5 | LOCKED |

依文件第 90 節，沒有建立名為 first-real-baseline 的報告；沒有把測試數值冒充真實研究結果。

- [Authority 報告](phase4f1-authority-report-3.0.10.json)
- [Component Dataset 報告](phase4f1-component-dataset-3.0.10.json)
- [20 筆 lineage](phase4f1-lineage-3.0.10.json)
- [本機驗證摘要](phase4f1-execution-3.0.10.json)

## CLI

入口為 `dotnet run --project src/WorkJournal.Evidence.Cli --`。所有新輸出檔不可已存在。

```text
--component-dataset-request <closure.json> <requester> <new-file>
--component-lineage <evidence-store> <evidence-id> <new-file>
--component-link-price <component-input> <evidence-store> <evidence-id> <new-file>
--authority-claims <case-id> <closure-store> <evidence-store> <new-file>
--authority-conflicts <case-id> <closure-store> <evidence-store> <new-file>
--authority-coverage <input> <store> <evidence> <authority> <closure> --output <new-file>
--component-dataset-evaluate <input> <store> <evidence> <authority> <closure> --output <new-file>
--component-dataset-approve <input> <store> <evidence> <authority> <closure> --output <new-file>
--component-dataset-replay <approval-id> <store> <evidence> <authority> <closure> <new-file>
--component-dataset-revoke <approval-id> <store> <evidence> <authority> <closure> <status> <reason> <time>
--component-baseline <approval-id> <template> <code-sha> <store> <evidence> <authority> <closure> --output <new-file>
--component-baseline-replay <run-id> <store> <evidence> <authority> <closure> <new-file>
--component-report <run-id> <store> <evidence> <authority> <closure> <new-file>
--closure-supersede <old-id> <approved-new-id> <closure-store> <evidence-store> <time> <reviewer> <reason> <new-file>
```

Store 必須與 evidence／authority／closure 來源目錄分開。準備申請的 `RealApproved` DataMode 代表請求真實核准路徑，只有非空且驗證成功的 ApprovalReceipt 才代表實際核准。baseline 編譯須使用對應提交的 `-p:SourceRevisionId=<40字元SHA>`；沒有 matching SHA 的本機開發 build 不能宣稱相同 SHA 的研究。

## 測試與發布驗收

22 項本機 build、Legacy、研究與 CLI 回歸通過；最後受影響測試為 closure 120 項、component dataset 53 項，既有 real-data contract 42 項。測試驗證拒絕缺證據、PIT／coverage／lineage／revision／parser／replay 等契約；**不是已核准真實資料或真實 baseline 成功的證明**。既有 positive baseline fixtures 保留於 V3RealDataChecks；新的完整真實核准與 wrapper baseline 路徑仍須正式 authority evidence 才能作真實驗收。

Candidate 計算與 Research 核心摘要未變；沒有新增 SQL migration、改動 Legacy 評分或開啟 Azure SQL 超額計費。原有 Pkcs 9.0.11 NU1902／NU1903 警告仍存在。

Latest SHA 以 [3.0.10 Release](https://github.com/a7716013-stack/worklog/releases/tag/v3.0.10) 對應提交及相同 SHA CI 的 golden-master.json 為準。發布後的確切 SHA、workflow IDs、套件雜湊、網站檢查結果另保存於本機 `docs/release-3.0.10-status.local.json`。GitHub gate 通過後才部署；登入頁 HTTP 200 不代表完成 Google OAuth 登入驗證。
