namespace Carina.Api.Common;

public class ServiceResult
{
    protected ServiceResult(bool isSuccess, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public string? ErrorMessage { get; }

    public static ServiceResult Success() => new(true, null);

    public static ServiceResult Failure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ServiceResult(false, errorMessage);
    }
}

public class ServiceResult<T> : ServiceResult
{
    protected ServiceResult(bool isSuccess, T? data, string? errorMessage)
        : base(isSuccess, errorMessage)
    {
        Data = data;
    }

    public T? Data { get; }

    public static ServiceResult<T> Success(T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new ServiceResult<T>(true, data, null);
    }

    public new static ServiceResult<T> Failure(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ServiceResult<T>(false, default, errorMessage);
    }
}

public sealed class ServiceResult<T, TError> : ServiceResult<T>
    where TError : struct, Enum
{
    private ServiceResult(bool isSuccess, T? data, string? errorMessage, TError errorType)
        : base(isSuccess, data, errorMessage)
    {
        ErrorType = errorType;
    }

    public TError ErrorType { get; }

    public new static ServiceResult<T, TError> Success(T data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return new ServiceResult<T, TError>(true, data, null, default);
    }

    public static ServiceResult<T, TError> Failure(string errorMessage, TError errorType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        return new ServiceResult<T, TError>(false, default, errorMessage, errorType);
    }
}
