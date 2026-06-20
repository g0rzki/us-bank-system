using System.Globalization;
using System.Xml.Linq;

namespace UsBankSystem.Api.Integrations.FedNow;

public record IncomingPacs008(
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

public class Pacs008Parser
{
    public static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08";

    public IncomingPacs008 Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new InvalidOperationException("Empty XML document");

        var transfer = root.Element(Ns + "FIToFICstmrCdtTrf")
            ?? throw new InvalidOperationException("Missing FIToFICstmrCdtTrf element");

        var grpHdr = transfer.Element(Ns + "GrpHdr")
            ?? throw new InvalidOperationException("Missing GrpHdr element");

        var msgId = grpHdr.Element(Ns + "MsgId")?.Value
            ?? throw new InvalidOperationException("Missing MsgId");

        var txInfo = transfer.Element(Ns + "CdtTrfTxInf")
            ?? throw new InvalidOperationException("Missing CdtTrfTxInf element");

        var endToEndId = txInfo.Element(Ns + "PmtId")?.Element(Ns + "EndToEndId")?.Value
            ?? throw new InvalidOperationException("Missing EndToEndId");

        var amtElement = txInfo.Element(Ns + "IntrBkSttlmAmt")
            ?? throw new InvalidOperationException("Missing IntrBkSttlmAmt");
        var amount = decimal.Parse(amtElement.Value, CultureInfo.InvariantCulture);
        var currency = amtElement.Attribute("Ccy")?.Value ?? "USD";

        var debtorAgt = txInfo.Element(Ns + "DbtrAgt")?.Element(Ns + "FinInstnId")?.Element(Ns + "ClrSysMmbId");
        var debtorBankName = debtorAgt?.Element(Ns + "nm")?.Value ?? string.Empty;
        var debtorBankRtn = debtorAgt?.Element(Ns + "MmbId")?.Value ?? string.Empty;

        var debtorName = txInfo.Element(Ns + "Dbtr")?.Element(Ns + "Nm")?.Value ?? string.Empty;
        var debtorAcct = txInfo.Element(Ns + "DbtrAcct")?.Element(Ns + "Id")?.Element(Ns + "Othr")?.Element(Ns + "Id")?.Value ?? string.Empty;

        var creditorAgt = txInfo.Element(Ns + "CdtrAgt")?.Element(Ns + "FinInstnId")?.Element(Ns + "ClrSysMmbId");
        var creditorBankName = creditorAgt?.Element(Ns + "nm")?.Value ?? string.Empty;
        var creditorBankRtn = creditorAgt?.Element(Ns + "MmbId")?.Value ?? string.Empty;

        var creditorName = txInfo.Element(Ns + "Cdtr")?.Element(Ns + "Nm")?.Value ?? string.Empty;
        var creditorAcct = txInfo.Element(Ns + "CdtrAcct")?.Element(Ns + "Id")?.Element(Ns + "Othr")?.Element(Ns + "Id")?.Value ?? string.Empty;

        var description = txInfo.Element(Ns + "RmtInf")?.Element(Ns + "Ustrd")?.Value;

        return new IncomingPacs008(
            msgId, endToEndId, amount, currency,
            debtorBankName, debtorBankRtn, debtorName, debtorAcct,
            creditorBankName, creditorBankRtn, creditorName, creditorAcct,
            description
        );
    }
}
