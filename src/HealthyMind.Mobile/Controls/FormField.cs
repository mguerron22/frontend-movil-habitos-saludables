using Microsoft.Maui.Controls.Shapes;

namespace HealthyMind.Mobile.Controls;

/// <summary>
/// Campo de formulario con el estilo del mock: etiqueta, caja rosada redondeada con ícono y mensaje de error debajo.
/// <code>&lt;controls:FormField Label="Usuario" Icon="ic_user.png" Text="{Binding Username}" Error="{Binding UsernameError}" /&gt;</code>
/// </summary>
public sealed class FormField : ContentView
{
    public static readonly BindableProperty LabelProperty =
        BindableProperty.Create(nameof(Label), typeof(string), typeof(FormField), string.Empty);

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(ImageSource), typeof(FormField));

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(FormField), string.Empty, BindingMode.TwoWay);

    public static readonly BindableProperty PlaceholderProperty =
        BindableProperty.Create(nameof(Placeholder), typeof(string), typeof(FormField), string.Empty);

    public static readonly BindableProperty ErrorProperty =
        BindableProperty.Create(nameof(Error), typeof(string), typeof(FormField), propertyChanged: OnErrorChanged);

    public static readonly BindableProperty IsPasswordProperty =
        BindableProperty.Create(nameof(IsPassword), typeof(bool), typeof(FormField), false);

    public static readonly BindableProperty KeyboardProperty =
        BindableProperty.Create(nameof(Keyboard), typeof(Keyboard), typeof(FormField), Keyboard.Default);

    private readonly Label _error;
    private readonly Border _shell;

    public FormField()
    {
        var label = new Label { Style = (Style)Application.Current!.Resources["FieldLabel"] };
        label.SetBinding(Microsoft.Maui.Controls.Label.TextProperty, static (FormField field) => field.Label, source: this);

        var icon = new Image { WidthRequest = 20, HeightRequest = 20, VerticalOptions = LayoutOptions.Center };
        icon.SetBinding(Image.SourceProperty, static (FormField field) => field.Icon, source: this);

        var entry = new Entry { IsSpellCheckEnabled = false, IsTextPredictionEnabled = false };
        entry.SetBinding(Entry.TextProperty, static (FormField field) => field.Text, BindingMode.TwoWay, source: this);
        entry.SetBinding(Entry.PlaceholderProperty, static (FormField field) => field.Placeholder, source: this);
        entry.SetBinding(Entry.IsPasswordProperty, static (FormField field) => field.IsPassword, source: this);
        entry.SetBinding(InputView.KeyboardProperty, static (FormField field) => field.Keyboard, source: this);
        Grid.SetColumn(entry, 1);

        _shell = new Border
        {
            Style = (Style)Application.Current.Resources["InputShell"],
            Content = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
                ColumnSpacing = 10,
                Children = { icon, entry },
            },
        };

        _error = new Label { Style = (Style)Application.Current.Resources["FieldError"], IsVisible = false };

        Content = new VerticalStackLayout { Spacing = 6, Children = { label, _shell, _error } };
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string? Error
    {
        get => (string?)GetValue(ErrorProperty);
        set => SetValue(ErrorProperty, value);
    }

    public bool IsPassword
    {
        get => (bool)GetValue(IsPasswordProperty);
        set => SetValue(IsPasswordProperty, value);
    }

    public Keyboard Keyboard
    {
        get => (Keyboard)GetValue(KeyboardProperty);
        set => SetValue(KeyboardProperty, value);
    }

    /// <summary>Con error: mensaje visible y borde rojo para que se note qué campo corregir.</summary>
    private static void OnErrorChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var field = (FormField)bindable;
        var message = newValue as string;
        var hasError = !string.IsNullOrEmpty(message);

        field._error.Text = message;
        field._error.IsVisible = hasError;
        field._shell.Stroke = hasError ? Color.FromArgb("#B42318") : Colors.Transparent;
        field._shell.StrokeThickness = hasError ? 1.5 : 0;
        field._shell.StrokeShape = new RoundRectangle { CornerRadius = 16 };
    }
}
