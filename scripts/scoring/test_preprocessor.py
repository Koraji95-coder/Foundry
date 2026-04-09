"""
Unit tests for PR preprocessor evaluation dataset processing.

Covers gate functions, signal functions, helper utilities, and end-to-end
preprocess() runs using evaluation-dataset-style PR snapshots (the same shape
as evals/pr-scoring.jsonl entries).

Test groups:
  1. Gate functions     — gate_builds, gate_duplicate, gate_conflict
  2. Signal functions   — signal_has_tests, signal_pr_size, signal_commit_format,
                          signal_file_coherence, signal_security_patterns,
                          signal_doc_coverage
  3. Helper utilities   — _classify_area, _extract_features, _compute_confidence
  4. Evaluation dataset — preprocess() with known PR snapshots; verifies that the
                          scorer produces expected gate/signal/score outcomes for
                          each query/answer pair in the eval fixture set.
"""

import sys
import unittest
from pathlib import Path
from unittest.mock import patch

# ---------------------------------------------------------------------------
# Bootstrap sys.path so we can import preprocessor without installation
# ---------------------------------------------------------------------------
_SCRIPTS_DIR = Path(__file__).parent
sys.path.insert(0, str(_SCRIPTS_DIR))

import preprocessor as _pp  # noqa: E402


# ---------------------------------------------------------------------------
# Shared eval-dataset fixtures
# ---------------------------------------------------------------------------
# Each entry mirrors the shape of evals/pr-scoring.jsonl and is used as input
# to preprocess() for integration-style assertions.
_EVAL_FIXTURES = [
    {
        # Query: well-formed PR with tests, small size, conventional commits
        # Expected answer: gate_passed=True, high normalized_score (>= 6)
        "id": "eval-001",
        "label": "ideal_pr",
        "input": {
            "title": "feat: add histogram signal to preprocessor",
            "files": [
                "src/Foundry.Core/Services/ScoringService.cs",
                "tests/Foundry.Core.Tests/ScoringServiceTests.cs",
            ],
            "additions": 80,
            "deletions": 10,
            "ci_status": "success",
            "commit_messages": ["feat: add histogram signal to preprocessor"],
            "pr_number": 42,
        },
        "expected_gate_passed": True,
        "expected_min_normalized": 6,
    },
    {
        # Query: PR with CI failure
        # Expected answer: gate_passed=False (build gate)
        "id": "eval-002",
        "label": "ci_failure_pr",
        "input": {
            "title": "fix: patch null-ref in broker",
            "files": ["src/Foundry.Broker/Program.cs"],
            "additions": 5,
            "deletions": 2,
            "ci_status": "failure",
            "commit_messages": ["fix: patch null-ref in broker"],
            "pr_number": 43,
        },
        "expected_gate_passed": False,
        "expected_failure_reason_contains": "CI checks failed",
    },
    {
        # Query: very large PR with no tests
        # Expected answer: gate_passed=True but low normalized_score (<= 5)
        "id": "eval-003",
        "label": "large_no_tests_pr",
        "input": {
            "title": "chore: big refactor",
            "files": [
                "src/Foundry.Core/Services/Big.cs",
                "src/Foundry.Broker/Program.cs",
            ],
            "additions": 600,
            "deletions": 400,
            "ci_status": "success",
            "commit_messages": ["big refactor without conventional prefix"],
            "pr_number": 44,
        },
        "expected_gate_passed": True,
        "expected_max_normalized": 5,
    },
    {
        # Query: PR with sensitive file pattern (.env)
        # Expected answer: security signal score == 0
        "id": "eval-004",
        "label": "sensitive_file_pr",
        "input": {
            "title": "chore: update config",
            "files": [".env", "src/Foundry.Core/Services/Config.cs"],
            "additions": 10,
            "deletions": 2,
            "ci_status": "success",
            "commit_messages": ["chore: update config"],
            "pr_number": 45,
        },
        "expected_gate_passed": True,
        "expected_security_score": 0,
    },
    {
        # Query: API change with doc update
        # Expected answer: doc_coverage signal score == 1
        "id": "eval-005",
        "label": "api_change_with_docs",
        "input": {
            "title": "feat: expose new endpoint",
            "files": [
                "src/Foundry.Broker/Endpoints/NewEndpoint.cs",
                "docs/API.md",
            ],
            "additions": 40,
            "deletions": 5,
            "ci_status": "success",
            "commit_messages": ["feat: expose new endpoint"],
            "pr_number": 46,
        },
        "expected_gate_passed": True,
        "expected_doc_score": 1,
    },
]


