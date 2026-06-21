using System.Globalization;
using System.Xml.Linq;

namespace UsBankSystem.Api.Integrations.FedNow;

public record Pacs008Data(
    string MsgId,
    string EndToEndId,
    decimal Amount,
    string Currency,
    string DebtorBankName,
    string DebtorBankRtn,
    string DebtorName,
    string DebtorAccountNumber,
    string CreditorBankName,
    string CreditorBankRtn,
    string CreditorName,
    string CreditorAccountNumber,
    string? Description
);

public class Pacs008Builder
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08";

    public string Build(Pacs008Data data)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "Document",
                new XElement(Ns + "FIToFICstmrCdtTrf",
                    new XElement(Ns + "GrpHdr",
                        new XElement(Ns + "MsgId", data.MsgId),
                        new XElement(Ns + "CreDtTm", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"))
                    ),
                    new XElement(Ns + "CdtTrfTxInf",
                        new XElement(Ns + "PmtId",
                            new XElement(Ns + "EndToEndId", data.EndToEndId)
                        ),
                        new XElement(Ns + "IntrBkSttlmAmt",
                            new XAttribute("Ccy", data.Currency),
                            data.Amount.ToString("F2", CultureInfo.InvariantCulture)
                        ),
                        new XElement(Ns + "DbtrAgt",
                            new XElement(Ns + "FinInstnId",
                                new XElement(Ns + "ClrSysMmbId",
                                    // lowercase "nm" per partner XSD (ClrSysMmbId/nm), not ISO "Nm" (used in Dbtr/Cdtr)
                                    new XElement(Ns + "nm", data.DebtorBankName),
                                    new XElement(Ns + "MmbId", data.DebtorBankRtn)
                                )
                            )
                        ),
                        new XElement(Ns + "Dbtr",
                            new XElement(Ns + "Nm", data.DebtorName)
                        ),
                        new XElement(Ns + "DbtrAcct",
                            new XElement(Ns + "Id",
                                new XElement(Ns + "Othr",
                                    new XElement(Ns + "Id", data.DebtorAccountNumber),
                                    new XElement(Ns + "SchmeNm",
                                        new XElement(Ns + "Prtry", "US_ACCT")
                                    )
                                )
                            )
                        ),
                        new XElement(Ns + "CdtrAgt",
                            new XElement(Ns + "FinInstnId",
                                new XElement(Ns + "ClrSysMmbId",
                                    new XElement(Ns + "nm", data.CreditorBankName),
                                    new XElement(Ns + "MmbId", data.CreditorBankRtn)
                                )
                            )
                        ),
                        new XElement(Ns + "Cdtr",
                            new XElement(Ns + "Nm", data.CreditorName)
                        ),
                        new XElement(Ns + "CdtrAcct",
                            new XElement(Ns + "Id",
                                new XElement(Ns + "Othr",
                                    new XElement(Ns + "Id", data.CreditorAccountNumber),
                                    new XElement(Ns + "SchmeNm",
                                        new XElement(Ns + "Prtry", "US_ACCT")
                                    )
                                )
                            )
                        ),
                        new XElement(Ns + "RmtInf",
                            new XElement(Ns + "Ustrd", data.Description ?? string.Empty)
                        )
                    )
                )
            )
        );

        return doc.Declaration + Environment.NewLine + doc.ToString();
    }
}
