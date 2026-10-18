namespace Routegraph;

public sealed class HopGap(int number)
{
    public string NumberText { get; } = number.ToString("00");
    public string Description => $"Hop {NumberText} did not respond";
}
