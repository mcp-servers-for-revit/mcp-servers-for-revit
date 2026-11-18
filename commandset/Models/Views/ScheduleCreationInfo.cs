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

namespace RevitMCPCommandSet.Models.Views;

/// <summary>
///     Information for schedule creation
/// </summary>
public class ScheduleCreationInfo
{
    /// <summary>
    ///     Default constructor
    /// </summary>
    public ScheduleCreationInfo()
    {
        Parameters = new Dictionary<string, object>();
        Fields = new List<ScheduleFieldInfo>();
        Filters = new List<ScheduleFilterInfo>();
        SortFields = new List<ScheduleSortInfo>();
        GroupFields = new List<ScheduleGroupInfo>();
    }

    /// <summary>
    ///     Schedule name
    /// </summary>
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    ///     Schedule type (Regular, KeySchedule, MaterialTakeoff)
    /// </summary>
    [JsonProperty("type")]
    public string Type { get; set; } = "Regular";

    /// <summary>
    ///     Category ID for the schedule
    /// </summary>
    [JsonProperty("categoryId")]
    public int CategoryId { get; set; }

    /// <summary>
    ///     Category name for the schedule
    /// </summary>
    [JsonProperty("categoryName")]
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>
    ///     Template view unique ID to apply
    /// </summary>
    [JsonProperty("templateId")]
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>
    ///     Show title in schedule
    /// </summary>
    [JsonProperty("showTitle")]
    public bool? ShowTitle { get; set; } = true;

    /// <summary>
    ///     Show column headers in schedule
    /// </summary>
    [JsonProperty("showHeaders")]
    public bool? ShowHeaders { get; set; } = true;

    /// <summary>
    ///     Show grid lines in schedule
    /// </summary>
    [JsonProperty("showGridLines")]
    public bool? ShowGridLines { get; set; } = true;

    /// <summary>
    ///     Show outlines in schedule
    /// </summary>
    [JsonProperty("showOutlines")]
    public bool? ShowOutlines { get; set; } = true;

    /// <summary>
    ///     Preset name for pre-configured field sets
    /// </summary>
    [JsonProperty("preset")]
    public string Preset { get; set; } = string.Empty;

    /// <summary>
    ///     Whether the schedule is itemized (shows every instance)
    /// </summary>
    [JsonProperty("isItemized")]
    public bool? IsItemized { get; set; }

    /// <summary>
    ///     Show grand total row
    /// </summary>
    [JsonProperty("showGrandTotal")]
    public bool? ShowGrandTotal { get; set; }

    /// <summary>
    ///     Show grand total count
    /// </summary>
    [JsonProperty("showGrandTotalCount")]
    public bool? ShowGrandTotalCount { get; set; }

    /// <summary>
    ///     Title for the grand total row
    /// </summary>
    [JsonProperty("grandTotalTitle")]
    public string GrandTotalTitle { get; set; } = string.Empty;

    /// <summary>
    ///     Include elements from linked files
    /// </summary>
    [JsonProperty("includeLinkedFiles")]
    public bool? IncludeLinkedFiles { get; set; }

    /// <summary>
    ///     Family ID for note block schedules
    /// </summary>
    [JsonProperty("familyId")]
    public long FamilyId { get; set; } = 0;

    /// <summary>
    ///     Fields to include in the schedule
    /// </summary>
    [JsonProperty("fields")]
    public List<ScheduleFieldInfo> Fields { get; set; }

    /// <summary>
    ///     Filters to apply to the schedule
    /// </summary>
    [JsonProperty("filters")]
    public List<ScheduleFilterInfo> Filters { get; set; }

    /// <summary>
    ///     Clear existing filters before applying new ones
    /// </summary>
    [JsonProperty("clearExistingFilters")]
    public bool ClearExistingFilters { get; set; } = true;

    /// <summary>
    ///     Sort fields for the schedule
    /// </summary>
    [JsonProperty("sortFields")]
    public List<ScheduleSortInfo> SortFields { get; set; }

    /// <summary>
    ///     Clear existing sorts before applying new ones
    /// </summary>
    [JsonProperty("clearExistingSorts")]
    public bool ClearExistingSorts { get; set; } = true;

    /// <summary>
    ///     Group fields for the schedule
    /// </summary>
    [JsonProperty("groupFields")]
    public List<ScheduleGroupInfo> GroupFields { get; set; }

    /// <summary>
    ///     Clear existing groups before applying new ones
    /// </summary>
    [JsonProperty("clearExistingGroups")]
    public bool ClearExistingGroups { get; set; } = true;

    /// <summary>
    ///     Additional parameters
    /// </summary>
    [JsonProperty("parameters")]
    public Dictionary<string, object> Parameters { get; set; }
}

