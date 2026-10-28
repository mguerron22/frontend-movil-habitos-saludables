using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Views;

public partial class UnlockPage : ContentPage
{
    private readonly UnlockViewModel _viewModel;
    private bool _autoPrompted;

    public UnlockPage(UnlockViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>Al abrir la app se pide la huella de una vez (solo la primera vez); después, con el botón.</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();

        if (_autoPrompted)
        {
            return;
        }

        _autoPrompted = true;
        await _viewModel.UnlockCommand.ExecuteAsync(null);
    }
}