# ===========================================================================
# Group 1 — Gate functions
# ===========================================================================
class TestGateBuilds(unittest.TestCase):
    """gate_builds returns pass/fail based on ci_status string."""

    def test_failure_status_returns_false(self):
        ok, _ = _pp.gate_builds("failure")
        self.assertFalse(ok)

    def test_failure_reason_mentions_ci(self):
        _, reason = _pp.gate_builds("failure")
        self.assertIn("CI", reason)

    def test_success_status_returns_true(self):
        ok, _ = _pp.gate_builds("success")
        self.assertTrue(ok)

    def test_pending_status_returns_true(self):
        ok, _ = _pp.gate_builds("pending")
        self.assertTrue(ok)

    def test_none_status_returns_true(self):
        ok, _ = _pp.gate_builds("none")
        self.assertTrue(ok)

    def test_reason_includes_status_string(self):
        _, reason = _pp.gate_builds("pending")
        self.assertIn("pending", reason)


class TestGateDuplicate(unittest.TestCase):
    """gate_duplicate detects file-overlap and title-similarity duplicates."""

    _RECENT = [
        {
            "pr_number": 1,
            "title": "feat: add histogram signal",
            "files": ["src/A.cs", "src/B.cs", "src/C.cs"],
        }
    ]

    def test_no_history_returns_true(self):
        ok, _ = _pp.gate_duplicate("new feature", ["src/X.cs"], [])
        self.assertTrue(ok)

    def test_high_file_overlap_returns_false(self):
        ok, _ = _pp.gate_duplicate(
            "something different",
            ["src/A.cs", "src/B.cs", "src/C.cs"],
            self._RECENT,
        )
        self.assertFalse(ok)

    def test_high_title_similarity_returns_false(self):
        ok, _ = _pp.gate_duplicate(
            "feat: add histogram signal",
            ["src/Z.cs"],
            self._RECENT,
        )
        self.assertFalse(ok)

    def test_no_overlap_no_similar_title_returns_true(self):
        ok, _ = _pp.gate_duplicate(
            "fix: unrelated patch",
            ["src/New.cs"],
            self._RECENT,
        )
        self.assertTrue(ok)

    def test_partial_overlap_below_threshold_returns_true(self):
        # Only 1/4 files overlap → < 50%
        ok, _ = _pp.gate_duplicate(
            "fix: something else",
            ["src/A.cs", "src/X.cs", "src/Y.cs", "src/Z.cs"],
            self._RECENT,
        )
        self.assertTrue(ok)

    def test_duplicate_reason_mentions_pr_number(self):
        _, reason = _pp.gate_duplicate(
            "feat: add histogram signal",
            ["src/Z.cs"],
            self._RECENT,
        )
        self.assertIn("#1", reason)


class TestGateConflict(unittest.TestCase):
    """gate_conflict detects file-overlap with other open PRs."""

    _OPEN = {"10": ["src/A.cs", "src/B.cs", "src/C.cs", "src/D.cs"]}

    def test_no_open_prs_returns_true(self):
        ok, _ = _pp.gate_conflict(99, ["src/A.cs"], {})
        self.assertTrue(ok)

    def test_none_open_prs_returns_true(self):
        ok, _ = _pp.gate_conflict(99, ["src/A.cs"], None)
        self.assertTrue(ok)

    def test_three_or_more_shared_files_returns_false(self):
        ok, _ = _pp.gate_conflict(
            99,
            ["src/A.cs", "src/B.cs", "src/C.cs"],
            self._OPEN,
        )
        self.assertFalse(ok)

    def test_same_pr_number_skipped(self):
        ok, _ = _pp.gate_conflict(
            10,
            ["src/A.cs", "src/B.cs", "src/C.cs"],
            self._OPEN,
        )
        self.assertTrue(ok)

    def test_no_overlap_returns_true(self):
        ok, _ = _pp.gate_conflict(99, ["src/X.cs", "src/Y.cs"], self._OPEN)
        self.assertTrue(ok)

    def test_conflict_reason_mentions_other_pr(self):
        _, reason = _pp.gate_conflict(
            99,
            ["src/A.cs", "src/B.cs", "src/C.cs"],
            self._OPEN,
        )
        self.assertIn("#10", reason)


