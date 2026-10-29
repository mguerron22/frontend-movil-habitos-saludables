using HealthyMind.Mobile.Core.ViewModels;
using HealthyMind.Mobile.Services;

namespace HealthyMind.Mobile.Views;

public partial class HabitEditorPage : ContentPage, IQueryAttributable
{
    private readonly HabitEditorViewModel _viewModel;

    public HabitEditorPage(HabitEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    /// <summary>Recibe el id del hábito a editar; sin id es un hábito nuevo.</summary>
    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var habitId = query.TryGetValue(Routes.HabitIdParameter, out var value) && value is Guid id ? id : (Guid?)null;
        await _viewModel.LoadAsync(habitId);
    }
}
