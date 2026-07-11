using System.Text.Json;
using LeafLedger.Modules.Ledger.Domain.PostingValidity;
using LeafLedger.Modules.Ledger.Tests.Fixtures;
using Xunit;

namespace LeafLedger.Modules.Ledger.Tests;

internal static class FixtureCases
{
    public static TheoryData<LedgerCorePostingFixture> ForUnit(string unit)
    {
        var data = new TheoryData<LedgerCorePostingFixture>();
        foreach (var fixture in LedgerCorePostingFixtureLoader.LoadSelected().Where(item => item.Unit == unit))
        {
            data.Add(fixture);
        }

        return data;
    }
}

internal static class GoldenAssertions
{
    public static void MatchPostingError(JsonElement expected, PostingValidityError? actual, FixtureIds ids)
    {
        if (expected.TryGetProperty("ok", out _))
        {
            Assert.Null(actual);
            return;
        }

        Assert.NotNull(actual);
        var expectedError = expected.GetProperty("error");
        Assert.Equal("PostingValidityError", expectedError.GetProperty("type").GetString());
        var expectedIssues = expectedError.GetProperty("issues").EnumerateArray().ToArray();
        Assert.Equal(expectedIssues.Length, actual.Issues.Count);
        for (var index = 0; index < expectedIssues.Length; index++)
        {
            var expectedIssue = expectedIssues[index];
            var issue = actual.Issues[index];
            Assert.Equal(expectedIssue.GetProperty("entityType").GetString(), ToWireValue(issue.EntityType));
            Assert.Equal(expectedIssue.GetProperty("entityId").GetInt64(), ids.Reverse(issue.EntityId));
            Assert.Equal(expectedIssue.GetProperty("reason").GetString(), ToWireValue(issue.Reason));
            Assert.Equal(FixtureJson.Date(expectedIssue, "txDate"), issue.TxDate);
            Assert.Equal(FixtureJson.OptionalString(expectedIssue, "source"), issue.Source);
        }
    }

    private static string ToWireValue(PostingEntityType value) => value switch
    {
        PostingEntityType.Account => "account",
        PostingEntityType.BusinessPartner => "businessPartner",
        PostingEntityType.User => "user",
        PostingEntityType.Project => "project",
        _ => throw new InvalidDataException($"Unknown posting entity type '{value}'."),
    };

    private static string ToWireValue(PostingValidityReason value) => value switch
    {
        PostingValidityReason.Missing => "missing",
        PostingValidityReason.Inactive => "inactive",
        PostingValidityReason.Future => "future",
        PostingValidityReason.Expired => "expired",
        _ => throw new InvalidDataException($"Unknown posting-validity reason '{value}'."),
    };
}
