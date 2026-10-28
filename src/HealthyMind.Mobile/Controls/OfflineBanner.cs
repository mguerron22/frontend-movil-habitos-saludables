using HealthyMind.Mobile.Core.Connectivity;
using Microsoft.Maui.Controls.Shapes;

namespace HealthyMind.Mobile.Controls;

/// <summary>
/// Franja que avisa "Sin conexión" y desaparece sola al volver. Se pone arriba de cualquier pantalla:
/// <c>&lt;controls:OfflineBanner /&gt;</c>.
/// </summary>
public sealed class OfflineBanner : ContentView
{
    private IConnectivityService? _connectivity;

    public OfflineBanner()
    {
        IsVisible = false;
        Content = new Border
        {
            BackgroundColor = Color.FromArgb("#FFEBD9"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(14, 10),
            Margin = new Thickness(20, 8, 20, 0),
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Image { Source = "ic_wifi_off.png", WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center },
                    new Label
                    {
                        Text = "Sin conexión. Tus cambios se guardan en el teléfono y se enviarán al volver.",
                        TextColor = Color.FromArgb("#B45309"),
                        FontSize = 13,
                        FontFamily = "OpenSansSemibold",
                        LineBreakMode = LineBreakMode.WordWrap,
                        MaximumWidthRequest = 280,
                        VerticalOptions = LayoutOptions.Center,
                    },
                },
            },
        };
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
            IsVisible = !_connectivity.IsOnline;
        }
    }

    // El aviso puede llegar desde un hilo de fondo (timer o petición HTTP): la UI se toca en el hilo principal.
    private void OnStatusChanged(object? sender, bool online) =>
        Dispatcher.Dispatch(() => IsVisible = !online);
}
