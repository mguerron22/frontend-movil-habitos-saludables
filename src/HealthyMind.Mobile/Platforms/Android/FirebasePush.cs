using Android.App;
using Android.Gms.Tasks;
using Firebase.Messaging;
using HealthyMind.Mobile.Core.Notifications;
using Microsoft.Extensions.Logging;
using Task = System.Threading.Tasks.Task;

namespace HealthyMind.Mobile.Platforms.Android;

/// <summary>Pide el token de Firebase Messaging. Sin Google Play Services (o sin google-services.json) devuelve null.</summary>
internal sealed class AndroidPushTokenSource(ILogger<AndroidPushTokenSource> logger) : IPushTokenSource
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<string?> GetTokenAsync()
    {
        try
        {
            var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            FirebaseMessaging.Instance.GetToken().AddOnCompleteListener(new TokenListener(completion));
            return await completion.Task.WaitAsync(Timeout);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            logger.LogInformation(exception, "Firebase no entregó el token de avisos push.");
            return null;
        }
    }

    private sealed class TokenListener(TaskCompletionSource<string?> completion) : Java.Lang.Object, IOnCompleteListener
    {
        public void OnComplete(global::Android.Gms.Tasks.Task task) =>
            completion.TrySetResult(task.IsSuccessful ? task.Result?.ToString() : null);
    }
}

/// <summary>
/// Recibe los push de datos de Firebase, aunque la app esté cerrada: arma el aviso y lanza una
/// sincronización para guardar la recomendación en el teléfono.
/// </summary>
[Service(Name = "ec.edu.uisrael.healthymind.HealthyMindMessagingService", Exported = false)]
[IntentFilter(["com.google.firebase.MESSAGING_EVENT"])]
internal sealed class HealthyMindMessagingService : FirebaseMessagingService
{
    public override void OnNewToken(string token)
    {
        base.OnNewToken(token);
        var registration = IPlatformApplication.Current?.Services.GetService<PushRegistration>();
        if (registration is not null)
        {
            _ = registration.EnsureRegisteredAsync(token);
        }
    }

    public override void OnMessageReceived(RemoteMessage message)
    {
        base.OnMessageReceived(message);
        var handler = IPlatformApplication.Current?.Services.GetService<PushMessageHandler>();
        if (handler is null)
        {
            return;
        }

        var data = message.Data.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (handler.Handle(data))
        {
            RecommendationCheckJob.RunNow(this, askForNew: false);
        }
    }
}