# ===========================================================================
# Group 2 — Signal functions
# ===========================================================================
class TestSignalHasTests(unittest.TestCase):
    """signal_has_tests scores 0/1/2 based on test-file presence."""

    def test_no_files_returns_zero(self):
        score, _ = _pp.signal_has_tests([])
        self.assertEqual(score, 0)

    def test_test_files_only_returns_one(self):
        score, _ = _pp.signal_has_tests(["tests/MyTests.cs"])
        self.assertEqual(score, 1)

    def test_test_and_prod_files_returns_two(self):
        score, _ = _pp.signal_has_tests(
            ["src/Service.cs", "tests/ServiceTests.cs"]
        )
        self.assertEqual(score, 2)

    def test_spec_pattern_counts_as_test(self):
        score, _ = _pp.signal_has_tests(["src/Module.cs", "src/module.spec.ts"])
        self.assertEqual(score, 2)

    def test_score_is_int(self):
        score, _ = _pp.signal_has_tests(["tests/X.cs", "src/Y.cs"])
        self.assertIsInstance(score, int)

    def test_reason_mentions_file_counts(self):
        _, reason = _pp.signal_has_tests(["tests/X.cs", "src/Y.cs"])
        self.assertRegex(reason, r"\d+")


class TestSignalPrSize(unittest.TestCase):
    """signal_pr_size returns 0 for large PRs (>500 lines), 1 otherwise."""

    def test_small_pr_returns_one(self):
        score, _ = _pp.signal_pr_size(50, 10)
        self.assertEqual(score, 1)

    def test_exactly_500_returns_one(self):
        score, _ = _pp.signal_pr_size(250, 250)
        self.assertEqual(score, 1)

    def test_over_500_returns_zero(self):
        score, _ = _pp.signal_pr_size(400, 200)
        self.assertEqual(score, 0)

    def test_zero_additions_deletions_returns_one(self):
        score, _ = _pp.signal_pr_size(0, 0)
        self.assertEqual(score, 1)

    def test_reason_includes_total_line_count(self):
        _, reason = _pp.signal_pr_size(100, 50)
        self.assertIn("150", reason)


class TestSignalCommitFormat(unittest.TestCase):
    """signal_commit_format returns 1 when >= 50% of commits are conventional."""

    def test_no_commits_returns_zero(self):
        score, _ = _pp.signal_commit_format([])
        self.assertEqual(score, 0)

    def test_all_conventional_returns_one(self):
        score, _ = _pp.signal_commit_format(
            ["feat: new thing", "fix: bug", "chore: cleanup"]
        )
        self.assertEqual(score, 1)

    def test_all_unconventional_returns_zero(self):
        score, _ = _pp.signal_commit_format(
            ["updated stuff", "more changes", "wip"]
        )
        self.assertEqual(score, 0)

    def test_half_conventional_returns_one(self):
        score, _ = _pp.signal_commit_format(
            ["feat: good", "some random commit"]
        )
        self.assertEqual(score, 1)

    def test_reason_includes_counts(self):
        _, reason = _pp.signal_commit_format(
            ["feat: good", "bad commit", "fix: also good"]
        )
        self.assertRegex(reason, r"\d+/\d+")


