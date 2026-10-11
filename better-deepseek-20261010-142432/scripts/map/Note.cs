namespace CustomRhythia.Map;

public enum HitResult
{
    None,
    Hit,
    Miss,
}

/// <summary>
/// A single hit object placed on the 3x3 grid.
/// X and Y are in [-1, 1]; (0,0) is the center cell.
/// </summary>
public class Note
{
    public int Index;
    public int Millisecond;
    public float X;
    public float Y;

    public HitResult LastResult = HitResult.None;

    /// <summary>
    /// Timing offset in ms for the score graph (negative = miss).
    /// </summary>
    public float HitOffset = 0f;

    public Note(int index, int millisecond, float x, float y)
    {
        Index = index;
        Millisecond = millisecond;
        X = x;
        Y = y;
    }

    /// <summary>
    /// Returns true if the given cursor position (world units) overlaps this note.
    /// </summary>
    public bool IsCursorInside(float cursorX, float cursorY)
    {
        float pad = (float)(Constants.HIT_BOX_SIZE + 0.5);
        return cursorX >= X - pad && cursorX <= X + pad
            && cursorY >= Y - pad && cursorY <= Y + pad;
    }
}
