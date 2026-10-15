using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Common;

/// <summary>
///     面状构件
/// </summary>
public class SurfaceElement
{
    public SurfaceElement()
    {
        Parameters = new Dictionary<string, double>();
    }

    /// <summary>
    ///     构件类型
    /// </summary>
    [JsonProperty("category")]
    public string Category { get; set; } = "INVALID";

    /// <summary>
    ///     类型Id
    /// </summary>
    [JsonProperty("typeId")]
    public long TypeId { get; set; } = -1;

    /// <summary>
    ///     壳形轮廓边界
    /// </summary>
    [JsonProperty("boundary")]
    public JZFace Boundary { get; set; }

    /// <summary>
    ///     厚度
    /// </summary>
    [JsonProperty("thickness")]
    public double Thickness { get; set; }

    /// <summary>
    /// Id of the level to place on. When set, baseLevel is ignored and baseOffset is measured from this level.
    /// </summary>
    [JsonProperty("levelId")]
    public long LevelId { get; set; } = -1;

    /// <summary>
    ///     底部标高
    /// </summary>
    [JsonProperty("baseLevel")]
    public double BaseLevel { get; set; }

    /// <summary>
    ///     底部偏移
    /// </summary>
    [JsonProperty("baseOffset")]
    public double BaseOffset { get; set; }

    /// <summary>
    ///     参数化属性
    /// </summary>
    public Dictionary<string, double> Parameters { get; set; }
}