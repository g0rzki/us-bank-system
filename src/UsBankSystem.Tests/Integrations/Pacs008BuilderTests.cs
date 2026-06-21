using System.Xml.Linq;
using UsBankSystem.Api.Integrations.FedNow;

namespace UsBankSystem.Tests.Integrations;

public class Pacs008BuilderTests
{
    private static readonly XNamespace Ns = "urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08";

    private static Pacs008Data SampleData() => new(
        MsgId: "MSG-20260617-0001",
        EndToEndId: "E2E-abc123",
        Amount: 1500.50m,
        Currency: "USD",
        DebtorBankName: "Baguette Bank",
        DebtorBankRtn: "040104018",
        DebtorName: "Teto",
        DebtorAccountNumber: "123456789012",
        CreditorBankName: "Leek Bank",
        CreditorBankRtn: "010101012",
        CreditorName: "Miku",
        CreditorAccountNumber: "333999333999",
        Description: "Payment for 31 baguettes"
    );

    [Fact]
    public void Build_ProducesValidXml_WithCorrectNamespace()
    {
        var xml = new Pacs008Builder().Build(SampleData());
        var doc = XDocument.Parse(xml);
        Assert.Equal(Ns, doc.Root!.Name.Namespace);
    }

    [Fact]
    public void Build_ContainsMsgIdAndEndToEndId()
    {
        var xml = new Pacs008Builder().Build(SampleData());
        var doc = XDocument.Parse(xml);
        var msgId = doc.Root!.Element(Ns + "FIToFICstmrCdtTrf")!
            .Element(Ns + "GrpHdr")!.Element(Ns + "MsgId")!.Value;
        Assert.Equal("MSG-20260617-0001", msgId);

        var e2e = doc.Root!.Element(Ns + "FIToFICstmrCdtTrf")!
            .Element(Ns + "CdtTrfTxInf")!
            .Element(Ns + "PmtId")!.Element(Ns + "EndToEndId")!.Value;
        Assert.Equal("E2E-abc123", e2e);
    }

    [Fact]
    public void Build_ContainsCorrectAmount()
    {
        var xml = new Pacs008Builder().Build(SampleData());
        var doc = XDocument.Parse(xml);
        var amt = doc.Root!.Element(Ns + "FIToFICstmrCdtTrf")!
            .Element(Ns + "CdtTrfTxInf")!
            .Element(Ns + "IntrBkSttlmAmt")!;
        Assert.Equal("1500.50", amt.Value);
        Assert.Equal("USD", amt.Attribute("Ccy")!.Value);
    }

    [Fact]
    public void Build_ContainsDebtorAndCreditorBankInfo()
    {
        var xml = new Pacs008Builder().Build(SampleData());
        var doc = XDocument.Parse(xml);
        var txInfo = doc.Root!.Element(Ns + "FIToFICstmrCdtTrf")!
            .Element(Ns + "CdtTrfTxInf")!;

        var debtorRtn = txInfo.Element(Ns + "DbtrAgt")!
            .Element(Ns + "FinInstnId")!.Element(Ns + "ClrSysMmbId")!
            .Element(Ns + "MmbId")!.Value;
        Assert.Equal("040104018", debtorRtn);

        var creditorRtn = txInfo.Element(Ns + "CdtrAgt")!
            .Element(Ns + "FinInstnId")!.Element(Ns + "ClrSysMmbId")!
            .Element(Ns + "MmbId")!.Value;
        Assert.Equal("010101012", creditorRtn);
    }

    [Fact]
    public void Build_RoundTripWithParser()
    {
        var builder = new Pacs008Builder();
        var parser = new Pacs008Parser();
        var data = SampleData();
        var xml = builder.Build(data);
        var parsed = parser.Parse(xml);

        Assert.Equal(data.MsgId, parsed.MsgId);
        Assert.Equal(data.EndToEndId, parsed.EndToEndId);
        Assert.Equal(data.Amount, parsed.Amount);
        Assert.Equal(data.Currency, parsed.Currency);
        Assert.Equal(data.DebtorBankRtn, parsed.DebtorBankRtn);
        Assert.Equal(data.CreditorBankRtn, parsed.CreditorBankRtn);
        Assert.Equal(data.DebtorName, parsed.DebtorName);
        Assert.Equal(data.CreditorName, parsed.CreditorName);
        Assert.Equal(data.Description, parsed.Description);
    }
}
