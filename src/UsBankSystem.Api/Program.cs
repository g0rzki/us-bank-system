using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UsBankSystem.Api.Configuration;
using UsBankSystem.Api.Extensions;
using UsBankSystem.Api.Integrations;
using UsBankSystem.Api.Integrations.FedNow;
using UsBankSystem.Api.Integrations.Sftp;
using UsBankSystem.Api.Middleware;
using UsBankSystem.Api.Services;
using UsBankSystem.Api.Services.Polling;
using UsBankSystem.Infrastructure.Persistence;

DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddScoped<UsBankSystem.Api.Services.AuthService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.AccountService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.TransactionService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.JuniorService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.TransferService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.Payments.InternalPaymentService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.Payments.AchPaymentService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.Payments.RtpPaymentService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.Payments.FedNowPaymentService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.Payments.SwiftPaymentService>();
builder.Services.AddScoped<UsBankSystem.Api.Services.CardService>();
builder.Services.AddHostedService<UsBankSystem.Api.Services.CardExpiryJob>();
builder.Services.AddSingleton<ISftpService, SftpService>();
builder.Services.AddSingleton<IAchTraceSequencer, AchTraceSequencer>();
builder.Services.AddSingleton<IncomingTransferProcessor>();
builder.Services.AddHostedService<AchPollingService>();
// CORS
builder.Services.AddCors(opt => opt.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration["Cors:Origin"] ?? "http://localhost:5173")
     .AllowAnyHeader()
     .AllowAnyMethod()));

// Payment config
builder.Configuration.AddJsonFile("payment-config.json", optional: false, reloadOnChange: true);
builder.Services.Configure<PaymentSessionConfig>(
    builder.Configuration.GetSection("PaymentSessions"));

// Payment gateways — adresy z konfiguracji (env var lub .env); domyślnie mock stubs na localhost
builder.Services.AddScoped<AchGateway>();
builder.Services.AddHttpClient<RtpGateway>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Integrations:RtpUrl"] ?? "http://localhost:6002"));
builder.Services.AddHttpClient<FedNowMqGateway>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Integrations:FedNowMqUrl"] ?? "http://localhost:8770"));
builder.Services.AddSingleton<Pacs008Builder>();
builder.Services.AddSingleton<Pacs002Parser>();
builder.Services.AddSingleton<Pacs008Parser>();
builder.Services.AddSingleton<Pacs002Builder>();
builder.Services.AddSingleton<Pain013Parser>();
builder.Services.AddSingleton<Pain014Builder>();
builder.Services.AddHostedService<FedNowPollingService>();
builder.Services.AddHttpClient<SwiftGateway>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Integrations:SwiftUrl"] ?? "http://localhost:6004"));
builder.Services.AddHttpClient<CardsGateway>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Integrations:CardsUrl"] ?? "http://localhost:6005"));
builder.Services.AddHttpClient<KlikApiClient>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Integrations:BlikUrl"] ?? "http://localhost:6006"));
// Resolve IKlikApiClient via the typed-HttpClient-factory instance (not a plain Scoped)
builder.Services.AddScoped<IKlikApiClient>(sp => sp.GetRequiredService<KlikApiClient>());
builder.Services.AddScoped<BlikService>();
builder.Services.AddHttpClient<KlikP2pClient>(c =>
    c.BaseAddress = new Uri(builder.Configuration["Integrations:BlikUrl"] ?? "http://localhost:6006"));
builder.Services.AddScoped<IKlikP2pClient>(sp => sp.GetRequiredService<KlikP2pClient>());
builder.Services.AddScoped<PhoneAliasService>();

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerWithJwt();

// JWT Auth
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt__Secret is not configured");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

// EF Core + PostgreSQL
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Health check
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>();

var app = builder.Build();

// Migrate and seed outside IsDevelopment() so Docker production containers
// also apply migrations on startup without a separate migration step.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
        await DbSeeder.SeedAsync(db);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ErrorHandlingMiddleware>();
app.UseCors();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();

public partial class Program { }