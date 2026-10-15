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

namespace RevitMCPCommandSet.Models.Architecture;

/// <summary>
///     Information about room creation parameters
/// </summary>
public class RoomCreationInfo
{
    /// <summary>
    ///     Default constructor
    /// </summary>
    public RoomCreationInfo()
    {
        Location = new JZPoint(0, 0, 0);
        Options = new Dictionary<string, object>();
    }

    /// <summary>
    ///     Room location point (mm)
    /// </summary>
    [JsonProperty("location")]
    public JZPoint Location { get; set; }

    /// <summary>
    ///     Room name
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = "Room";

    /// <summary>
    ///     Room number
    /// </summary>
    [JsonProperty("number")]
    public string Number { get; set; }

    /// <summary>
    ///     Level elevation (mm)
    /// </summary>
    [JsonProperty("level")]
    public double Level { get; set; }

    /// <summary>
    ///     Level ID in Revit
    /// </summary>
    [JsonProperty("levelId")]
    public long LevelId { get; set; }

    /// <summary>
    ///     Room height (mm)
    /// </summary>
    [JsonProperty("height")]
    public double Height { get; set; }

    /// <summary>
    ///     Room phase ID
    /// </summary>
    [JsonProperty("phaseId")]
    public long PhaseId { get; set; }

    /// <summary>
    ///     Upper limit level ID
    /// </summary>
    [JsonProperty("upperLimitId")]
    public long UpperLimitId { get; set; }

    /// <summary>
    ///     Offset from upper limit (mm)
    /// </summary>
    [JsonProperty("limitOffset")]
    public double LimitOffset { get; set; }

    /// <summary>
    ///     Offset from base level (mm)
    /// </summary>
    [JsonProperty("baseOffset")]
    public double BaseOffset { get; set; }

    /// <summary>
    ///     Department the room belongs to
    /// </summary>
    [JsonProperty("department")]
    public string Department { get; set; }

    /// <summary>
    ///     Comments for the room
    /// </summary>
    [JsonProperty("comments")]
    public string Comments { get; set; }

    /// <summary>
    ///     Additional options
    /// </summary>
    [JsonProperty("options")]
    public Dictionary<string, object> Options { get; set; }
}