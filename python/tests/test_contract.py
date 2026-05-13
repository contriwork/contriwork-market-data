"""Cross-language contract conformance runner.

Loads cases from ``contract-tests/test_cases.json`` and asserts that the
Python implementation produces the result the fixture describes. C# and
TypeScript runners load the same file and MUST produce identical results.

Fixture shape: see ``contract-tests/test_cases.json`` example_case_shape.
"""

from __future__ import annotations

import json
from decimal import Decimal
from pathlib import Path
from typing import Any

import pytest

from contriwork_market_data import (
    AdapterRegistry,
    CacheConfig,
    ClientConfig,
    Interval,
    MarketDataClient,
    RateLimitConfig,
    StreamingConfig,
)
from contriwork_market_data._clock import ManualClock
from contriwork_market_data.adapters import InMemoryAdapter, InMemoryFailMode
from contriwork_market_data.types import Capability

FIXTURE = (
    Path(__file__).resolve().parents[2] / "contract-tests" / "test_cases.json"
)


def _load_cases() -> list[dict[str, Any]]:
    data = json.loads(FIXTURE.read_text(encoding="utf-8"))
    assert data["schema_version"] == 1, (
        "contract fixture schema_version changed — update all three language "
        f"runners. Got {data['schema_version']}"
    )
    cases: list[dict[str, Any]] = data.get("cases", [])
    return [c for c in cases if "python" not in c.get("skip_languages", [])]


@pytest.mark.contract
def test_fixture_is_well_formed() -> None:
    data = json.loads(FIXTURE.read_text(encoding="utf-8"))
    assert isinstance(data.get("cases"), list) and len(data["cases"]) > 0
    assert data.get("contract_revision") == "v1"


def _build_adapter(spec: dict[str, Any], clock: ManualClock) -> InMemoryAdapter:
    cap_kwargs = {
        "supported_markets": ("*",),
        "supported_intervals": tuple(Interval),
        "supported_quote_currencies": "ANY",
        "supports_order_book": spec.get("supports_order_book", True),
        "supports_native_streaming": spec.get("supports_native_streaming", False),
        "rate_limit_per_minute": spec.get("rate_limit_per_minute", 9999),
        "requires_auth": spec.get("requires_auth", False),
    }
    if "supported_intervals" in spec:
        cap_kwargs["supported_intervals"] = tuple(
            Interval(v) for v in spec["supported_intervals"]
        )
    if "supported_quote_currencies" in spec:
        sqc = spec["supported_quote_currencies"]
        cap_kwargs["supported_quote_currencies"] = (
            "ANY" if sqc == "ANY" else tuple(sqc)
        )
    capability = Capability(**cap_kwargs)  # type: ignore[arg-type]
    fail_modes = [
        InMemoryFailMode(
            symbol=fm["symbol"],
            code=fm["code"],
            fail_first_n=fm.get("fail_first_n"),
        )
        for fm in spec.get("fail_modes", [])
    ]
    return InMemoryAdapter(
        adapter_id=spec["id"],
        data=spec.get("data", {}),
        capability=capability,
        fail_modes=fail_modes,
        api_key=spec.get("api_key"),
        clock=clock,
    )


def _build_client(
    setup: dict[str, Any],
) -> tuple[MarketDataClient, dict[str, InMemoryAdapter], ManualClock]:
    clock_spec = setup.get("clock") or {}
    clock = ManualClock(epoch_seconds=float(clock_spec.get("epoch_seconds", 0.0)))
    adapters: dict[str, InMemoryAdapter] = {
        spec["id"]: _build_adapter(spec, clock) for spec in setup.get("adapters", [])
    }
    chains = {
        market: [adapters[aid] for aid in adapter_ids]
        for market, adapter_ids in setup.get("client_chain", {}).items()
    }
    cache_cfg = CacheConfig(**(setup.get("cache") or {}))
    rl_cfg = RateLimitConfig(**(setup.get("rate_limit") or {}))
    stream_cfg = StreamingConfig(**(setup.get("streaming") or {}))
    config = ClientConfig(cache=cache_cfg, rate_limit=rl_cfg, streaming=stream_cfg)
    client = MarketDataClient(
        registry=AdapterRegistry(chains), config=config, clock=clock
    )
    return client, adapters, clock


async def _invoke(
    client: MarketDataClient,
    method: str,
    args: dict[str, Any],
) -> Any:
    if method == "get_spot":
        return await client.get_spot(
            args["symbol"], args["market"], args.get("quote_currency", "USD")
        )
    if method == "get_ohlcv":
        since = args.get("since")
        from datetime import datetime as _dt
        return await client.get_ohlcv(
            args["symbol"],
            args["market"],
            Interval(args["interval"]),
            _dt.fromisoformat(since.replace("Z", "+00:00")) if isinstance(since, str) else since,
            args.get("limit", 100),
        )
    if method == "get_order_book":
        return await client.get_order_book(
            args["symbol"], args["market"], args.get("depth", 20)
        )
    raise ValueError(f"unsupported method in fixture: {method!r}")


