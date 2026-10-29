using System.Net;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class AccountTests
{
    [Fact]
    public void New_password_follows_the_rules_and_differs_from_the_current_one()
    {
        var vm = new ChangePasswordViewModel(null!, null!, null!, null!)
        {
            CurrentPassword = "Clave1234",
            NewPassword = "Clave1234",
            ConfirmPassword = "Otra1234",
        };

        Assert.False(vm.Validate());
        Assert.Null(vm.CurrentPasswordError);
        Assert.Equal("Debe ser distinta de la actual.", vm.NewPasswordError);
        Assert.Equal("Las contraseñas no coinciden.", vm.ConfirmPasswordError);

        vm.NewPassword = vm.ConfirmPassword = "Nueva12345";
        Assert.True(vm.Validate());
    }

    [Fact]
    public void Edit_profile_needs_names_and_a_valid_email()
    {
        var vm = new EditProfileViewModel(null!, null!, null!, null!) { FirstName = " ", LastName = "Pérez", Email = "ana@" };

        Assert.False(vm.Validate());
        Assert.Equal("Escribe tu nombre.", vm.FirstNameError);
        Assert.Null(vm.LastNameError);
        Assert.Equal("Escribe un correo válido.", vm.EmailError);
    }

    [Fact]
    public void This_phone_goes_first_and_the_rest_newest_first()
    {
        var old = new SessionResponse(Guid.NewGuid(), "tablet", null, Samples.Now.AddDays(-5), Samples.Now.AddDays(55));
        var recent = new SessionResponse(Guid.NewGuid(), "laptop", "Laptop", Samples.Now.AddDays(-1), Samples.Now.AddDays(59));
        var current = new SessionResponse(Guid.NewGuid(), "device-test", "Pixel", Samples.Now.AddDays(-9), Samples.Now.AddDays(51));

        var ordered = SessionsViewModel.Order([old, recent, current], "device-test");

        Assert.Equal(["Pixel", "Laptop", "Dispositivo sin nombre"], ordered.Select(session => session.Name));
        Assert.True(ordered[0].IsCurrent);
        Assert.StartsWith("Este teléfono", ordered[0].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_admin_cannot_suspend_their_own_account()
    {
        var me = Samples.User();
        var other = Samples.User(Guid.NewGuid()) with { Status = UserStatuses.Suspended };

        var self = AdminUserItem.From(me, me.Id);
        var suspended = AdminUserItem.From(other, me.Id);

        Assert.False(self.CanToggle);
        Assert.True(suspended.CanToggle);
        Assert.False(suspended.IsActive);
        Assert.Equal("Reactivar", suspended.ActionText);
    }

    [Fact]
    public async Task Admin_search_sends_text_status_and_page()
    {
        var server = new FakeServer();
        string? query = null;
        server.On("/api/admin/users", request =>
        {
            query = request.RequestUri!.Query;
            return Task.FromResult(FakeServer.Json(HttpStatusCode.OK, new PagedResult<UserProfile>([], 2, 20, 0, 0)));
        });
        var api = new AdminUsersApi(new AuthenticatedApiClient(new HttpClient(server), new FakeConnectivity(), new FakeSettings()));

        await api.SearchAsync(" ana pérez ", UserStatuses.Suspended, 2);

        Assert.Equal("?page=2&pageSize=20&search=ana%20p%C3%A9rez&status=Suspended", query);
    }
}
