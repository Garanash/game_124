namespace Realmforge.Rules
{
    /// <summary>Outcome of a rules validation; <see cref="Code"/> is a stable key for client localization.</summary>
    public readonly struct RuleResult
    {
        private RuleResult(bool success, string code, string message)
        {
            Success = success;
            Code = code;
            Message = message;
        }

        public bool Success { get; }
        public string Code { get; }
        public string Message { get; }

        public static RuleResult Ok()
        {
            return new RuleResult(true, "ok", string.Empty);
        }

        public static RuleResult Fail(string code, string message)
        {
            return new RuleResult(false, code, message);
        }

        public override string ToString()
        {
            return Success ? "ok" : Code + ": " + Message;
        }
    }
}
