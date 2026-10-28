# 2.0.2 — Phase 2 V2 Foundations 實作報告

本版依「V2 Foundations 版本2」新增可追溯、可歷史重播的基礎資料及服務。既有網站評分入口仍使用原有模型；本階段不計算新的 FullEvidence100 分數。

## A. Baseline

- Start SHA：`bc799e0df5ecb0b652e111372a9aa0918ad8ac6d`。
- 基準 Golden Master：GitHub Actions run `40594895401`，同 SHA、conclusion=success。
- Branch：`codex/v2-foundations-2.0.2`。
- End SHA：以該分支／PR head 及 CI `golden-master.json` 的 commitSHA 為準，避免在提交內容中宣稱自己的尚未產生 SHA。
- 本機 `.git` 仍指向舊版本；未 reset、覆蓋或上傳舊指標。提交直接基於上述已驗證遠端基準建立。
- 基準檔案比對：346 個既有檔案內容未變（忽略 BOM／換行）；其餘既有變更只限 CI、DbContext、model snapshot、DI 註冊、專案版號與版本文件。Legacy 計算程式及測試 expected values 均未修改。

## B. Changed Files

新增：

- `src/WorkJournal.Web/Models/V2/Identity.cs`
- `src/WorkJournal.Web/Models/V2/Foundations.cs`
- `src/WorkJournal.Web/Data/V2IdentityMapping.cs`
- `src/WorkJournal.Web/Data/V2FoundationMapping.cs`
- `src/WorkJournal.Web/Services/V2/CanonicalJson.cs`
- `src/WorkJournal.Web/Services/V2/FoundationService.cs`
- `src/WorkJournal.Web/Services/V2/FoundationRegistration.cs`
- `src/WorkJournal.Web/Data/Migrations/20261028133412_AddV2InstrumentIdentity.cs`
- `src/WorkJournal.Web/Data/Migrations/20261028133412_AddV2InstrumentIdentity.Designer.cs`
- `src/WorkJournal.Web/Data/Migrations/20261028134159_AddV2SnapshotsAndDecisionFoundations.cs`
- `src/WorkJournal.Web/Data/Migrations/20261028134159_AddV2SnapshotsAndDecisionFoundations.Designer.cs`
- `tests/V2FoundationChecks/V2FoundationChecks.csproj`
- `tests/V2FoundationChecks/Program.cs`
- `docs/implementation-2.0.2.md`
- `docs/releases/2.0.2.md`

修改：

- `src/WorkJournal.Web/Data/JournalDbContext.cs`：註冊新增 entity mapping 及 V2 儲存保護。
- `src/WorkJournal.Web/Data/Migrations/JournalDbContextModelSnapshot.cs`：EF 模型快照。
- `src/WorkJournal.Web/Program.cs`：新增 V2 服務 DI。
- `src/WorkJournal.Web/WorkJournal.Web.csproj`：Version=2.0.2。
- `.github/workflows/ci.yml`：新增 V2 SQL job，彙總要求四個 job 同 SHA 成功。
- `README.md`、`CHANGELOG.md`：版本及說明。

## C. Database

Migration 順序：

1. `20261028133412_AddV2InstrumentIdentity`
2. `20261028134159_AddV2SnapshotsAndDecisionFoundations`

新增資料表：

| 分組 | Tables |
|---|---|
| 身分 | V2Instruments、V2InstrumentSymbols、V2LegacyInstrumentMappings、V2CorporateActions |
| 資料 | V2DatasetSnapshots、V2DataProvenance |
| Universe | V2UniverseSnapshots、V2UniverseMembers |
| Benchmark | V2BenchmarkDefinitions、V2BenchmarkVersions、V2BenchmarkMappings |
| 版本與追蹤 | V2VersionSets、V2ParameterSets、V2DecisionTraces |

DB 結構約束：永久鍵唯一；代號／市場／有效起點唯一；Legacy 來源／市場／StockId／起點唯一；Universe 成員複合主鍵；快照／版本集／追蹤內容雜湊唯一；ParameterSet Code+Version 唯一；Benchmark Code 及 DefinitionId+Version 唯一。有效期間必須終點大於起點，品質／Universe／文件狀態有 CHECK constraint。

FK 連接 Instrument、快照、來源、Universe、Benchmark、VersionSet、ParameterSet、DecisionTrace；採 Restrict 刪除。PIT 查詢有 snapshot/instrument/AvailableAt 索引；trace 有 instrument/date/type 索引。

`Up` 僅新增表與索引，沒有 Legacy Drop/Alter/Rename 或歷史資料回填。`Down` 只移除本次新增表，正式有 V2 資料後不應當作無損回滾。

本機測試在 GUID 暫存資料庫完整執行 migrations，並確認 EF 無 pending model changes；測試資料庫最後經既有 ownership guard 清除。此次實作未對線上資料庫執行 migration。

