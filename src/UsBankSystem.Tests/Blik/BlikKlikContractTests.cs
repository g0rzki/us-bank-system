using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Models.Requests;
using UsBankSystem.Api.Services;
using UsBankSystem.Core.Domain.Blik;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Blik;

/// <summary>
/// Testy kontraktowe integracji us-bank-system vs KLIK-payments.
/// Weryfikuja ze KlikApiClient wysyla payloady zgodne z tym czego oczekuje
/// KLIK-payments, i ze BlikService poprawnie przetwarza webhooki od KLIK.
/// </summary>
public class BlikKlikContractTests
{
    private static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private BlikService CreateService(AppDbContext db, IKlikApiClient? client = null) =>
        new(db, client ?? new SpyKlikApiClient(), NullLogger<BlikService>.Instance);

    private async Task<(AppDbContext db, Guid userId, Guid accountId)> SetupUser(decimal balance = 500m)
    {
        var db = CreateDb();
        var user = new User
        {
            Id = Guid.NewGuid(), Email = "test@example.com", PasswordHash = "hash",
            FirstName = "Test", LastName = "User", Status = "active", CreatedAt = DateTime.UtcNow
        };
        var account = new Account
        {
            Id = Guid.NewGuid(), UserId = user.Id, AccountNumber = "1000000001",
            Type = "checking", Balance = balance, ReservedBalance = 0,
            Currency = "USD", Status = "active", CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return (db, user.Id, account.Id);
    }

    private static HttpResponseMessage JsonOk(object payload) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, SnakeCase), Encoding.UTF8, "application/json")
        };

    // ──────────────────────────────────────────────────────────────────────────
    // Kontrakt: KlikApiClient -> POST /api/v1/codes/generate
    // KLIK-payments CodeGenerateRequestSerializer wymaga: user_id + zone
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateCode_RequestToKlik_ContainsUserIdAndZoneFields()
    {
        var handler = new SpyHandler((req, _) =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var doc = JsonDocument.Parse(body).RootElement;
            Assert.True(doc.TryGetProperty("user_id", out var uid), "Brak pola user_id");
            Assert.False(string.IsNullOrEmpty(uid.GetString()), "user_id jest pusty");
            Assert.True(doc.TryGetProperty("zone", out var zone), "Brak pola zone");
            Assert.Equal("US", zone.GetString());
            return Task.FromResult(JsonOk(new
            {
                code = "654321",
                expires_in = 120,
                expires_at = DateTimeOffset.UtcNow.AddSeconds(120).ToString("O")
            }));
        });

        var client = BuildKlikClient(handler, "dev-key");
        var result = await client.GenerateCodeAsync("user-abc");

        Assert.True(result.Success);
        Assert.Equal("654321", result.Code);
    }

    [Fact]
    public async Task GenerateCode_KlikReturns422_ClientReturnsFailureWithErrorCode()
    {
        // KLIK-payments zwraca 422 przy zone mismatch. KlikApiClient musi przetlumaczyc to na Success=false.
        var handler = new SpyHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.UnprocessableEntity)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":\"422_ZONE_MISMATCH\",\"message\":\"Strefa US != PL\"}}",
                    Encoding.UTF8, "application/json")
            }));

        var client = BuildKlikClient(handler, "dev-key");
        var result = await client.GenerateCodeAsync("user-abc");

        Assert.False(result.Success);
        Assert.Contains("422_ZONE_MISMATCH", result.Error);
    }

    [Fact]
    public async Task GenerateCode_KlikReturns401_ClientReturnsFailure()
    {
        // KLIK-payments zwraca 401 gdy X-KLIK-Bank-Api-Key jest nieznany.
        var handler = new SpyHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":\"401_UNAUTHORIZED\",\"message\":\"Unknown API key\"}}",
                    Encoding.UTF8, "application/json")
            }));

        var client = BuildKlikClient(handler, "wrong-key");
        var result = await client.GenerateCodeAsync("user-abc");

        Assert.False(result.Success);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Kontrakt: KlikApiClient -> POST /api/v1/payments/confirm
    // KLIK-payments PaymentConfirmRequestSerializer: { transaction_id, status, reject_reason? }
    // Walidacja: REJECTED wymaga reject_reason; ACCEPTED nie moze miec reject_reason.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmPayment_Accepted_SendsStatusAcceptedWithNullOrNoRejectReason()
    {
        // KLIK waliduje: przy ACCEPTED reject_reason musi byc pusty.
        // KlikApiClient wysyla null -> sprawdzamy ze pole jest null lub nieobecne.
        string? capturedBody = null;
        var handler = new SpyHandler((req, _) =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Task.FromResult(JsonOk(new
            {
                transaction_id = "tx-001",
                status = "COMPLETED",
                amount_gross = "150.00",
                klik_fee = "0.45",
                agent_fee = "1.50",
                merchant_net = "148.05",
                currency = "USD",
                reject_reason = "",
                completed_at = "2026-06-10T12:00:00Z"
            }));
        });

        var client = BuildKlikClient(handler, "dev-key");
        await client.ConfirmPaymentAsync("tx-001", accepted: true, rejectReason: null);

        Assert.NotNull(capturedBody);
        var doc = JsonDocument.Parse(capturedBody!).RootElement;
        Assert.Equal("ACCEPTED", doc.GetProperty("status").GetString());

        // Przy ACCEPTED reject_reason powinno byc null lub nieobecne
        if (doc.TryGetProperty("reject_reason", out var rr))
            Assert.True(rr.ValueKind == JsonValueKind.Null || rr.GetString() == "",
                "reject_reason przy ACCEPTED nie powinno miec wartosci");
    }

    [Fact]
    public async Task ConfirmPayment_InsufficientFunds_SendsInsuficientFundsEnumValue()
    {
        // KLIK RejectReason enum: INSUFFICIENT_FUNDS, USER_DECLINED, PIN_FAILED, AML_BLOCK, OTHER.
        // BlikService wysyla "INSUFFICIENT_FUNDS" — musi pasowac do KLIK enum.
        string? capturedRejectReason = null;
        var handler = new SpyHandler((req, _) =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var doc = JsonDocument.Parse(body).RootElement;
            capturedRejectReason = doc.TryGetProperty("reject_reason", out var rr) ? rr.GetString() : null;
            return Task.FromResult(JsonOk(new
            {
                transaction_id = "tx-002",
                status = "REJECTED",
                amount_gross = "50.00",
                klik_fee = (object?)null,
                agent_fee = (object?)null,
                merchant_net = (object?)null,
                currency = "USD",
                reject_reason = "INSUFFICIENT_FUNDS",
                completed_at = (object?)null
            }));
        });

        var client = BuildKlikClient(handler, "dev-key");
        await client.ConfirmPaymentAsync("tx-002", accepted: false, rejectReason: "INSUFFICIENT_FUNDS");

        var validKlikRejectReasons = new[] { "INSUFFICIENT_FUNDS", "USER_DECLINED", "PIN_FAILED", "AML_BLOCK", "OTHER" };
        Assert.Contains(capturedRejectReason, validKlikRejectReasons);
        Assert.Equal("INSUFFICIENT_FUNDS", capturedRejectReason);
    }

    [Fact]
    public async Task ConfirmPayment_UserDeclined_SendsUserDeclinedEnumValue()
    {
        string? capturedRejectReason = null;
        var handler = new SpyHandler((req, _) =>
        {
            var body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var doc = JsonDocument.Parse(body).RootElement;
            capturedRejectReason = doc.TryGetProperty("reject_reason", out var rr) ? rr.GetString() : null;
            return Task.FromResult(JsonOk(new
            {
                transaction_id = "tx-003",
                status = "REJECTED",
                amount_gross = "50.00",
                klik_fee = (object?)null,
                agent_fee = (object?)null,
                merchant_net = (object?)null,
                currency = "USD",
                reject_reason = "USER_DECLINED",
                completed_at = (object?)null
            }));
        });

        var client = BuildKlikClient(handler, "dev-key");
        await client.ConfirmPaymentAsync("tx-003", accepted: false, rejectReason: "USER_DECLINED");

        Assert.Equal("USER_DECLINED", capturedRejectReason);
    }

    [Fact]
    public async Task ConfirmPayment_KlikReturns409_ClientReturnsFailure()
    {
        // KLIK zwraca 409 gdy tx jest juz zamknieta (ACCEPTED po REJECTED lub TIMEOUT).
        var handler = new SpyHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    "{\"error\":{\"code\":\"409_TRANSACTION_ALREADY_CLOSED\",\"message\":\"Transaction is already TIMEOUT\"}}",
                    Encoding.UTF8, "application/json")
            }));

        var client = BuildKlikClient(handler, "dev-key");
        var result = await client.ConfirmPaymentAsync("tx-timeout", accepted: true, rejectReason: null);

        Assert.False(result.Success);
        Assert.Contains("409_TRANSACTION_ALREADY_CLOSED", result.Error);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Kontrakt: KlikApiClient nagłówki
    // KLIK-payments wymaga X-KLIK-Bank-Api-Key i Idempotency-Key
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task KlikApiClient_SendsXKlikApiKeyHeader()
    {
        string? capturedApiKey = null;
        var handler = new SpyHandler((req, _) =>
        {
            if (req.Headers.TryGetValues("X-KLIK-Bank-Api-Key", out var vals))
                capturedApiKey = vals.FirstOrDefault();
            else if (req.Headers.NonValidated.TryGetValues("X-KLIK-Bank-Api-Key", out var nv))
                capturedApiKey = nv.FirstOrDefault();
            return Task.FromResult(JsonOk(new
            {
                code = "999888",
                expires_in = 120,
                expires_at = DateTimeOffset.UtcNow.AddSeconds(120).ToString("O")
            }));
        });

        var client = BuildKlikClient(handler, "my-secret-key");
        await client.GenerateCodeAsync("user-x");

        Assert.Equal("my-secret-key", capturedApiKey);
    }

    [Fact]
    public async Task KlikApiClient_EachRequestGetsUniqueIdempotencyKey()
    {
        // KLIK @idempotent_endpoint wymaga naglowka Idempotency-Key.
        // Kazde wywolanie musi dostac unikalny klucz.
        var capturedKeys = new List<string>();
        var handler = new SpyHandler((req, _) =>
        {
            req.Headers.TryGetValues("Idempotency-Key", out var vals);
            var key = vals?.FirstOrDefault();
            if (key != null) capturedKeys.Add(key);
            return Task.FromResult(JsonOk(new
            {
                code = "111222",
                expires_in = 120,
                expires_at = DateTimeOffset.UtcNow.AddSeconds(120).ToString("O")
            }));
        });

        var client = BuildKlikClient(handler, "key");
        await client.GenerateCodeAsync("user-y");
        await client.GenerateCodeAsync("user-z");

        Assert.Equal(2, capturedKeys.Count);
        Assert.Equal(2, capturedKeys.Distinct().Count());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Kontrakt: KlikApiClient odpornosc na bledy sieci
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateCode_NetworkTimeout_ReturnsFailureDoesNotThrow()
    {
        var handler = new SpyHandler((_, _) =>
            throw new HttpRequestException("Connection refused"));

        var client = BuildKlikClient(handler, "key");
        var result = await client.GenerateCodeAsync("user-abc");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ConfirmPayment_NetworkTimeout_ReturnsFailureDoesNotThrow()
    {
        var handler = new SpyHandler((_, _) =>
            throw new HttpRequestException("Connection refused"));

        var client = BuildKlikClient(handler, "key");
        var result = await client.ConfirmPaymentAsync("tx-net", accepted: true, rejectReason: null);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task GenerateCode_KlikReturns502EmptyBody_ReturnsFailureDoesNotThrow()
    {
        var handler = new SpyHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("", Encoding.UTF8, "application/json")
            }));

        var client = BuildKlikClient(handler, "key");
        var result = await client.GenerateCodeAsync("user-abc");

        Assert.False(result.Success);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Kontrakt: webhook od KLIK -> BlikService.HandleAuthorizeWebhookAsync
    // authorize_webhook_task wysyla: amount jako STRING ("150.00"),
    // expiry_time jako ISO 8601 z offset, is_on_us bool
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WebhookAuthorize_AmountFromKlikAsDecimal_ParsedCorrectly()
    {
        // authorize_webhook_task (Python) wysyla amount = str(Decimal) -> "150.00".
        // DecimalOrStringJsonConverter musi to poprawnie zdeserializowac.
        var (db, userId, accountId) = await SetupUser();
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90).UtcDateTime, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var payload = new KlikWebhookAuthorizeRequest
        {
            TransactionId = Guid.NewGuid().ToString(),
            UserId = userId.ToString(),
            Amount = 150.00m,
            Currency = "USD",
            MerchantName = "Sklep Test",
            IsOnUs = false,
            ExpiryTime = DateTime.UtcNow.AddSeconds(120),
            Zone = "US"
        };

        var svc = CreateService(db);
        await svc.HandleAuthorizeWebhookAsync(payload);

        var auth = await db.BlikAuthorizations.FirstAsync();
        Assert.Equal(150.00m, auth.Amount);
    }

    [Fact]
    public async Task WebhookAuthorize_ExpiryTimeAsUtcOffset_StoredCloseToUtcDateTime()
    {
        // authorize_webhook_task wysyla expiry_time jako ISO z +00:00.
        // BlikAuthorization.ExpiryTime to DateTime — konwersja musi zachowac UTC.
        var (db, userId, accountId) = await SetupUser();
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90).UtcDateTime, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var expectedExpiry = DateTime.UtcNow.AddSeconds(90);
        var payload = new KlikWebhookAuthorizeRequest
        {
            TransactionId = Guid.NewGuid().ToString(),
            UserId = userId.ToString(),
            Amount = 50m,
            Currency = "USD",
            MerchantName = "Shop",
            IsOnUs = false,
            ExpiryTime = expectedExpiry,
            Zone = "US"
        };

        var svc = CreateService(db);
        await svc.HandleAuthorizeWebhookAsync(payload);

        var auth = await db.BlikAuthorizations.FirstAsync();
        // Roznica powinna byc < 1s.
        var diffSeconds = Math.Abs((auth.ExpiryTime - expectedExpiry).TotalSeconds);
        Assert.True(diffSeconds < 1.0,
            $"ExpiryTime rozni sie o {diffSeconds}s od oczekiwanego UTC — mozliwy blad konwersji DateTimeOffset->DateTime");
    }

    [Fact]
    public async Task WebhookAuthorize_IsOnUsTrue_StoredCorrectly()
    {
        // is_on_us=true oznacza ze sender_bank == merchant.settlement_bank.
        var (db, userId, accountId) = await SetupUser();
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90).UtcDateTime, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var payload = new KlikWebhookAuthorizeRequest
        {
            TransactionId = Guid.NewGuid().ToString(),
            UserId = userId.ToString(),
            Amount = 75m,
            Currency = "USD",
            MerchantName = "On-Us Store",
            IsOnUs = true,
            ExpiryTime = DateTime.UtcNow.AddSeconds(60),
            Zone = "US"
        };

        var svc = CreateService(db);
        await svc.HandleAuthorizeWebhookAsync(payload);

        var auth = await db.BlikAuthorizations.FirstAsync();
        Assert.True(auth.IsOnUs);
    }

    [Fact]
    public async Task WebhookAuthorize_KlikUuidTransactionId_StoredVerbatim()
    {
        // authorize_webhook_task wysyla transaction_id = str(transaction.id) — UUID bez nawiasow.
        var (db, userId, accountId) = await SetupUser();
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90).UtcDateTime, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var klikTxId = "a3b4c5d6-e7f8-9012-3456-789abcdef012";
        var payload = new KlikWebhookAuthorizeRequest
        {
            TransactionId = klikTxId,
            UserId = userId.ToString(),
            Amount = 30m,
            Currency = "USD",
            MerchantName = "Shop",
            IsOnUs = false,
            ExpiryTime = DateTime.UtcNow.AddSeconds(60),
            Zone = "US"
        };

        var svc = CreateService(db);
        await svc.HandleAuthorizeWebhookAsync(payload);

        var auth = await db.BlikAuthorizations.FirstAsync();
        Assert.Equal(klikTxId, auth.KlikTransactionId);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Full cycle: webhook in -> decide -> confirm out
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullCycle_WebhookIn_Approve_ConfirmAcceptedSentWithCorrectTxId()
    {
        var (db, userId, accountId) = await SetupUser(balance: 300m);
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90).UtcDateTime, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var txId = Guid.NewGuid().ToString();
        var webhookPayload = new KlikWebhookAuthorizeRequest
        {
            TransactionId = txId,
            UserId = userId.ToString(),
            Amount = 100m,
            Currency = "USD",
            MerchantName = "Coffee Shop",
            IsOnUs = false,
            ExpiryTime = DateTime.UtcNow.AddSeconds(90),
            Zone = "US"
        };

        var spy = new SpyKlikApiClient();
        var svc = CreateService(db, spy);

        await svc.HandleAuthorizeWebhookAsync(webhookPayload);
        var auth = await db.BlikAuthorizations.FirstAsync();
        await svc.DecideAsync(userId, auth.Id, accepted: true);

        Assert.Equal("ACCEPTED", spy.LastConfirmStatus);
        Assert.Equal(txId, spy.LastConfirmTransactionId);
        Assert.Null(spy.LastRejectReason);

        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(200m, account!.Balance);

        var tx = await db.Transactions.FirstAsync();
        Assert.Equal(100m, tx.Amount);
        Assert.Equal("debit", tx.Type);
        Assert.Equal(txId, tx.ReferenceId);
    }

    [Fact]
    public async Task FullCycle_WebhookIn_Reject_ConfirmUserDeclinedSentToKlik()
    {
        var (db, userId, accountId) = await SetupUser(balance: 300m);
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90).UtcDateTime, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var spy = new SpyKlikApiClient();
        var svc = CreateService(db, spy);

        var txId = Guid.NewGuid().ToString();
        await svc.HandleAuthorizeWebhookAsync(new KlikWebhookAuthorizeRequest
        {
            TransactionId = txId, UserId = userId.ToString(), Amount = 50m,
            Currency = "USD", MerchantName = "Shop", IsOnUs = false,
            ExpiryTime = DateTime.UtcNow.AddSeconds(90), Zone = "US"
        });
        var auth = await db.BlikAuthorizations.FirstAsync();
        await svc.DecideAsync(userId, auth.Id, accepted: false);

        Assert.Equal("REJECTED", spy.LastConfirmStatus);
        Assert.Equal("USER_DECLINED", spy.LastRejectReason);
        Assert.Equal(0, await db.Transactions.CountAsync());

        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(300m, account!.Balance);
    }

    [Fact]
    public async Task FullCycle_WebhookIn_ApproveInsufficientFunds_ConfirmInsufficientFundsSentToKlik()
    {
        var (db, userId, accountId) = await SetupUser(balance: 10m);
        db.BlikCodes.Add(new BlikCode
        {
            Id = Guid.NewGuid(), UserId = userId, AccountId = accountId,
            Code = "123456", Status = BlikCodeStatus.Active,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(90).UtcDateTime, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var spy = new SpyKlikApiClient();
        var svc = CreateService(db, spy);

        await svc.HandleAuthorizeWebhookAsync(new KlikWebhookAuthorizeRequest
        {
            TransactionId = Guid.NewGuid().ToString(), UserId = userId.ToString(), Amount = 200m,
            Currency = "USD", MerchantName = "Big Store", IsOnUs = false,
            ExpiryTime = DateTime.UtcNow.AddSeconds(90), Zone = "US"
        });
        var auth = await db.BlikAuthorizations.FirstAsync();
        await svc.DecideAsync(userId, auth.Id, accepted: true);

        Assert.Equal("REJECTED", spy.LastConfirmStatus);
        Assert.Equal("INSUFFICIENT_FUNDS", spy.LastRejectReason);
        Assert.Equal(0, await db.Transactions.CountAsync());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Kontrakt: BlikService rzuca gdy ConfirmPaymentAsync wroci z Success=false
    // i NIE debitoruje konta — KLIK jest autorytatywny przy accept
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Approve_KlikConfirmFails_SetsFailedStatusAndNoDebit()
    {
        var (db, userId, accountId) = await SetupUser(balance: 200m);
        var authId = Guid.NewGuid();
        db.BlikAuthorizations.Add(new BlikAuthorization
        {
            Id = authId, KlikTransactionId = Guid.NewGuid().ToString(),
            UserId = userId, AccountId = accountId,
            Amount = 100m, Currency = "USD", MerchantName = "Store",
            IsOnUs = false, Status = BlikAuthorizationStatus.Pending,
            ExpiryTime = DateTime.UtcNow.AddSeconds(60), CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var failingClient = new SpyKlikApiClient(confirmSuccess: false);
        var svc = CreateService(db, failingClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.DecideAsync(userId, authId, accepted: true));

        Assert.Equal(0, await db.Transactions.CountAsync());
        var account = await db.Accounts.FindAsync(accountId);
        Assert.Equal(200m, account!.Balance);

        var auth = await db.BlikAuthorizations.FindAsync(authId);
        Assert.Equal(BlikAuthorizationStatus.Failed, auth!.Status);
        Assert.NotNull(auth.DecidedAt);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static KlikApiClient BuildKlikClient(SpyHandler handler, string apiKey) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://klik-mock:6006") },
            NullLogger<KlikApiClient>.Instance,
            new FakeConfiguration(apiKey));

    private class SpyKlikApiClient(bool confirmSuccess = true) : IKlikApiClient
    {
        public string? LastConfirmStatus { get; private set; }
        public string? LastRejectReason { get; private set; }
        public string? LastConfirmTransactionId { get; private set; }

        public Task<KlikGenerateCodeResult> GenerateCodeAsync(string userId, CancellationToken ct = default)
            => Task.FromResult(new KlikGenerateCodeResult(true, "123456", DateTime.UtcNow.AddSeconds(120), null));

        public Task<KlikConfirmResult> ConfirmPaymentAsync(string transactionId, bool accepted, string? rejectReason, CancellationToken ct = default)
        {
            LastConfirmStatus = accepted ? "ACCEPTED" : "REJECTED";
            LastRejectReason = rejectReason;
            LastConfirmTransactionId = transactionId;
            return Task.FromResult(new KlikConfirmResult(confirmSuccess, confirmSuccess ? null : "KLIK error"));
        }
    }

    private class SpyHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => handler(request, ct);
    }

    private class FakeConfiguration(string klikApiKey) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "Integrations:KlikApiKey" ? klikApiKey : null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() =>
            new CancellationChangeToken(CancellationToken.None);

        public IConfigurationSection GetSection(string key) => new FakeSection(key);

        private class FakeSection(string key) : IConfigurationSection
        {
            public string? this[string k] { get => null; set { } }
            public string Key => key;
            public string Path => key;
            public string? Value { get => null; set { } }
            public IEnumerable<IConfigurationSection> GetChildren() => [];
            public IChangeToken GetReloadToken() =>
                new CancellationChangeToken(CancellationToken.None);
            public IConfigurationSection GetSection(string k) => new FakeSection(k);
        }
    }
}
