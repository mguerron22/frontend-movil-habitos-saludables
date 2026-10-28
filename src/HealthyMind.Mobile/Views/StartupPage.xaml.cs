using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Views;

public partial class StartupPage : ContentPage
{
    private readonly StartupViewModel _viewModel;
    private bool _started;

    public StartupPage(StartupViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
        {
            return;
        }

        _started = true;
        await _viewModel.StartAsync();
    }
}
