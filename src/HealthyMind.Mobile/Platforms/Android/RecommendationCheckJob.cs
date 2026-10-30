using Android.App;
using Android.App.Job;
using Android.Content;
using Android.OS;
using HealthyMind.Mobile.Core.Notifications;
using Microsoft.Extensions.Logging;

namespace HealthyMind.Mobile.Platforms.Android;

/// <summary>
/// Revisión periódica con la app cerrada: Android la despierta más o menos cada hora (cuando hay red y le
/// conviene a la batería), sincroniza y avisa si llegó una recomendación nueva. La IA genera como mucho una
/// por día, así que no hace falta más seguido; sin Firebase ni servidor de push.
/// </summary>
[Service(Name = "ec.edu.uisrael.healthymind.RecommendationCheckJob", Permission = "android.permission.BIND_JOB_SERVICE", Exported = false)]
internal sealed class RecommendationCheckJob : JobService
{
    private const int JobId = 1001;
    private const int PresentationJobId = 1002;
    private const string AskForNewExtra = "ask_for_new";
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private CancellationTokenSource? _cancellation;

    /// <summary>Programa la revisión si no lo está; sobrevive a reinicios del teléfono.</summary>
    public static void Schedule(Context context)
    {
        var scheduler = (JobScheduler)context.GetSystemService(Context.JobSchedulerService)!;
        if (scheduler.GetPendingJob(JobId) is not null)
        {
            return;
        }

        var job = new JobInfo.Builder(JobId, new ComponentName(context, Java.Lang.Class.FromType(typeof(RecommendationCheckJob))));
        job.SetRequiredNetworkType(NetworkType.Any);
        job.SetPeriodic((long)Interval.TotalMilliseconds);
        job.SetPersisted(true);
        scheduler.Schedule(job.Build()!);
    }

    /// <summary>
    /// Modo presentación: una revisión ya mismo que además pide a la IA una recomendación nueva.
    /// Va como trabajo (y no dentro de la alarma) porque la IA puede tardar más de lo que Android le da a una alarma.
    /// </summary>
    public static void RunPresentationCheck(Context context)
    {
        var extras = new PersistableBundle();
        extras.PutBoolean(AskForNewExtra, true);

        var job = new JobInfo.Builder(PresentationJobId, new ComponentName(context, Java.Lang.Class.FromType(typeof(RecommendationCheckJob))));
        job.SetRequiredNetworkType(NetworkType.Any);
        job.SetExtras(extras);
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            job.SetExpedited(true); // Corre enseguida aunque la app esté cerrada.
        }
        else
        {
            job.SetOverrideDeadline(0);
        }

        ((JobScheduler)context.GetSystemService(Context.JobSchedulerService)!).Schedule(job.Build()!);
    }

    public override bool OnStartJob(JobParameters? parameters)
    {
        var services = IPlatformApplication.Current?.Services;
        if (services is null)
        {
            return false;
        }

        _cancellation = new CancellationTokenSource();
        var askForNew = parameters?.Extras.GetBoolean(AskForNewExtra) == true;
        _ = RunAsync(services, parameters, askForNew, _cancellation.Token);
        return true; // Sigue trabajando en segundo plano; avisa con JobFinished.
    }

    public override bool OnStopJob(JobParameters? parameters)
    {
        _cancellation?.Cancel();
        return true; // Android lo cortó (se fue la red): que lo reintente.
    }

    private async Task RunAsync(IServiceProvider services, JobParameters? parameters, bool askForNew, CancellationToken cancellationToken)
    {
        try
        {
            await services.GetRequiredService<BackgroundCheck>().RunAsync(askForNew, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Una revisión fallida no debe cerrar la app: la próxima vuelta lo intenta de nuevo.
            services.GetService<ILogger<RecommendationCheckJob>>()?.LogWarning(exception, "Falló la revisión en segundo plano.");
        }
        finally
        {
            JobFinished(parameters, wantsReschedule: false);
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }
}