## D. Instrument Identity

InstrumentId 由明確且穩定的 PermanentKey 產生，不從 Symbol 推定。呼叫者須提供有根據的永久識別，不會把現有代號自動當永久身分。

`Legacy Source + Market + StockId + 時間` 對照 InstrumentId。舊表、舊 StockId FK 及既有查詢仍保留；不自動搬移歷史資料。

代號歷史使用 `[ValidFrom, ValidTo)` 半開區間。改代號會關閉同一 Instrument 的前一個未結束區間；跨 Instrument 的重疊代號會拒絕。代號重用需另一個永久身分及不重疊期間。

Currency 必須由呼叫者提供三位大寫代碼，不寫死 TWD。CorporateAction 只預留事件身分、種類、時間與來源，不包含會計／調整引擎。

## E. Temporal / PIT

- EffectiveAt：經濟事件生效或資料所描述的時間。
- AvailableAt：可驗證的來源／市場公開可取得時間；未知保留 null，不以期間截止日或下載時間補造。
- ObservedAt：本系統第一次取得該資料的時間。
- RetrievedAt：本次來源表示的取得時間，可晚於首次 ObservedAt，不可早於它。
- DataCutoff：研究允許使用資料的截止時間。

DateTimeOffset 在儲存／讀取時統一 UTC，日期另用 DateOnly。服務建立時間取自既有 TimeProvider。

PIT 選擇「市場當時可得」語意：AvailableAt <= DataCutoff；ObservedAt 用於來源稽核，因此後來匯入但能證明當時已公開的資料可參與歷史研究。這不代表該系統當時真的持有或交易過該資料；如未來需要「系統當時實際已知」重播，應另建明確的查詢契約。

IPitQueryService 必須傳 instrument、snapshot ID 與精確 cutoff，不提供 GetLatest 給歷史重播。快照 cutoff 不一致會拒絕；AvailableAt=null 或晚於 cutoff 的記錄不回傳。回傳值保留品質狀態，不能將 Missing／Invalid 自動當數值 0。

## F. Snapshots

DatasetSnapshot 保存表頭及不可變 DataProvenance：包含來源、時間、品質、標準化 PayloadJson、checksum、選用 RawReference。歷史重播讀取凍結 payload，不回查目前行情資料表。

建立時依內容計算完整 SHA-256 checksum 與固定 Guid identity，忽略輸入建立時間及 ID；資料記錄排序不影響整體身分。來源取得時間屬於稽核內容，變更它會形成不同快照。

UniverseSnapshot 保存當時成員、UniverseEligibility 與 ReasonCode。新的 Universe 不影響歷史成員；UniverseEligibility 不代表 StrategyEligibility。

公開建立服務直接在交易內建立 Finalized 文件及子記錄，避免對外暴露半成品。資料模型保留 Draft／Failed 狀態，未提供完整工作流程編輯 API。Finalized 內容及狀態不得回退或覆寫，子記錄也不可追加／修改／刪除。

跨實例重試使用 transaction-owned sp_getapplock、Serializable transaction 與 unique constraints；非 static SemaphoreSlim。鎖依文件類別分組，適合目前單人系統；大量吞吐時可在保留相同 invariant 下細化鎖粒度。

## G. Versioning

VersionSet 保存 ModelVersion、FeatureVersion、UniverseVersion、BenchmarkVersionId、DataVersion、可選 StrategyVersion，並有 ExtensionsJson 供未來真正需要的政策版本擴充。Benchmark 用 FK 精確引用定義版本，另以 Mapping 保存 Instrument 有效期間。

Benchmark 定義／版本／對照採 append-only。Phase 2 提供 EF 儲存契約，未實作 Benchmark 管理畫面或 Relative Strength calculator。

ParameterSet 的 Code+Version 不得重用成不同參數。Canonical JSON 遞迴按 ordinal 排序物件屬性、保留陣列順序、拒絕重複鍵，精確正規化十進位數值；1、1.0、1e0 一致，巨大整數不經 double 四捨五入。不同 JSON 字串仍視為不同字串，不猜測日期／字串的額外語意。JSON 深度上限 64、數字長度／指數範圍有防護，屬技術輸入限制而非交易政策。

Checksum 是參數內容雜湊；同內容可有不同命名版本。ParameterSet 身分還包含 Code、Version。

## H. Decision Trace

Durable DecisionTrace 透過 FK 連接精確 instrument、dataset、universe、version set、parameter set，另保存 TradeDate、DecisionTime、DataCutoff、DecisionType、DecisionStatus、ReasonCode、DetailsJson。

建立時驗證所有文件已 Finalized、日期及 cutoff 相符、資料／Universe 版本一致、Instrument 存在歷史 Universe、Benchmark Mapping 與版本在當時有效。DecisionTime 不得早於 DataCutoff。

