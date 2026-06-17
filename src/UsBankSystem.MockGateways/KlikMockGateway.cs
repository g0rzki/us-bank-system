using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// KLIK mock — C2B: POST /api/v1/codes/generate, POST /simulate/initiate,
//                    POST /api/v1/payments/confirm, GET /api/v1/payments/status/{id}
//             P2P:  POST /api/v1/aliases/register, GET /api/v1/aliases/lookup/{phone},
//                    DELETE /api/v1/aliases/{phone}
static class KlikMockGateway
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static WebApplication Build(int port, string bankWebhookUrl)
    {
        var codes = new ConcurrentDictionary<string, CodeEntry>();
        var transactions = new ConcurrentDictionary<string, TxEntry>();
        var aliases = new ConcurrentDictionary<string, AliasEntry>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
        ConfigureLogging(builder);

        var app = builder.Build();
        var logger = app.Services.GetRequiredService<ILogger<WebApplication>>();
        var webhookClient = new HttpClient { BaseAddress = new Uri(bankWebhookUrl) };

        // POST /api/v1/codes/generate
        app.MapPost("/api/v1/codes/generate", (HttpRequest req) =>
        {
            var body = ReadBody(req);
            var request = JsonSerializer.Deserialize<GenerateRequest>(body, JsonOpts);
            if (string.IsNullOrEmpty(request?.UserId))
                return Results.BadRequest(KlikError("400_INVALID_REQUEST", "user_id is required"));

            var code = Random.Shared.Next(100000, 1000000).ToString();
            var expiresAt = DateTime.UtcNow.AddSeconds(120);
            codes[code] = new CodeEntry(request.UserId, expiresAt, false);
            logger.LogInformation("[KLIK] Generated code {Code} for user {UserId}", code, request.UserId);

            return Results.Ok(new { code, expires_in = 120, expires_at = expiresAt });
        });

        // POST /simulate/initiate — simulates a merchant agent scanning the code
        app.MapPost("/simulate/initiate", (HttpRequest req) =>
        {
            var body = ReadBody(req);
            var request = JsonSerializer.Deserialize<InitiateRequest>(body, JsonOpts);
            if (request == null)
                return Results.BadRequest(KlikError("400_INVALID_REQUEST", "Invalid request body"));

            if (!codes.TryGetValue(request.Code, out var entry))
                return Results.NotFound(KlikError("404_CODE_NOT_FOUND", "Code not found or already expired"));

            if (entry.ExpiresAt < DateTime.UtcNow)
            {
                codes.TryRemove(request.Code, out _);
                return Results.NotFound(KlikError("404_CODE_EXPIRED", "Code has expired"));
            }

            if (entry.Used)
                return Results.Conflict(KlikError("409_CODE_ALREADY_USED", "Code has already been used"));

            codes[request.Code] = entry with { Used = true };

            var transactionId = Guid.NewGuid().ToString();
            var expiryTime = DateTime.UtcNow.AddSeconds(90);
            transactions[transactionId] = new TxEntry(
                request.Code, request.Amount, request.Currency ?? "USD",
                request.MerchantName ?? "Unknown Merchant", entry.UserId, "PENDING", DateTime.UtcNow);

            logger.LogInformation("[KLIK] Transaction {TxId} PENDING — code {Code}, amount {Amount}", transactionId, request.Code, request.Amount);

            // Fire-and-forget: call bank webhook
            _ = Task.Run(async () =>
            {
                var payload = new
                {
                    transaction_id = transactionId,
                    user_id = entry.UserId,
                    amount = request.Amount.ToString("F2"),
                    currency = request.Currency ?? "USD",
                    merchant_name = request.MerchantName ?? "Unknown Merchant",
                    is_on_us = request.IsOnUs,
                    expiry_time = expiryTime,
                    zone = "US"
                };
                var json = JsonSerializer.Serialize(payload, JsonOpts);
                try
                {
                    using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/klik/webhook/authorize")
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };
                    var resp = await webhookClient.SendAsync(httpReq);
                    logger.LogInformation("[KLIK] Webhook authorize → {Status}", resp.StatusCode);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[KLIK] Failed to call bank webhook for transaction {TxId}", transactionId);
                }
            });

            return Results.Ok(new { transaction_id = transactionId, status = "pending" });
        });

        // POST /api/v1/payments/confirm
        app.MapPost("/api/v1/payments/confirm", (HttpRequest req) =>
        {
            var body = ReadBody(req);
            var request = JsonSerializer.Deserialize<ConfirmRequest>(body, JsonOpts);
            if (request == null || string.IsNullOrEmpty(request.TransactionId))
                return Results.BadRequest(KlikError("400_INVALID_REQUEST", "transaction_id is required"));

            if (!transactions.TryGetValue(request.TransactionId, out var tx))
                return Results.NotFound(KlikError("404_TRANSACTION_NOT_FOUND", "Transaction not found"));

            if (tx.Status != "PENDING")
            {
                // Idempotent: same decision is OK, conflict for different decision
                if ((request.Status == "ACCEPTED" && tx.Status == "COMPLETED") ||
                    (request.Status == "REJECTED" && tx.Status == "REJECTED"))
                    return Results.Ok(BuildConfirmResponse(request.TransactionId, tx));

                return Results.Conflict(KlikError("409_TRANSACTION_ALREADY_CLOSED", $"Transaction is already {tx.Status}"));
            }

            var newStatus = request.Status == "ACCEPTED" ? "COMPLETED" : "REJECTED";
            transactions[request.TransactionId] = tx with { Status = newStatus };
            var updatedTx = transactions[request.TransactionId];

            logger.LogInformation("[KLIK] Transaction {TxId} → {Status}", request.TransactionId, newStatus);
            return Results.Ok(BuildConfirmResponse(request.TransactionId, updatedTx));
        });

        // GET /api/v1/payments/status/{id}
        app.MapGet("/api/v1/payments/status/{id}", (string id) =>
        {
            if (!transactions.TryGetValue(id, out var tx))
                return Results.NotFound(KlikError("404_TRANSACTION_NOT_FOUND", "Transaction not found"));

            return Results.Ok(new { transaction_id = id, status = tx.Status, created_at = tx.CreatedAt });
        });

        // POST /api/v1/aliases/register
        app.MapPost("/api/v1/aliases/register", (HttpRequest req) =>
        {
            var body = ReadBody(req);
            var request = JsonSerializer.Deserialize<AliasRegisterRequest>(body, JsonOpts);
            if (string.IsNullOrEmpty(request?.Phone) || request.AccountIdentifier is null)
                return Results.BadRequest(KlikError("400_INVALID_REQUEST", "phone and account_identifier are required"));

            if (string.IsNullOrEmpty(request.AccountIdentifier.RoutingNumber) ||
                string.IsNullOrEmpty(request.AccountIdentifier.AccountNumber))
                return Results.BadRequest(KlikError("400_INVALID_ACCOUNT_IDENTIFIER", "routing_number and account_number are required"));

            if (aliases.ContainsKey(request.Phone))
                return Results.Conflict(KlikError("409_ALIAS_ALREADY_EXISTS", $"Alias for {request.Phone} already registered"));

            var aliasId = Guid.NewGuid().ToString();
            var registeredAt = DateTime.UtcNow;
            aliases[request.Phone] = new AliasEntry(
                aliasId, request.Phone,
                request.AccountIdentifier.RoutingNumber,
                request.AccountIdentifier.AccountNumber,
                registeredAt);

            logger.LogInformation("[KLIK] P2P alias registered: {Phone} → alias {AliasId}", request.Phone, aliasId);
            return Results.Ok(new { alias_id = aliasId, phone = request.Phone, registered_at = registeredAt });
        });

        // GET /api/v1/aliases/lookup/{phone}
        app.MapGet("/api/v1/aliases/lookup/{phone}", (string phone) =>
        {
            var decoded = Uri.UnescapeDataString(phone);
            if (!aliases.TryGetValue(decoded, out var alias))
                return Results.NotFound(KlikError("404_ALIAS_NOT_FOUND", $"No alias registered for {decoded}"));

            return Results.Ok(new
            {
                phone = alias.Phone,
                bank_id = "US_BANK_MOCK",
                bank_code = "US_BANK_MOCK",
                account_identifier = new
                {
                    type = "us_routing",
                    routing_number = alias.RoutingNumber,
                    account_number = alias.AccountNumber
                }
            });
        });

        // DELETE /api/v1/aliases/{phone}
        app.MapDelete("/api/v1/aliases/{phone}", (string phone) =>
        {
            var decoded = Uri.UnescapeDataString(phone);
            if (!aliases.TryRemove(decoded, out _))
                return Results.NotFound(KlikError("404_ALIAS_NOT_FOUND", $"No alias registered for {decoded}"));

            logger.LogInformation("[KLIK] P2P alias deleted: {Phone}", decoded);
            return Results.NoContent();
        });

        app.MapGet("/health", () => Results.Ok(new { channel = "KLIK", port, mode = "mock" }));

        return app;
    }

    private static object BuildConfirmResponse(string transactionId, TxEntry tx)
    {
        var klikFee = Math.Round(tx.Amount * 0.01m, 2);
        var agentFee = Math.Round(tx.Amount * 0.005m, 2);
        var merchantNet = tx.Amount - klikFee - agentFee;
        return new
        {
            transaction_id = transactionId,
            status = tx.Status,
            amount_gross = tx.Amount,
            klik_fee = klikFee,
            agent_fee = agentFee,
            merchant_net = merchantNet,
            currency = tx.Currency,
            completed_at = DateTime.UtcNow
        };
    }

    private static object KlikError(string code, string message) =>
        new { error = new { code, message } };

    private static string ReadBody(HttpRequest req)
    {
        req.EnableBuffering();
        using var reader = new StreamReader(req.Body, leaveOpen: true);
        var body = reader.ReadToEndAsync().GetAwaiter().GetResult();
        req.Body.Position = 0;
        return body;
    }

    private static void ConfigureLogging(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
    }

    private record CodeEntry(string UserId, DateTime ExpiresAt, bool Used);
    private record TxEntry(string Code, decimal Amount, string Currency, string MerchantName, string UserId, string Status, DateTime CreatedAt);
    private record AliasEntry(string AliasId, string Phone, string RoutingNumber, string AccountNumber, DateTime RegisteredAt);
    private record GenerateRequest(string? UserId, string? Zone);
    private record InitiateRequest(string Code, decimal Amount, string? Currency, string? MerchantName, string? UserId, bool IsOnUs);
    private record ConfirmRequest(string? TransactionId, string Status, string? RejectReason);
    private record AliasRegisterRequest(string? Phone, AliasAccountIdentifier? AccountIdentifier, string? Zone);
    private record AliasAccountIdentifier(string? Type, string? RoutingNumber, string? AccountNumber);
}
