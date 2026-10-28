using HealthyMind.Mobile.Core.Auth;

namespace HealthyMind.Mobile.Views;

public partial class HabitsPage : ContentPage
{
    private readonly ISessionManager _session;

    public HabitsPage(ISessionManager session)
    {
        InitializeComponent();
        _session = session;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Header.Initials = _session.CurrentUser?.Initials ?? string.Empty;
    }
}
