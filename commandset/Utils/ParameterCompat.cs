using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Extension methods for Definition to handle API differences between Revit versions.
    /// Revit 2022 introduced Definition.GetDataType()/GetGroupTypeId() and the nested
    /// SpecTypeId groups (String, Boolean, Int). Revit 2020-2021 expose the
    /// ParameterType and BuiltInParameterGroup enums instead.
    /// </summary>
    public static class DefinitionExtensions
    {
        /// <summary>
        /// Identifier of the parameter data type, for display purposes.
        /// </summary>
        public static string GetDataTypeName(this Definition definition)
        {
            if (definition == null) return "";
#if REVIT2022_OR_GREATER
            return definition.GetDataType()?.TypeId ?? "";
#else
            return definition.ParameterType.ToString();
#endif
        }

        /// <summary>
        /// Identifier of the parameter group, for display purposes.
        /// </summary>
        public static string GetGroupName(this Definition definition)
        {
            if (definition == null) return "";
#if REVIT2022_OR_GREATER
            return definition.GetGroupTypeId()?.TypeId ?? "";
#else
            return definition.ParameterGroup.ToString();
#endif
        }

        /// <summary>
        /// True for length, angle, area and volume parameters.
        /// </summary>
        public static bool IsDimensionDataType(this Definition definition)
        {
            if (definition == null) return false;
#if REVIT2022_OR_GREATER
            var dataType = definition.GetDataType();
            return dataType != null &&
                   (dataType.Equals(SpecTypeId.Length) ||
                    dataType.Equals(SpecTypeId.Angle) ||
                    dataType.Equals(SpecTypeId.Area) ||
                    dataType.Equals(SpecTypeId.Volume));
#else
            switch (definition.ParameterType)
            {
                case ParameterType.Length:
                case ParameterType.Angle:
                case ParameterType.Area:
                case ParameterType.Volume:
                    return true;
                default:
                    return false;
            }
#endif
        }

        /// <summary>
        /// True for Yes/No (boolean) parameters.
        /// </summary>
        public static bool IsYesNoDataType(this Definition definition)
        {
            if (definition == null) return false;
#if REVIT2022_OR_GREATER
            var dataType = definition.GetDataType();
            return dataType != null && dataType.Equals(SpecTypeId.Boolean.YesNo);
#else
            return definition.ParameterType == ParameterType.YesNo;
#endif
        }
    }

    /// <summary>
    /// Shared parameter creation and binding, compatible with all supported Revit versions.
    /// Revit 2022+ uses ForgeTypeId (SpecTypeId/GroupTypeId), Revit 2020-2021 the
    /// ParameterType and BuiltInParameterGroup enums.
    /// </summary>
    public static class SharedParameterCompat
    {
        /// <summary>
        /// Creation options for a text shared parameter.
        /// </summary>
        public static ExternalDefinitionCreationOptions CreateTextOptions(string name)
        {
#if REVIT2022_OR_GREATER
            return new ExternalDefinitionCreationOptions(name, SpecTypeId.String.Text);
#else
            return new ExternalDefinitionCreationOptions(name, ParameterType.Text);
#endif
        }

        /// <summary>
        /// Binds a definition to the document under the named parameter group.
        /// Unknown group names fall back to the "Data" group.
        /// </summary>
        public static bool InsertBinding(Document doc, Definition definition, Binding binding, string groupName)
        {
            return doc.ParameterBindings.Insert(definition, binding, ResolveGroup(groupName));
        }

        /// <summary>
        /// Replaces an existing binding of a definition, under the named parameter group.
        /// </summary>
        public static bool ReInsertBinding(Document doc, Definition definition, Binding binding, string groupName)
        {
            return doc.ParameterBindings.ReInsert(definition, binding, ResolveGroup(groupName));
        }

#if REVIT2022_OR_GREATER
        private static readonly Dictionary<string, ForgeTypeId> _groupCache = BuildGroupCache();

        private static Dictionary<string, ForgeTypeId> BuildGroupCache()
        {
            var cache = new Dictionary<string, ForgeTypeId>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in typeof(GroupTypeId).GetProperties(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (prop.GetValue(null) is ForgeTypeId value)
                    cache[prop.Name] = value;
            }
            return cache;
        }

        private static ForgeTypeId ResolveGroup(string groupName)
        {
            if (string.IsNullOrEmpty(groupName))
                return GroupTypeId.Data;

            return _groupCache.TryGetValue(groupName, out var typeId)
                ? typeId
                : GroupTypeId.Data;
        }
#else
        private static readonly Dictionary<string, BuiltInParameterGroup> _groupCache = BuildGroupCache();

        private static Dictionary<string, BuiltInParameterGroup> BuildGroupCache()
        {
            // Keyed by both the enum name (PG_IDENTITY_DATA) and the GroupTypeId-style
            // name used by Revit 2022+ (IdentityData), so callers can pass either.
            var cache = new Dictionary<string, BuiltInParameterGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (BuiltInParameterGroup group in Enum.GetValues(typeof(BuiltInParameterGroup)))
            {
                var name = group.ToString();
                cache[name] = group;

                var alias = ToForgeStyleName(name);
                if (!string.IsNullOrEmpty(alias) && !cache.ContainsKey(alias))
                    cache[alias] = group;
            }
            return cache;
        }

        private static string ToForgeStyleName(string enumName)
        {
            if (!enumName.StartsWith("PG_", StringComparison.Ordinal))
                return null;

            var parts = enumName.Substring(3).Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(p =>
                char.ToUpperInvariant(p[0]) + p.Substring(1).ToLowerInvariant()));
        }

        private static BuiltInParameterGroup ResolveGroup(string groupName)
        {
            if (string.IsNullOrEmpty(groupName))
                return BuiltInParameterGroup.PG_DATA;

            return _groupCache.TryGetValue(groupName, out var group)
                ? group
                : BuiltInParameterGroup.PG_DATA;
        }
#endif
    }

    /// <summary>
    /// Extension methods for IndependentTag to handle API differences between Revit versions.
    /// Multi-reference tags (GetTaggedLocalElementIds) arrived in Revit 2022; earlier
    /// versions expose a single TaggedLocalElementId.
    /// </summary>
    public static class IndependentTagExtensions
    {
        /// <summary>
        /// Ids of the local elements the tag references.
        /// </summary>
        public static ICollection<ElementId> GetTaggedLocalElementIdsCompat(this IndependentTag tag)
        {
            if (tag == null) return new List<ElementId>();
#if REVIT2022_OR_GREATER
            return tag.GetTaggedLocalElementIds().ToList();
#else
            var id = tag.TaggedLocalElementId;
            return id == null || id == ElementId.InvalidElementId
                ? new List<ElementId>()
                : new List<ElementId> { id };
#endif
        }
    }
}
