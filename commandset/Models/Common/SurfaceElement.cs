using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Common;

/// <summary>
///     Surface-based element
/// </summary>
public class SurfaceElement
{
    public SurfaceElement()
    {
        Parameters = new Dictionary<string, double>();
    }

    /// <summary>
    ///     Element category
    /// </summary>
    [JsonProperty("category")]
    public string Category { get; set; } = "INVALID";

    /// <summary>
    ///     Type ID
    /// </summary>
    [JsonProperty("typeId")]
    public int TypeId { get; set; } = -1;

    /// <summary>
    ///     Shell profile boundary
    /// </summary>
    [JsonProperty("boundary")]
    public JZFace Boundary { get; set; }

    /// <summary>
    ///     Thickness
    /// </summary>
    [JsonProperty("thickness")]
    public double Thickness { get; set; }

    /// <summary>
    ///     Base level
    /// </summary>
    [JsonProperty("baseLevel")]
    public double BaseLevel { get; set; }

    /// <summary>
    ///     Base offset
    /// </summary>
    [JsonProperty("baseOffset")]
    public double BaseOffset { get; set; }

    /// <summary>
    ///     Parametric properties
    /// </summary>
    public Dictionary<string, double> Parameters { get; set; }
}
