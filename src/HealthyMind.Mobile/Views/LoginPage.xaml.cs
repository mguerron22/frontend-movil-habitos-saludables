using HealthyMind.Mobile.Core.ViewModels;
using HealthyMind.Mobile.Services;

namespace HealthyMind.Mobile.Views;

public partial class LoginPage : ContentPage, IQueryAttributable
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>Recibe el aviso que manda otra pantalla al traer al usuario de vuelta al login.</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(Routes.NoticeParameter, out var notice) && notice is string text)
        {
            _viewModel.Notice = string.IsNullOrEmpty(text) ? null : text;
        }
    }
}
