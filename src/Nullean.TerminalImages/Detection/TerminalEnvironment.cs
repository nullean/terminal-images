namespace Nullean.TerminalImages.Detection;

/// <summary>A view of environment variables, so detection can be tested without touching the real environment.</summary>
public sealed class TerminalEnvironment
{
	private readonly Func<string, string?> _get;

	private TerminalEnvironment(Func<string, string?> get) => _get = get;

	/// <summary>The process environment.</summary>
	public static TerminalEnvironment Current { get; } = new(Environment.GetEnvironmentVariable);

	/// <summary>An environment made of the given variables.</summary>
	public static TerminalEnvironment From(IReadOnlyDictionary<string, string> variables)
	{
		ArgumentNullException.ThrowIfNull(variables);
		return new TerminalEnvironment(name => variables.TryGetValue(name, out var value) ? value : null);
	}

	/// <summary>An environment that has <paramref name="name"/> set to <paramref name="value"/> and otherwise looks like this one.</summary>
	public TerminalEnvironment With(string name, string value)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);
		ArgumentNullException.ThrowIfNull(value);
		return new TerminalEnvironment(key => string.Equals(key, name, StringComparison.Ordinal) ? value : _get(key));
	}

	/// <summary>The value of a variable, or null when it is unset or empty.</summary>
	public string? Get(string name)
	{
		var value = _get(name);
		return string.IsNullOrEmpty(value) ? null : value;
	}

	/// <summary>Whether the variable is set to something.</summary>
	public bool Has(string name) => Get(name) is not null;

	/// <summary>Whether the variable equals <paramref name="expected"/>, ignoring case.</summary>
	public bool Is(string name, string expected) => string.Equals(Get(name), expected, StringComparison.OrdinalIgnoreCase);

	/// <summary>Whether the variable starts with <paramref name="prefix"/>, ignoring case.</summary>
	public bool StartsWith(string name, string prefix) => Get(name)?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true;
}
