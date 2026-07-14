using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

using backend.main.features.clubs.staff;

namespace backend.main.features.clubs.invitations.contracts.requests
{
    public sealed class CreateClubStaffInvitationRequest
    {
        /// <summary>A registered user's username or email address.</summary>
        [Required]
        [MaxLength(320)]
        public string Identifier { get; set; } = string.Empty;

        /// <summary>
        /// Manager or Volunteer. Annotated so the role can be posted as its string name
        /// ("Manager"/"Volunteer") regardless of the global enum-serialization setting.
        /// </summary>
        [Required]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ClubStaffRole Role
        {
            get; set;
        }
    }
}
