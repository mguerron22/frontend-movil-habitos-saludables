using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Settings;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Dispositivos con la sesión abierta; se cierra la de cualquiera menos este (para eso está "Cerrar sesión").</summary>
public sealed partial class SessionsViewModel(
    ProfileApi profileApi,
    IAppSettings settings,
    INavigationService navigation,
    IDialogService dialogs) : ViewModelBase
{
    public ObservableCollection<DeviceSession> Sessions { get; } = [];

    public bool HasOthers => Sessions.Any(session => !session.IsCurrent);

    public async Task LoadAsync()
    {
        IReadOnlyList<SessionResponse> sessions = [];
        if (!await RunAsync(async () => sessions = await profileApi.GetSessionsAsync()))
        {
            return;
        }

        Sessions.Clear();
        foreach (var session in Order(sessions, settings.DeviceId))
        {
            Sessions.Add(session);
        }

        OnPropertyChanged(nameof(HasOthers));
    }

    /// <summary>Este teléfono primero; los demás del más reciente al más antiguo.</summary>
    internal static IReadOnlyList<DeviceSession> Order(IEnumerable<SessionResponse> sessions, string currentDeviceId) =>
        sessions
            .OrderByDescending(session => session.DeviceId == currentDeviceId)
            .ThenByDescending(session => session.CreatedAt)
            .Select(session => new DeviceSession(
                session.Id,
                string.IsNullOrWhiteSpace(session.DeviceName) ? "Dispositivo sin nombre" : session.DeviceName,
                "Desde el " + session.CreatedAt.ToLocalTime().ToString("d 'de' MMMM, HH:mm", HomeViewModel.Spanish),
                session.DeviceId == currentDeviceId))
            .ToList();

    [RelayCommand]
    private async Task RevokeAsync(DeviceSession session)
    {
        if (session.IsCurrent || !await dialogs.ConfirmAsync(
                "Cerrar sesión en el dispositivo",
                $"\"{session.Name}\" tendrá que ingresar la contraseña otra vez.",
                "Cerrar sesión",
                "Cancelar"))
        {
            return;
        }

        if (await RunAsync(() => profileApi.RevokeSessionAsync(session.Id)))
        {
            Sessions.Remove(session);
            OnPropertyChanged(nameof(HasOthers));
        }
    }

    [RelayCommand]
    private async Task RevokeOthersAsync()
    {
        var others = Sessions.Where(session => !session.IsCurrent).ToList();
        if (others.Count == 0 || !await dialogs.ConfirmAsync(
                "Cerrar las demás sesiones",
                $"Se cerrará la sesión en {HabitFormat.Count(others.Count, "dispositivo", "dispositivos")}. En este teléfono sigues dentro.",
                "Cerrar sesiones",
                "Cancelar"))
        {
            return;
        }

        await RunAsync(async () =>
        {
            foreach (var session in others)
            {
                await profileApi.RevokeSessionAsync(session.Id);
                Sessions.Remove(session);
            }
        });
        OnPropertyChanged(nameof(HasOthers));
    }

    [RelayCommand]
    private Task GoBackAsync() => navigation.GoBackAsync();
}

public sealed record DeviceSession(Guid Id, string Name, string Since, bool IsCurrent)
{
    public string Detail => IsCurrent ? "Este teléfono • " + Since : Since;
}
