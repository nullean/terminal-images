namespace Nullean.TerminalImages;

/// <summary>A size in terminal cells.</summary>
public readonly record struct CellSize(int Columns, int Rows)
{
	/// <summary>Zero by zero: unspecified.</summary>
	public static CellSize Unspecified => default;

	/// <summary>Whether both dimensions are positive.</summary>
	public bool IsSpecified => Columns > 0 && Rows > 0;
}
