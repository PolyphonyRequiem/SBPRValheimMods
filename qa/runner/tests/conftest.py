"""Make the engine-free `fsm` package importable from the test suite without
installing anything. The package lives at qa/runner/fsm; tests live at
qa/runner/tests. Add qa/runner to sys.path.
"""
import sys
from pathlib import Path

RUNNER_DIR = Path(__file__).resolve().parent.parent  # qa/runner
if str(RUNNER_DIR) not in sys.path:
    sys.path.insert(0, str(RUNNER_DIR))

import pytest  # noqa: E402

from helpers import golden_context, golden_transport  # noqa: E402


@pytest.fixture
def golden():
    """A fresh golden (transport, context) that produces a clean PASS. Each test
    perturbs one thing off this baseline to prove the verdict flips to FAIL."""
    return golden_transport(), golden_context()
