namespace LanFlow.Desktop.Presentation;

public readonly record struct WindowAppearanceState(
    double WindowOpacity,
    byte SurfaceAlpha,
    double ContentOpacity);
