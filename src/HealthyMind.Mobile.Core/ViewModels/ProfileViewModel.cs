using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Habits;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Core.Settings;
using HealthyMind.Mobile.Core.Sync;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Perfil (RF01): datos, huella, accesos a editar perfil, contraseña y dispositivos, administración y cerrar sesión.</summary>
public sealed partial class ProfileViewModel(
    ISessionManager session,
    BiometricEnrollment biometricEnrollment,
    IBiometricAuthenticator biometric,
    IAppSettings settings,
    ISyncService sync,
    RecordLocation location,
    PresentationMode presentation,
    IAppNotifications notifications,
    INavigationService navigation,
    IDialogService dialogs,
    IConnectivityService connectivity,
    IUiDispatcher ui,
    DailyReminder reminder) : ViewModelBase
{
    private bool _loading;
    private bool _watching;

    /// <summary>Editar perfil, contraseña, dispositivos y administración hablan directo con el servidor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOffline))]
    private bool _isOnline = true;

    public bool IsOffline => !IsOnline;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _initials = string.Empty;

    [ObservableProperty]
    private bool _isAdmin;

    [ObservableProperty]
    private bool _isBiometricAvailable;

    [ObservableProperty]
    private bool _isBiometricEnabled;

    [ObservableProperty]
    private bool _isLocationEnabled;

    [ObservableProperty]
    private bool _isPresentationEnabled;

    /// <summary>Recordatorio diario de hábitos pendientes (funciona sin conexión).</summary>
    [ObservableProperty]
    private bool _isReminderEnabled;

    /// <summary>Hora del recordatorio (TimeSpan porque es lo que usa el TimePicker).</summary>
    [ObservableProperty]
    private TimeSpan _reminderTime = DailyReminder.DefaultTime.ToTimeSpan();

    public async Task LoadAsync()
    {
        IsOnline = connectivity.IsOnline;
        if (!_watching)
        {
            connectivity.StatusChanged += OnConnectivityChanged;
            _watching = true;
        }

        _loading = true;
        var user = session.CurrentUser;
        FullName = user?.FullName ?? string.Empty;
        Email = user?.Email ?? string.Empty;
        Username = user is null ? string.Empty : "@" + user.Username;
        Initials = user?.Initials ?? string.Empty;
        IsAdmin = user?.IsAdmin == true;
        IsBiometricAvailable = await biometric.IsAvailableAsync();
        IsBiometricEnabled = settings.BiometricEnabled;
        IsLocationEnabled = location.IsEnabled;
        IsPresentationEnabled = presentation.IsEnabled;
        IsReminderEnabled = reminder.IsEnabled;
        ReminderTime = reminder.Time.ToTimeSpan();
        _loading = false;
    }

    /// <summary>El interruptor de huella: activarla exige confirmar con la huella.</summary>
    async partial void OnIsBiometricEnabledChanged(bool value)
    {
        if (_loading || value == settings.BiometricEnabled)
        {
            return;
        }

        if (!value)
        {
            biometricEnrollment.Disable();
            return;
        }

        if (!await biometricEnrollment.EnableAsync())
        {
            _loading = true;
            IsBiometricEnabled = false;
            _loading = false;
        }
    }

    /// <summary>Guardar la ubicación de cada registro (RF03): activarla pide el permiso de ubicación.</summary>
    async partial void OnIsLocationEnabledChanged(bool value)
    {
        if (_loading || value == location.IsEnabled)
        {
            return;
        }

        if (await location.SetEnabledAsync(value) != value)
        {
            _loading = true;
            IsLocationEnabled = false;
            _loading = false;
            await dialogs.AlertAsync(
                "Sin permiso de ubicación",
                "Para guardar dónde registras tus hábitos, permite la ubicación a HealthyMindIA en los ajustes del teléfono.");
        }
    }

    /// <summary>Modo presentación: la IA escribe cada 3 minutos para mostrar las notificaciones.</summary>
    async partial void OnIsPresentationEnabledChanged(bool value)
    {
        if (_loading || value == presentation.IsEnabled)
        {
            return;
        }

        if (!value)
        {
            presentation.Disable();
            return;
        }

        if (!await notifications.EnsurePermissionAsync())
        {
            _loading = true;
            IsPresentationEnabled = false;
            _loading = false;
            await dialogs.AlertAsync(
                "Sin permiso de notificaciones",
                "Permite las notificaciones a HealthyMindIA en los ajustes del teléfono para ver los avisos de la IA.");
            return;
        }

        presentation.Enable();
        if (!presentation.IsPrecise && await dialogs.ConfirmAsync(
                "Avisos a tiempo",
                "Para que el aviso llegue justo cada 3 minutos, activa \"Alarmas y recordatorios\" para HealthyMindIA. Sin eso, Android puede atrasarlo.",
                "Abrir ajustes",
                "Ahora no"))
        {
            presentation.OpenPrecisionSettings();
        }
    }

    async partial void OnIsReminderEnabledChanged(bool value)
    {
        if (_loading || value == reminder.IsEnabled)
        {
            return;
        }

        if (!value)
        {
            reminder.Disable();
            return;
        }

        if (!await notifications.EnsurePermissionAsync())
        {
            _loading = true;
            IsReminderEnabled = false;
            _loading = false;
            await dialogs.AlertAsync(
                "Sin permiso de notificaciones",
                "Permite las notificaciones a HealthyMindIA en los ajustes del teléfono para recibir el recordatorio.");
            return;
        }

        reminder.Enable(TimeOnly.FromTimeSpan(ReminderTime));
    }

    partial void OnReminderTimeChanged(TimeSpan value)
    {
        if (!_loading && reminder.IsEnabled)
        {
            reminder.Enable(TimeOnly.FromTimeSpan(value));
        }
    }

    /// <summary>Al salir de la pantalla deja de escuchar la conexión (el servicio vive toda la app).</summary>
    public void Unload()
    {
        if (_watching)
        {
            connectivity.StatusChanged -= OnConnectivityChanged;
            _watching = false;
        }
    }

    [RelayCommand]
    private Task EditProfileAsync() => WhenOnlineAsync(navigation.GoToEditProfileAsync);

    [RelayCommand]
    private Task ChangePasswordAsync() => WhenOnlineAsync(navigation.GoToChangePasswordAsync);

    [RelayCommand]
    private Task SessionsAsync() => WhenOnlineAsync(navigation.GoToSessionsAsync);

    [RelayCommand]
    private Task AdminUsersAsync() => WhenOnlineAsync(navigation.GoToAdminUsersAsync);

    private Task WhenOnlineAsync(Func<Task> open) =>
        connectivity.IsOnline
            ? open()
            : dialogs.AlertAsync("Necesita conexión", OfflineReasons.Describe(connectivity.Reason) + "\n\nEsta opción se usa con conexión al servidor.");

    private void OnConnectivityChanged(object? sender, bool online) =>
        _ = ui.InvokeAsync(() =>
        {
            IsOnline = online;
            return Task.CompletedTask;
        });

    [RelayCommand]
    private async Task LogoutAsync()
    {
        // Los datos se quedan en el teléfono; solo se pierde lo pendiente si después entra otra persona.
        var pending = await PendingAfterSyncAsync();
        var warning = pending == 0
            ? string.Empty
            : $"\n\nTienes {pending} cambio(s) sin sincronizar: se enviarán cuando vuelvas a entrar. Si entra otra persona en este teléfono, se perderán.";

        var confirmed = await dialogs.ConfirmAsync(
            "Cerrar sesión",
            "Tendrás que ingresar tu contraseña para volver a entrar. Tus datos se quedan en este teléfono." + warning,
            "Cerrar sesión",
            "Cancelar");

        if (confirmed && await RunAsync(() => session.LogoutAsync()))
        {
            await navigation.GoToLoginAsync();
        }
    }

    /// <summary>Para prestar o vender el teléfono: cierra la sesión y borra todo lo guardado.</summary>
    [RelayCommand]
    private async Task ClearDeviceDataAsync()
    {
        var pending = await PendingAfterSyncAsync();
        var warning = pending == 0
            ? string.Empty
            : $"\n\nOjo: tienes {pending} cambio(s) sin sincronizar que se perderán. Conéctate a internet antes para guardarlos.";

        var confirmed = await dialogs.ConfirmAsync(
            "Borrar datos de este teléfono",
            "Se cerrará la sesión y se borrarán tus hábitos, registros y preferencias guardados aquí. "
            + "Tu cuenta y lo ya sincronizado siguen en el servidor." + warning,
            "Borrar",
            "Cancelar");

        if (confirmed && await RunAsync(() => session.ClearDeviceDataAsync()))
        {
            await navigation.GoToLoginAsync();
        }
    }

    private async Task<int> PendingAfterSyncAsync()
    {
        await sync.SyncAsync();
        return await sync.PendingCountAsync();
    }
}
