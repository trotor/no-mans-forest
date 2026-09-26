using Godot;

namespace Nmf.Game;

/// <summary>
/// The way to the next objective: an arrow at the edge of the screen (with the name and distance) while it is off
/// screen, a marker over it when it is in view.
/// </summary>
public partial class GuideView : Control
{
    private static readonly Color Fill = new(0.98f, 0.86f, 0.3f);
    private static readonly Color Ink = new(0.12f, 0.1f, 0.06f);

    /// <summary>Screen position of the objective, or null for no guide.</summary>
    public Vector2? Target { get; set; }
    public string Text { get; set; } = "";
    /// <summary>What the bars at the top and bottom cover.</summary>
    public float TopMargin { get; set; } = 110;
    public float BottomMargin { get; set; } = 200;

    public override void _Draw()
    {
        if (Target is not { } target)
            return;
        var font = ThemeDB.FallbackFont;
        var view = GetViewportRect().Size;
        var inner = new Rect2(40, TopMargin, view.X - 80, view.Y - TopMargin - BottomMargin);
        if (inner.Size.X <= 0 || inner.Size.Y <= 0)
            return;
        if (inner.HasPoint(target))
        {
            // In view: a small pointer above the place.
            var tip = target + new Vector2(0, -10);
            DrawColoredPolygon([tip, tip + new Vector2(-9, -16), tip + new Vector2(9, -16)], Fill);
            DrawPolyline([tip, tip + new Vector2(-9, -16), tip + new Vector2(9, -16), tip], Ink, 2f);
            Label(font, tip + new Vector2(0, -22));
            return;
        }
        // Off screen: an arrow on the edge of the view, pointing the way.
        var centre = inner.GetCenter();
        var dir = (target - centre).Normalized();
        float tx = dir.X == 0 ? float.MaxValue : (dir.X > 0 ? inner.End.X - centre.X : inner.Position.X - centre.X) / dir.X;
        float ty = dir.Y == 0 ? float.MaxValue : (dir.Y > 0 ? inner.End.Y - centre.Y : inner.Position.Y - centre.Y) / dir.Y;
        var at = centre + dir * Mathf.Min(tx, ty);
        var side = new Vector2(-dir.Y, dir.X);
        Vector2[] arrow = [at + dir * 18, at - dir * 10 + side * 12, at - dir * 4, at - dir * 10 - side * 12];
        DrawColoredPolygon(arrow, Fill);
        DrawPolyline([.. arrow, arrow[0]], Ink, 2f);
        Label(font, at - dir * 30);
    }

    private void Label(Font font, Vector2 centre)
    {
        var size = font.GetStringSize(Text, HorizontalAlignment.Left, -1, 16);
        var box = new Rect2(centre - new Vector2(size.X / 2 + 6, size.Y / 2 + 2), size + new Vector2(12, 4));
        var view = GetViewportRect().Size;
        box.Position = new Vector2(Mathf.Clamp(box.Position.X, 4, view.X - box.Size.X - 4), Mathf.Clamp(box.Position.Y, 4, view.Y - box.Size.Y - 4));
        DrawRect(box, new Color(0.93f, 0.9f, 0.8f, 0.92f));
        DrawRect(box, Ink, false, 1.5f);
        DrawString(font, box.Position + new Vector2(6, size.Y - 2), Text, HorizontalAlignment.Left, -1, 16, Ink);
    }
}
