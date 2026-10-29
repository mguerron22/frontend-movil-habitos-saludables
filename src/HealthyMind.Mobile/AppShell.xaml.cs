using HealthyMind.Mobile.Services;
using HealthyMind.Mobile.Views;

namespace HealthyMind.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pantallas que se apilan encima de otra (con "atrás"). Las pantallas nuevas registran aquí la suya.
        Routing.RegisterRoute(Routes.Register, typeof(RegisterPage));
        Routing.RegisterRoute(Routes.HabitEditor, typeof(HabitEditorPage));
    }
}
