# Worklog 3.0.7 — Phase 4D 實作報告

本版在 3.0.6 的歷史資料建構與重播基礎上，加入資料授權、證據審核、資料集核准與真實研究入口。**程式可執行核准流程，不代表既有真實資料已獲核准。** 目前真實 baseline、IC／RankIC、OOS 仍未執行。

## SOURCE

基準 commit：`5d6edf515b463ff79a95657fdcf101eb36b02169`（3.0.6）。依據桌面 `# Worklog Phase 4D.txt` 第 0–7 節，該檔案截止於證據類型 `ProviderDocumentation`。以下狀態轉換路徑、保存格式、政策摘要與完整資料集門檻是本版明列的實作決策，不宣稱來自尚未提供的後續章節。

新增 `HistoricalDataAuthority`：AuthorityId、Provider、DatasetType、SourceIdentity、availability／revision／calendar／price 政策版本、適用日期、EvidenceReferences、ReviewedAt／ReviewedBy、Status、ReasonCode。額外的 `PolicyScopeDigest` 綁定完整 FrozenAvailabilityPolicy、Calendar 與 PricePolicy，避免同版本名稱被換成不同內容。

新增 `HistoricalAvailabilityEvidence`：文件指定的身分、類型、來源引用、CapturedAt、Digest、ClaimedAvailabilityRule、適用日期、Timezone、VerificationStatus、ReviewerNotes；另保存 BodyBase64 原始證據內容。支援五種證據類型：OfficialSpecification、OfficialAnnouncement、OfficialArchiveMetadata、TimestampedArchive、ProviderDocumentation。

`ClaimedAvailabilityRule` 在本版是被主張的凍結 availability policy **版本引用**；具體 Rule、LocalPublicationTime、Timezone 與證據來源由對應政策及 PolicyScopeDigest 綁定。不存在預設公布時刻，也不把 TradeDate／ObservedAt 當作 AvailableAt。

## 審核與執行邊界

1. 先封存證據並確認其內容 SHA256；Verified 是審核者的明確判定，不因來源為 TWSE 或證據類型為官方文件而自動成立。
2. 新授權只能以 Draft 登錄。流程為 `Draft → EvidenceCollected → UnderReview → Approved / Rejected`；Approved 可轉 Superseded。Rejected／Superseded 為終止狀態，修訂定義須新 AuthorityId。
3. 每次轉換記錄前一筆 digest、順序與新狀態。CreateNew 防止覆寫；expected digest 防止過期審核覆蓋新狀態。進入 Approved 前要求封存的 Verified 證據，核准資料進入研究時再逐筆檢查適用範圍。
4. 每筆可用事實需要唯一 AuthorityFactBinding，來源 URI、Provider、DatasetType、修訂政策、政策摘要、有效期間與證據都必須匹配。只有 Approved 能授權；原 3.0.6 PIT、修訂、股票池、基準及價格檢查照常執行。
5. `AuthoritySubmission` 包含原資料集輸入、授權、證據、逐筆 binding 及 EvaluatedAt。資料集 review 必須明確綁定 SubmissionDigest、審核人、時間與理由。沒有 review 就保持阻擋。
6. 本版初始核准範圍要求所有申請日期及各資料領域完整覆蓋，且沒有下市歷史缺漏警示；尚未制定部分真實研究的核准政策。既有合成分項研究與舊資料重播保留。
7. 新真實研究只能從核准封存進入；舊 `--research`／`--pit-research` 不能直接繞過。研究入口驗證 registry 的目前狀態，Superseded 會阻止新執行。
8. Future outcomes 保持獨立，另驗證每個收盤價、可得時間、價格基準與歷史身分都對應已授權事實，並要求核准的交易日曆。它們不會進入 Candidate 特徵輸入。
9. ResearchStore 封存 `authority-approval.json` 並納入 manifest。過去核准的研究可依當時證據重播，即使授權後來被取代；新研究則必須重新核准。

Candidate v1、FullEvidence100 權重與 ResearchEngine 的計算公式未修改。ResearchStore 僅增加新執行入口守門及授權封存；既有 3.0.5／3.0.6 研究與資料集重播維持相容。不增加網站寫入 API，不建立資料庫 migration。

## EXECUTION

