using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UltimatePos.Application.Common.Dtos
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public ApiError? Error { get; init; }
        public object? Meta { get; init; }

        public static ApiResponse<T> Ok(T data, object? meta = null) => new()
        {
            Success = true,
            Data = data,
            Meta = meta
        };

        public static ApiResponse<T> Fail(string code, string message, IReadOnlyDictionary<string, string[]>? details = null) => new()
        {
            Success = false,
            Error = new ApiError(code, message, details)
        };

        /// <param name="Details">Field-keyed messages (camelCase field names) for validation failures, so each can be shown beside its input.</param>
        public record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Details = null);
    }
}
