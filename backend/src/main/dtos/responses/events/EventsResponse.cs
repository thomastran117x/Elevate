using backend.main.models.enums;

namespace backend.main.dtos.responses.events
{
    public class EventResponse
    {
        public int Id
        {
            get; set;
        }
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public string Location { get; set; } = null!;
        public string ImageUrl { get; set; } = null!;
        public bool IsPrivate
        {
            get; set;
        }
        public int MaxParticipants
        {
            get; set;
        }
        public int RegisterCost
        {
            get; set;
        }
        public DateTime StartTime
        {
            get; set;
        }
        public DateTime? EndTime
        {
            get; set;
        }
        public int ClubId
        {
            get; set;
        }
        public DateTime CreatedAt
        {
            get; set;
        }
        public EventStatus Status { get; set; }
    }
}
