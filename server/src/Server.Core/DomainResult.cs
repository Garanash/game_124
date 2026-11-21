using Realmforge.Rules;

namespace Realmforge.Server.Core;

public interface IDomainOutcome
{
    bool Success { get; }
}

/// <summary>Serializable outcome returned across grain and HTTP boundaries; codes are stable for client localization.</summary>
public sealed record DomainResult(bool Success, string Code, string Message) : IDomainOutcome
{
    public static readonly DomainResult Ok = new(true, "ok", string.Empty);

    public static DomainResult Fail(string code, string message) => new(false, code, message);

    public static DomainResult From(RuleResult result) => result.Success ? Ok : Fail(result.Code, result.Message);
}

public sealed record DomainResult<T>(bool Success, string Code, string Message, T? Value) : IDomainOutcome
{
    public static DomainResult<T> Ok(T value) => new(true, "ok", string.Empty, value);

    public static DomainResult<T> Fail(string code, string message) => new(false, code, message, default);

    public static DomainResult<T> Fail(DomainResult result) => new(false, result.Code, result.Message, default);
}
