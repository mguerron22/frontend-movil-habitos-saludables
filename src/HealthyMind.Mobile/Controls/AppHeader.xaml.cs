using HealthyMind.Mobile.Core.Connectivity;

namespace HealthyMind.Mobile.Controls;

public partial class AppHeader : ContentView
{
    private IConnectivityService? _connectivity;

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

        _connectivity = Handler?.MauiContext?.Services.GetService<IConnectivityService>();
        if (_connectivity is not null)
        {
            _connectivity.StatusChanged += OnStatusChanged;
            OfflineIcon.IsVisible = !_connectivity.IsOnline;
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
