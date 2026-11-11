using System;

namespace GraduationPlatform.Common
{
    public class Result
    {
        public bool IsSuccess { get; set; }
        public string Message { get; set; }
        public string ErrorCode { get; set; }

        public Result(bool isSuccess, string message, string errorCode = null)
        {
            IsSuccess = isSuccess;
            Message = message;
            ErrorCode = errorCode;
        }

        public static Result Ok(string message = "Operation succeeded")
        {
            return new Result(true, message);
        }

        public static Result Fail(string message, string errorCode = null)
        {
            return new Result(false, message, errorCode);
        }
    }

    public class Result<T> : Result
    {
        public T Data { get; set; }

        public Result(bool isSuccess, T data, string message, string errorCode = null)
            : base(isSuccess, message, errorCode)
        {
            Data = data;
        }

        public static Result<T> Ok(T data, string message = "Operation succeeded")
        {
            return new Result<T>(true, data, message);
        }

        public new static Result<T> Fail(string message, string errorCode = null)
        {
            return new Result<T>(false, default(T), message, errorCode);
        }
    }
}
