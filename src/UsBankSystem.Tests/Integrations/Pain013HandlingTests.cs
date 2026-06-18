using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Services.Polling;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;
using UsBankSystem.Tests.Helpers;

namespace UsBankSystem.Tests.Integrations;

public class Pain013HandlingTests
{
    private const string Pain013Template = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:pain.013.001.07">
          <CdtrPmtActvtnReq>
            <GrpHdr>
              <MsgId>MSG-20260618-RFP-0001</MsgId>
              <CreDtTm>2026-06-18T12:00:00</CreDtTm>
            </GrpHdr>
            <PmtInf>
              <PmtInfId>PI-20260618-0001</PmtInfId>
              <PmtMtd>TRA</PmtMtd>
              <Cdtr><Nm>Miku</Nm></Cdtr>
              <CdtrAcct><Id><Othr><Id>333999333999</Id><SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm></Othr></Id></CdtrAcct>
              <CdtrAgt><FinInstnId><ClrSysMmbId><nm>Leek Bank</nm><MmbId>010101012</MmbId></ClrSysMmbId></FinInstnId></CdtrAgt>
              <DrctDbtTxInf>
                <PmtId><EndToEndId>E2E-20260618-0001</EndToEndId></PmtId>
                <InstdAmt Ccy="USD">100.00</InstdAmt>
                <Dbtr><Nm>Teto</Nm></Dbtr>
                <DbtrAcct><Id><Othr><Id>{0}</Id><SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm></Othr></Id></DbtrAcct>
                <DbtrAgt><FinInstnId><ClrSysMmbId><nm>Baguette Bank</nm><MmbId>040104018</MmbId></ClrSysMmbId></FinInstnId></DbtrAgt>
                <RmtInf><Ustrd>Test payment</Ustrd></RmtInf>
              </DrctDbtTxInf>
            </PmtInf>
          </CdtrPmtActvtnReq>
        </Document>
        """;

    private static DbContextOptions<AppDbContext> CreateDbOptions() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private static AppDbContext CreateDb(DbContextOptions<AppDbContext> options) => new(options);

    private static IOptions<PaymentSessionConfig> CreatePaymentConfig() =>
        Options.Create(new PaymentSessionConfig
        {
            Ach = new AchConfig { BatchWindowMinutes = 1, CutoffHour = 23 },
            Rtp = new TimeoutConfig { TimeoutSeconds = 10 },
            FedNow = new FedNowConfig { TimeoutSeconds = 10, PollIntervalSeconds = 1, BankRtn = "040104018", BankLegalName = "Baguette Bank" },
            Swift = new SwiftConfig { TimeoutSeconds = 10 }
        });

    private static FedNowMqGateway CreateMqGateway(HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpClient(new MockHttpMessageHandler(status, """{"status":"sent"}"""))
            { BaseAddress = new Uri("http://localhost:8770") },
            NullLogger<FedNowMqGateway>.Instance);

    private static FedNowPollingService CreateService(DbContextOptions<AppDbContext> dbOptions, HttpStatusCode mqStatus = HttpStatusCode.OK)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new AppDbContext(dbOptions));
        services.AddScoped(_ => CreateMqGateway(mqStatus));
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new FedNowPollingService(
            scopeFactory,
            new Pacs002Parser(),
            new Pacs008Parser(),
            new Pain013Parser(),
            new Pacs008Builder(),
            new Pacs002Builder(),
            new Pain014Builder(),
            CreatePaymentConfig(),
            NullLogger<FedNowPollingService>.Instance);
    }

    [Fact]
    public async Task HandlePain013_PersistsTransferBeforeSendingAccp()
    {
        var options = CreateDbOptions();
        using var setupDb = CreateDb(options);
        var user = new User { Id = Guid.NewGuid(), Email = "debtor@example.com", PasswordHash = "hash", FirstName = "Teto", LastName = "Debtor", Status = "active", CreatedAt = DateTime.UtcNow };
        setupDb.Users.Add(user);
        var account = new Account { Id = Guid.NewGuid(), UserId = user.Id, AccountNumber = "123456789012", Type = "checking", Balance = 500m, ReservedBalance = 0m, Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow };
        setupDb.Accounts.Add(account);
        await setupDb.SaveChangesAsync();

        var service = CreateService(options);
        var xml = string.Format(Pain013Template, account.AccountNumber);

        await service.ProcessMessageAsync(Encoding.UTF8.GetBytes(xml), CancellationToken.None);

        using var assertDb = CreateDb(options);
        var transfer = await assertDb.Transfers.FirstOrDefaultAsync();
        Assert.NotNull(transfer);
        Assert.Equal("pending", transfer.Status);
        Assert.Equal("E2E-20260618-0001", transfer.ExternalReferenceId);
        Assert.Equal(account.Id, transfer.FromAccountId);
        Assert.Equal(100m, transfer.Amount);
    }

    [Fact]
    public async Task HandlePain013_NullUser_SendsRjctAndNoTransfer()
    {
        var options = CreateDbOptions();
        using var setupDb = CreateDb(options);
        var account = new Account { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), AccountNumber = "123456789012", Type = "checking", Balance = 500m, ReservedBalance = 0m, Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow };
        setupDb.Accounts.Add(account);
        await setupDb.SaveChangesAsync();

        var service = CreateService(options);
        var xml = string.Format(Pain013Template, account.AccountNumber);

        await service.ProcessMessageAsync(Encoding.UTF8.GetBytes(xml), CancellationToken.None);

        using var assertDb = CreateDb(options);
        Assert.Equal(0, await assertDb.Transfers.CountAsync());
        var reloaded = await assertDb.Accounts.FindAsync(account.Id);
        Assert.Equal(500m, reloaded!.Balance);
        Assert.Equal(0m, reloaded.ReservedBalance);
    }

    private const string Pacs008Template = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Document xmlns="urn:iso:std:iso:20022:tech:xsd:pacs.008.001.08">
          <FIToFICstmrCdtTrf>
            <GrpHdr>
              <MsgId>MSG-20260618-0001</MsgId>
              <CreDtTm>2026-06-18T12:00:00</CreDtTm>
            </GrpHdr>
            <CdtTrfTxInf>
              <PmtId><EndToEndId>E2E-20260618-INCOMING-001</EndToEndId></PmtId>
              <IntrBkSttlmAmt Ccy="USD">200.00</IntrBkSttlmAmt>
              <DbtrAgt><FinInstnId><ClrSysMmbId><nm>Leek Bank</nm><MmbId>010101012</MmbId></ClrSysMmbId></FinInstnId></DbtrAgt>
              <Dbtr><Nm>External Sender</Nm></Dbtr>
              <DbtrAcct><Id><Othr><Id>999888777</Id><SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm></Othr></Id></DbtrAcct>
              <CdtrAgt><FinInstnId><ClrSysMmbId><nm>Baguette Bank</nm><MmbId>040104018</MmbId></ClrSysMmbId></FinInstnId></CdtrAgt>
              <Cdtr><Nm>Local Receiver</Nm></Cdtr>
              <CdtrAcct><Id><Othr><Id>{0}</Id><SchmeNm><Prtry>US_ACCT</Prtry></SchmeNm></Othr></Id></CdtrAcct>
              <RmtInf><Ustrd>Incoming payment</Ustrd></RmtInf>
            </CdtTrfTxInf>
          </FIToFICstmrCdtTrf>
        </Document>
        """;

