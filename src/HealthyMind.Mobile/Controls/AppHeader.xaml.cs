using HealthyMind.Mobile.Core.Connectivity;
using HealthyMind.Mobile.Core.Notifications;
using HealthyMind.Mobile.Services;

namespace HealthyMind.Mobile.Controls;

public partial class AppHeader : ContentView
{
    private IConnectivityService? _connectivity;
    private RecommendationBadge? _badge;

    public static readonly BindableProperty SectionProperty = BindableProperty.Create(
        nameof(Section),
        typeof(string),
        typeof(AppHeader),
        string.Empty,
        propertyChanged: (bindable, _, value) => ((AppHeader)bindable).SectionLabel.Text = (string)value);

    public static readonly BindableProperty InitialsProperty = BindableProperty.Create(
        nameof(Initials),
        typeof(string),
        typeof(AppHeader),
        string.Empty,
        propertyChanged: (bindable, _, value) => ((AppHeader)bindable).InitialsLabel.Text = (string)value);

    public AppHeader()
    {
        InitializeComponent();
    }

    /// <summary>Nombre de la pestaña debajo de la marca: "Inicio", "Estadísticas"...</summary>
    public string Section
    {
        get => (string)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (_connectivity is not null)
        {
            _connectivity.StatusChanged -= OnStatusChanged;
        }

        if (_badge is not null)
        {
            _badge.Changed -= OnBadgeChanged;
        }

        var services = Handler?.MauiContext?.Services;
        _connectivity = services?.GetService<IConnectivityService>();
        if (_connectivity is not null)
        {
            _connectivity.StatusChanged += OnStatusChanged;
            OfflineIcon.IsVisible = !_connectivity.IsOnline;
        }

        _badge = services?.GetService<RecommendationBadge>();
        if (_badge is not null)
        {
            _badge.Changed += OnBadgeChanged;
            UnreadDot.IsVisible = _badge.HasUnread;
        }
    }

    private void OnBadgeChanged(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(() => UnreadDot.IsVisible = _badge?.HasUnread == true);

    private async void OnBellTapped(object? sender, TappedEventArgs e)
    {
        if (Shell.Current is { } shell)
        {
            await shell.GoToAsync($"//{Routes.Main}/{Routes.Insights}");
        }
    }

    // El aviso puede llegar desde un hilo de fondo: la UI se toca en el hilo principal.
    private void OnStatusChanged(object? sender, bool online) =>
        Dispatcher.Dispatch(() => OfflineIcon.IsVisible = !online);

    private async void OnOfflineIconTapped(object? sender, TappedEventArgs e)
    {
        var page = Window?.Page;
        if (page is not null && _connectivity is not null)
        {
            await page.DisplayAlert("Sin conexión", OfflineReasons.Describe(_connectivity.Reason), "Entendido");
        }
    }
}
