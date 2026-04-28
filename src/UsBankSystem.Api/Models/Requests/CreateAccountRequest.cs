using System.ComponentModel.DataAnnotations;
using UsBankSystem.Core.Domain.Common;

namespace UsBankSystem.Api.Models.Requests;

public class CreateAccountRequest
{
    [Required]
    public string Type { get; set; } = null!;

    public string Currency { get; set; } = CurrencyCode.USD;
}
