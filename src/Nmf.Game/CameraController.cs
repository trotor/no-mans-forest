using Godot;

namespace Nmf.Game;

/// <summary>Pan with WASD/arrows, middle mouse drag or two-finger trackpad pan; zoom with wheel or pinch.</summary>
public partial class CameraController : Camera2D
{
    private const float PanSpeed = 900f;
    private const float MinZoom = 0.5f;
    private const float MaxZoom = 4f;

    public Vector2 WorldSize { get; set; }

    public override void _Ready()
    {
        // Keep the view inside the map instead of showing empty space past its edges.
        LimitLeft = 0;
        LimitTop = 0;
        LimitRight = (int)WorldSize.X;
        LimitBottom = (int)WorldSize.Y;
    }

    public override void _Process(double delta)
    {
        var direction = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) direction.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) direction.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) direction.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) direction.Y += 1;
        if (direction != Vector2.Zero)
            MoveBy(direction.Normalized() * PanSpeed * (float)delta / Zoom.X);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                ZoomBy(1.1f);
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                ZoomBy(1f / 1.1f);
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & MouseButtonMask.Middle) != 0:
                MoveBy(-motion.Relative / Zoom.X);
                break;
            case InputEventPanGesture pan:
                MoveBy(pan.Delta * 20f / Zoom.X);
                break;
            case InputEventMagnifyGesture magnify:
                ZoomBy(magnify.Factor);
                break;
        }
    }

    public void CenterOn(Vector2 point) => Position = ClampToWorld(point);

    private void MoveBy(Vector2 offset) => Position = ClampToWorld(Position + offset);

    /// <summary>Keeps the camera centre where the whole view stays on the map (or centred if the map is smaller).</summary>
    private Vector2 ClampToWorld(Vector2 centre)
    {
        var half = GetViewportRect().Size / (2f * Zoom.X);
        float x = WorldSize.X <= 2 * half.X ? WorldSize.X / 2 : Mathf.Clamp(centre.X, half.X, WorldSize.X - half.X);
        float y = WorldSize.Y <= 2 * half.Y ? WorldSize.Y / 2 : Mathf.Clamp(centre.Y, half.Y, WorldSize.Y - half.Y);
        return new Vector2(x, y);
    }

    private void ZoomBy(float factor)
    {
        float zoom = Mathf.Clamp(Zoom.X * factor, MinZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
        Position = ClampToWorld(Position);
    }
}
