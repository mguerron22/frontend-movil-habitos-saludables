using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Navigation;

namespace HealthyMind.Mobile;

public partial class App : Application
{
    private readonly AppShell _shell;

    public App(AppShell shell, ISessionManager session, INavigationService navigation)
    {
        InitializeComponent();
        _shell = shell;

        // El diseño es claro; no se adapta al modo oscuro del sistema.
        UserAppTheme = AppTheme.Light;

        // Si la sesión muere en medio del uso (venció o suspendieron la cuenta), se vuelve al login con el motivo.
        session.SessionEnded += (_, args) => _ = navigation.GoToLoginAsync(args.Message);
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(_shell) { Title = "HealthyMindIA" };
}
