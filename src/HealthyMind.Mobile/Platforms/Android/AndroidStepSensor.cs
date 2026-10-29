using Android.Content;
using Android.Hardware;
using Android.OS;
using HealthyMind.Mobile.Core.Habits;

namespace HealthyMind.Mobile.Platforms.Android;

/// <summary>
/// Contador de pasos del sistema (TYPE_STEP_COUNTER): lo cuenta el propio chip del teléfono aunque la app esté
/// cerrada, así que basta una lectura al abrir la pantalla de inicio.
/// </summary>
internal sealed class AndroidStepSensor : IStepSensor
{
    /// <summary>El sensor manda su valor al registrarse; si no llega en este tiempo, se deja para la próxima.</summary>
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(3);

    private static SensorManager? Manager => Platform.AppContext.GetSystemService(Context.SensorService) as SensorManager;

    public bool IsSupported => Manager?.GetDefaultSensor(SensorType.StepCounter) is not null;

    public async Task<bool> EnsurePermissionAsync()
    {
        // Antes de Android 10 el contador de pasos no pedía permiso.
        if (!OperatingSystem.IsAndroidVersionAtLeast(29))
        {
            return true;
        }

        var status = await Permissions.CheckStatusAsync<ActivityRecognitionPermission>();
        if (status != PermissionStatus.Granted)
        {
            status = await MainThread.InvokeOnMainThreadAsync(Permissions.RequestAsync<ActivityRecognitionPermission>);
        }

        return status == PermissionStatus.Granted;
    }

    public async Task<long?> ReadTotalAsync(CancellationToken cancellationToken = default)
    {
        var manager = Manager;
        var sensor = manager?.GetDefaultSensor(SensorType.StepCounter);
        if (manager is null || sensor is null)
        {
            return null;
        }

        var listener = new FirstReading();
        manager.RegisterListener(listener, sensor, SensorDelay.Normal);
        try
        {
            return await listener.Value.WaitAsync(ReadTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        finally
        {
            manager.UnregisterListener(listener);
        }
    }

    private sealed class FirstReading : Java.Lang.Object, ISensorEventListener
    {
        private readonly TaskCompletionSource<long?> _value = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<long?> Value => _value.Task;

        public void OnSensorChanged(SensorEvent? e)
        {
            if (e?.Values is { Count: > 0 } values)
            {
                _value.TrySetResult((long)values[0]);
            }
        }

        public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy)
        {
        }
    }
}

/// <summary>Permiso de actividad física (Android 10+), que MAUI no trae.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
internal sealed class ActivityRecognitionPermission : Permissions.BasePlatformPermission
{
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
        [(global::Android.Manifest.Permission.ActivityRecognition, true)];
}
