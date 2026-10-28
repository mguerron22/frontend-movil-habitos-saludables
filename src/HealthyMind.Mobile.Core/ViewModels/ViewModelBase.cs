using CommunityToolkit.Mvvm.ComponentModel;
using HealthyMind.Mobile.Core.Api;

namespace HealthyMind.Mobile.Core.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    public const string OfflineMessage = "No hay conexión con el servidor. Revisa tu internet e inténtalo de nuevo.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool IsNotBusy => !IsBusy;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// Ejecuta una acción mostrando el indicador de carga y convirtiendo los errores conocidos en un mensaje para el usuario.
    /// </summary>
    /// <returns>true si terminó sin errores.</returns>
    protected async Task<bool> RunAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await action();
            return true;
        }
        catch (ApiException exception)
        {
            OnApiError(exception);
            return false;
        }
        catch (ServerUnreachableException)
        {
            ErrorMessage = OfflineMessage;
            return false;
        }
        catch (SessionExpiredException exception)
        {
            // La app ya navega al login al enterarse; aquí solo se deja el motivo.
            ErrorMessage = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Las pantallas con formularios lo sobrescriben para marcar los campos con error.</summary>
    protected virtual void OnApiError(ApiException exception) => ErrorMessage = exception.Message;
}
