using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class RegisterPhoneAliasRequest
{
    [Required]
    public string Phone { get; set; } = null!;
}
