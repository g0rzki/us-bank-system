using System.Globalization;
using System.Xml.Linq;

namespace UsBankSystem.Api.Integrations.FedNow;

public record IncomingPain013(
    string MsgId,
    string PmtInfId,
    string EndToEndId,
    decimal Amount,
    string Currency,
    string CreditorName,
    string CreditorAccountNumber,
    string CreditorBankName,
    string CreditorBankRtn,
    string DebtorName,
    string DebtorAccountNumber,
    string DebtorBankName,
    string DebtorBankRtn,
    string? Description
);

public class Pain013Parser
{
    public static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.013.001.07";

    public IncomingPain013 Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new InvalidOperationException("Empty XML document");

        var req = root.Element(Ns + "CdtrPmtActvtnReq")
            ?? throw new InvalidOperationException("Missing CdtrPmtActvtnReq element");

        var grpHdr = req.Element(Ns + "GrpHdr")
            ?? throw new InvalidOperationException("Missing GrpHdr element");

        var msgId = grpHdr.Element(Ns + "MsgId")?.Value
            ?? throw new InvalidOperationException("Missing MsgId");

        var pmtInf = req.Element(Ns + "PmtInf")
            ?? throw new InvalidOperationException("Missing PmtInf element");

        var pmtInfId = pmtInf.Element(Ns + "PmtInfId")?.Value
            ?? throw new InvalidOperationException("Missing PmtInfId");

        var creditorName = pmtInf.Element(Ns + "Cdtr")?.Element(Ns + "Nm")?.Value ?? string.Empty;
        var creditorAcct = pmtInf.Element(Ns + "CdtrAcct")?.Element(Ns + "Id")?.Element(Ns + "Othr")?.Element(Ns + "Id")?.Value ?? string.Empty;

        var creditorAgt = pmtInf.Element(Ns + "CdtrAgt")?.Element(Ns + "FinInstnId")?.Element(Ns + "ClrSysMmbId");
        var creditorBankName = creditorAgt?.Element(Ns + "nm")?.Value ?? string.Empty;
        var creditorBankRtn = creditorAgt?.Element(Ns + "MmbId")?.Value ?? string.Empty;

        var ddTxInf = pmtInf.Element(Ns + "DrctDbtTxInf")
            ?? throw new InvalidOperationException("Missing DrctDbtTxInf element");

        var endToEndId = ddTxInf.Element(Ns + "PmtId")?.Element(Ns + "EndToEndId")?.Value
            ?? throw new InvalidOperationException("Missing EndToEndId");

        var amtElement = ddTxInf.Element(Ns + "InstdAmt")
            ?? throw new InvalidOperationException("Missing InstdAmt");
        var amount = decimal.Parse(amtElement.Value, CultureInfo.InvariantCulture);
        var currency = amtElement.Attribute("Ccy")?.Value ?? "USD";

        var debtorName = ddTxInf.Element(Ns + "Dbtr")?.Element(Ns + "Nm")?.Value ?? string.Empty;
        var debtorAcct = ddTxInf.Element(Ns + "DbtrAcct")?.Element(Ns + "Id")?.Element(Ns + "Othr")?.Element(Ns + "Id")?.Value ?? string.Empty;

        var debtorAgt = ddTxInf.Element(Ns + "DbtrAgt")?.Element(Ns + "FinInstnId")?.Element(Ns + "ClrSysMmbId");
        var debtorBankName = debtorAgt?.Element(Ns + "nm")?.Value ?? string.Empty;
        var debtorBankRtn = debtorAgt?.Element(Ns + "MmbId")?.Value ?? string.Empty;

        var description = ddTxInf.Element(Ns + "RmtInf")?.Element(Ns + "Ustrd")?.Value;

        return new IncomingPain013(
            msgId, pmtInfId, endToEndId, amount, currency,
            creditorName, creditorAcct, creditorBankName, creditorBankRtn,
            debtorName, debtorAcct, debtorBankName, debtorBankRtn,
            description
        );
    }
}