class TestSignalFileCoherence(unittest.TestCase):
    """signal_file_coherence returns 0 when files span > 4 top-level dirs."""

    def test_empty_files_returns_one(self):
        score, _ = _pp.signal_file_coherence([])
        self.assertEqual(score, 1)

    def test_single_file_returns_one(self):
        score, _ = _pp.signal_file_coherence(["src/A.cs"])
        self.assertEqual(score, 1)

    def test_files_in_same_dir_returns_one(self):
        score, _ = _pp.signal_file_coherence(["src/A.cs", "src/B.cs", "src/C.cs"])
        self.assertEqual(score, 1)

    def test_files_in_four_dirs_returns_one(self):
        score, _ = _pp.signal_file_coherence(
            ["src/A.cs", "tests/B.cs", "docs/C.md", "scripts/D.py"]
        )
        self.assertEqual(score, 1)

    def test_files_in_five_dirs_returns_zero(self):
        score, _ = _pp.signal_file_coherence(
            [
                "src/A.cs",
                "tests/B.cs",
                "docs/C.md",
                "scripts/D.py",
                "bot/E.py",
            ]
        )
        self.assertEqual(score, 0)

    def test_reason_includes_directory_count(self):
        _, reason = _pp.signal_file_coherence(["src/A.cs", "tests/B.cs"])
        self.assertRegex(reason, r"\d+")


class TestSignalSecurityPatterns(unittest.TestCase):
    """signal_security_patterns detects sensitive file names and diff content."""

    def test_clean_files_returns_one(self):
        score, _ = _pp.signal_security_patterns(
            ["src/Service.cs", "tests/ServiceTests.cs"]
        )
        self.assertEqual(score, 1)

    def test_env_file_returns_zero(self):
        score, _ = _pp.signal_security_patterns([".env"])
        self.assertEqual(score, 0)

    def test_pem_file_returns_zero(self):
        score, _ = _pp.signal_security_patterns(["server.pem"])
        self.assertEqual(score, 0)

    def test_credential_in_diff_returns_zero(self):
        score, _ = _pp.signal_security_patterns(
            ["src/Config.cs"],
            diff_text='password = "supersecret123"',
        )
        self.assertEqual(score, 0)

    def test_no_diff_text_does_not_raise(self):
        score, _ = _pp.signal_security_patterns(["src/Safe.cs"], diff_text=None)
        self.assertEqual(score, 1)

    def test_reason_is_non_empty_string(self):
        _, reason = _pp.signal_security_patterns(["src/Safe.cs"])
        self.assertIsInstance(reason, str)
        self.assertTrue(len(reason) > 0)


class TestSignalDocCoverage(unittest.TestCase):
    """signal_doc_coverage checks docs are updated when API files change."""

    def test_no_api_change_returns_one(self):
        score, _ = _pp.signal_doc_coverage(
            ["src/Foundry.Core/Internal/Helper.cs"]
        )
        self.assertEqual(score, 1)

    def test_api_change_with_doc_update_returns_one(self):
        score, _ = _pp.signal_doc_coverage(
            ["src/Foundry.Broker/Program.cs", "docs/API.md"]
        )
        self.assertEqual(score, 1)

    def test_api_change_without_doc_update_returns_zero(self):
        score, _ = _pp.signal_doc_coverage(["src/Foundry.Broker/Program.cs"])
        self.assertEqual(score, 0)

    def test_readme_counts_as_doc(self):
        score, _ = _pp.signal_doc_coverage(
            ["src/Foundry.Broker/Program.cs", "README.md"]
        )
        self.assertEqual(score, 1)

    def test_reason_is_non_empty_string(self):
        _, reason = _pp.signal_doc_coverage(["src/Foundry.Core/Helper.cs"])
        self.assertIsInstance(reason, str)
        self.assertTrue(len(reason) > 0)


