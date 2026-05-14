namespace backend.main.features.clubs.staff
{
    public class ClubStaff
    {
        public int Id { get; set; }
        public int ClubId { get; set; }
        public int UserId { get; set; }
        public ClubStaffRole Role { get; set; } = ClubStaffRole.Manager;
        public int GrantedByUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
