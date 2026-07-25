namespace backend.main.features.events.access
{
    /// <summary>
    /// The single visibility policy for private event reads across public endpoints.
    ///
    /// Extracted from EventsService so that components which must not depend on
    /// IEventsService — notably the waitlist promoter, which is itself called *from*
    /// EventsService — can still evaluate visibility without creating a DI cycle.
    /// EventsService delegates to this; do not reimplement the policy anywhere else.
    /// </summary>
    public interface IEventAccessChecker
    {
        Task<bool> CanViewEventAsync(Events ev, int? userId, string? userRole);
    }
}