相同決策內容可重試；不同決策內容產生新 trace，不覆蓋歷史。GetDecisionAsync 可重查持久內容，並非 application log。研究拒絕結果可引用 Ineligible 成員，因此不把 Universe 成員資格偷換成交易決策。

EF SaveChanges 保護會查詢資料庫已保存狀態，不信任 detached entity 宣稱的 OriginalValues。這是應用程式寫入邊界；直接 SQL／ExecuteUpdate 等繞過 SaveChanges 的管理路徑不在此保護內，V2 服務未使用這些路徑改寫歷史內容。

## I. Tests

本機 V2 Foundation：45 項通過，exit code 0。

- Instrument：固定身分重試、改代號、歷史邊界、代號重用、重疊拒絕、Legacy 時間對照、DB 永久鍵唯一。
- PIT：截止前一秒及當下包含、後一秒排除、未知 AvailableAt 排除、舊事件日期不代表已公開、UTC 儲存、不同 cutoff 拒絕。
- Snapshot：排序／重試固定身分、表頭及子記錄不可變、禁止追加、四個獨立 SQL 連線並行建立同一新快照只生成一份。
- Universe：重試、不同日期成員互不影響、成員不可變。
- Version／Parameter：版本改動改變身分、Finalized 不能降回 Draft、參數標準化、重複鍵拒絕、缺失 Benchmark FK 拒絕。
- Trace：精確引用、重試、cutoff／Universe／版本／參數不符拒絕、不可變；detached entity 不能偽造 Draft 覆寫。
- Migration：兩支 migration 完整執行，EF model snapshot 一致。

命令：

```powershell
dotnet build WorkJournal.sln -c Release --artifacts-path artifacts/build202
dotnet run --project tests/V2FoundationChecks -c Release --artifacts-path artifacts/build202
```

本機 SQL Server 2025 Express；CI 使用 SQL Server 2022 Developer。所有 SQL 測試使用既有 GUID 測試資料庫防護；不讀寫正式資料庫。`--fast` 可只跑不需 SQL 的 canonical／Evidence contract 檢查。

## J. Legacy Regression

本機結果：

```text
build = PASS (exit 0)
legacy-fast = PASS (all exit 0)
legacy-sql = PASS (all exit 0)
legacy-golden-master = PASS (local constituent checks)
```

- MarketRadarChecks：220 checks。
- FinMindChecks：全部 deterministic checks。
- LegacySwing75Checks：74 拒絕、75 下一開盤成交、不當日成交、Missing 拒絕，4 checks。
- PaperTradingChecks：59 SQL 主檢查及該 harness 的額外檢查。
- StrategyCenterChecks：41 checks。
- MarketRadarTrackingChecks：35 checks，另含 enrollment／StockResearch 子檢查。

上述每個 project 執行 `dotnet run --project tests/<project> -c Release --artifacts-path artifacts/build202`；詳細本機 log 為 `artifacts/foundation202-*.log`，命令 exit code 為 `artifacts/foundation202-legacy-results.json`。

第一次一般 Release 建置因既有本機網站占用 DLL 而失敗；改用獨立 artifacts 輸出後成功。未關閉使用者本機網站。原有 Pkcs 9.0.11 NU1902／NU1903 警告仍存在，未在本次變更套件。

CI 增加 `v2-foundation` job，原 Golden gate 加入 V2_RESULT 與 SHA 比對，任一 job 失敗或 SHA 不同都不能通過。遠端執行證據以該 PR 的 Actions run／`golden-master-evidence` artifact 為準；本機通過不冒充遠端已通過。

## K. Production Values

```text
V2Threshold = UNRESOLVED
No production threshold invented
No allocation value invented
No risk limit invented
```

FullEvidence100 未實作正式 calculator。預留 EvidenceValue／五種 EvidenceStatus，不把 Missing 轉成 0；ScoreEvidence、StrategySetup、StrategyEligibility、TriggerEvent、TradeDecision 保持不同 DecisionKind。未新增券商呼叫、策略／選股／配置／風控／帳本／交割／績效引擎。

文件鎖定的後續分數權重仍為 Trend25、Momentum15、VolumePrice15、Institutional20、RelativeStrength15、RiskQuality10，共 100；只記錄設計，不作本版 production scoring。

## L. Final Status

```text
PHASE 2 IMPLEMENTATION = COMPLETE
PHASE 2 VALIDATION = PASSED (local build, 45 V2 checks, all Legacy checks)
```

遠端同 SHA CI 狀態由 PR Checks 與 evidence artifact 提供。此版本為 Foundation 實作，沒有提前進入 Phase 3；也尚未將本次新增 migration 套用線上系統。
