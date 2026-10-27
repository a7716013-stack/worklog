# 2.0.1 實作報告 — Legacy Golden Master

本報告記錄 Phase 1 已完成的實作與驗證。Radar、Swing 舊模型保持凍結；FullEvidence100、V2 API 及新門檻尚未啟用。原先被 artifacts 忽略的報告與 JSON 證據已納入 docs，由 Git 版本控制。

## Commit lineage

- BaselineSHA: d08e5b6fa300c2987fbf58d17e03b7e906cd2862
- Existing 1.5.3 preservation: 8fa9194ff247dcb20705ec2ddab81026f1e4b70f
- Phase 1 implementation: bb6df3eaea31ed85807d191816b34812fed225c0
- ImplementationSHA including version/documentation: 0dc518c0b74b41ecb92493b23efea3f8bb16893c
- Branch: codex/2.0.1-legacy-golden-master
- PR: https://github.com/a7716013-stack/worklog/pull/1
- WorkflowRunId: 40593571820
- Workflow: https://github.com/a7716013-stack/worklog/actions/runs/40593571820

## Production Semantic Diff

Phase 1: NONE. GitHub compare confirms the implementation commit changes only the 13 test/CI files below.

The preceding preservation commit includes already-deployed 1.5.3 data-source/cache/availability behavior and is not claimed to have zero semantic difference from the 1.5.2 baseline. Core Radar calculator, Swing calculator/backtester and paper accounting remain unchanged. Version metadata is separate.

## Changed Files (Phase 1)

- .github/workflows/ci.yml
- tests/MarketRadarChecks/Program.cs
- tests/FinMindChecks/SwingChecks.cs
- tests/FinMindChecks/FinMindChecks.csproj
- tests/FinMindChecks/LegacySwing75Checks/LegacySwing75Checks.csproj
- tests/FinMindChecks/LegacySwing75Checks/Program.cs
- tests/PaperTradingChecks/Program.cs
- tests/PaperTradingChecks/TestDatabase.cs
- tests/StrategyCenterChecks/Program.cs
- tests/StrategyCenterChecks/StrategyCenterChecks.csproj
- tests/MarketRadarTrackingChecks/Program.cs
- tests/MarketRadarTrackingChecks/MarketRadarTrackingChecks.csproj
- tests/MarketRadarTrackingChecks/StockResearchChecks.cs

Version/documentation: src/WorkJournal.Web/WorkJournal.Web.csproj, README.md, CHANGELOG.md, docs/releases/2.0.1.md.

## Database Changes

No production table, column, index, constraint or migration changes. Only ephemeral GUID test databases created and deleted. The nested favorites migration fixture was included because the blocking RadarTracking suite invokes it and it also hardcoded SQL connectivity.

## Domain Invariants

Missing != Zero; no weight redistribution; Radar and Swing remain distinct; LegacySwing75 != V2Threshold; next-session execution; valuation != execution; immutable recommendation snapshots; account isolation; test database cleanup cannot target a configured production database.

## Local tests

Environment: Windows 10.0.26200 win-x64, .NET SDK 10.0.401, MSBuild 18.9.11, runtime 10.0.12. SQL Server 2025 Express 17.0.1000.7.

| Command | Exit | Result |
|---|---:|---|
| dotnet build WorkJournal.sln -c Release --artifacts-path artifacts/checks201 --no-restore | 0 | PASS |
| dotnet run --project tests/MarketRadarChecks --no-restore | 0 | 220 checks; exact Radar 94 |
| dotnet run --project tests/FinMindChecks --no-restore | 0 | Existing indicator/ETF/FinMind and exact Swing 60 |
| dotnet run --project tests/FinMindChecks/LegacySwing75Checks | 0 | 74 reject, 75 next-open entry, no same-day entry, missing reject |
| dotnet run --project tests/PaperTradingChecks -c Release --artifacts-path artifacts/checks201 | 0 | 59 domain checks plus 9 intraday checks and database guards |
| dotnet run --project tests/StrategyCenterChecks -c Release --artifacts-path artifacts/checks201 | 0 | 41 checks |
| dotnet run --project tests/MarketRadarTrackingChecks -c Release --artifacts-path artifacts/checks201 | 0 | 35 tracking + 18 enrollment + 16 research checks |

Earlier execution issues: sandbox directory permissions required escalation; the running local website locked the normal Release DLL so SQL suites were rebuilt in an isolated artifacts directory. An initial test expectation incorrectly awarded MACD points to the linear fixture; reading the frozen EMA algorithm established DIF=Signal=7, correcting only the test expectation to 60. No production code was changed to make tests pass.

## CI / Known Gaps

CI jobs: build, legacy-fast, legacy-sql, legacy-golden-master all completed successfully. Aggregate logs verified all three checkout SHAs equal ImplementationSHA `0dc518c0b74b41ecb92493b23efea3f8bb16893c`. Completed 2026-10-27 22:18:16 UTC / 2026-10-28 06:18:16 Asia/Taipei. Runner Ubuntu 26.04.1; CI uses the pinned global.json SDK and SQL Server 2022 container.

Evidence artifact: https://github.com/a7716013-stack/worklog/actions/runs/40593571820/artifacts/12775825473

Artifact SHA256: 80023e43b61fdf4439672832be6d7c5468050445fb467c4e1cef4716e95dcc51

The aggregate JSON is tracked as [legacy-golden-master-2.0.1.json](legacy-golden-master-2.0.1.json). Job IDs: build 122503667340, fast 122503667331, SQL 122503666837, aggregate 122504293994.

Browser tests: NOT_PART_OF_GATE. External provider probe: NOT_PART_OF_GATE. Existing Pkcs 9.0.11 NU1902/NU1903 warnings remain. No FullEvidence100 implementation, invented normalization, V2 threshold, deployment or Phase 2 work.

The original workspace Git metadata remains on its old local main; remote branch lineage above was created through GitHub from the verified baseline. Do not blindly push the original local main. The new remote commit contains the reviewed working files; CI uses a clean checkout of that exact SHA.

## Final Status

IMPLEMENTATION DIFF = PASS

LEGACY GOLDEN MASTER = PASSED

Final Status: PASS (Phase 1 only). Phase 2 unlocked but not started. This evidence describes the validated implementation commit; subsequent release/documentation commits and deployment are traceable through PR #1, main history and the 2.0.1 release.
