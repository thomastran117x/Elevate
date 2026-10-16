using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace backend.tests.Unit.Support;

/// <summary>
/// A response feature that runs its OnStarting and OnCompleted callbacks when told to, as a server
/// does. The default test feature never runs them.
/// </summary>
internal sealed class CallbackResponseFeature : HttpResponseFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];
    private readonly List<(Func<object, Task> Callback, object State)> _onCompleted = [];
    private bool _started;

    public override bool HasStarted => _started;

    public override void OnStarting(Func<object, Task> callback, object state) =>
        _onStarting.Add((callback, state));

    public override void OnCompleted(Func<object, Task> callback, object state) =>
        _onCompleted.Add((callback, state));

    public static CallbackResponseFeature AttachTo(HttpContext context)
    {
        var feature = new CallbackResponseFeature();
        context.Features.Set<IHttpResponseFeature>(feature);
        return feature;
    }

    /// <summary>Runs the OnStarting callbacks, most recently registered first.</summary>
    public async Task StartAsync()
    {
        if (_started)
            return;

        _started = true;
        for (var i = _onStarting.Count - 1; i >= 0; i--)
            await _onStarting[i].Callback(_onStarting[i].State);
    }

    /// <summary>Starts the response if needed, then runs the OnCompleted callbacks.</summary>
    public async Task CompleteAsync()
    {
        await StartAsync();
        for (var i = _onCompleted.Count - 1; i >= 0; i--)
            await _onCompleted[i].Callback(_onCompleted[i].State);
    }
}
