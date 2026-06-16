using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class CreateAchTransferRequest : IValidatableObject
{
    [Required]
    public Guid FromAccountId { get; set; }

    [Required]
    [RegularExpression(@"^\d{9}$", ErrorMessage = "Routing number must be exactly 9 digits")]
    public string ToRoutingNumber { get; set; } = null!;

    [Required]
    public string ToAccountNumber { get; set; } = null!;

    [Required]
    [MaxLength(22, ErrorMessage = "Recipient name must be 22 characters or fewer")]
    public string RecipientName { get; set; } = null!;

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // ABA MOD-10 checksum — catches invalid routing numbers before they reach the gateway
        if (ToRoutingNumber is { Length: 9 } && ToRoutingNumber.All(char.IsDigit))
        {
            int[] weights = [3, 7, 1, 3, 7, 1, 3, 7, 1];
            var sum = ToRoutingNumber.Select((c, i) => (c - '0') * weights[i]).Sum();
            if (sum % 10 != 0)
                yield return new ValidationResult(
                    "Invalid ABA routing number (checksum failed)",
                    [nameof(ToRoutingNumber)]);
        }
    }
}
