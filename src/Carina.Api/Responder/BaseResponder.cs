namespace Carina.Api.Responder;

public sealed record BaseResponder<T>(bool Status, string Message, T? Data)
{
    public static BaseResponder<T> Success(T data) => new(true, string.Empty, data);

    public static BaseResponder<T> Error(string message) => new(false, message, default);
}
