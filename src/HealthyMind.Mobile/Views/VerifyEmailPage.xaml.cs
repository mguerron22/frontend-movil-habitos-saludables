using HealthyMind.Mobile.Core.ViewModels;
using HealthyMind.Mobile.Services;

namespace HealthyMind.Mobile.Views;

public partial class VerifyEmailPage : ContentPage, IQueryAttributable
{
    private readonly VerifyEmailViewModel _viewModel;

    public VerifyEmailPage(VerifyEmailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue(Routes.EmailParameter, out var email) && email is string text)
        {
            _viewModel.Email = text;
        }
    }
}