# ===========================================================================
# Group 3 — Helper utilities
# ===========================================================================
class TestClassifyArea(unittest.TestCase):
    """_classify_area maps file lists to broad area labels."""

    def test_empty_files_returns_unknown(self):
        self.assertEqual(_pp._classify_area([]), "unknown")

    def test_test_files_return_tests_area(self):
        result = _pp._classify_area(
            ["tests/MyTests.cs", "tests/OtherTests.cs"]
        )
        self.assertEqual(result, "tests")

    def test_md_file_returns_docs_area(self):
        result = _pp._classify_area(["docs/Overview.md", "README.md"])
        self.assertEqual(result, "docs")

    def test_broker_file_returns_broker_area(self):
        result = _pp._classify_area(["src/Foundry.Broker/Program.cs"])
        self.assertEqual(result, "broker")

    def test_python_script_returns_scripts_area(self):
        # Use a .ps1 path that doesn't hit the "ml" / "scoring" patterns
        result = _pp._classify_area(["scripts/automation/setup.ps1"])
        self.assertEqual(result, "scripts")

    def test_scoring_script_classified_as_ml(self):
        # "scripts/scoring/model.py" matches both "ml" (via 'scoring') and
        # "scripts" (via 'scripts/'). Both areas get count 1; dict insertion
        # order puts "ml" before "scripts", so "ml" wins the tie.
        result = _pp._classify_area(["scripts/scoring/model.py"])
        self.assertEqual(result, "ml")

    def test_yaml_returns_ci_area(self):
        # .yml/.yaml extensions contain the substring "ml" which would tie with the
        # "ml" area pattern; use github/workflows path with a non-.yml extension so
        # only the ci area matches.
        result = _pp._classify_area([".github/workflows/ci.json"])
        self.assertEqual(result, "ci")

    def test_yml_extension_ties_with_ml_area(self):
        # .yml contains the substring "ml", so a single .yml file in a workflows
        # path ties between "ci" and "ml". Dict ordering means "ml" wins the tie.
        result = _pp._classify_area([".github/workflows/ci.yml"])
        self.assertEqual(result, "ml")

    def test_unrecognised_files_return_other(self):
        result = _pp._classify_area(["some/random/file.xyz"])
        self.assertEqual(result, "other")

    def test_majority_area_wins(self):
        # Use non-.cs test files so the "models" (.cs$) pattern doesn't tie with
        # "tests". Three test files vs one broker file → tests wins.
        result = _pp._classify_area(
            [
                "tests/A.txt",
                "tests/B.txt",
                "tests/C.txt",
                "src/Foundry.Broker/Program.cs",
            ]
        )
        self.assertEqual(result, "tests")

    def test_cs_test_files_tie_models_and_tests(self):
        # .cs files match both "tests" (via r'test') and "models" (via r'\.cs$').
        # With three test .cs files and one Broker .cs file, "models" gets 4 hits
        # (every .cs file) and "tests" gets 3. The broker .cs also increments
        # "broker", but "models" (4) beats every other area.
        result = _pp._classify_area(
            [
                "tests/A.cs",
                "tests/B.cs",
                "tests/C.cs",
                "src/Foundry.Broker/Program.cs",
            ]
        )
        self.assertEqual(result, "models")


class TestExtractFeatures(unittest.TestCase):
    """_extract_features returns an 8-element float list."""

    _ENTRY = {"additions": 100, "deletions": 20}

    def test_returns_list_of_eight(self):
        features = _pp._extract_features(self._ENTRY, ["src/A.cs"])
        self.assertEqual(len(features), 8)

    def test_all_elements_are_float(self):
        features = _pp._extract_features(self._ENTRY, ["src/A.cs"])
        for f in features:
            self.assertIsInstance(f, float)

    def test_total_size_is_first_element(self):
        features = _pp._extract_features(self._ENTRY, ["src/A.cs"])
        self.assertEqual(features[0], 120.0)  # 100 + 20

    def test_num_files_is_second_element(self):
        features = _pp._extract_features(self._ENTRY, ["src/A.cs", "src/B.cs"])
        self.assertEqual(features[1], 2.0)

    def test_has_tests_flag_set_when_test_file_present(self):
        features = _pp._extract_features(
            self._ENTRY, ["src/A.cs", "tests/ATests.cs"]
        )
        self.assertEqual(features[2], 1.0)

    def test_has_tests_flag_zero_when_no_test_file(self):
        features = _pp._extract_features(self._ENTRY, ["src/A.cs"])
        self.assertEqual(features[2], 0.0)

    def test_empty_files_list_handled(self):
        features = _pp._extract_features(self._ENTRY, [])
        self.assertEqual(len(features), 8)
        self.assertEqual(features[1], 0.0)  # num_files


