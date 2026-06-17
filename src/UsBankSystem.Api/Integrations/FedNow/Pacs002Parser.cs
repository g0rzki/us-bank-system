using System.Xml.Linq;

namespace UsBankSystem.Api.Integrations.FedNow;

public record Pacs002Result(
    string OriginalMsgId,
    string OriginalEndToEndId,
    string GroupStatus,
    string TransactionStatus,
    string? AccountServicerRef
);

public class Pacs002Parser
{
    public static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pacs.002.001.10";

    public Pacs002Result Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new InvalidOperationException("Empty XML document");

        var report = root.Element(Ns + "FIToFIPmtStsRpt")
            ?? throw new InvalidOperationException("Missing FIToFIPmtStsRpt element");

        var grpInfo = report.Element(Ns + "OrgnlGrpInfAndSts")
            ?? throw new InvalidOperationException("Missing OrgnlGrpInfAndSts element");

        var originalMsgId = grpInfo.Element(Ns + "OrgnlMsgId")?.Value
            ?? throw new InvalidOperationException("Missing OrgnlMsgId");

        var groupStatus = grpInfo.Element(Ns + "GrpSts")?.Value
            ?? throw new InvalidOperationException("Missing GrpSts");

        var txInfo = report.Element(Ns + "TxInfAndSts")
            ?? throw new InvalidOperationException("Missing TxInfAndSts element");

        var originalEndToEndId = txInfo.Element(Ns + "OrgnlEndToEndId")?.Value
            ?? throw new InvalidOperationException("Missing OrgnlEndToEndId");

        var txStatus = txInfo.Element(Ns + "TxSts")?.Value
            ?? throw new InvalidOperationException("Missing TxSts");

        var acctSvcrRef = txInfo.Element(Ns + "AcctSvcrRef")?.Value;

        return new Pacs002Result(originalMsgId, originalEndToEndId, groupStatus, txStatus, acctSvcrRef);
    }
}
