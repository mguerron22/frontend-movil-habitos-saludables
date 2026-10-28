using HealthyMind.Mobile.Services;
using HealthyMind.Mobile.Views;

namespace HealthyMind.Mobile;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Pantallas que se apilan encima de otra (con "atrás"). Las partes 2 y 3 registran aquí las suyas.
        Routing.RegisterRoute(Routes.Register, typeof(RegisterPage));
    }
}
