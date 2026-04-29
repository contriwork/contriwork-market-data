using System.Text.Json;
using Xunit;

namespace Contriwork.PackageName.Tests;

/// <summary>
/// Contract test runner — one of three (Python / C# / TypeScript) that MUST
/// produce identical results against the shared fixture.
/// Do not add cases here; add them to <c>contract-tests/test_cases.json</c>
/// and convert the placeholder <c>[Fact]</c> below to a <c>[Theory]</c>
/// backed by <c>LoadCases</c> once the fixture has entries.
/// </summary>
public sealed class ContractTests
{
    private static readonly string FixturePath = Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..", "..",
        "contract-tests", "test_cases.json");

    [Fact]
    public void Fixture_Is_WellFormed()
    {
        var fullPath = Path.GetFullPath(FixturePath);
        Assert.True(File.Exists(fullPath), $"fixture missing at {fullPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
        Assert.Equal(1, doc.RootElement.GetProperty("schema_version").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("cases", out _));
        Assert.True(doc.RootElement.TryGetProperty("contract_revision", out _));
    }

    [Fact]
    public void Cases_Are_Loadable()
    {
        // Replace this [Fact] with a [Theory] + [MemberData(nameof(LoadCases))]
        // once contract-tests/test_cases.json has entries and the port has methods.
        var cases = LoadCases().ToList();
        Assert.NotNull(cases);
    }

    public static IEnumerable<object[]> LoadCases()
    {
        var fullPath = Path.GetFullPath(FixturePath);
        if (!File.Exists(fullPath))
        {
            yield break;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (doc.RootElement.GetProperty("schema_version").GetInt32() != 1)
        {
            throw new InvalidOperationException(
                "contract fixture schema_version changed — update all three language runners");
        }

        if (!doc.RootElement.TryGetProperty("cases", out var casesEl))
        {
            yield break;
        }

        foreach (var el in casesEl.EnumerateArray())
        {
            var skip = el.TryGetProperty("skip_languages", out var skipEl)
                && skipEl.EnumerateArray().Any(s => s.GetString() == "csharp");
            if (skip)
            {
                continue;
            }

            yield return new object[] { el.GetProperty("name").GetString() ?? "unnamed" };
        }
    }
}
