using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class CreateJuniorAccountRequest
{
    [Required]
    public Guid ParentAccountId { get; set; }

    [Required]
    public DateOnly DateOfBirth { get; set; }
}