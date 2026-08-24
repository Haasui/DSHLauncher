namespace DshLauncher.Core;

/// <summary>官方 /api RPC 的业务错误（ok=false 时的 error.code/message）。</summary>
public sealed class DshApiException : Exception
{
    public DshApiException(string code, string message) : base($"{code}: {message}")
    {
        ErrorCode = code;
    }

    public string ErrorCode { get; }
}