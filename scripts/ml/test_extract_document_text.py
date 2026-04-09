"""
Tests for error message sanitization in extract_document_text.py.

Verifies that main() never exposes raw exception messages in its JSON output:
  - Any exception from extract() → {"ok": false, "error": "<static string>"}
  - The raw exception message must NOT appear anywhere in the JSON output.
"""

import json
import sys
import unittest
from io import StringIO
from pathlib import Path
from unittest.mock import patch

# ---------------------------------------------------------------------------
# Import the module under test
# ---------------------------------------------------------------------------
_SCRIPTS_DIR = Path(__file__).parent
sys.path.insert(0, str(_SCRIPTS_DIR))

import extract_document_text as _module  # noqa: E402


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------
_STATIC_ERROR = "An unexpected error occurred. See server logs for details."
_FAKE_PATH = "/tmp/fake_document.txt"


# ---------------------------------------------------------------------------
# Helper
# ---------------------------------------------------------------------------
def _run_main_with_argv(argv: list[str]) -> dict:
    """Run main() with the given sys.argv and return parsed stdout JSON."""
    with patch.object(sys, "argv", argv):
        captured = StringIO()
        with patch("sys.stdout", captured):
            _module.main()
    output = captured.getvalue().strip()
    return json.loads(output)


def _run_main_with_extract_raising(exc: Exception) -> dict:
    """Run main() where extract() raises the given exception and return parsed stdout JSON."""
    with patch.object(_module, "extract", side_effect=exc):
        return _run_main_with_argv(["extract_document_text.py", _FAKE_PATH])


# ---------------------------------------------------------------------------
# Group 1: Wrong argument count
# ---------------------------------------------------------------------------
class TestArgumentValidation(unittest.TestCase):
    """main() must return a static error when called with wrong argument count."""

    def test_no_args_returns_ok_false(self):
        result = _run_main_with_argv(["extract_document_text.py"])
        self.assertFalse(result["ok"])

    def test_no_args_returns_static_error(self):
        result = _run_main_with_argv(["extract_document_text.py"])
        self.assertEqual(result["error"], "Expected a single file path argument.")

    def test_too_many_args_returns_ok_false(self):
        result = _run_main_with_argv(["extract_document_text.py", "a.txt", "b.txt"])
        self.assertFalse(result["ok"])

    def test_too_many_args_returns_static_error(self):
        result = _run_main_with_argv(["extract_document_text.py", "a.txt", "b.txt"])
        self.assertEqual(result["error"], "Expected a single file path argument.")


# ---------------------------------------------------------------------------
# Group 2: Exception sanitization — one test per exception type
# ---------------------------------------------------------------------------
class TestExceptionSanitization(unittest.TestCase):
    """extract() raising any exception must produce a sanitized static error string."""

    def _assert_sanitized_error(self, result: dict) -> None:
        self.assertFalse(result["ok"])
        self.assertEqual(result["error"], _STATIC_ERROR)

    def test_value_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(ValueError("Unsupported extension: .xyz"))
        )

    def test_runtime_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(RuntimeError("internal failure"))
        )

    def test_os_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(OSError("permission denied: /etc/shadow"))
        )

    def test_file_not_found_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(FileNotFoundError("/secret/path/file.txt"))
        )

    def test_key_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(KeyError("sensitive_key"))
        )

    def test_type_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(TypeError("expected str, got int"))
        )

    def test_attribute_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(AttributeError("has no attribute 'text'"))
        )

    def test_index_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(IndexError("list index out of range"))
        )

    def test_zero_division_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(ZeroDivisionError("division by zero"))
        )

    def test_not_implemented_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(NotImplementedError("not yet implemented"))
        )

    def test_memory_error_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(MemoryError("out of memory"))
        )

    def test_exception_returns_static_detail(self):
        self._assert_sanitized_error(
            _run_main_with_extract_raising(Exception("generic failure"))
        )


# ---------------------------------------------------------------------------
# Group 3: No raw message leakage
# ---------------------------------------------------------------------------
class TestNoRawMessageLeakage(unittest.TestCase):
    """The raw exception message must NEVER appear anywhere in the JSON response."""

    def test_sensitive_value_error_not_leaked(self):
        sentinel = "SENSITIVE_FILE_PATH_SECRET_12345"
        result = _run_main_with_extract_raising(ValueError(sentinel))
        self.assertNotIn(sentinel, result.get("error", ""))
        self.assertNotIn(sentinel, json.dumps(result))

    def test_sensitive_os_error_not_leaked(self):
        sentinel = "SECRET_CREDENTIALS_IN_PATH_67890"
        result = _run_main_with_extract_raising(OSError(sentinel))
        self.assertNotIn(sentinel, result.get("error", ""))
        self.assertNotIn(sentinel, json.dumps(result))

    def test_sensitive_runtime_error_not_leaked(self):
        sentinel = "INTERNAL_API_KEY_ABCDEF"
        result = _run_main_with_extract_raising(RuntimeError(sentinel))
        self.assertNotIn(sentinel, result.get("error", ""))
        self.assertNotIn(sentinel, json.dumps(result))

    def test_error_detail_is_exactly_static_string(self):
        result = _run_main_with_extract_raising(Exception("dynamic content here"))
        self.assertEqual(result["error"], _STATIC_ERROR)

    def test_response_has_no_traceback_field(self):
        result = _run_main_with_extract_raising(RuntimeError("something"))
        self.assertNotIn("traceback", result)
        self.assertNotIn("exception", result)


if __name__ == "__main__":
    unittest.main()
