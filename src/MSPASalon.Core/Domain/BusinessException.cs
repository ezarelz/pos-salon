namespace MSPASalon.Core.Domain;

/// <summary>
/// A business-rule violation that should be shown to the user as-is (Indonesian message).
/// Anything else that escapes a service is a technical error.
/// </summary>
public sealed class BusinessException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public BusinessException(string message) : base(message) => Errors = [message];

    public BusinessException(IReadOnlyList<string> errors) : base(string.Join(Environment.NewLine, errors))
        => Errors = errors;
}