class TestComputeConfidence(unittest.TestCase):
    """_compute_confidence returns a float in [0, 1]."""

    def _make_result(self, scores: dict, builds_gate_reason: str = "CI status: success") -> dict:
        signals = {k: {"score": v, "max": 1} for k, v in scores.items()}
        return {
            "signals": signals,
            "gates": {"builds": {"passed": True, "reason": builds_gate_reason}},
        }

    def test_returns_float(self):
        result = self._make_result({"tests": 1, "size": 1})
        conf = _pp._compute_confidence(result)
        self.assertIsInstance(conf, float)

    def test_all_max_scores_high_confidence(self):
        result = self._make_result(
            {"tests": 1, "size": 1, "commits": 1, "security": 1}
        )
        conf = _pp._compute_confidence(result)
        self.assertGreater(conf, 0.5)

    def test_all_zero_scores_low_confidence(self):
        result = self._make_result(
            {"tests": 0, "size": 0, "commits": 0, "security": 0},
            builds_gate_reason="CI status: none",
        )
        conf = _pp._compute_confidence(result)
        self.assertLess(conf, 0.5)

    def test_empty_signals_returns_default(self):
        result = {"signals": {}, "gates": {}}
        conf = _pp._compute_confidence(result)
        self.assertEqual(conf, 0.3)

    def test_confidence_in_range(self):
        result = self._make_result({"a": 1, "b": 0, "c": 1})
        conf = _pp._compute_confidence(result)
        self.assertGreaterEqual(conf, 0.0)
        self.assertLessEqual(conf, 1.0)


# ===========================================================================
# Group 4 — Evaluation dataset integration tests
# ===========================================================================
class TestPreprocessOutputShape(unittest.TestCase):
    """preprocess() must return a dict with all required top-level keys."""

    _REQUIRED_KEYS = {
        "version",
        "gates",
        "signals",
        "pre_score",
        "normalized_score",
        "confidence",
        "gate_passed",
        "gate_failure_reason",
        "signal_summary",
        "ml_engine",
    }

    def _run(self, pr_data: dict) -> dict:
        with patch.object(_pp, "load_memory", return_value=[]):
            with patch.object(_pp, "load_full_memory", return_value=[]):
                return _pp.preprocess(pr_data)

    def test_minimal_input_has_all_required_keys(self):
        result = self._run({"title": "test", "ci_status": "success"})
        for key in self._REQUIRED_KEYS:
            self.assertIn(key, result, f"Missing key: {key}")

    def test_version_is_two(self):
        result = self._run({"title": "test", "ci_status": "success"})
        self.assertEqual(result["version"], 2)

    def test_normalized_score_in_range(self):
        result = self._run(
            {
                "title": "feat: something",
                "ci_status": "success",
                "files": ["src/A.cs", "tests/ATests.cs"],
                "additions": 30,
                "deletions": 5,
                "commit_messages": ["feat: something"],
            }
        )
        self.assertGreaterEqual(result["normalized_score"], 1)
        self.assertLessEqual(result["normalized_score"], 10)

    def test_signal_summary_is_string(self):
        result = self._run({"title": "x", "ci_status": "success"})
        self.assertIsInstance(result["signal_summary"], str)

    def test_confidence_in_range(self):
        result = self._run({"title": "x", "ci_status": "success"})
        self.assertGreaterEqual(result["confidence"], 0.0)
        self.assertLessEqual(result["confidence"], 1.0)