async def _consume_stream(
    client: MarketDataClient,
    args: dict[str, Any],
    yield_count: int,
) -> list[Any]:
    collected: list[Any] = []
    gen = client.subscribe_ticker(
        args["symbol"],
        args["market"],
        polling_fallback=args.get("polling_fallback", True),
        polling_interval_s=args.get("polling_interval_s", 4.0),
    )
    async for ticker in gen:
        collected.append(ticker)
        if len(collected) >= yield_count:
            break
    await gen.aclose()
    return collected


def _assert_expected_output(
    case: dict[str, Any],
    result: Any,
    adapters: dict[str, InMemoryAdapter],
) -> None:
    expected = case.get("expected_output")
    if expected is None:
        return
    type_label = expected.get("type", "")
    fields = expected.get("fields", {}) or {}

    def _dec_eq(actual: Decimal, expected_value: Any) -> bool:
        return Decimal(str(expected_value)) == actual

    if type_label == "SpotPrice":
        for field, value in fields.items():
            actual = getattr(result, field)
            if isinstance(actual, Decimal):
                assert _dec_eq(actual, value), (
                    f"SpotPrice.{field}: expected {value!r} got {actual!r}"
                )
            else:
                assert actual == value, (
                    f"SpotPrice.{field}: expected {value!r} got {actual!r}"
                )
    elif type_label.startswith("list[Candle]"):
        assert isinstance(result, list)
        if "length" in expected:
            assert len(result) == expected["length"]
        if expected.get("ordered_ascending_by") == "timestamp":
            assert all(
                result[i].timestamp <= result[i + 1].timestamp
                for i in range(len(result) - 1)
            )
        if "all_timestamps_at_or_after" in expected:
            from datetime import datetime as _dt
            min_ts = _dt.fromisoformat(
                expected["all_timestamps_at_or_after"].replace("Z", "+00:00")
            )
            assert all(c.timestamp >= min_ts for c in result)
    elif type_label == "OrderBook":
        for field, value in fields.items():
            assert getattr(result, field) == value
        if "bids_length" in expected:
            assert len(result.bids) == expected["bids_length"]
        if "asks_length" in expected:
            assert len(result.asks) == expected["asks_length"]
        if expected.get("bids_sorted_descending_by_price"):
            prices = [b.price for b in result.bids]
            assert prices == sorted(prices, reverse=True)
        if expected.get("asks_sorted_ascending_by_price"):
            prices = [a.price for a in result.asks]
            assert prices == sorted(prices)
    elif type_label == "list[Ticker]":
        assert isinstance(result, list)
        if "length" in expected:
            assert len(result) == expected["length"]
        for key in ("all_have_field", "all_have_field_2"):
            if key in expected:
                field, value = expected[key].split(":", 1)
                for t in result:
                    actual = getattr(t, field)
                    if isinstance(actual, Decimal):
                        assert _dec_eq(actual, value)
                    else:
                        assert str(actual) == value

    if "adapter_call_count" in expected:
        for adapter_id, expected_count in expected["adapter_call_count"].items():
            adapter = adapters[adapter_id]
            # The orchestrator only invokes one op per adapter call so the sum
            # across ops covers the total fixture-relevant call count.
            actual = sum(adapter.call_counts.values())
            assert actual == expected_count, (
                f"adapter {adapter_id} call count: expected {expected_count}, "
                f"got {actual}"
            )


@pytest.mark.contract
@pytest.mark.asyncio
@pytest.mark.parametrize("case", _load_cases(), ids=lambda c: c["name"])
async def test_case(case: dict[str, Any]) -> None:
    client, adapters, clock = _build_client(case["setup"])
    operation = case["operation"]
    method = operation["method"]
    args = operation["args"]
    expected_error = case.get("expected_error")

    if method == "subscribe_ticker":
        yield_count = operation.get("yield_count", 0)
        if expected_error is not None:
            from contriwork_market_data.errors import error_for_code
            with pytest.raises(error_for_code(expected_error["code"])):
                await _consume_stream(client, args, yield_count or 1)
            return
        result = await _consume_stream(client, args, yield_count)
        _assert_expected_output(case, result, adapters)
        return

    repeat = operation.get("repeat", 1)
    advance = float(operation.get("advance_clock_between_calls_s", 0))

    last: Any = None
    for i in range(repeat):
        if expected_error is not None:
            from contriwork_market_data.errors import error_for_code
            with pytest.raises(error_for_code(expected_error["code"])) as info:
                await _invoke(client, method, args)
            msg_contains = expected_error.get("message_contains")
            if msg_contains:
                assert msg_contains in str(info.value), (
                    f"expected message to contain {msg_contains!r}, "
                    f"got {info.value!r}"
                )
            return
        last = await _invoke(client, method, args)
        if i < repeat - 1 and advance > 0:
            clock.advance(advance)
    _assert_expected_output(case, last, adapters)
