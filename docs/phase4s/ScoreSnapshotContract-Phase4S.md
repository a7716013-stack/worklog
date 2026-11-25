# Score snapshot contract

ScoreComparison contains Mode, Legacy and Candidate VersionedScore, nullable Difference, BaselineStatus, OosStatus and PromotionStatus. Each score carries ModelVersion, FormulaDigest, ParameterSetId, TotalScore, AvailableWeight, ScoreStatus, ComponentScores, FeatureTrace, DatasetSnapshotId, DataCutoff, BlockingReasons and ResearchStatus.

Numbers are decimal without display rounding; unavailable totals/components are null. Candidate values require all six components for TotalScore. Legacy component rules are retained as their own model and are not relabeled as Candidate's six domains. CandidateFixture directly calls the existing calculator, preserves 19 feature traces, and always labels its output SYNTHETIC_FIXTURE_NOT_REAL_VALIDATION. There is no HTTP route accepting fixture/eligibility/approval flags.

SnapshotJson gains optional Scoring on newly created recommendations. Existing JSON deserializes with Scoring=null. No read path writes or backfills a score. New API DTO Scoring is optional, preserving published v1 compatibility. Strategy signals and their database contracts remain unchanged. DataCutoff=null means unknown, not assumed valid. Candidate snapshot identity/parameters remain null when there is no approved dataset.
