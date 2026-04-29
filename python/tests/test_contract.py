"""Contract tests — load cases from contract-tests/test_cases.json and assert.

This runner is one of three (Python / C# / TypeScript) that MUST produce the
same results on the same fixture. Do not add cases here — add them in the
shared JSON fixture so the other two runners see them.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

FIXTURE = Path(__file__).resolve().parents[2] / "contract-tests" / "test_cases.json"


def _load_cases() -> list[dict[str, Any]]:
    data = json.loads(FIXTURE.read_text(encoding="utf-8"))
    assert data["schema_version"] == 1, (
        f"contract fixture schema_version changed — update all three language runners. "
        f"Got {data['schema_version']}"
    )
    cases: list[dict[str, Any]] = data.get("cases", [])
    return [c for c in cases if "python" not in c.get("skip_languages", [])]


@pytest.mark.contract
def test_fixture_is_well_formed() -> None:
    data = json.loads(FIXTURE.read_text(encoding="utf-8"))
    assert isinstance(data.get("cases"), list)
    assert data.get("contract_revision") is not None


@pytest.mark.contract
@pytest.mark.parametrize("case", _load_cases(), ids=lambda c: c["name"])
def test_case(case: dict[str, Any]) -> None:
    """Per-case driver. Implementations fill this in once the port has a method."""
    pytest.skip(
        "contract runner scaffold — replace with real port invocation "
        "once CONTRACT.md methods are implemented"
    )
