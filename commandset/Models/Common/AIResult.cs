namespace RevitMCPCommandSet.Models.Common;

public class AIResult<T>
{
    /// <summary>
    ///     Whether successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    ///     Message
    /// </summary>
    public string Message { get; set; }

    /// <summary>
    ///     Response data
    /// </summary>
    public T Response { get; set; }
}