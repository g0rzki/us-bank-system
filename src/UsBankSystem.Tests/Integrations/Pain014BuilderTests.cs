using System.Xml.Linq;
using UsBankSystem.Api.Integrations.FedNow;

namespace UsBankSystem.Tests.Integrations;

public class Pain014BuilderTests
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pain.014.001.07";

    [Fact]
    public void Build_ProducesValidXml_WithCorrectNamespace()
    {
        var data = new Pain014Data(
            MsgId: "MSG-RPT-001",
            OriginalMsgId: "MSG-RFP-001",
            OriginalEndToEndId: "E2E-test",
            Status: "ACCP"
        );

        var xml = new Pain014Builder().Build(data);
        var doc = XDocument.Parse(xml);
        Assert.Equal(Ns, doc.Root!.Name.Namespace);
    }

    [Fact]
    public void Build_ContainsOriginalMsgIdAndEndToEndId()
    {
        var data = new Pain014Data(
            MsgId: "MSG-RPT-001",
            OriginalMsgId: "MSG-RFP-001",
            OriginalEndToEndId: "E2E-test",
            Status: "ACCP"
        );

        var xml = new Pain014Builder().Build(data);
        var doc = XDocument.Parse(xml);
        var report = doc.Root!.Element(Ns + "CdtrPmtActvtnRpt")!;

        var originalMsgId = report.Element(Ns + "OrgnlGrpInfAndSts")!
            .Element(Ns + "OrgnlMsgId")!.Value;
        Assert.Equal("MSG-RFP-001", originalMsgId);

        var e2e = report.Element(Ns + "TxInfAndSts")!
            .Element(Ns + "OrgnlEndToEndId")!.Value;
        Assert.Equal("E2E-test", e2e);
    }

    [Fact]
    public void Build_ACCP_Status()
    {
        var data = new Pain014Data(
            MsgId: "MSG-RPT-001",
            OriginalMsgId: "MSG-RFP-001",
            OriginalEndToEndId: "E2E-test",
            Status: "ACCP"
        );

        var xml = new Pain014Builder().Build(data);
        var doc = XDocument.Parse(xml);
        var grpSts = doc.Root!.Element(Ns + "CdtrPmtActvtnRpt")!
            .Element(Ns + "OrgnlGrpInfAndSts")!
            .Element(Ns + "GrpSts")!.Value;
        Assert.Equal("ACCP", grpSts);

        var txSts = doc.Root!.Element(Ns + "CdtrPmtActvtnRpt")!
            .Element(Ns + "TxInfAndSts")!
            .Element(Ns + "TxSts")!.Value;
        Assert.Equal("ACCP", txSts);
    }

    [Fact]
    public void Build_RJCT_Status()
    {
        var data = new Pain014Data(
            MsgId: "MSG-RPT-002",
            OriginalMsgId: "MSG-RFP-002",
            OriginalEndToEndId: "E2E-test2",
            Status: "RJCT"
        );

        var xml = new Pain014Builder().Build(data);
        var doc = XDocument.Parse(xml);
        var txSts = doc.Root!.Element(Ns + "CdtrPmtActvtnRpt")!
            .Element(Ns + "TxInfAndSts")!
            .Element(Ns + "TxSts")!.Value;
        Assert.Equal("RJCT", txSts);
    }
}
