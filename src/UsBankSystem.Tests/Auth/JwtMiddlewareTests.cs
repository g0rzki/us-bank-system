using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Tests.Auth;

public class JwtMiddlewareTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public JwtMiddlewareTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("Jwt__Secret", "test_secret_minimum_32_characters_required!");

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                if (descriptor != null) services.Remove(descriptor);

                services.AddDbContext<AppDbContext>(opt =>
                    opt.UseInMemoryDatabase("JwtTestDb"));
            });
        });
    }

    private HttpClient CreateClient() => _factory.CreateClient();

    private static StringContent Json(object obj) =>
        new(JsonSerializer.Serialize(obj), Encoding.UTF8, "application/json");

    private async Task<string> GetToken(HttpClient client)
    {
        var reg = await client.PostAsync("/auth/register", Json(new
        {
            email = "jwt@example.com",
            password = "Password123!",
            firstName = "Jan",
            lastName = "Kowalski"
        }));
        var regBody = await reg.Content.ReadAsStringAsync();
        Assert.True(reg.StatusCode == System.Net.HttpStatusCode.Created || reg.StatusCode == System.Net.HttpStatusCode.Conflict,
            $"Register failed: {reg.StatusCode} - {regBody}");

        var response = await client.PostAsync("/auth/login", Json(new
        {
            email = "jwt@example.com",
            password = "Password123!"
        }));

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"Login failed: {response.StatusCode} - {body}");
        return JsonDocument.Parse(body).RootElement.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task ProtectedEndpoint_NoToken_Returns401()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/accounts/some-id");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_InvalidToken_Returns401()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalidtoken");
        var response = await client.GetAsync("/accounts/some-id");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpoint_ValidToken_DoesNotReturn401()
    {
        var client = CreateClient();
        var token = await GetToken(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/accounts/some-id");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task HealthEndpoint_NoToken_Returns200()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AuthRegister_NoToken_Returns201()
    {
        var client = CreateClient();
        var response = await client.PostAsync("/auth/register", Json(new
        {
            email = "new@example.com",
            password = "Password123!",
            firstName = "Jan",
            lastName = "Kowalski"
        }));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
