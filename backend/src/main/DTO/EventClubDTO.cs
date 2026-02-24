using System.ComponentModel.DataAnnotations;

namespace backend.main.DTOs
{
    public class EventClubCreateRequest
    {
        [Required]
        [StringLength(30, ErrorMessage = "Name cannot exceed 30 characters.")]
        public required string Name
        {
            get; set;
        }
        [Required]
        [StringLength(70, ErrorMessage = "Description cannot exceed 70 characters.")]
        public required string Description
        {
            get; set;
        }
        [Required]
        [StringLength(30, ErrorMessage = "Location cannot exceed 30 characters.")]
        public required string Location
        {
            get; set;
        }
        public required string EventImage
        {
            get; set;
        }
        public string? Intesnity
        {
            get; set;
        }
        public required DateTime StartTime
        {
            get; set;
        }
        public DateTime? EndTime
        {
            get; set;
        }
    }

    public class EventClubUpdateRequest
    {
        [Required]
        [StringLength(30, ErrorMessage = "Name cannot exceed 30 characters.")]
        public required string Name
        {
            get; set;
        }
        [Required]
        [StringLength(70, ErrorMessage = "Description cannot exceed 70 characters.")]
        public required string Description
        {
            get; set;
        }
        [Required]
        [StringLength(30, ErrorMessage = "Location cannot exceed 30 characters.")]
        public required string Location
        {
            get; set;
        }
        public required string EventImage
        {
            get; set;
        }
        public string? Intesnity
        {
            get; set;
        }
        public required DateTime StartTime
        {
            get; set;
        }
        public DateTime? EndTime
        {
            get; set;
        }
    }

    public class EventClubResponse
    {
        public EventClubResponse(int id, string name, string description, string location, string eventimage, DateTime starttime)
        {
            Id = id;
            Name = name;
            Description = description;
            Location = location;
            EventImage = eventimage;
            StartTime = starttime;
        }

        [Required]
        public int Id
        {
            get; set;
        }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        public string Description { get; set; } = string.Empty;

        [Required]
        public string Location { get; set; } = string.Empty;

        [Required]
        public string EventImage { get; set; } = string.Empty;
        [Required]
        public DateTime StartTime { get; set; } = DateTime.Now;

        public string? Intensity
        {
            get; set;
        }
        public DateTime? EndTime
        {
            get; set;
        }
    }
}
