using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Common;

/// <summary>
/// 线状构件
/// </summary>
public class LineElement
{
    public LineElement()
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
    ///     路径曲线
    /// </summary>
    [JsonProperty("locationLine")]
    public JZLine LocationLine { get; set; }

    /// <summary>
    ///     厚度
    /// </summary>
    [JsonProperty("thickness")]
    public double Thickness { get; set; }

    /// <summary>
    ///     高度
    /// </summary>
    [JsonProperty("height")]
    public double Height { get; set; }

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
    ///     底部偏移/基于面的偏移
    /// </summary>
    [JsonProperty("baseOffset")]
    public double BaseOffset { get; set; }

    /// <summary>
    ///     参数化属性
    /// </summary>
    [JsonProperty("parameters")]
    public Dictionary<string, double> Parameters { get; set; }
}