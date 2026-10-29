namespace HealthyMind.Mobile.Controls;

/// <summary>
/// Anillo de progreso dibujado a mano (GraphicsView, sin librerías), como el "3/5" de la tarjeta de energía.
/// <code>&lt;controls:ProgressRing Progress="{Binding Progress}" WidthRequest="84" HeightRequest="84" /&gt;</code>
/// </summary>
public sealed class ProgressRing : GraphicsView
{
    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(ProgressRing), 0.0, propertyChanged: Redraw);

    public static readonly BindableProperty ThicknessProperty =
        BindableProperty.Create(nameof(Thickness), typeof(float), typeof(ProgressRing), 8f, propertyChanged: Redraw);

    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(ProgressRing), Color.FromArgb("#33FFFFFF"), propertyChanged: Redraw);

    public static readonly BindableProperty FillColorProperty =
        BindableProperty.Create(nameof(FillColor), typeof(Color), typeof(ProgressRing), Color.FromArgb("#5FE3CC"), propertyChanged: Redraw);

    public ProgressRing()
    {
        Drawable = new RingDrawable(this);
    }

    /// <summary>De 0 a 1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public float Thickness
    {
        get => (float)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    public Color FillColor
    {
        get => (Color)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) => ((GraphicsView)bindable).Invalidate();

    private sealed class RingDrawable(ProgressRing ring) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var inset = ring.Thickness / 2 + 1;
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height) - inset * 2;
            var x = dirtyRect.Center.X - size / 2;
            var y = dirtyRect.Center.Y - size / 2;

            canvas.StrokeSize = ring.Thickness;
            canvas.StrokeLineCap = LineCap.Round;

            canvas.StrokeColor = ring.TrackColor;
            canvas.DrawEllipse(x, y, size, size);

            var progress = Math.Clamp(ring.Progress, 0, 1);
            if (progress <= 0)
            {
                return;
            }

            canvas.StrokeColor = ring.FillColor;
            if (progress >= 1)
            {
                canvas.DrawEllipse(x, y, size, size);
                return;
            }

            // Empieza arriba (90°) y avanza en sentido horario.
            var end = 90 - (float)(360 * progress);
            canvas.DrawArc(x, y, size, size, 90, end, clockwise: true, closed: false);
        }
    }
}

/// <summary>Barra de progreso con extremos redondeados (la de "Progreso diario").</summary>
public sealed class RoundedProgressBar : GraphicsView
{
    public static readonly BindableProperty ProgressProperty =
        BindableProperty.Create(nameof(Progress), typeof(double), typeof(RoundedProgressBar), 0.0, propertyChanged: Redraw);

    public static readonly BindableProperty TrackColorProperty =
        BindableProperty.Create(nameof(TrackColor), typeof(Color), typeof(RoundedProgressBar), Color.FromArgb("#33FFFFFF"), propertyChanged: Redraw);

    public static readonly BindableProperty FillColorProperty =
        BindableProperty.Create(nameof(FillColor), typeof(Color), typeof(RoundedProgressBar), Color.FromArgb("#5FE3CC"), propertyChanged: Redraw);

    public RoundedProgressBar()
    {
        HeightRequest = 10;
        Drawable = new BarDrawable(this);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    public Color FillColor
    {
        get => (Color)GetValue(FillColorProperty);
        set => SetValue(FillColorProperty, value);
    }

    private static void Redraw(BindableObject bindable, object oldValue, object newValue) => ((GraphicsView)bindable).Invalidate();

    private sealed class BarDrawable(RoundedProgressBar bar) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var radius = dirtyRect.Height / 2;
            canvas.FillColor = bar.TrackColor;
            canvas.FillRoundedRectangle(dirtyRect, radius);

            var progress = (float)Math.Clamp(bar.Progress, 0, 1);
            if (progress <= 0)
            {
                return;
            }

            // Nunca más angosta que su alto, para que el extremo redondeado se vea bien.
            var width = Math.Max(dirtyRect.Width * progress, dirtyRect.Height);
            canvas.FillColor = bar.FillColor;
            canvas.FillRoundedRectangle(dirtyRect.X, dirtyRect.Y, width, dirtyRect.Height, radius);
        }
    }
}
