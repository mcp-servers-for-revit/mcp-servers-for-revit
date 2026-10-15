using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Common;

/// <summary>
///     点状构件
/// </summary>
public class PointElement
{
    public PointElement()
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
    ///     定位点坐标
    /// </summary>
    [JsonProperty("locationPoint")]
    public JZPoint LocationPoint { get; set; }

    /// <summary>
    ///     宽度
    /// </summary>
    [JsonProperty("width")]
    public double Width { get; set; } = -1;

    /// <summary>
    ///     深度
    /// </summary>
    [JsonProperty("depth")]
    public double Depth { get; set; }

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
    ///     底部偏移
    /// </summary>
    [JsonProperty("baseOffset")]
    public double BaseOffset { get; set; }

    /// <summary>
    ///     旋转角度（度），用于非宿主构件（如家具）
    /// </summary>
    [JsonProperty("rotation")]
    public double Rotation { get; set; } = 0;

    /// <summary>
    ///     显式宿主墙体ElementId，-1表示自动检测
    /// </summary>
    [JsonProperty("hostWallId")]
    public long HostWallId { get; set; } = -1;

    /// <summary>
    ///     是否翻转门窗朝向
    /// </summary>
    [JsonProperty("facingFlipped")]
    public bool FacingFlipped { get; set; } = false;

    /// <summary>
    ///     参数化属性
    /// </summary>
    [JsonProperty("parameters")]
    public Dictionary<string, double> Parameters { get; set; }
}
