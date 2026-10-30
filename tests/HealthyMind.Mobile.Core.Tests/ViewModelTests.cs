using HealthyMind.Mobile.Core.Auth;
using HealthyMind.Mobile.Core.Navigation;
using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class ViewModelTests
{
    private static RegisterViewModel NewRegister() => new(null!, null!, null!);

    [Fact]
    public void Register_validation_matches_backend_rules()
    {
        var vm = NewRegister();
        vm.Username = "ab";
        vm.Email = "no-es-correo";
        vm.Password = "corta";
        vm.ConfirmPassword = "otra";

        Assert.False(vm.Validate());
        Assert.Equal("Escribe tu nombre.", vm.FirstNameError);
        Assert.Equal("El usuario debe tener entre 4 y 50 caracteres.", vm.UsernameError);
        Assert.Equal("Escribe un correo válido.", vm.EmailError);
        Assert.Equal("Mínimo 8 caracteres.", vm.PasswordError);
        Assert.Equal("Las contraseñas no coinciden.", vm.ConfirmPasswordError);
    }

    [Fact]
    public void Password_needs_upper_lower_and_digit()
    {
        var vm = NewRegister();
        vm.Password = vm.ConfirmPassword = "solominusculas1";

        vm.Validate();

        Assert.Equal("Usa al menos una mayúscula, una minúscula y un número.", vm.PasswordError);
    }

    [Fact]
    public void Valid_form_has_no_errors()
    {
        var vm = NewRegister();
        vm.FirstName = "Ana";
        vm.LastName = "Pérez";
        vm.Username = "ana.perez_01";
        vm.Email = "ana@correo.com";
        vm.Password = vm.ConfirmPassword = "Clave1234";

        Assert.True(vm.Validate());
    }

    [Theory]
    [InlineData(7, "¡Buenos días")]
    [InlineData(15, "¡Buenas tardes")]
    [InlineData(21, "¡Buenas noches")]
    public void Greeting_depends_on_the_hour(int hour, string expected) =>
        Assert.Equal(expected, HomeViewModel.GreetingFor(hour));

    [Fact]
    public async Task Login_requires_both_fields_before_calling_the_server()
    {
        var vm = new LoginViewModel(null!, null!, new FakeSettings(), new FakeConnectivity(), null!, null!);

        await vm.LoginCommand.ExecuteAsync(null);

        Assert.Equal("Escribe tu usuario o correo y tu contraseña.", vm.ErrorMessage);
    }

    [Fact]
    public async Task Biometrics_are_offered_only_once_and_only_if_available()
    {
        var settings = new FakeSettings();
        var dialogs = new CountingDialogs(accept: false);
        var enrollment = new BiometricEnrollment(new StubBiometric(available: true), settings, dialogs);

        await enrollment.OfferIfNeededAsync();
        await enrollment.OfferIfNeededAsync();

        Assert.Equal(1, dialogs.Confirms);
        Assert.True(settings.BiometricOffered);
        Assert.False(settings.BiometricEnabled);
    }

    [Fact]
    public async Task A_new_tap_replaces_a_fingerprint_dialog_that_never_answered()
    {
        var biometric = new SilentBiometric();
        var vm = new UnlockViewModel(null!, biometric, null!, null!);

        // Android descartó el primer diálogo sin avisar: sin el arreglo, el botón quedaba trabado.
        var first = vm.UnlockCommand.ExecuteAsync(null);
        var second = vm.UnlockCommand.ExecuteAsync(null);

        Assert.Equal(2, biometric.Prompts.Count);
        Assert.True(biometric.Prompts[0].IsCancellationRequested);
        Assert.False(biometric.Prompts[1].IsCancellationRequested);
        await first;

        biometric.Answer(new BiometricResult(BiometricOutcome.Failed, "Huella no reconocida."));
        await second;
        Assert.Equal("Huella no reconocida.", vm.ErrorMessage);
    }

    /// <summary>Como el BiometricPrompt descartado: solo termina si se cancela o si se le responde a mano.</summary>
    private sealed class SilentBiometric : IBiometricAuthenticator
    {
        private TaskCompletionSource<BiometricResult>? _current;

        public List<CancellationToken> Prompts { get; } = [];

        public Task<bool> IsAvailableAsync() => Task.FromResult(true);

        public Task<BiometricResult> AuthenticateAsync(string title, string subtitle, CancellationToken cancellationToken = default)
        {
            Prompts.Add(cancellationToken);
            var completion = new TaskCompletionSource<BiometricResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => completion.TrySetResult(new BiometricResult(BiometricOutcome.Cancelled)));
            _current = completion;
            return completion.Task;
        }

        public void Answer(BiometricResult result) => _current!.TrySetResult(result);
    }

    private sealed class StubBiometric(bool available) : IBiometricAuthenticator
    {
        public Task<bool> IsAvailableAsync() => Task.FromResult(available);

        public Task<BiometricResult> AuthenticateAsync(string title, string subtitle, CancellationToken cancellationToken = default) =>
            Task.FromResult(BiometricResult.Success());
    }

    private sealed class CountingDialogs(bool accept) : IDialogService
    {
        public int Confirms { get; private set; }

        public Task AlertAsync(string title, string message, string accept = "Entendido") => Task.CompletedTask;

        public Task<bool> ConfirmAsync(string title, string message, string acceptText, string cancel)
        {
            Confirms++;
            return Task.FromResult(accept);
        }

        public Task<string?> PromptAsync(string title, string message, string? initialValue, string placeholder, PromptKind kind = PromptKind.Text) =>
            Task.FromResult<string?>(null);
    }
}
