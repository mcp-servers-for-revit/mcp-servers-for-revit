using Autodesk.Revit.DB;
using RevitMCPCommandSet.Models.Common;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Gedeelde hulpfuncties voor get_available_parameters en get_parameters: elementen opzoeken, de in
    /// de Revit-interface zichtbare parameters bepalen en parameters beschrijven/uitlezen.
    /// </summary>
    public static class ParameterReadUtils
    {
        public const string SourceInstance = "instance";
        public const string SourceType = "type";

        public static Element ResolveElement(Document doc, string elementIdString)
        {
            if (!TryParseElementId(elementIdString, out long idValue))
            {
                return null;
            }

            return doc.GetElement(ElementIdExtensions.FromLong(idValue));
        }

        public static bool TryParseElementId(string elementIdString, out long idValue)
        {
            idValue = 0;
            if (!long.TryParse(elementIdString, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out idValue)) return false;
#if !REVIT2024_OR_GREATER
            if (idValue < int.MinValue || idValue > int.MaxValue) return false;
#endif
            return true;
        }

        /// <summary>
        /// Het familietype van een element, of null als het element zelf een type is of geen type heeft.
        /// </summary>
        public static ElementType GetElementType(Document doc, Element element)
        {
            if (element is ElementType)
            {
                return null;
            }

            ElementId typeId = element.GetTypeId();
            if (typeId == null || typeId == ElementId.InvalidElementId)
            {
                return null;
            }

            return doc.GetElement(typeId) as ElementType;
        }

        /// <summary>
        /// De parameters die de gebruiker in het Properties-palet (of bij een type: de Type
        /// Properties-dialoog) ziet, in dezelfde volgorde. Verborgen/interne parameters vallen hierbuiten.
        /// </summary>
        public static List<Parameter> GetVisibleParameters(Element element)
        {
            return element.GetOrderedParameters()
                .Where(p => p?.Definition != null)
                .ToList();
        }

        public static Parameter FindVisibleParameter(Element element, string name)
        {
            return GetVisibleParameters(element).FirstOrDefault(p => p.Definition.Name == name);
        }

        public static ParameterDefinitionOutput DescribeParameter(Parameter parameter)
        {
            return new ParameterDefinitionOutput
            {
                Name = parameter.Definition.Name,
                DataType = GetDataTypeLabel(parameter.Definition),
                StorageType = parameter.StorageType.ToString(),
                Group = GetGroupLabel(parameter.Definition),
                IsReadOnly = parameter.IsReadOnly
            };
        }

        /// <summary>
        /// Leest de waarde van een parameter uit. Double-waarden worden omgerekend van Revit's interne
        /// eenheden (voet) naar de weergave-eenheden van het model, zodat ze één-op-één terug te geven zijn
        /// aan set_parameters.
        /// </summary>
        public static ParameterValueOutput ReadParameter(Document doc, Parameter parameter, string source)
        {
            var output = new ParameterValueOutput
            {
                Name = parameter.Definition.Name,
                Found = true,
                Source = source,
                StorageType = parameter.StorageType.ToString(),
                IsReadOnly = parameter.IsReadOnly,
                DisplayValue = parameter.StorageType == StorageType.String
                    ? parameter.AsString()
                    : parameter.AsValueString()
            };

            if (!parameter.HasValue)
            {
                return output;
            }



            switch (parameter.StorageType)
            {
                case StorageType.Double:
                    double internalValue = parameter.AsDouble();
                    if (HasDisplayUnit(parameter))
                    {
#if REVIT2022_OR_GREATER
                        var unit = parameter.GetUnitTypeId();
                        output.Unit = LabelUtils.GetLabelForUnit(unit);
#else
                        var unit = parameter.DisplayUnitType;
                        output.Unit = LabelUtils.GetLabelFor(unit);
#endif
                        output.Value = Math.Round(UnitUtils.ConvertFromInternalUnits(internalValue, unit), 6);
                    }
                    else output.Value = internalValue;
                    break;

                case StorageType.Integer:
                    int intValue = parameter.AsInteger();
                    if (parameter.Definition.IsYesNoDataType())
                    {
                        output.Value = intValue == 1;
                    }
                    else
                    {
                        output.Value = intValue;
                    }
                    break;

                case StorageType.String:
                    output.Value = parameter.AsString();
                    break;

                case StorageType.ElementId:
                    ElementId idValue = parameter.AsElementId();
                    output.Value = idValue == null || idValue == ElementId.InvalidElementId
                        ? (object)null
                        : idValue.GetValue();
                    break;
            }

            return output;
        }

        internal static bool HasDisplayUnit(Parameter parameter)
        {
#if REVIT2022_OR_GREATER
            var type = GetDataType(parameter.Definition);
            return type != null && UnitUtils.IsMeasurableSpec(type);
#else
            try { return UnitUtils.IsValidDisplayUnit(parameter.DisplayUnitType); }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException) { return false; }
#endif
        }

#if REVIT2022_OR_GREATER
        private static ForgeTypeId GetDataType(Definition definition)
        {
            try
            {
                ForgeTypeId dataType = definition.GetDataType();
                return dataType == null || string.IsNullOrEmpty(dataType.TypeId) ? null : dataType;
            }
            catch
            {
                return null;
            }
        }

        private static string GetDataTypeLabel(Definition definition)
        {
            ForgeTypeId dataType = GetDataType(definition);
            if (dataType == null)
            {
                return null;
            }

            try
            {
                if (SpecUtils.IsSpec(dataType))
                {
                    return LabelUtils.GetLabelForSpec(dataType);
                }

                // Familietype-parameters hebben een categorie als gegevenstype
                if (Category.IsBuiltInCategory(dataType))
                {
                    BuiltInCategory category = Category.GetBuiltInCategory(dataType);
                    return $"Family Type ({LabelUtils.GetLabelFor(category)})";
                }
            }
            catch
            {
                // Label niet te bepalen: geen gegevenstype teruggeven
            }

            return null;
        }

        private static string GetGroupLabel(Definition definition)
        {
            try
            {
                ForgeTypeId groupTypeId = definition.GetGroupTypeId();
                return groupTypeId == null || string.IsNullOrEmpty(groupTypeId.TypeId)
                    ? null
                    : LabelUtils.GetLabelForGroup(groupTypeId);
            }
            catch
            {
                return null;
            }
        }
#else
        private static string GetDataTypeLabel(Definition definition) => definition.GetDataTypeName();
        private static string GetGroupLabel(Definition definition) => definition.GetGroupName();
#endif
    }
}
