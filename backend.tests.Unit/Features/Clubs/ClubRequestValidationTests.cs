using System.ComponentModel.DataAnnotations;
using System.Text;

using backend.main.features.clubs.contracts.requests;

using FluentAssertions;

using Microsoft.AspNetCore.Http;

namespace backend.tests.Unit.Features.Clubs;

public class ClubRequestValidationTests
{
    [Fact]
    public void ClubCreateRequest_ShouldRequireCoreFields_AndValidateFormatFields()
    {
        var request = new ClubCreateRequest
        {
            Name = new string('N', 31),
            Description = new string('D', 31),
            Clubtype = "science",
            ClubImage = null!,
            Phone = "abc",
            Email = "not-an-email"
        };

        var results = Validate(request);

        results.Select(item => item.ErrorMessage).Should().Contain([
            "Name cannot exceed 30 characters.",
            "Clubtype must be one of: sport, sports, academic, social, cultural, game, gaming, music, other.",
            "Club image is required.",
            "The Phone field is not a valid phone number.",
            "The Email field is not a valid e-mail address."
        ]);
    }

    [Fact]
    public void ClubCreateRequest_ShouldValidateSuccessfully_ForValidPayload()
    {
        var request = new ClubCreateRequest
        {
            Name = "Chess Club",
            Description = "Weekly games",
            Clubtype = "gaming",
            ClubImage = CreateFormFile("club.png"),
            Phone = "+1 555-0100",
            Email = "club@test.local"
        };

        Validate(request).Should().BeEmpty();
    }

    private static IFormFile CreateFormFile(string fileName)
    {
        var bytes = Encoding.UTF8.GetBytes("image");
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "clubImage", fileName);
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
