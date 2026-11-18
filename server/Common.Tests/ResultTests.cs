using VRCX.Common.Problems;
using VRCX.Common;
using VRCX.Common.Errors;

namespace VRCX.Common.Tests;

// What a feature hands back to a controller or an MCP tool: one of its cases and never both.
public class ResultTests
{
    private static string Describe(Result<string?> result) => result switch
    {
        VrcxProblem problem => $"problem {problem.Type}",
        null => "nothing",
        string value => $"value {value}",
    };

    private static async Task<Result<string?>> ReadAsync(int found)
    {
        await Task.Yield();
        if (found < 0) return AccountError.NoLiveSession;
        return found == 0 ? null : "avtr_1";
    }

    [Test]
    public async Task A_value_and_a_problem_are_returned_as_they_are_and_matched_by_case()
    {
        await Assert.That(Describe(await ReadAsync(1))).IsEqualTo("value avtr_1");
        await Assert.That(Describe(await ReadAsync(-1))).IsEqualTo("problem Account.NoLiveSession");
    }

    [Test]
    public async Task A_read_that_found_nothing_is_neither_a_value_nor_a_problem()
    {
        var nothing = await ReadAsync(0);
        await Assert.That(Describe(nothing)).IsEqualTo("nothing");
        await Assert.That(nothing is VrcxProblem).IsFalse();
    }
}
