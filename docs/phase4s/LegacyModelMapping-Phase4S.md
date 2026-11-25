# Legacy model mapping

LegacyRadar-v1 identifies the unchanged MarketRadarScoreCalculator implementation previously stored as recommendation ScoreVersion V1-1.2.3. Existing ScoreVersion is not overwritten. Six parts remain 20/20/20/15/10/15; Qualified >=70 applies only to its complete integer score. LegacySwing75-v1 identifies the unchanged SwingCalculator; its complete-score 75 threshold, penalties and seven strategy predicates remain independent of Candidate.

Adapter FormulaDigest values are SHA256 of the unchanged calculator source (UTF-8, LF). Candidate FormulaDigest continues to use FormulaCatalog's existing embedded-source identity, not these legacy digests. Legacy parameter identifiers are explicit fixed-contract labels. Legacy data lacks a finalized Candidate dataset identity, so DatasetSnapshotId is null; unavailable cutoff is null, never invented as PIT eligibility.