- 新增 58 項授權檢查：六狀態、來源／版本／適用期間／政策摘要、未知公布時間、證據完整性、審核順序、不可覆寫、過期摘要、逐筆 binding、核准 receipt、入口防繞過、標籤來源、核准日曆、取代後阻擋及歷史重播。
- 既有 PIT 45、Research 78、Candidate 104、Evidence 64、Foundation 45 檢查及 Legacy／SQL 回歸通過。詳細執行證據在 `artifacts/phase307-regression.json`、`phase307-authority-final.log`。
- 新增 `v3-authority` CI，保留原九項工作並納入同 SHA 的 Golden Master gate。
- 正向核准／研究測試使用明確標示的模擬 authority metadata，為了測試入口而將 fixture 的 Synthetic flag 設為 false；這不是任何真實來源的核准證據。
- 真實樣本 CLI 執行 `--authority-digest`、`--authority-evaluate`、`--authority-replay` 並驗證 `--authority-research` 正確拒絕。另重播原 3.0.6 真實資料封存驗證相容性。
- 本機服務使用 `Start-WorkJournal307.ps1`、`http://localhost:5197`；測試與研究檔案不放入 Azure 網站發布包。
- 原有 Pkcs 9.0.11 的 NU1902／NU1903 套件警告仍存在，本版沒有調整套件。

## RESEARCH

沿用 3.0.6 已封存的 21 筆真實 TWSE 來源事實：2330 的 2026-09 價格 20 筆及 2026-09-30 法人事實 1 筆。未重新抓取、未更改來源內容或補造歷史時間。

本次沒有可核准的 HistoricalDataAuthority，也沒有足以證明原始公布時間及修訂身分的 Verified evidence，因此結果為 `BLOCKED_BY_AUTHORITY_OR_DATA`，Approved ResearchSnapshot=0。Raw archive 可重播，不等於 Approved PIT dataset。

真實 Candidate-v1 baseline、IC／RankIC、OOS 均為 **NOT_RUN**。未自動替換現有網站評分、未制定買賣門檻、未 promotion 模型。

## KNOWN GAPS

- AUTHORITY_GAP：真實來源沒有完成審核的資料授權、逐筆 binding 與核准資料集 receipt。
- AVAILABILITY／REVISION：仍缺可驗證的歷史公布時間、適用政策與原始修訂證據。
- UNIVERSE／BENCHMARK／CALENDAR／CORPORATE_ACTION：沿用 3.0.6 缺口；不能以今日股票名單或未審核價格調整方式替代。
- 研究參數、歷史分類及完整資料涵蓋仍需建立可審查輸入；完整度門檻不會轉換成分數或模型有效性。
- 本版是**受控本機 CLI 的審核紀錄與執行檢查**。ReviewedBy 是操作者提供的審核人聲明，未連接企業帳號驗證、電子簽章、獨立審核者授權名單或外部文件真偽服務。程式能檢查摘要、版本、時間及範圍，不能自行判斷文件內容是否足以證明歷史事實。檔案系統寫入權限及備份需由部署操作者管理。

機器可讀結果見 [phase4d-gap-3.0.7.json](phase4d-gap-3.0.7.json)。來源補齊後，須先實際審查證據，再建立核准紀錄；通過程式測試不會解鎖真實模型 promotion。

## CLI 使用

先 `dotnet build src/WorkJournal.Evidence.Cli -c Release`；以下參數傳給該 CLI（或 `dotnet run --project src/WorkJournal.Evidence.Cli -- ...`）。

```text
--authority-evidence evidence.json authority-store
--authority-policy-digest dataset-request.json POLICY_GUID
--authority-register draft-authority.json authority-store
--authority-review authority-store AUTHORITY_GUID EXPECTED_DIGEST EvidenceCollected ISO_TIME REVIEWER REASON
--authority-review authority-store AUTHORITY_GUID EXPECTED_DIGEST UnderReview ISO_TIME REVIEWER REASON
--authority-review authority-store AUTHORITY_GUID EXPECTED_DIGEST Approved ISO_TIME REVIEWER REASON
--authority-export authority-store AUTHORITY_GUID new-authority.json
--authority-digest submission.json
--authority-evaluate submission.json authority-store
--authority-replay authority-store REPORT_DIGEST
--authority-research authority-store REPORT_DIGEST research-template.json research-store
```

各狀態輸出新的 EXPECTED_DIGEST；資料集 review 另填入 `DatasetApprovalReview`，不是執行 evaluate 後自動核准。第一次未核准的 evaluate 與後續核准結果以不同 ReportDigest 保存；不能覆寫同一結果。範圍或證據變動後須重新審核。
