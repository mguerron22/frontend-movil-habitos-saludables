using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Views;

public partial class HabitsPage : ContentPage
{
    private readonly HabitsViewModel _viewModel;

    public HabitsPage(HabitsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
}
