# Worklog 3.2.1 — Phase 4K + 會員存取與資料隔離

本版依兩份文件實作：共用 ETL/provider/quality/lineage 核心、六個唯讀 API、研究頁面監控，以及集中會員功能授權。既有 Identity/Google/Cookie、七項 PIT、獨立構面核准及模型公式沿用。使用者已明確授權 Git 與線上發布；沒有新增正式資料庫 migration。

實際處理兩個既有 TWSE 封存，2 個真實 ETL 工作、21 筆事實（價格20、法人1），重播一致；沒有新增網路取得。TPEx adapter 有 deterministic fixture 驗證；未宣稱取得真實 TPEx 歷史資料。FinMind ETL 待明確授權/資料契約，沒有把既有 token 輸出至報告。資料品質、歷史權威與 PIT 分開，Unknown 不轉成零。

原13來源版本、43取得工作、23版本綁定紀錄、20查核需求保留。1筆觀測、0合格觀測、0核准資料集/樣本；真實 baseline NOT_RUN、門檻 UNRESOLVED、NOT_PROMOTED、Phase5 LOCKED。仍缺歷史發布時間、修訂留存、身分/universe/calendar/price basis 及足量研究樣本。成功匯入不是模型驗證成功。

API/Application 優先：ETL orchestration 與 ICurrentUser contract 放在 Application；UI 透過 /api/v1 取得監控資料，不實作公式。Api→Web 的舊 hosting/Identity/EF 相依仍屬過渡，詳 ETLArchitecture/AuthorizationImplementation。基本市場行情公開，會員才能使用波段、回測、策略/分析工作與虛擬投資。A/B 私人資料由伺服器 owner 及既有交易鎖保護。

Microsoft.Data.SqlClient 6.1.6 原傳遞 Pkcs 9.0.11；以直接相依固定修正版 System.Security.Cryptography.Pkcs 9.0.21，已確認還原結果及無警告編譯。依據 Microsoft [GHSA-846f-f764-76mx](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx) 與 [GHSA-2fmv-rv2c-59x7](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7)。登入回歸126項通過。

報告索引：Audit-3.2.1.md、ETLArchitecture-3.2.1.md、ProviderCapabilityMatrix/EtlPipelineReport/DataQualityReport/DataLineageReport/AuthorityClosureReport/PITEligibilityReport/DatasetReadiness/ModelValidationStatus-3.2.1.json、APIContractChange-3.2.1.md、AccessControlAudit.md、RouteAccessMatrix.md、AuthorizationImplementation.md、AuthorizationTestReport.md、engineering-3.2.1.json。

本機入口 Start-WorkJournal3210.ps1，http://localhost:5207；研究監控 /ResearchValidation。受控 CLI 的 transformCodeSha 綁定原始碼 commit，最終發布 commit 可另外包含該 SHA 產生的不可變報告；四個執行組件與線上 health 綁定最終發布 SHA。CI 須對最終整合 head 全綠後才 fast-forward main，並驗證線上版號、SHA、readiness、公開頁與受保護端點。