    [Fact]
    public async Task HandleIncomingPacs008_SetsCorrectDirection()
    {
        var options = CreateDbOptions();
        using var setupDb = CreateDb(options);
        var user = new User { Id = Guid.NewGuid(), Email = "receiver@example.com", PasswordHash = "hash", FirstName = "Local", LastName = "Receiver", Status = "active", CreatedAt = DateTime.UtcNow };
        setupDb.Users.Add(user);
        var account = new Account { Id = Guid.NewGuid(), UserId = user.Id, AccountNumber = "123456789012", Type = "checking", Balance = 500m, ReservedBalance = 0m, Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow };
        setupDb.Accounts.Add(account);
        await setupDb.SaveChangesAsync();

        var service = CreateService(options);
        var xml = string.Format(Pacs008Template, account.AccountNumber);

        await service.ProcessMessageAsync(Encoding.UTF8.GetBytes(xml), CancellationToken.None);

        using var assertDb = CreateDb(options);
        var transfer = await assertDb.Transfers.FirstOrDefaultAsync();
        Assert.NotNull(transfer);
        Assert.Null(transfer.FromAccountId);
        Assert.Equal(account.Id, transfer.ToAccountId);
        Assert.Equal("completed", transfer.Status);
        Assert.Equal(200m, transfer.Amount);
    }
}
