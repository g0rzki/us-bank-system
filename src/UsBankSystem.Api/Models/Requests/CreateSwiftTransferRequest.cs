using System.ComponentModel.DataAnnotations;

namespace UsBankSystem.Api.Models.Requests;

public class CreateSwiftTransferRequest
{
    [Required]
    public Guid FromAccountId { get; set; }

    [Required]
    public string Iban { get; set; } = null!;

    [Required]
    public string Bic { get; set; } = null!;

    [Required]
    public string BeneficiaryName { get; set; } = null!;

    public string? BeneficiaryAddress { get; set; }

    [Required]
    [Range(0.01, double.MaxValue)]
    public decimal Amount { get; set; }

    [Required]
    public string Currency { get; set; } = "USD";

    /// <summary>SHA = shared costs, OUR = sender pays all, BEN = beneficiary pays all (MT103 field 71A)</summary>
    [Required]
    public string ChargeBearer { get; set; } = "SHA";

    /// <summary>Requested settlement date (MT103 field 32A value date). Defaults to next business day.</summary>
    public DateOnly? ValueDate { get; set; }

    /// <summary>Purpose / remittance info, e.g. invoice number (MT103 field 70)</summary>
    public string? RemittanceInfo { get; set; }

    public string? Description { get; set; }
}
