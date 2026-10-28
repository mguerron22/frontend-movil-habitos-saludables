using System.Net;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class ApiTests
{
    private sealed class StubTokens : IAccessTokenSource
    {
        public int Renewals { get; private set; }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult("viejo");

        public Task<string> RenewAfterRejectionAsync(string rejectedToken, CancellationToken cancellationToken)
        {
            Renewals++;
            return Task.FromResult("nuevo");
        }
    }

    [Fact]
    public async Task Unauthorized_request_is_retried_once_with_a_renewed_token()
    {
        var server = new FakeServer();
        server.On("/api/habits", request => Task.FromResult(FakeServer.Json(
            request.Headers.Authorization?.Parameter == "nuevo" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
            new[] { "ok" })));
        var tokens = new StubTokens();
        var client = new AuthenticatedApiClient(
            new HttpClient(new AuthenticatedHttpHandler(tokens) { InnerHandler = server }),
            new FakeConnectivity(),
            new FakeSettings());

        var result = await client.PostAsync<string[]>("api/habits", new { name = "Agua" });

        Assert.Equal(["ok"], result);
        Assert.Equal(1, tokens.Renewals);
        Assert.Equal(["viejo", "nuevo"], server.AuthorizationHeaders);
    }

    [Fact]
    public async Task Validation_errors_from_the_backend_reach_each_field()
    {
        var server = new FakeServer();
        server.On("/api/auth/register", HttpStatusCode.BadRequest, new
        {
            title = "Datos inválidos",
            detail = "Revisa los campos marcados.",
            errors = new Dictionary<string, string[]> { ["Password"] = ["La contraseña debe tener al menos un número."] },
        });
        var client = new AnonymousApiClient(new HttpClient(server), new FakeConnectivity(), new FakeSettings());

        var error = await Assert.ThrowsAsync<ApiException>(() => client.PostAsync<object>("api/auth/register", new { }));

        Assert.Equal("La contraseña debe tener al menos un número.", error.Message);
        Assert.Equal("La contraseña debe tener al menos un número.", error.FirstErrorFor("password"));
    }

    [Fact]
    public async Task Business_errors_show_the_backend_detail()
    {
        var server = new FakeServer();
        server.On("/api/auth/login", HttpStatusCode.Unauthorized, new { title = "No autenticado", detail = "Usuario o contraseña incorrectos." });
        var client = new AnonymousApiClient(new HttpClient(server), new FakeConnectivity(), new FakeSettings());

        var error = await Assert.ThrowsAsync<ApiException>(() => client.PostAsync<object>("api/auth/login", new { }));

        Assert.True(error.IsUnauthorized);
        Assert.Equal("Usuario o contraseña incorrectos.", error.Message);
    }

    [Fact]
    public async Task Unreachable_server_switches_the_app_to_offline()
    {
        var connectivity = new FakeConnectivity();
        var client = new AnonymousApiClient(new HttpClient(new FailingHandler()), connectivity, new FakeSettings());

        await Assert.ThrowsAsync<ServerUnreachableException>(() => client.PostAsync<object>("api/auth/login", new { }));

        Assert.False(connectivity.IsOnline);
    }

    [Theory]
    [InlineData("http://192.168.1.20:5080/", true, "http://192.168.1.20:5080")]
    [InlineData("  https://api.healthymind.ec ", true, "https://api.healthymind.ec")]
    [InlineData("192.168.1.20:5080", false, null)]
    [InlineData("ftp://servidor", false, null)]
    public void Server_address_must_be_http(string input, bool valid, string? expected)
    {
        Assert.Equal(valid, ServerAddress.TryNormalize(input, out var normalized));
        if (valid)
        {
            Assert.Equal(expected, normalized);
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("Conexión rechazada");
    }
}
