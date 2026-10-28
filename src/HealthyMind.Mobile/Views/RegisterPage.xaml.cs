using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Views;

public partial class RegisterPage : ContentPage
{
    public RegisterPage(RegisterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
