using System.Globalization;
using System.Xml.Linq;

namespace UsBankSystem.Api.Integrations.FedNow;

public record Pacs002Data(
    string MsgId,
    string OriginalMsgId,
    string OriginalEndToEndId,
    string Status,
    string AccountServicerRef,
    decimal Amount,
    string Currency,
    string DebtorBankName,
    string DebtorBankRtn,
    string DebtorName,
    string DebtorAccountNumber,
    string CreditorBankName,
    string CreditorBankRtn,
    string CreditorName,
    string CreditorAccountNumber
);

public class Pacs002Builder
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.10";

    public string Build(Pacs002Data data)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "Document",
                new XElement(Ns + "FIToFIPmtStsRpt",
                    new XElement(Ns + "GrpHdr",
                        new XElement(Ns + "MsgId", data.MsgId),
                        new XElement(Ns + "CreDtTm", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss")),
                        new XElement(Ns + "InstgAgt",
                            new XElement(Ns + "FinInstnId",
                                new XElement(Ns + "Nm", data.CreditorBankName)
                            )
                        ),
                        new XElement(Ns + "InstdAgt",
                            new XElement(Ns + "FinInstnId",
                                new XElement(Ns + "Nm", data.DebtorBankName)
                            )
                        )
                    ),
                    new XElement(Ns + "OrgnlGrpInfAndSts",
                        new XElement(Ns + "OrgnlMsgId", data.OriginalMsgId),
                        new XElement(Ns + "GrpSts", data.Status)
                    ),
                    new XElement(Ns + "TxInfAndSts",
                        new XElement(Ns + "OrgnlEndToEndId", data.OriginalEndToEndId),
                        new XElement(Ns + "TxSts", data.Status),
                        new XElement(Ns + "AcctSvcrRef", data.AccountServicerRef),
                        new XElement(Ns + "OrgnlTxRef",
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
                            )
                        )
                    )
                )
            )
        );

        return doc.Declaration + Environment.NewLine + doc.ToString();
    }
}