/// <summary>
///     Information about a schedule field
/// </summary>
public class ScheduleFieldInfo
{
    /// <summary>
    ///     Parameter ID for the field
    /// </summary>
    [JsonProperty("parameterId")]
    public int ParameterId { get; set; }

    /// <summary>
    ///     Parameter name for the field
    /// </summary>
    [JsonProperty("parameterName")]
    public string ParameterName { get; set; } = string.Empty;

    /// <summary>
    ///     Field type (Instance, Type, Count, Formula, Phasing)
    /// </summary>
    [JsonProperty("fieldType")]
    public string FieldType { get; set; } = "Instance";

    /// <summary>
    ///     Column heading for the field
    /// </summary>
    [JsonProperty("heading")]
    public string Heading { get; set; } = string.Empty;

    /// <summary>
    ///     Whether the field is a calculated field
    /// </summary>
    [JsonProperty("isCalculatedField")]
    public bool IsCalculatedField { get; set; }

    /// <summary>
    ///     Formula for calculated fields
    /// </summary>
    [JsonProperty("formula")]
    public string Formula { get; set; } = string.Empty;

    /// <summary>
    ///     Width of the field in pixels
    /// </summary>
    [JsonProperty("width")]
    public double Width { get; set; }

    /// <summary>
    ///     Whether the field is hidden
    /// </summary>
    [JsonProperty("isHidden")]
    public bool IsHidden { get; set; }

    /// <summary>
    ///     Horizontal alignment (Left, Center, Right)
    /// </summary>
    [JsonProperty("horizontalAlignment")]
    public string HorizontalAlignment { get; set; } = "Left";

    /// <summary>
    ///     Grid column width in schedule (in sheet units)
    /// </summary>
    [JsonProperty("gridColumnWidth")]
    public double? GridColumnWidth { get; set; }

    /// <summary>
    ///     Format option (e.g. DUT_METERS for length, or custom format string)
    /// </summary>
    [JsonProperty("formatOption")]
    public string FormatOption { get; set; } = string.Empty;

    /// <summary>
    ///     Accuracy for numeric fields
    /// </summary>
    [JsonProperty("accuracy")]
    public int? Accuracy { get; set; }

    /// <summary>
    ///     Use thousand separator for numeric fields
    /// </summary>
    [JsonProperty("useThousandSeparator")]
    public bool? UseThousandSeparator { get; set; }
}

/// <summary>
///     Information about a schedule filter
/// </summary>
public class ScheduleFilterInfo
{
    /// <summary>
    ///     Field name to filter by
    /// </summary>
    [JsonProperty("fieldName")]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    ///     Field index to filter by (alternative to name)
    /// </summary>
    [JsonProperty("fieldIndex")]
    public int FieldIndex { get; set; } = -1;

    /// <summary>
    ///     Filter type (Equals, NotEquals, GreaterThan, etc.)
    /// </summary>
    [JsonProperty("filterType")]
    public string FilterType { get; set; } = "Equal";

    /// <summary>
    ///     Filter value
    /// </summary>
    [JsonProperty("filterValue")]
    public string FilterValue { get; set; } = string.Empty;
}

/// <summary>
///     Information about schedule sorting
/// </summary>
public class ScheduleSortInfo
{
    /// <summary>
    ///     Field name to sort by
    /// </summary>
    [JsonProperty("fieldName")]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    ///     Field index to sort by (alternative to name)
    /// </summary>
    [JsonProperty("fieldIndex")]
    public int FieldIndex { get; set; } = -1;

    /// <summary>
    ///     Sort order (Ascending, Descending)
    /// </summary>
    [JsonProperty("sortOrder")]
    public string SortOrder { get; set; } = "Ascending";
}

/// <summary>
///     Information about schedule grouping
/// </summary>
public class ScheduleGroupInfo
{
    /// <summary>
    ///     Field name to group by
    /// </summary>
    [JsonProperty("fieldName")]
    public string FieldName { get; set; } = string.Empty;

    /// <summary>
    ///     Field index to group by (alternative to name)
    /// </summary>
    [JsonProperty("fieldIndex")]
    public int FieldIndex { get; set; } = -1;

    /// <summary>
    ///     Sort order (Ascending, Descending)
    /// </summary>
    [JsonProperty("sortOrder")]
    public string SortOrder { get; set; } = "Ascending";

    /// <summary>
    ///     Show header for group
    /// </summary>
    [JsonProperty("showHeader")]
    public bool ShowHeader { get; set; } = true;

    /// <summary>
    ///     Show footer for group
    /// </summary>
    [JsonProperty("showFooter")]
    public bool ShowFooter { get; set; }

    /// <summary>
    ///     Show blank line after group
    /// </summary>
    [JsonProperty("showBlankLine")]
    public bool ShowBlankLine { get; set; }

    /// <summary>
    ///     Format data for headers/footers
    /// </summary>
    [JsonProperty("formatData")]
    public string FormatData { get; set; } = string.Empty;
}