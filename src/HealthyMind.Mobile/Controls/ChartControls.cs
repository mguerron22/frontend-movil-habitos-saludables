using System.Collections;
using System.Collections.Specialized;
using HealthyMind.Mobile.Core.ViewModels;

namespace HealthyMind.Mobile.Controls;

/// <summary>Base de los gráficos: redibuja cuando cambia la colección enlazada.</summary>
public abstract class ChartView : GraphicsView
{
    public static readonly BindableProperty ItemsSourceProperty =
        BindableProperty.Create(nameof(ItemsSource), typeof(IEnumerable), typeof(ChartView), propertyChanged: OnItemsSourceChanged);

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    protected IReadOnlyList<T> Items<T>() => ItemsSource?.OfType<T>().ToList() ?? [];

    private static void OnItemsSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var chart = (ChartView)bindable;
        if (oldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= chart.OnCollectionChanged;
        }

        if (newValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += chart.OnCollectionChanged;
        }

        chart.Invalidate();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Invalidate();
}

/// <summary>
/// "Ritmo semanal" del mock: una pastilla por día con el cumplimiento; hoy en menta y con su porcentaje encima.
/// </summary>
public sealed class WeeklyBarsChart : ChartView
{
    private static readonly Color Track = Color.FromArgb("#F1E7E4");
    private static readonly Color Fill = Color.FromArgb("#0E6B5E");
    private static readonly Color TodayFill = Color.FromArgb("#5FE3CC");
    private static readonly Color Label = Color.FromArgb("#56625F");

    public WeeklyBarsChart()
    {
        HeightRequest = 220;
        Drawable = new BarsDrawable(this);
    }

    private sealed class BarsDrawable(WeeklyBarsChart chart) : IDrawable
    {
        private const float LabelHeight = 26;
        private const float TopLabel = 24;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var bars = chart.Items<BarItem>();
            if (bars.Count == 0)
            {
                return;
            }

            var slot = dirtyRect.Width / bars.Count;
            var barWidth = Math.Min(28f, slot * 0.55f);
            var top = dirtyRect.Top + TopLabel;
            var height = dirtyRect.Height - TopLabel - LabelHeight;

            canvas.FontSize = 13;
            for (var i = 0; i < bars.Count; i++)
            {
                var bar = bars[i];
                var x = dirtyRect.Left + slot * i + (slot - barWidth) / 2;

                canvas.FillColor = Track;
                canvas.FillRoundedRectangle(x, top, barWidth, height, barWidth / 2);

                if (!bar.IsFuture && bar.Value > 0)
                {
                    var filled = Math.Max(height * (float)bar.Value, barWidth);
                    canvas.FillColor = bar.IsToday ? TodayFill : Fill;
                    canvas.FillRoundedRectangle(x, top + height - filled, barWidth, filled, barWidth / 2);
                }

                if (bar.IsToday && !string.IsNullOrEmpty(bar.Percent))
                {
                    canvas.FontColor = Fill;
                    canvas.DrawString(bar.Percent, x - 20, dirtyRect.Top, barWidth + 40, TopLabel - 4, HorizontalAlignment.Center, VerticalAlignment.Center);
                }

                canvas.FontColor = Label;
                canvas.DrawString(bar.Letter, x - 10, top + height + 4, barWidth + 20, LabelHeight - 4, HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }
}

/// <summary>"Distribución por dimensión" del mock: dona con una porción por categoría y el total al centro.</summary>
public sealed class DonutChart : ChartView
{
    public static readonly BindableProperty CenterTextProperty =
        BindableProperty.Create(nameof(CenterText), typeof(string), typeof(DonutChart), string.Empty, propertyChanged: (b, _, _) => ((GraphicsView)b).Invalidate());

    public static readonly BindableProperty CaptionProperty =
        BindableProperty.Create(nameof(Caption), typeof(string), typeof(DonutChart), "focos", propertyChanged: (b, _, _) => ((GraphicsView)b).Invalidate());

    public DonutChart()
    {
        WidthRequest = 130;
        HeightRequest = 130;
        Drawable = new DonutDrawable(this);
    }

    public string CenterText
    {
        get => (string)GetValue(CenterTextProperty);
        set => SetValue(CenterTextProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    private sealed class DonutDrawable(DonutChart chart) : IDrawable
    {
        private const float Thickness = 18;
        private const float GapDegrees = 4;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var size = Math.Min(dirtyRect.Width, dirtyRect.Height) - Thickness - 2;
            var x = dirtyRect.Center.X - size / 2;
            var y = dirtyRect.Center.Y - size / 2;

            canvas.StrokeSize = Thickness;
            canvas.StrokeLineCap = LineCap.Butt;

            var slices = chart.Items<CategorySlice>();
            if (slices.Count <= 1)
            {
                // Un arco de 360° empieza y termina en el mismo punto y no dibuja nada: una sola porción es un círculo.
                canvas.StrokeColor = slices.Count == 0 ? Color.FromArgb("#F1E7E4") : Color.FromArgb(slices[0].Color);
                canvas.DrawEllipse(x, y, size, size);
            }
            else
            {
                // Desde arriba y en sentido horario, con una pequeña separación entre porciones.
                var start = 90f;
                foreach (var slice in slices)
                {
                    var sweep = (float)(360 * slice.Share);
                    var gap = slices.Count > 1 ? GapDegrees : 0;
                    canvas.StrokeColor = Color.FromArgb(slice.Color);
                    canvas.DrawArc(x, y, size, size, start - gap / 2, start - sweep + gap / 2, clockwise: true, closed: false);
                    start -= sweep;
                }
            }

            canvas.FontColor = Color.FromArgb("#1B2422");
            canvas.FontSize = 26;
            canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
            canvas.DrawString(chart.CenterText, dirtyRect.Left, dirtyRect.Center.Y - 22, dirtyRect.Width, 30, HorizontalAlignment.Center, VerticalAlignment.Center);
            canvas.Font = Microsoft.Maui.Graphics.Font.Default;
            canvas.FontSize = 12;
            canvas.FontColor = Color.FromArgb("#56625F");
            canvas.DrawString(chart.Caption, dirtyRect.Left, dirtyRect.Center.Y + 6, dirtyRect.Width, 18, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
