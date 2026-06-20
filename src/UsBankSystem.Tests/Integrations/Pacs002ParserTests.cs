using UsBankSystem.Api.Integrations.FedNow;

namespace UsBankSystem.Tests.Integrations;

public class Pacs002ParserTests
{
    private static string BuildPacs002Xml(string txSts, string grpSts = "ACCP") => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.002.001.10">
          <FIToFIPmtStsRpt>
            <GrpHdr>
              <MsgId>MSG-20260617-STS-0001</MsgId>
              <CreDtTm>2026-06-17T12:00:00</CreDtTm>
            </GrpHdr>
            <OrgnlGrpInfAndSts>
              <OrgnlMsgId>MSG-20260617-0001</OrgnlMsgId>
              <GrpSts>{grpSts}</GrpSts>
            </OrgnlGrpInfAndSts>
            <TxInfAndSts>
              <OrgnlEndToEndId>E2E-abc123</OrgnlEndToEndId>
              <TxSts>{txSts}</TxSts>
              <AcctSvcrRef>REF-001</AcctSvcrRef>
            </TxInfAndSts>
          </FIToFIPmtStsRpt>
        </Document>
        """;

    [Fact]
    public void Parse_ACCP_Status()
    {
        var result = new Pacs002Parser().Parse(BuildPacs002Xml("ACCP"));
        Assert.Equal("MSG-20260617-0001", result.OriginalMsgId);
        Assert.Equal("E2E-abc123", result.OriginalEndToEndId);
        Assert.Equal("ACCP", result.TransactionStatus);
        Assert.Equal("ACCP", result.GroupStatus);
        Assert.Equal("REF-001", result.AccountServicerRef);
    }

    [Fact]
    public void Parse_RJCT_Status()
    {
        var result = new Pacs002Parser().Parse(BuildPacs002Xml("RJCT", "RJCT"));
        Assert.Equal("RJCT", result.TransactionStatus);
        Assert.Equal("RJCT", result.GroupStatus);
    }

    [Fact]
    public void Parse_PDNG_Status()
    {
        var result = new Pacs002Parser().Parse(BuildPacs002Xml("PDNG", "PDNG"));
        Assert.Equal("PDNG", result.TransactionStatus);
    }

    [Fact]
    public void Parse_BLCK_Status()
    {
        var result = new Pacs002Parser().Parse(BuildPacs002Xml("BLCK", "BLCK"));
        Assert.Equal("BLCK", result.TransactionStatus);
    }

    [Fact]
    public void Parse_ACTC_Status()
    {
        var result = new Pacs002Parser().Parse(BuildPacs002Xml("ACTC", "ACTC"));
        Assert.Equal("ACTC", result.TransactionStatus);
    }

    [Fact]
    public void Parse_InvalidXml_Throws()
    {
        Assert.Throws<System.Xml.XmlException>(() => new Pacs002Parser().Parse("not xml"));
    }

    [Fact]
    public void RoundTrip_BuilderAndParser()
    {
        var builder = new Pacs002Builder();
        var parser = new Pacs002Parser();

        var data = new Pacs002Data(
            MsgId: "MSG-STS-001",
            OriginalMsgId: "MSG-ORIG-001",
            OriginalEndToEndId: "E2E-test",
            Status: "ACCP",
            AccountServicerRef: "REF-SVC-001",
            Amount: 250.00m,
            Currency: "USD",
            DebtorBankName: "Bank A",
            DebtorBankRtn: "111111111",
            DebtorName: "Alice",
            DebtorAccountNumber: "1234",
            CreditorBankName: "Bank B",
            CreditorBankRtn: "222222222",
            CreditorName: "Bob",
            CreditorAccountNumber: "5678"
        );

        var xml = builder.Build(data);
        var result = parser.Parse(xml);

        Assert.Equal("MSG-ORIG-001", result.OriginalMsgId);
        Assert.Equal("E2E-test", result.OriginalEndToEndId);
        Assert.Equal("ACCP", result.TransactionStatus);
        Assert.Equal("REF-SVC-001", result.AccountServicerRef);
    }
}
