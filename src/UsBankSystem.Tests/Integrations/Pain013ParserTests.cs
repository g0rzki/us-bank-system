using UsBankSystem.Api.Integrations.FedNow;

namespace UsBankSystem.Tests.Integrations;

public class Pain013ParserTests
{
    private const string SamplePain013 = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:pain.013.001.07">
          <CdtrPmtActvtnReq>
            <GrpHdr>
              <MsgId>MSG-20260617-RFP-0001</MsgId>
              <CreDtTm>2026-06-17T12:00:00</CreDtTm>
            </GrpHdr>
            <PmtInf>
              <PmtInfId>PI-20260617-0001</PmtInfId>
              <PmtMtd>TRA</PmtMtd>
              <Cdtr>
                <Nm>Miku</Nm>
              </Cdtr>
              <CdtrAcct>
                <Id>
                  <Othr>
                    <Id>333999333999</Id>
                    <SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm>
                  </Othr>
                </Id>
              </CdtrAcct>
              <CdtrAgt>
                <FinInstnId>
                  <ClrSysMmbId>
                    <nm>Leek Bank</nm>
                    <MmbId>010101012</MmbId>
                  </ClrSysMmbId>
                </FinInstnId>
              </CdtrAgt>
              <DrctDbtTxInf>
                <PmtId>
                  <EndToEndId>E2E-20260617-0001</EndToEndId>
                </PmtId>
                <InstdAmt Ccy="USD">1500.50</InstdAmt>
                <Dbtr>
                  <Nm>Teto</Nm>
                </Dbtr>
                <DbtrAcct>
                  <Id>
                    <Othr>
                      <Id>123456789012</Id>
                      <SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm>
                    </Othr>
                  </Id>
                </DbtrAcct>
                <DbtrAgt>
                  <FinInstnId>
                    <ClrSysMmbId>
                      <nm>Baguette Bank</nm>
                      <MmbId>040104018</MmbId>
                    </ClrSysMmbId>
                  </FinInstnId>
                </DbtrAgt>
                <RmtInf>
                  <Ustrd>Payment for 31 baguettes</Ustrd>
                </RmtInf>
              </DrctDbtTxInf>
            </PmtInf>
          </CdtrPmtActvtnReq>
        </Document>
        """;

    [Fact]
    public void Parse_ExtractsAllFields()
    {
        var result = new Pain013Parser().Parse(SamplePain013);
        Assert.Equal("MSG-20260617-RFP-0001", result.MsgId);
        Assert.Equal("PI-20260617-0001", result.PmtInfId);
        Assert.Equal("E2E-20260617-0001", result.EndToEndId);
        Assert.Equal(1500.50m, result.Amount);
        Assert.Equal("USD", result.Currency);
        Assert.Equal("Miku", result.CreditorName);
        Assert.Equal("333999333999", result.CreditorAccountNumber);
        Assert.Equal("Leek Bank", result.CreditorBankName);
        Assert.Equal("010101012", result.CreditorBankRtn);
        Assert.Equal("Teto", result.DebtorName);
        Assert.Equal("123456789012", result.DebtorAccountNumber);
        Assert.Equal("Baguette Bank", result.DebtorBankName);
        Assert.Equal("040104018", result.DebtorBankRtn);
        Assert.Equal("Payment for 31 baguettes", result.Description);
    }

    [Fact]
    public void Parse_InvalidXml_Throws()
    {
        Assert.Throws<System.Xml.XmlException>(() => new Pain013Parser().Parse("not xml"));
    }

    [Fact]
    public void Parse_MissingEndToEndId_Throws()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:pain.013.001.07">
              <CdtrPmtActvtnReq>
                <GrpHdr><MsgId>MSG-001</MsgId><CreDtTm>2026-06-17T12:00:00</CreDtTm></GrpHdr>
                <PmtInf>
                  <PmtInfId>PI-001</PmtInfId>
                  <DrctDbtTxInf>
                    <PmtId></PmtId>
                    <InstdAmt Ccy="USD">100</InstdAmt>
                  </DrctDbtTxInf>
                </PmtInf>
              </CdtrPmtActvtnReq>
            </Document>
            """;
        Assert.Throws<InvalidOperationException>(() => new Pain013Parser().Parse(xml));
    }
}
