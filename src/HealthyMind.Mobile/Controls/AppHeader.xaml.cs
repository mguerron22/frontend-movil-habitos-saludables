namespace HealthyMind.Mobile.Controls;

public partial class AppHeader : ContentView
{
    public static readonly BindableProperty SectionProperty = BindableProperty.Create(
        nameof(Section),
        typeof(string),
        typeof(AppHeader),
        string.Empty,
        propertyChanged: (bindable, _, value) => ((AppHeader)bindable).SectionLabel.Text = (string)value);

    public static readonly BindableProperty InitialsProperty = BindableProperty.Create(
        nameof(Initials),
        typeof(string),
        typeof(AppHeader),
        string.Empty,
        propertyChanged: (bindable, _, value) => ((AppHeader)bindable).InitialsLabel.Text = (string)value);

    public AppHeader()
    {
        InitializeComponent();
    }

    /// <summary>Nombre de la pestaña debajo de la marca: "Inicio", "Estadísticas"...</summary>
    public string Section
    {
        get => (string)GetValue(SectionProperty);
        set => SetValue(SectionProperty, value);
    }

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }
}
