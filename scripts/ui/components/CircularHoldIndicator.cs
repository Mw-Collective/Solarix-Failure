using Godot;

namespace SolarixFailure;

public partial class CircularHoldIndicator : Control
{
    private static readonly Color RingTrack = new(0.18f, 0.25f, 0.12f, 0.82f);
    private static readonly Color RingProgress = new(0.58f, 0.76f, 0.16f);
    private static readonly Color RingGlow = new(0.58f, 0.76f, 0.16f, 0.16f);
    private double _value;

    [Export(PropertyHint.Range, "0,100,0.1")]
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0.0, 100.0);
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        Vector2 center = Size * 0.5f;
        float radius = MathF.Max(1.0f, MathF.Min(Size.X, Size.Y) * 0.5f - 5.0f);
        const float startAngle = -MathF.PI / 2.0f;
        float endAngle = startAngle + (Mathf.Tau * (float)(_value / 100.0));

        DrawCircle(center, radius - 2.0f, new Color(0.004f, 0.01f, 0.006f, 0.76f));
        DrawArc(center, radius, startAngle, startAngle + Mathf.Tau, 64,
            RingTrack, 2.0f, true);

        if (_value <= 0.0)
            return;

        DrawArc(center, radius, startAngle, endAngle, 64,
            RingGlow, 7.0f, true);
        DrawArc(center, radius, startAngle, endAngle, 64,
            RingProgress, 3.0f, true);

        Vector2 endpoint = center + Vector2.FromAngle(endAngle) * radius;
        DrawCircle(endpoint, 1.5f, RingProgress);
    }
}
