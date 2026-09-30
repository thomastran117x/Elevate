using backend.main.shared.exceptions.http;
using backend.main.utilities;

using FluentAssertions;

using Microsoft.AspNetCore.Mvc;

namespace backend.tests.Unit.Shared.Utilities;

public class HandleErrorTests
{
    [Fact]
    public void Resolve_ShouldRethrowCancellation_RatherThanTurningItIntoAServerError()
    {
        // Controllers catch broadly and call this. Resolving a cancelled request to a 500 envelope
        // here is what made every client disconnect look like a server fault, in every endpoint
        // that passes its request token down. Rethrowing leaves the decision to the middleware
        // that knows whether the client left or the request simply ran out of time.
        var cancelled = new OperationCanceledException();

        var act = () => HandleError.Resolve(cancelled);

        act.Should().Throw<OperationCanceledException>().Which.Should().BeSameAs(cancelled);
    }

    [Fact]
    public void Resolve_ShouldMapAppExceptionsToTheirOwnStatus()
    {
        var result = HandleError.Resolve(new ConflictException("nope")) as ObjectResult;

        result!.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void Resolve_ShouldMapUnknownExceptionsToInternalServerError()
    {
        var result = HandleError.Resolve(new InvalidOperationException("boom")) as ObjectResult;

        result!.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }
}
