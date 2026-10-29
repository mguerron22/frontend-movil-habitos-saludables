using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Navigation;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Editar nombre, apellido y correo (RF01). Necesita conexión: el servidor valida que el correo no esté tomado.</summary>
public sealed partial class EditProfileViewModel(
    ISessionManager session,
    ProfileApi profileApi,
    INavigationService navigation,
    IDialogService dialogs) : ViewModelBase
{
    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string? _firstNameError;

    [ObservableProperty]
    private string? _lastNameError;

    [ObservableProperty]
    private string? _emailError;

    public void Load()
    {
        var user = session.CurrentUser;
        FirstName = user?.FirstName ?? string.Empty;
        LastName = user?.LastName ?? string.Empty;
        Email = user?.Email ?? string.Empty;
        Username = user is null ? string.Empty : "@" + user.Username;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!Validate())
        {
            ErrorMessage = "Revisa los campos marcados.";
            return;
        }

        var request = new UpdateProfileRequest(FirstName.Trim(), LastName.Trim(), Email.Trim(), session.CurrentUser?.TimeZoneId);
        var ok = await RunAsync(async () => await session.UpdateCurrentUserAsync(await profileApi.UpdateAsync(request)));
        if (ok)
        {
            await dialogs.AlertAsync("Perfil actualizado", "Tus datos se guardaron.");
            await navigation.GoBackAsync();
        }
    }

    [RelayCommand]
    private Task CancelAsync() => navigation.GoBackAsync();

    internal bool Validate()
    {
        FirstNameError = AccountRules.Required(FirstName, "Escribe tu nombre.");
        LastNameError = AccountRules.Required(LastName, "Escribe tu apellido.");
        EmailError = AccountRules.Email(Email);
        return FirstNameError is null && LastNameError is null && EmailError is null;
    }

    protected override void OnApiError(ApiException exception)
    {
        base.OnApiError(exception);
        FirstNameError = exception.FirstErrorFor("firstName") ?? FirstNameError;
        LastNameError = exception.FirstErrorFor("lastName") ?? LastNameError;
        EmailError = exception.FirstErrorFor("email") ?? EmailError;

        // Correo ya registrado por otra cuenta.
        if (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            EmailError = exception.Message;
        }
    }
}