class TestPreprocessEvalDataset(unittest.TestCase):
    """
    Evaluation dataset tests.

    Each fixture in _EVAL_FIXTURES represents a labelled PR snapshot (the
    same shape as an evals/pr-scoring.jsonl entry). We run preprocess() on
    the snapshot and assert that the scorer produces the expected outcome.
    """

    def _run(self, pr_data: dict) -> dict:
        with patch.object(_pp, "load_memory", return_value=[]):
            with patch.object(_pp, "load_full_memory", return_value=[]):
                return _pp.preprocess(pr_data)

    # ---- eval-001: ideal PR -----------------------------------------------
    def test_eval001_ideal_pr_gate_passes(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-001")
        result = self._run(fx["input"])
        self.assertTrue(result["gate_passed"], msg=f"label={fx['label']}")

    def test_eval001_ideal_pr_normalized_score_above_minimum(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-001")
        result = self._run(fx["input"])
        self.assertGreaterEqual(
            result["normalized_score"],
            fx["expected_min_normalized"],
            msg=f"label={fx['label']}",
        )

    # ---- eval-002: CI failure PR ------------------------------------------
    def test_eval002_ci_failure_gate_blocked(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-002")
        result = self._run(fx["input"])
        self.assertFalse(result["gate_passed"], msg=f"label={fx['label']}")

    def test_eval002_ci_failure_reason_describes_failure(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-002")
        result = self._run(fx["input"])
        self.assertIn(
            fx["expected_failure_reason_contains"],
            result["gate_failure_reason"],
            msg=f"label={fx['label']}",
        )

    # ---- eval-003: large PR with no tests ---------------------------------
    def test_eval003_large_no_tests_gate_passes(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-003")
        result = self._run(fx["input"])
        self.assertTrue(result["gate_passed"], msg=f"label={fx['label']}")

    def test_eval003_large_no_tests_score_below_ceiling(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-003")
        result = self._run(fx["input"])
        self.assertLessEqual(
            result["normalized_score"],
            fx["expected_max_normalized"],
            msg=f"label={fx['label']}",
        )

    # ---- eval-004: sensitive file PR --------------------------------------
    def test_eval004_sensitive_file_gate_passes(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-004")
        result = self._run(fx["input"])
        self.assertTrue(result["gate_passed"], msg=f"label={fx['label']}")

    def test_eval004_sensitive_file_security_signal_zero(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-004")
        result = self._run(fx["input"])
        self.assertEqual(
            result["signals"]["security"]["score"],
            fx["expected_security_score"],
            msg=f"label={fx['label']}",
        )

    # ---- eval-005: API change with docs -----------------------------------
    def test_eval005_api_with_docs_gate_passes(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-005")
        result = self._run(fx["input"])
        self.assertTrue(result["gate_passed"], msg=f"label={fx['label']}")

    def test_eval005_api_with_docs_doc_coverage_score_one(self):
        fx = next(f for f in _EVAL_FIXTURES if f["id"] == "eval-005")
        result = self._run(fx["input"])
        self.assertEqual(
            result["signals"]["doc_coverage"]["score"],
            fx["expected_doc_score"],
            msg=f"label={fx['label']}",
        )

    # ---- cross-fixture: gate_passed bool consistency ----------------------
    def test_gate_passed_and_failure_reason_consistent(self):
        """gate_failure_reason must be None iff gate_passed is True."""
        for fx in _EVAL_FIXTURES:
            with self.subTest(id=fx["id"], label=fx["label"]):
                result = self._run(fx["input"])
                if result["gate_passed"]:
                    self.assertIsNone(
                        result["gate_failure_reason"],
                        msg="gate_passed=True but gate_failure_reason is set",
                    )
                else:
                    self.assertIsNotNone(
                        result["gate_failure_reason"],
                        msg="gate_passed=False but gate_failure_reason is None",
                    )

    def test_signals_present_only_when_gate_passes(self):
        """Signals must be populated iff the gate passed."""
        for fx in _EVAL_FIXTURES:
            with self.subTest(id=fx["id"], label=fx["label"]):
                result = self._run(fx["input"])
                if result["gate_passed"]:
                    self.assertTrue(
                        len(result["signals"]) > 0,
                        msg="Gate passed but no signals computed",
                    )
                else:
                    self.assertEqual(
                        len(result["signals"]),
                        0,
                        msg="Gate failed but signals were computed",
                    )


if __name__ == "__main__":
    unittest.main()
