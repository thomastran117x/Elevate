namespace backend.main.features.clubs.contracts.responses
{
    public class ClubResponse
    {
        public int Id
        {
            get; set;
        }

        public int OwnerId
        {
            get; set;
        }

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Clubtype { get; set; } = string.Empty;
        public string ClubImage { get; set; } = string.Empty;
        public int MemberCount { get; set; } = 0;
        public int EventCount { get; set; } = 0;
        public int AvaliableEventCount { get; set; } = 0;
        public int MaxMemberCount { get; set; } = 0;
        public bool IsPrivate { get; set; } = false;
        public int CurrentVersionNumber { get; set; } = 0;
        public bool IsOwner { get; set; } = false;
        public bool IsManager { get; set; } = false;
        public bool IsVolunteer { get; set; } = false;
        public bool CanManage { get; set; } = false;

        public string? Phone
        {
            get; set;
        }
        public string? Email
        {
            get; set;
        }
        public double? Rating
        {
            get; set;
        }
        public ClubResponse(int id, int userId, string name, string description, string clubtype, string clubimage, int memberCount, int eventCount, int avaliableEventCount, int maxMemberCount, bool isPrivate, int currentVersionNumber)
        {
            Id = id;
            OwnerId = userId;
            Name = name;
            Description = description;
            Clubtype = clubtype;
            ClubImage = clubimage;
            MemberCount = memberCount;
            EventCount = eventCount;
            AvaliableEventCount = avaliableEventCount;
            MaxMemberCount = maxMemberCount;
            IsPrivate = isPrivate;
            CurrentVersionNumber = currentVersionNumber;
        }
    }
}
