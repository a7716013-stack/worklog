# Phase 4P Architecture Audit
Baseline main: 5ddefcc6a26f3be4dbd9867eebd8ff8c8e16276f, v3.2.4. Remote ref and all tracked local blobs checked before edits. AGENTS.md and the user specification reviewed.
OfficialDataProviderAdapter already supports TWSE Price/Institutional and TPEx Price; FinMind has no approved ETL contract. Phase4JAcquisition owns safe DNS transport, persistent quotas and evidence acquisition. ForwardCaptureStore owns append-only observations, revision chains and per-plan ACQUIRE/STORE receipts. EtlScheduler owns leases, retry/recovery and archive dispatch; EtlPipeline owns normalized facts and lineage. Reuse these; no new timer or production HostedService.
Missing: approved multi-request scope, externally trusted revocable rights activation, real price acquisition orchestration before archive dispatch, temporal industry/suspension context and unified feature/outcome pilot preparation. Existing identity/session/action models, HistoricalDatasetBuilder, formula/outcome services, Authority/PIT/component gates and TrendSample-v1 remain authoritative.
Database migrations inspected: this extension uses existing file archives, no new database migration or production database change. API metadata extends ResearchValidation; processing stays Application/Domain. Existing Web computations outside this scope remain unchanged.
Production account acceptance, rights, exact scope and environment approval are external dependencies. Prior 542 API/SQL and 127 identity checks are baseline evidence, not this release results. Real acquisition, baseline, scheduling and promotion are not authorized by deployment authorization. User explicitly authorized Git upload and website deployment for 3.2.5.
Implementation follows PR-1 through PR-9 in specification order; one integrated release PR will preserve these work packages and dependencies.

<!-- binding -->
Implementation source SHA: 8f257043d0575897dc4bea0837fdef0c241d7f27
Input Phase4O digest: 3E8B32C6D3946FA87A477B43D8A4DA951B42C116870066DF14CDC664EE95EC81
Export digest: 0E159694507DB4519AFB0A4B883D023CC9E4A2F3306398737CF3539CFC2D587D
Contracts: Phase4P-v1 / HistoricalNormalized-v1 / TrendSample-v1.
