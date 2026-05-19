using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests;

/// <summary>
/// Cross-language contract conformance runner — one of three (Python /
/// C# / TypeScript) that load <c>contract-tests/test_cases.json</c> and MUST
/// produce identical results for every case.
/// </summary>
public sealed class ContractTests
{
    private static readonly string FixturePath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "..",
        "contract-tests", "test_cases.json"));

    /// <summary>Case names the runner exercises — used as xUnit theory data.</summary>
    /// <returns>One single-string row per non-skipped fixture case.</returns>
    public static IEnumerable<object[]> CaseNames()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var skipped = c.TryGetProperty("skip_languages", out var skip)
                && skip.EnumerateArray().Any(s => s.GetString() == "csharp");
            if (!skipped)
            {
                yield return [c.GetProperty("name").GetString()!];
            }
        }
    }

    [Fact]
    public void Fixture_Is_WellFormed()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath));
        Assert.Equal(1, doc.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal("v1", doc.RootElement.GetProperty("contract_revision").GetString());
        Assert.NotEmpty(doc.RootElement.GetProperty("cases").EnumerateArray());
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public async Task Case(string name)
    {
        var fixture = FindCase(name);
        var setup = fixture.GetProperty("setup");
        var clock = BuildClock(setup);
        var adapters = BuildAdapters(setup, clock);
        var client = BuildClient(setup, adapters, clock);
        var operation = fixture.GetProperty("operation");
        var method = operation.GetProperty("method").GetString()!;
        var args = operation.GetProperty("args");
        var expectedError = fixture.TryGetProperty("expected_error", out var ee)
            && ee.ValueKind != JsonValueKind.Null
                ? ee
                : (JsonElement?)null;

        if (method == "subscribe_ticker")
        {
            await RunStreaming(client, args, operation, fixture, expectedError);
            return;
        }

        var repeat = operation.TryGetProperty("repeat", out var r) ? r.GetInt32() : 1;
        var advance = operation.TryGetProperty("advance_clock_between_calls_s", out var a)
            ? a.GetDouble()
            : 0.0;

        object? last = null;
        for (var i = 0; i < repeat; i++)
        {
            if (expectedError is { } err)
            {
                var ex = await Assert.ThrowsAnyAsync<MarketDataException>(
                    () => Invoke(client, method, args));
                Assert.Equal(err.GetProperty("code").GetString(), ex.Code);
                if (err.TryGetProperty("message_contains", out var mc)
                    && mc.ValueKind == JsonValueKind.String)
                {
                    Assert.Contains(mc.GetString()!, ex.Message, StringComparison.Ordinal);
                }

                return;
            }

            last = await Invoke(client, method, args);
            if (i < repeat - 1 && advance > 0)
            {
                clock.Advance(advance);
            }
        }

        AssertExpectedOutput(fixture, last!, adapters);
    }

    private static JsonElement FindCase(string name)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FixturePath));
        foreach (var c in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            if (c.GetProperty("name").GetString() == name)
            {
                return c.Clone();
            }
        }

        throw new InvalidOperationException($"fixture case not found: {name}");
    }

    private static ManualClock BuildClock(JsonElement setup)
    {
        if (setup.TryGetProperty("clock", out var clock)
            && clock.TryGetProperty("epoch_seconds", out var epoch))
        {
            return new ManualClock(epoch.GetDouble());
        }

        return new ManualClock();
    }

    private static Dictionary<string, InMemoryAdapter> BuildAdapters(JsonElement setup, ManualClock clock)
    {
        var result = new Dictionary<string, InMemoryAdapter>(StringComparer.Ordinal);
        foreach (var spec in setup.GetProperty("adapters").EnumerateArray())
        {
            var id = spec.GetProperty("id").GetString()!;
            var capability = BuildCapability(spec);
            var data = BuildData(spec);
            var failModes = BuildFailModes(spec);
            var apiKey = spec.TryGetProperty("api_key", out var ak)
                && ak.ValueKind == JsonValueKind.String
                    ? ak.GetString()
                    : null;
            result[id] = new InMemoryAdapter(id, data, capability, failModes, apiKey, clock: clock);
        }

        return result;
    }

    private static Capability BuildCapability(JsonElement spec)
    {
        var intervals = spec.TryGetProperty("supported_intervals", out var si)
            ? si.EnumerateArray().Select(x => Enum.Parse<Interval>(x.GetString()!)).ToList()
            : Enum.GetValues<Interval>().ToList();

        QuoteCurrencySupport quotes = QuoteCurrencySupport.Any;
        if (spec.TryGetProperty("supported_quote_currencies", out var sqc)
            && sqc.ValueKind == JsonValueKind.Array)
        {
            quotes = QuoteCurrencySupport.Of(
                [.. sqc.EnumerateArray().Select(x => x.GetString()!)]);
        }

        return new Capability
        {
            SupportedMarkets = ["*"],
            SupportedIntervals = intervals,
            SupportedQuoteCurrencies = quotes,
            SupportsOrderBook = !spec.TryGetProperty("supports_order_book", out var ob)
                || ob.GetBoolean(),
            SupportsNativeStreaming = spec.TryGetProperty("supports_native_streaming", out var ns)
                && ns.GetBoolean(),
            RateLimitPerMinute = spec.TryGetProperty("rate_limit_per_minute", out var rpm)
                ? rpm.GetInt32()
                : 9999,
            RequiresAuth = spec.TryGetProperty("requires_auth", out var ra) && ra.GetBoolean(),
        };
    }

    private static Dictionary<string, InMemorySymbolData> BuildData(JsonElement spec)
    {
        var data = new Dictionary<string, InMemorySymbolData>(StringComparer.Ordinal);
        if (!spec.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
        {
            return data;
        }

        foreach (var symbolProp in dataEl.EnumerateObject())
        {
            data[symbolProp.Name] = BuildSymbolData(symbolProp.Value);
        }

        return data;
    }

    private static InMemorySymbolData BuildSymbolData(JsonElement el)
    {
        SpotPrice? spot = null;
        if (el.TryGetProperty("spot", out var spotEl))
        {
            spot = new SpotPrice
            {
                Symbol = "PLACEHOLDER",
                Last = Dec(spotEl.GetProperty("last")),
                QuoteCurrency = spotEl.TryGetProperty("quote_currency", out var qc)
                    ? qc.GetString()!
                    : "USD",
                Timestamp = DateTimeOffset.UnixEpoch,
                SourceAdapter = "PLACEHOLDER",
                Volume24h = OptDec(spotEl, "volume_24h"),
            };
        }

        var ohlcv = new Dictionary<Interval, IReadOnlyList<Candle>>();
        if (el.TryGetProperty("ohlcv", out var ohlcvEl))
        {
            foreach (var intervalProp in ohlcvEl.EnumerateObject())
            {
                var interval = Enum.Parse<Interval>(intervalProp.Name);
                var candles = intervalProp.Value.EnumerateArray()
                    .Select(c => new Candle
                    {
                        Timestamp = ParseTimestamp(c.GetProperty("timestamp").GetString()!),
                        Open = Dec(c.GetProperty("open")),
                        High = Dec(c.GetProperty("high")),
                        Low = Dec(c.GetProperty("low")),
                        Close = Dec(c.GetProperty("close")),
                        Volume = Dec(c.GetProperty("volume")),
                    })
                    .ToList();
                ohlcv[interval] = candles;
            }
        }

        OrderBook? book = null;
        if (el.TryGetProperty("order_book", out var bookEl))
        {
            book = new OrderBook
            {
                Symbol = "PLACEHOLDER",
                SourceAdapter = "PLACEHOLDER",
                Timestamp = DateTimeOffset.UnixEpoch,
                Bids = [.. bookEl.GetProperty("bids").EnumerateArray().Select(ParseLevel)],
                Asks = [.. bookEl.GetProperty("asks").EnumerateArray().Select(ParseLevel)],
            };
        }

        var tickerStream = new List<Ticker>();
        if (el.TryGetProperty("ticker_stream", out var streamEl))
        {
            foreach (var t in streamEl.EnumerateArray())
            {
                tickerStream.Add(new Ticker
                {
                    Symbol = "PLACEHOLDER",
                    Price = Dec(t.GetProperty("price")),
                    QuoteCurrency = t.TryGetProperty("quote_currency", out var tqc)
                        ? tqc.GetString()!
                        : "USD",
                    Timestamp = ParseTimestamp(t.GetProperty("timestamp").GetString()!),
                    SourceAdapter = "PLACEHOLDER",
                });
            }
        }

        return new InMemorySymbolData
        {
            Spot = spot,
            Ohlcv = ohlcv,
            OrderBook = book,
            TickerStream = tickerStream,
        };
    }

    private static BookLevel ParseLevel(JsonElement pair)
    {
        var items = pair.EnumerateArray().ToList();
        return new BookLevel(Dec(items[0]), Dec(items[1]));
    }

    private static List<InMemoryFailMode> BuildFailModes(JsonElement spec)
    {
        var modes = new List<InMemoryFailMode>();
        if (spec.TryGetProperty("fail_modes", out var fmEl))
        {
            foreach (var fm in fmEl.EnumerateArray())
            {
                modes.Add(new InMemoryFailMode(
                    fm.GetProperty("symbol").GetString()!,
                    fm.GetProperty("code").GetString()!,
                    fm.TryGetProperty("fail_first_n", out var n) ? n.GetInt32() : null));
            }
        }

        return modes;
    }

    private static MarketDataClient BuildClient(
        JsonElement setup,
        Dictionary<string, InMemoryAdapter> adapters,
        ManualClock clock)
    {
        var chains = new Dictionary<string, IReadOnlyList<IMarketDataAdapter>>(StringComparer.Ordinal);
        foreach (var chainProp in setup.GetProperty("client_chain").EnumerateObject())
        {
            chains[chainProp.Name] =
                [.. chainProp.Value.EnumerateArray().Select(id => adapters[id.GetString()!])];
        }

        var config = new ClientConfig
        {
            Cache = BuildCacheConfig(setup),
            RateLimit = BuildRateLimitConfig(setup),
            Streaming = BuildStreamingConfig(setup),
        };
        return new MarketDataClient(new AdapterRegistry(chains), config, clock);
    }

    private static CacheConfig BuildCacheConfig(JsonElement setup)
    {
        if (!setup.TryGetProperty("cache", out var c))
        {
            return new CacheConfig();
        }

        return new CacheConfig
        {
            Enabled = c.TryGetProperty("enabled", out var e) && e.GetBoolean(),
            SpotTtlSeconds = c.TryGetProperty("spot_ttl_s", out var s) ? s.GetInt32() : 5,
            OhlcvTtlSeconds = c.TryGetProperty("ohlcv_ttl_s", out var o) ? o.GetInt32() : 60,
            OrderBookTtlSeconds = c.TryGetProperty("order_book_ttl_s", out var ob) ? ob.GetInt32() : 1,
        };
    }

    private static RateLimitConfig BuildRateLimitConfig(JsonElement setup)
    {
        if (!setup.TryGetProperty("rate_limit", out var r))
        {
            return new RateLimitConfig();
        }

        return new RateLimitConfig
        {
            Enabled = !r.TryGetProperty("enabled", out var e) || e.GetBoolean(),
            Strategy = r.TryGetProperty("strategy", out var s) && s.GetString() == "bubble"
                ? RateLimitStrategy.Bubble
                : RateLimitStrategy.Fallthrough,
            MaxRetryAttempts = r.TryGetProperty("max_retry_attempts", out var m) ? m.GetInt32() : 3,
            InitialBackoffSeconds = r.TryGetProperty("initial_backoff_s", out var ib)
                ? ib.GetDouble()
                : 0.5,
            Jitter = !r.TryGetProperty("jitter", out var j) || j.GetBoolean(),
        };
    }

    private static StreamingConfig BuildStreamingConfig(JsonElement setup)
    {
        if (!setup.TryGetProperty("streaming", out var s))
        {
            return new StreamingConfig();
        }

        return new StreamingConfig
        {
            DefaultPollingIntervalSeconds = s.TryGetProperty("default_polling_interval_s", out var p)
                ? p.GetDouble()
                : 4.0,
        };
    }

    private static Task<object> Invoke(MarketDataClient client, string method, JsonElement args) =>
        method switch
        {
            "get_spot" => Box(client.GetSpotAsync(
                args.GetProperty("symbol").GetString()!,
                args.GetProperty("market").GetString()!,
                args.TryGetProperty("quote_currency", out var qc) ? qc.GetString()! : "USD")),
            "get_ohlcv" => Box(client.GetOhlcvAsync(
                args.GetProperty("symbol").GetString()!,
                args.GetProperty("market").GetString()!,
                Enum.Parse<Interval>(args.GetProperty("interval").GetString()!),
                args.TryGetProperty("since", out var since) && since.ValueKind == JsonValueKind.String
                    ? ParseTimestamp(since.GetString()!)
                    : null,
                args.TryGetProperty("limit", out var lim) ? lim.GetInt32() : 100)),
            "get_order_book" => Box(client.GetOrderBookAsync(
                args.GetProperty("symbol").GetString()!,
                args.GetProperty("market").GetString()!,
                args.TryGetProperty("depth", out var d) ? d.GetInt32() : 20)),
            _ => throw new InvalidOperationException($"unsupported method: {method}"),
        };

    private static async Task<object> Box<T>(Task<T> task) => (await task.ConfigureAwait(false))!;

    private static async Task RunStreaming(
        MarketDataClient client,
        JsonElement args,
        JsonElement operation,
        JsonElement fixture,
        JsonElement? expectedError)
    {
        var yieldCount = operation.TryGetProperty("yield_count", out var yc) ? yc.GetInt32() : 1;
        var collected = new List<Ticker>();

        async Task Consume()
        {
            using var cts = new CancellationTokenSource();
            await foreach (var ticker in client.SubscribeTickerAsync(
                args.GetProperty("symbol").GetString()!,
                args.GetProperty("market").GetString()!,
                !args.TryGetProperty("polling_fallback", out var pf) || pf.GetBoolean(),
                args.TryGetProperty("polling_interval_s", out var pi) ? pi.GetDouble() : 4.0,
                cts.Token).ConfigureAwait(false))
            {
                collected.Add(ticker);
                if (collected.Count >= yieldCount)
                {
                    await cts.CancelAsync().ConfigureAwait(false);
                    break;
                }
            }
        }

        if (expectedError is { } err)
        {
            var ex = await Assert.ThrowsAnyAsync<MarketDataException>(Consume);
            Assert.Equal(err.GetProperty("code").GetString(), ex.Code);
            return;
        }

        await Consume();
        AssertExpectedOutput(fixture, collected, adapters: null);
    }

    private static void AssertExpectedOutput(
        JsonElement fixture,
        object result,
        Dictionary<string, InMemoryAdapter>? adapters)
    {
        if (!fixture.TryGetProperty("expected_output", out var expected)
            || expected.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        var typeLabel = expected.TryGetProperty("type", out var t) ? t.GetString()! : string.Empty;

        switch (typeLabel)
        {
            case "SpotPrice":
                AssertSpot((SpotPrice)result, expected);
                break;
            case "list[Candle]":
                AssertCandles((IReadOnlyList<Candle>)result, expected);
                break;
            case "OrderBook":
                AssertOrderBook((OrderBook)result, expected);
                break;
            case "list[Ticker]":
                AssertTickers((List<Ticker>)result, expected);
                break;
            default:
                throw new InvalidOperationException($"unknown expected type: {typeLabel}");
        }

        if (expected.TryGetProperty("adapter_call_count", out var counts) && adapters is not null)
        {
            foreach (var entry in counts.EnumerateObject())
            {
                var total = adapters[entry.Name].CallCounts.Values.Sum();
                Assert.Equal(entry.Value.GetInt32(), total);
            }
        }
    }

    private static void AssertSpot(SpotPrice spot, JsonElement expected)
    {
        if (!expected.TryGetProperty("fields", out var fields))
        {
            return;
        }

        foreach (var field in fields.EnumerateObject())
        {
            switch (field.Name)
            {
                case "symbol":
                    Assert.Equal(field.Value.GetString(), spot.Symbol);
                    break;
                case "last":
                    Assert.Equal(
                        decimal.Parse(field.Value.GetString()!, CultureInfo.InvariantCulture),
                        spot.Last);
                    break;
                case "quote_currency":
                    Assert.Equal(field.Value.GetString(), spot.QuoteCurrency);
                    break;
                case "source_adapter":
                    Assert.Equal(field.Value.GetString(), spot.SourceAdapter);
                    break;
                default:
                    throw new InvalidOperationException($"unknown spot field: {field.Name}");
            }
        }
    }

    private static void AssertCandles(IReadOnlyList<Candle> candles, JsonElement expected)
    {
        if (expected.TryGetProperty("length", out var len))
        {
            Assert.Equal(len.GetInt32(), candles.Count);
        }

        if (expected.TryGetProperty("ordered_ascending_by", out var ob)
            && ob.GetString() == "timestamp")
        {
            for (var i = 1; i < candles.Count; i++)
            {
                Assert.True(candles[i - 1].Timestamp <= candles[i].Timestamp);
            }
        }

        if (expected.TryGetProperty("all_timestamps_at_or_after", out var minTs))
        {
            var min = ParseTimestamp(minTs.GetString()!);
            Assert.All(candles, c => Assert.True(c.Timestamp >= min));
        }
    }

    private static void AssertOrderBook(OrderBook book, JsonElement expected)
    {
        if (expected.TryGetProperty("fields", out var fields))
        {
            foreach (var field in fields.EnumerateObject())
            {
                if (field.Name == "symbol")
                {
                    Assert.Equal(field.Value.GetString(), book.Symbol);
                }
                else if (field.Name == "source_adapter")
                {
                    Assert.Equal(field.Value.GetString(), book.SourceAdapter);
                }
            }
        }

        if (expected.TryGetProperty("bids_length", out var bl))
        {
            Assert.Equal(bl.GetInt32(), book.Bids.Count);
        }

        if (expected.TryGetProperty("asks_length", out var al))
        {
            Assert.Equal(al.GetInt32(), book.Asks.Count);
        }

        if (expected.TryGetProperty("bids_sorted_descending_by_price", out var bd) && bd.GetBoolean())
        {
            for (var i = 1; i < book.Bids.Count; i++)
            {
                Assert.True(book.Bids[i - 1].Price >= book.Bids[i].Price);
            }
        }

        if (expected.TryGetProperty("asks_sorted_ascending_by_price", out var ad) && ad.GetBoolean())
        {
            for (var i = 1; i < book.Asks.Count; i++)
            {
                Assert.True(book.Asks[i - 1].Price <= book.Asks[i].Price);
            }
        }
    }

    private static void AssertTickers(List<Ticker> tickers, JsonElement expected)
    {
        if (expected.TryGetProperty("length", out var len))
        {
            Assert.Equal(len.GetInt32(), tickers.Count);
        }

        foreach (var key in new[] { "all_have_field", "all_have_field_2" })
        {
            if (expected.TryGetProperty(key, out var fieldSpec))
            {
                var parts = fieldSpec.GetString()!.Split(':', 2);
                Assert.All(tickers, t => AssertTickerField(t, parts[0], parts[1]));
            }
        }
    }

    private static void AssertTickerField(Ticker ticker, string field, string value)
    {
        switch (field)
        {
            case "source_adapter":
                Assert.Equal(value, ticker.SourceAdapter);
                break;
            case "price":
                Assert.Equal(decimal.Parse(value, CultureInfo.InvariantCulture), ticker.Price);
                break;
            default:
                throw new InvalidOperationException($"unknown ticker field: {field}");
        }
    }

    private static DateTimeOffset ParseTimestamp(string text) =>
        DateTimeOffset.Parse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static decimal Dec(JsonElement el) =>
        el.ValueKind == JsonValueKind.String
            ? decimal.Parse(el.GetString()!, CultureInfo.InvariantCulture)
            : el.GetDecimal();

    private static decimal? OptDec(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var el) && el.ValueKind != JsonValueKind.Null
            ? Dec(el)
            : null;
}
