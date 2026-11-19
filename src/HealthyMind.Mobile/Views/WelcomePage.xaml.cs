using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Views;

public partial class WelcomePage : ContentPage
{
    private readonly WelcomeViewModel _viewModel;

    public WelcomePage(WelcomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>Al volver de los ajustes de batería del sistema se actualiza el estado.</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Refresh();
    }
}
