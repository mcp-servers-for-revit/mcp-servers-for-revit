// 
//                       RevitAPI-Solutions
// Copyright (c) Duong Tran Quang (DTDucas) (baymax.contact@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
//

using Newtonsoft.Json;
using RevitMCPCommandSet.Models.Common;

namespace RevitMCPCommandSet.Models.Annotation;

/// <summary>
///     Information about dimension creation parameters
/// </summary>
public class DimensionCreationInfo
{
    /// <summary>
    ///     Default constructor
    /// </summary>
    public DimensionCreationInfo()
    {
        StartPoint = new JZPoint(0, 0, 0);
        EndPoint = new JZPoint(0, 0, 0);
        ElementIds = new List<long>();
        Options = new Dictionary<string, object>();
    }

    /// <summary>
    ///     Dimension start point (mm)
    /// </summary>
    [JsonProperty("startPoint")]
    public JZPoint StartPoint { get; set; }

    /// <summary>
    ///     Dimension end point (mm)
    /// </summary>
    [JsonProperty("endPoint")]
    public JZPoint EndPoint { get; set; }

    /// <summary>
    ///     Dimension line point - location of dimension line (mm)
    /// </summary>
    [JsonProperty("linePoint")]
    public JZPoint LinePoint { get; set; }

    /// <summary>
    ///     Elements to dimension
    /// </summary>
    [JsonProperty("elementIds")]
    public List<long> ElementIds { get; set; }

    /// <summary>
    ///     Dimension type
    /// </summary>
    [JsonProperty("dimensionType")]
    public string DimensionType { get; set; } = "Linear";

    /// <summary>
    ///     Dimension style ID
    /// </summary>
    [JsonProperty("dimensionStyleId")]
    public long DimensionStyleId { get; set; } = -1;

    /// <summary>
    ///     View ID - view to create dimension in
    /// </summary>
    [JsonProperty("viewId")]
    public long ViewId { get; set; } = -1;

    /// <summary>
    ///     Additional options
    /// </summary>
    [JsonProperty("options")]
    public Dictionary<string, object> Options { get; set; }
}