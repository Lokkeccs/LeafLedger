using LeafLedger.SharedKernel;
using Xunit;

namespace LeafLedger.SharedKernel.Tests;

public class ResultTests
{
    private static readonly DomainError SampleError = new("sample.failure", "Something went wrong.");

    [Fact]
    public void Success_carries_value()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_carries_error_and_has_no_value()
    {
        var result = Result<int>.Failure(SampleError);

        Assert.True(result.IsFailure);
        Assert.Equal(SampleError, result.Error);
        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Map_transforms_success_and_short_circuits_failure()
    {
        Assert.Equal(84, Result<int>.Success(42).Map(x => x * 2).Value);

        var mapped = Result<int>.Failure(SampleError).Map(x => x * 2);
        Assert.True(mapped.IsFailure);
        Assert.Equal(SampleError, mapped.Error);
    }

    [Fact]
    public void Bind_chains_success_and_short_circuits_failure()
    {
        var chained = Result<int>.Success(42).Bind(x => Result<string>.Success($"n={x}"));
        Assert.Equal("n=42", chained.Value);

        var shortCircuited = Result<int>.Failure(SampleError).Bind(x => Result<string>.Success($"n={x}"));
        Assert.True(shortCircuited.IsFailure);
    }

    [Fact]
    public void Match_dispatches_to_the_correct_branch()
    {
        Assert.Equal("ok:42", Result<int>.Success(42).Match(v => $"ok:{v}", e => $"err:{e.Code}"));
        Assert.Equal("err:sample.failure", Result<int>.Failure(SampleError).Match(v => $"ok:{v}", e => $"err:{e.Code}"));
    }

    [Fact]
    public void Failure_path_never_throws()
    {
        var result = Result<int>.Failure(SampleError);

        var exception = Record.Exception(() =>
        {
            _ = result.IsFailure;
            _ = result.Map(x => x + 1);
            _ = result.Bind(x => Result<int>.Success(x));
            _ = result.Match(_ => 0, _ => -1);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void Non_generic_result_supports_success_and_failure()
    {
        Assert.True(Result.Success().IsSuccess);
        Assert.True(Result.Failure(SampleError).IsFailure);
        Assert.Equal("handled", Result.Failure(SampleError).Match(() => "ok", e => "handled"));
    }
}
