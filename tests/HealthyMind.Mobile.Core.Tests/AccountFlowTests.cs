using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class AccountFlowTests
{
    [Theory]
    [InlineData("123456", true)]
    [InlineData(" 123 456 ", true)] // pegado del correo con espacios
    [InlineData("12345", false)]
    [InlineData("abcdef", false)]
    public void Email_code_needs_six_digits(string value, bool valid)
    {
        Assert.Equal(valid, EmailCode.IsValid(value));
    }

    [Fact]
    public void Welcome_is_shown_until_the_current_policy_is_accepted()
    {
        var settings = new FakeSettings();
        Assert.True(WelcomeViewModel.IsPending(settings));

        settings.ConsentVersion = WelcomeViewModel.CurrentConsentVersion;
        Assert.False(WelcomeViewModel.IsPending(settings));
    }

    [Fact]
    public void Dashboard_shows_tiles_and_fourteen_bars()
    {
        var today = new DateOnly(2026, 11, 19);
        var days = Enumerable.Range(0, 14).Select(i => new DailyCount(today.AddDays(i - 13), i == 13 ? 4 : 0)).ToList();
        var viewModel = new AdminDashboardViewModel(null!, null!);

        viewModel.Show(new AdminDashboard(10, 8, 2, 1, 3, 5, 6, 20, 40, 7, 75, 4, days));

        Assert.Equal("10", viewModel.AccountTiles[0].Value);
        Assert.Equal("75 % les sirvió", viewModel.UsageTiles[2].Caption);
        Assert.Equal(14, viewModel.SignUps.Count);
        Assert.Equal(80, viewModel.SignUps[^1].Height); // el día con más registros llena la barra
        Assert.Equal(4, viewModel.SignUps[0].Height); // los días en cero se ven como una línea
    }
}
