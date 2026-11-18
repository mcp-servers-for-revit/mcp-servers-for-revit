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

namespace RevitMCPCommandSet.Models.Views;

/// <summary>
///     Information for view creation
/// </summary>
public class ViewCreationInfo
{
    /// <summary>
    ///     Default constructor
    /// </summary>
    public ViewCreationInfo()
    {
        Parameters = new Dictionary<string, object>();
    }

    /// <summary>
    ///     View name
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     View type (FloorPlan, CeilingPlan, Elevation, Section, 3D)
    /// </summary>
    [JsonProperty("viewType")]
    public string ViewType { get; set; } = "FloorPlan";

    /// <summary>
    ///     Level elevation in millimeters (for plan views)
    /// </summary>
    [JsonProperty("levelElevation")]
    public double LevelElevation { get; set; }

    /// <summary>
    ///     View detail level (Coarse, Medium, Fine)
    /// </summary>
    [JsonProperty("detailLevel")]
    public string DetailLevel { get; set; } = "Medium";

    /// <summary>
    ///     View scale (e.g., 100 for 1:100)
    /// </summary>
    [JsonProperty("scale")]
    public int Scale { get; set; } = 100;

    /// <summary>
    ///     View family type name
    /// </summary>
    [JsonProperty("viewFamilyTypeName")]
    public string ViewFamilyTypeName { get; set; } = string.Empty;

    /// <summary>
    ///     Template view unique ID to apply
    /// </summary>
    [JsonProperty("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>
    ///     View direction for elevation/section views
    /// </summary>
    [JsonProperty("direction")]
    public JZPoint Direction { get; set; }

    /// <summary>
    ///     Additional parameters
    /// </summary>
    [JsonProperty("parameters")]
    public Dictionary<string, object> Parameters { get; set; }
}