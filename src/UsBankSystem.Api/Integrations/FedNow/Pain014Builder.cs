using System.Xml.Linq;

namespace UsBankSystem.Api.Integrations.FedNow;

public record Pain014Data(
    string MsgId,
    string OriginalMsgId,
    string OriginalEndToEndId,
    string Status
);

public class Pain014Builder
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.014.001.07";

    // pain.014 confirms receipt of payment request (pain.013), NOT settlement.
    // Does not change transfer status or book funds.
    public string Build(Pain014Data data)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(Ns + "Document",
                new XElement(Ns + "CdtrPmtActvtnRpt",
                    new XElement(Ns + "GrpHdr",
                        new XElement(Ns + "MsgId", data.MsgId),
                        new XElement(Ns + "CreDtTm", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"))
                    ),
                    new XElement(Ns + "OrgnlGrpInfAndSts",
                        new XElement(Ns + "OrgnlMsgId", data.OriginalMsgId),
                        new XElement(Ns + "GrpSts", data.Status)
                    ),
                    new XElement(Ns + "TxInfAndSts",
                        new XElement(Ns + "StsId", $"STS-{Guid.NewGuid():N}"),
                        new XElement(Ns + "OrgnlEndToEndId", data.OriginalEndToEndId),
                        new XElement(Ns + "TxSts", data.Status),
                        new XElement(Ns + "AccptncDtTm", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss"))
                    )
                )
            )
        );

        return doc.Declaration + Environment.NewLine + doc.ToString();
    }
}
