namespace GraderFunctionApp.Models
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public T? Data { get; set; }
        public string? Error { get; set; }
        public string? Details { get; set; }
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        public static ApiResponse<T> SuccessResult(T data)
        {
            return new ApiResponse<T>
            {
                Success = true,
                Data = data
            };
        }

        public static ApiResponse<T> ErrorResult(string error, string? details = null)
        {
            return new ApiResponse<T>
            {
                Success = false,
                Error = error,
                Details = details
            };
        }
    }

    public class ApiResponse
    {
        public bool Success { get; set; }
        public object? Data { get; set; }
        public string? Error { get; set; }
        public string? Details { get; set; }
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        public static ApiResponse SuccessResult()
        {
            return new ApiResponse
            {
                Success = true
            };
        }

        public static ApiResponse ErrorResult(string error, string? details = null)
        {
            return new ApiResponse
            {
                Success = false,
                Error = error,
                Details = details
            };
        }
    }
}
