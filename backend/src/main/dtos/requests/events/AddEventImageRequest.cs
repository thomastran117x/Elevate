using System.ComponentModel.DataAnnotations;

namespace backend.main.dtos.requests.events
{
    public class AddEventImageRequest
    {
        [Required]
        public string ImageUrl { get; set; } = null!;
    }
}
