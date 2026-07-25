using backend.main.features.clubs;
using backend.main.features.events.invitations;
using backend.main.features.events.registration;
using backend.main.features.payment;
using backend.main.infrastructure.database.core;

using Microsoft.EntityFrameworkCore;

namespace backend.main.features.events.access
{
    /// <summary>
    /// Deliberately depends only on IClubService, IEventInvitationService,
    /// IEventRegistrationRepository and the DbContext — none of which depend on
    /// IEventsService — so this stays safe to inject from the waitlist promoter.
    /// </summary>
    public sealed class EventAccessChecker : IEventAccessChecker
    {
        private readonly AppDatabaseContext _db;
        private readonly IClubService _clubService;
        private readonly IEventRegistrationRepository _registrationRepository;
        private readonly IEventInvitationService _invitationService;

        public EventAccessChecker(
            AppDatabaseContext db,
            IClubService clubService,
            IEventRegistrationRepository registrationRepository,
            IEventInvitationService invitationService)
        {
            _db = db;
            _clubService = clubService;
            _registrationRepository = registrationRepository;
            _invitationService = invitationService;
        }

        public async Task<bool> CanViewEventAsync(Events ev, int? userId, string? userRole)
        {
            // This is the single visibility policy for private event reads across public endpoints.
            if (!EventLifecyclePolicy.IsVisibleInPublicDetail(ev.LifecycleState))
                return false;

            if (!ev.isPrivate)
                return true;

            if (!userId.HasValue)
                return false;

            if (await _clubService.HasClubStaffAccessAsync(ev.ClubId, userId.Value, userRole))
                return true;

            var registration = await _registrationRepository.IsRegisteredAsync(ev.Id, userId.Value);
            if (registration != null)
                return true;

            if (await _invitationService.HasAcceptedInvitationAccessAsync(ev.Id, userId.Value))
                return true;

            var payment = await _db.Payments
                .AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.EventId == ev.Id &&
                    p.UserId == userId.Value &&
                    (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Succeeded));

            return payment != null;
        }
    }
}
