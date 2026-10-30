using HealthyMind.Mobile.Core.Models;
using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Core.Tests;

public sealed class HabitEditorTests
{
    private readonly HabitEditorViewModel _vm = new(null!, null!, null!);

    [Fact]
    public void Step_counter_is_offered_only_for_exercise()
    {
        _vm.SelectCategoryCommand.Execute(Category(HabitCategory.Hidratacion));
        Assert.False(_vm.CanCountSteps);

        _vm.SelectCategoryCommand.Execute(Category(HabitCategory.Ejercicio));
        Assert.True(_vm.CanCountSteps);
    }

    [Fact]
    public void Leaving_exercise_turns_the_step_counter_off()
    {
        _vm.SelectCategoryCommand.Execute(Category(HabitCategory.Ejercicio));
        _vm.UsesStepCounter = true;
        Assert.Equal("pasos", _vm.Unit);

        _vm.SelectCategoryCommand.Execute(Category(HabitCategory.Hidratacion));

        Assert.False(_vm.UsesStepCounter);
        Assert.Equal(string.Empty, _vm.Unit);
    }

    private CategoryOption Category(HabitCategory category) =>
        _vm.Categories.First(option => option.Info.Category == category);
}
