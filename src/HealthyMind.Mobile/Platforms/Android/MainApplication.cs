using Android.App;
using Android.Runtime;

namespace HealthyMind.Mobile;

[Application]
public class MainApplication : MauiApplication
{
	public MainApplication(IntPtr handle, JniHandleOwnership ownership)
		: base(handle, ownership)
	{
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

	public override void OnCreate()
	{
		base.OnCreate();
		Platforms.Android.RecommendationCheckJob.Schedule(this);
		IPlatformApplication.Current?.Services.GetRequiredService<Core.Notifications.PresentationMode>().Restore();
	}
}
