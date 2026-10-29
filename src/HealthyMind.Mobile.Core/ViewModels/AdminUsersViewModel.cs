using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HealthyMind.Mobile.Core.Api;
using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.Navigation;

namespace HealthyMind.Mobile.Core.ViewModels;

/// <summary>Administración de usuarios (RF01, rol Admin): buscar, suspender y reactivar cuentas.</summary>
public sealed partial class AdminUsersViewModel(
    AdminUsersApi adminApi,
    ISessionManager session,
    INavigationService navigation,
    IDialogService dialogs) : ViewModelBase
{
    private int _page = 1;
    private int _totalPages;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private StatusFilter _filter = StatusFilter.All;

    [ObservableProperty]
    private string _resultText = string.Empty;

    public ObservableCollection<AdminUserItem> Users { get; } = [];

    public bool IsAll => Filter == StatusFilter.All;

    public bool IsActiveFilter => Filter == StatusFilter.Active;

    public bool IsSuspendedFilter => Filter == StatusFilter.Suspended;

    public bool CanLoadMore => _page < _totalPages;

    public Task LoadAsync() => SearchAsync();

    partial void OnFilterChanged(StatusFilter value)
    {
        OnPropertyChanged(nameof(IsAll));
        OnPropertyChanged(nameof(IsActiveFilter));
        OnPropertyChanged(nameof(IsSuspendedFilter));
    }

    [RelayCommand]
    private Task SearchAsync() => LoadPageAsync(1);

    [RelayCommand]
    private Task LoadMoreAsync() => CanLoadMore ? LoadPageAsync(_page + 1) : Task.CompletedTask;

    [RelayCommand]
    private Task SetFilterAsync(StatusFilter filter)
    {
        Filter = filter;
        return LoadPageAsync(1);
    }

    [RelayCommand]
    private async Task ToggleStatusAsync(AdminUserItem item)
    {
        var suspend = item.IsActive;
        var confirmed = await dialogs.ConfirmAsync(
            suspend ? "Suspender cuenta" : "Reactivar cuenta",
            suspend
                ? $"{item.FullName} no podrá entrar y se cerrará su sesión en todos sus dispositivos."
                : $"{item.FullName} podrá volver a entrar con su contraseña.",
            suspend ? "Suspender" : "Reactivar",
            "Cancelar");
        if (!confirmed)
        {
            return;
        }

        UserProfile? updated = null;
        var ok = await RunAsync(async () => updated = suspend
            ? await adminApi.SuspendAsync(item.Id)
            : await adminApi.ReactivateAsync(item.Id));
        if (ok && updated is not null)
        {
            Users[Users.IndexOf(item)] = AdminUserItem.From(updated, session.CurrentUser?.Id);
        }
    }

    [RelayCommand]
    private Task GoBackAsync() => navigation.GoBackAsync();

    private async Task LoadPageAsync(int page)
    {
        PagedResult<UserProfile>? result = null;
        var status = Filter switch
        {
            StatusFilter.Active => UserStatuses.Active,
            StatusFilter.Suspended => UserStatuses.Suspended,
            _ => null,
        };

        if (!await RunAsync(async () => result = await adminApi.SearchAsync(Search, status, page)) || result is null)
        {
            return;
        }

        if (page == 1)
        {
            Users.Clear();
        }

        foreach (var user in result.Items)
        {
            Users.Add(AdminUserItem.From(user, session.CurrentUser?.Id));
        }

        _page = result.Page;
        _totalPages = result.TotalPages;
        ResultText = HabitFormat.Count(result.TotalCount, "usuario", "usuarios");
        OnPropertyChanged(nameof(CanLoadMore));
    }
}

public enum StatusFilter
{
    All,
    Active,
    Suspended,
}

/// <param name="IsSelf">El administrador no puede suspenderse a sí mismo.</param>
public sealed record AdminUserItem(Guid Id, string FullName, string Detail, string Initials, bool IsActive, bool IsAdmin, bool IsSelf)
{
    public bool CanToggle => !IsSelf;

    public string StatusText => IsActive ? "Activo" : "Suspendido";

    public string ActionText => IsActive ? "Suspender" : "Reactivar";

    public static AdminUserItem From(UserProfile user, Guid? currentUserId) => new(
        user.Id,
        user.FullName,
        $"@{user.Username} • {user.Email}",
        user.Initials,
        user.Status == UserStatuses.Active,
        user.IsAdmin,
        user.Id == currentUserId);
}
