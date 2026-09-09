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
        //public string Message { get; set; }
        public T? Data { get; set; }
        public ApiError? Error { get; init; }
        public static ApiResponse<T> Ok(T data) => new()
        {
            Success = true,
            Data = data
        };

        public static ApiResponse<T> Fail(string code,string message) => new()
        {
            Success = true,
            Error = new ApiError(code, message)
        };

        public record ApiError(string Code, string Message);
    }
    
}
