namespace backend.main.features.clubs
{
    public class ClubAccessInfo
    {
        public bool IsOwner { get; set; }
        public bool IsManager { get; set; }
        public bool IsVolunteer { get; set; }
        public bool CanManage { get; set; }
    }
}
