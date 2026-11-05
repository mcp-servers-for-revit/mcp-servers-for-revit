using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Commands;
using RevitMCPCommandSet.Models.Common;
using System.IO;
using System.Reflection;

namespace RevitMCPCommandSet.Utils
{
    public static class ProjectUtils
    {
        /// <summary>
        /// Algemene methode voor het maken van een familie-instantie
        /// </summary>
        /// <param name="doc">Huidig document</param>
        /// <param name="familySymbol">Familietype</param>
        /// <param name="locationPoint">Locatiepunt</param>
        /// <param name="locationLine">Basislijn</param>
        /// <param name="baseLevel">Onderste peil</param>
        /// <param name="topLevel">Tweede peil (voor TwoLevelsBased)</param>
        /// <param name="baseOffset">Onderste verschuiving (ft)</param>
        /// <param name="topOffset">Bovenste verschuiving (ft)</param>
        /// <param name="faceDirection">Referentierichting</param>
        /// <param name="handDirection">Referentierichting</param>
        /// <param name="view">Aanzicht</param>
        /// <returns>De aangemaakte familie-instantie, retourneert null bij falen</returns>
        public static FamilyInstance CreateInstance(
            this Document doc,
            FamilySymbol familySymbol,
            XYZ locationPoint = null,
            Line locationLine = null,
            Level baseLevel = null,
            Level topLevel = null,
            double baseOffset = -1,
            double topOffset = -1,
            XYZ faceDirection = null,
            XYZ handDirection = null,
            View view = null,
            Element explicitHost = null,
            bool snapToHostCenter = true)
        {
            // Basiscontrole van parameters
            if (doc == null)
                throw new ArgumentNullException($"Verplichte parameter {typeof(Document)} {nameof(doc)} ontbreekt!");
            if (familySymbol == null)
                throw new ArgumentNullException($"Verplichte parameter {typeof(FamilySymbol)} {nameof(familySymbol)} ontbreekt!");

            // Activeer het familietype
            if (!familySymbol.IsActive)
                familySymbol.Activate();

            FamilyInstance instance = null;

            // Kies de aanmaakmethode op basis van het plaatsingstype van de familie
            switch (familySymbol.Family.FamilyPlacementType)
            {
                // Familie gebaseerd op één peil (bijv. metrisch generiek model)
                case FamilyPlacementType.OneLevelBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(XYZ)} {nameof(locationPoint)} ontbreekt!");
                    // Met peilinformatie
                    if (baseLevel != null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            locationPoint,                  // De fysieke locatie waar de instantie wordt geplaatst
                            familySymbol,                   // Het FamilySymbol-object dat het te plaatsen instantietype vertegenwoordigt
                            baseLevel,                      // Het Level-object dat als basispeil van het object dient
                            StructuralType.NonStructural);  // Geeft het type constructie-element aan, indien van toepassing
                    }
                    // Zonder peilinformatie
                    else
                    {
                        instance = doc.Create.NewFamilyInstance(
                            locationPoint,                  // De fysieke locatie waar de instantie wordt geplaatst
                            familySymbol,                   // Het FamilySymbol-object dat het te plaatsen instantietype vertegenwoordigt
                            StructuralType.NonStructural);  // Geeft het type constructie-element aan, indien van toepassing
                    }
                    break;

                // Familie gebaseerd op één peil en een host (bijv. deuren, ramen)
                case FamilyPlacementType.OneLevelBasedHosted:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(XYZ)} {nameof(locationPoint)} ontbreekt!");

                    Element host = explicitHost;
                    XYZ placementPoint = locationPoint;

                    // If explicit host provided and it's a wall, snap to its centerline
                    if (host != null && snapToHostCenter && host is Wall explicitWall)
                    {
                        LocationCurve eLoc = explicitWall.Location as LocationCurve;
                        if (eLoc != null)
                        {
                            IntersectionResult eIr = eLoc.Curve.Project(locationPoint);
                            if (eIr != null)
                                placementPoint = new XYZ(eIr.XYZPoint.X, eIr.XYZPoint.Y, locationPoint.Z);
                        }
                    }

                    // Auto-detect host wall if not explicitly provided
                    if (host == null)
                    {
                        // Try geometric wall-centerline proximity first
                        var wallResult = doc.GetNearestWallByLocationLine(locationPoint, baseLevel);
                        if (wallResult.HasValue)
                        {
                            host = wallResult.Value.wall;
                            if (snapToHostCenter)
                                placementPoint = wallResult.Value.projectedPoint;
                        }
                        else
                        {
                            // Fall back to original ray-casting method
                            host = doc.GetNearestHostElement(locationPoint, familySymbol);
                        }
                    }

                    if (host == null)
                        throw new ArgumentNullException($"Geen geldige hostinformatie gevonden!");

                    if (baseLevel != null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            placementPoint,
                            familySymbol,
                            host,
                            baseLevel,
                            StructuralType.NonStructural);
                    }
                    else
                    {
                        instance = doc.Create.NewFamilyInstance(
                            placementPoint,
                            familySymbol,
                            host,
                            StructuralType.NonStructural);
                    }

                    // Set sill height for windows (baseOffset maps to sill height for hosted elements)
                    if (instance != null && baseOffset != -1)
                    {
                        Parameter sillParam = instance.get_Parameter(BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM);
                        if (sillParam != null && !sillParam.IsReadOnly)
                        {
                            sillParam.Set(baseOffset);
                        }
                    }
                    break;

                // Familie gebaseerd op twee peilen (bijv. kolommen)
                case FamilyPlacementType.TwoLevelsBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(XYZ)} {nameof(locationPoint)} ontbreekt!");
                    if (baseLevel == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(Level)} {nameof(baseLevel)} ontbreekt!");
                    // Bepaal of het een constructiekolom of een bouwkundige kolom is
                    StructuralType structuralType = StructuralType.NonStructural;
                    if (familySymbol.Category.Id.GetIntValue() == (int)BuiltInCategory.OST_StructuralColumns)
                        structuralType = StructuralType.Column;
                    instance = doc.Create.NewFamilyInstance(
                        locationPoint,              // De fysieke locatie waar de instantie wordt geplaatst
                        familySymbol,               // Het FamilySymbol-object dat het te plaatsen instantietype vertegenwoordigt
                        baseLevel,                  // Het Level-object dat als basispeil van het object dient
                        structuralType);            // Geeft het type constructie-element aan, indien van toepassing
                    // Stel onderste peil, bovenste peil, onderste verschuiving en bovenste verschuiving in
                    if (instance != null)
                    {
                        // Stel het basispeil en bovenste peil van de kolom in
                        if (baseLevel != null)
                        {
                            Parameter baseLevelParam = instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_PARAM);
                            if (baseLevelParam != null)
                                baseLevelParam.Set(baseLevel.Id);
                        }
                        if (topLevel != null)
                        {
                            Parameter topLevelParam = instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_PARAM);
                            if (topLevelParam != null)
                                topLevelParam.Set(topLevel.Id);
                        }
                        // Haal de parameter voor onderste verschuiving op
                        if (baseOffset != -1)
                        {
                            Parameter baseOffsetParam = instance.get_Parameter(BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM);
                            if (baseOffsetParam != null && baseOffsetParam.StorageType == StorageType.Double)
                            {
                                // Converteer millimeters naar Revit-interne eenheden
                                double baseOffsetInternal = baseOffset;
                                baseOffsetParam.Set(baseOffsetInternal);
                            }
                        }
                        // Haal de parameter voor bovenste verschuiving op
                        if (topOffset != -1)
                        {
                            Parameter topOffsetParam = instance.get_Parameter(BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM);
                            if (topOffsetParam != null && topOffsetParam.StorageType == StorageType.Double)
                            {
                                // Converteer millimeters naar Revit-interne eenheden
                                double topOffsetInternal = topOffset;
                                topOffsetParam.Set(topOffsetInternal);
                            }
                        }
                    }
                    break;

                // De familie is specifiek voor een aanzicht (bijvoorbeeld, detailaantekeningen)
                case FamilyPlacementType.ViewBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(XYZ)} {nameof(locationPoint)} ontbreekt!");
                    instance = doc.Create.NewFamilyInstance(
                        locationPoint,  // De oorsprong van de familie-instantie. Bij het maken in een plattegrond (ViewPlan) wordt deze op de plattegrond geprojecteerd
                        familySymbol,   // Het familiesymbool-object dat het te plaatsen instantietype vertegenwoordigt
                        view);          // Het 2D-aanzicht waarin de familie-instantie wordt geplaatst
                    break;

                // Familie gebaseerd op een werkvlak (bijv. vlakgebaseerd metrisch generiek model, inclusief vlak-gebaseerd, wand-gebaseerd, enz.)
                case FamilyPlacementType.WorkPlaneBased:
                    if (locationPoint == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(XYZ)} {nameof(locationPoint)} ontbreekt!");
                    // Haal het dichtstbijzijnde hostvlak op
                    Reference hostFace = doc.GetNearestFaceReference(locationPoint, 1000 / 304.8);
                    if (hostFace == null)
                        throw new ArgumentNullException($"Geen geldige hostinformatie gevonden!");
                    if (faceDirection == null || faceDirection == XYZ.Zero)
                    {
                        var result = doc.GenerateDefaultOrientation(hostFace);
                        faceDirection = result.FacingOrientation;
                    }
                    // Maak de familie-instantie op het vlak met een punt en richting
                    instance = doc.Create.NewFamilyInstance(
                        hostFace,               // Referentie naar het vlak
                        locationPoint,          // Het punt op het vlak waar de instantie wordt geplaatst
                        faceDirection,          // Vector die de oriëntatie van de familie-instantie bepaalt. Let op: deze richting bepaalt de rotatie van de instantie op het vlak en mag daarom niet evenwijdig zijn aan de vlaknormaal
                        familySymbol);          // Het FamilySymbol-object dat het te plaatsen instantietype vertegenwoordigt. Let op: dit FamilySymbol moet een familie vertegenwoordigen waarvan het FamilyPlacementType WorkPlaneBased is
                    break;

                // Familie gebaseerd op een lijn binnen een werkvlak (bijv. lijngebaseerd metrisch generiek model)
                case FamilyPlacementType.CurveBased:
                    if (locationLine == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(Line)} {nameof(locationLine)} ontbreekt!");

                    // Haal het dichtstbijzijnde hostvlak op (geen afwijking toegestaan)
                    Reference lineHostFace = doc.GetNearestFaceReference(locationLine.Evaluate(0.5, true), 1e-5);
                    if (lineHostFace != null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            lineHostFace,   // Referentie naar het vlak
                            locationLine,   // De curve waarop de familie-instantie is gebaseerd
                            familySymbol);  // Een FamilySymbol-object dat het te plaatsen instantietype vertegenwoordigt. Let op: dit Symbol moet een familie vertegenwoordigen waarvan het FamilyPlacementType WorkPlaneBased of CurveBased is
                    }
                    else
                    {
                        instance = doc.Create.NewFamilyInstance(
                            locationLine,                   // De curve waarop de familie-instantie is gebaseerd
                            familySymbol,                   // Een FamilySymbol-object dat het te plaatsen instantietype vertegenwoordigt. Let op: dit Symbol moet een familie vertegenwoordigen waarvan het FamilyPlacementType WorkPlaneBased of CurveBased is
                            baseLevel,                      // Een Level-object dat als basispeil van het object dient
                            StructuralType.NonStructural);  // Geeft het type constructie-element aan, indien van toepassing
                    }
                    if (instance != null)
                    {
                        // Haal de parameter voor onderste verschuiving op
                        if (baseOffset != -1)
                        {
                            Parameter baseOffsetParam = instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM);
                            if (baseOffsetParam != null && baseOffsetParam.StorageType == StorageType.Double)
                            {
                                // Converteer millimeters naar Revit-interne eenheden
                                double baseOffsetInternal = baseOffset;
                                baseOffsetParam.Set(baseOffsetInternal);
                            }
                        }
                    }
                    break;

                // Familie gebaseerd op een lijn binnen een specifiek aanzicht (bijv. detailcomponenten)
                case FamilyPlacementType.CurveBasedDetail:
                    if (locationLine == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(Line)} {nameof(locationLine)} ontbreekt!");
                    if (view == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(View)} {nameof(view)} ontbreekt!");
                    instance = doc.Create.NewFamilyInstance(
                        locationLine,   // De lijnpositie van de familie-instantie. Deze lijn moet in het aanzichtvlak liggen
                        familySymbol,   // Het familiesymbool-object dat het te plaatsen instantietype vertegenwoordigt
                        view);          // Het 2D-aanzicht waarin de familie-instantie wordt geplaatst
                    break;

                // Structureel curvegestuurde familie (bijv. balken, schoren of schuine kolommen)
                case FamilyPlacementType.CurveDrivenStructural:
                    if (locationLine == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(Line)} {nameof(locationLine)} ontbreekt!");
                    if (baseLevel == null)
                        throw new ArgumentNullException($"Verplichte parameter {typeof(Level)} {nameof(baseLevel)} ontbreekt!");
                    instance = doc.Create.NewFamilyInstance(
                        locationLine,                   // De curve waarop de familie-instantie is gebaseerd
                        familySymbol,                   // Een FamilySymbol-object dat het te plaatsen instantietype vertegenwoordigt. Let op: dit Symbol moet een familie vertegenwoordigen waarvan het FamilyPlacementType WorkPlaneBased of CurveBased is
                        baseLevel,                      // Een Level-object dat als basispeil van het object dient
                        StructuralType.Beam);           // Geeft het type constructie-element aan, indien van toepassing
                    break;

                // Adaptieve familie (bijv. adaptief metrisch generiek model, gevelpaneel)
                case FamilyPlacementType.Adaptive:
                    throw new NotImplementedException("De aanmaakmethode voor FamilyPlacementType.Adaptive is niet geïmplementeerd!");

                default:
                    break;
            }
            return instance;
        }

        /// <summary>
        /// Genereert de standaard oriëntatie- en handrichting (standaard is de lange zijde de HandOrientation en de korte zijde de FacingOrientation)
        /// </summary>
        /// <param name="hostFace"></param>
        /// <returns></returns>
        public static (XYZ FacingOrientation, XYZ HandOrientation) GenerateDefaultOrientation(this Document doc, Reference hostFace)
        {
            var facingOrientation = new XYZ();  // Oriëntatierichting: de richting van de positieve Y-as van de familie na het laden
            var handOrientation = new XYZ();    // Handrichting: de richting van de positieve X-as van de familie na het laden

            // Stap 1: haal het vlak-object op uit de Reference
            Face face = doc.GetElement(hostFace.ElementId).GetGeometryObjectFromReference(hostFace) as Face;

            // Stap 2: haal het contour van het vlak op
            List<Curve> profile = null;
            // Verzameling van contourlijnen; elke sublijst vertegenwoordigt een volledig gesloten contour, de eerste is meestal de buitencontour
            List<List<Curve>> profiles = new List<List<Curve>>();
            // Haal alle contourlussen op (buitencontour en eventuele binnengaten)
            EdgeArrayArray edgeLoops = face.EdgeLoops;
            // Doorloop elke contourlus
            foreach (EdgeArray loop in edgeLoops)
            {
                List<Curve> currentLoop = new List<Curve>();
                // Haal elke rand in de lus op
                foreach (Edge edge in loop)
                {
                    Curve curve = edge.AsCurve();
                    currentLoop.Add(curve);
                }
                // Voeg toe aan de resultaatverzameling als de huidige lus randen bevat
                if (currentLoop.Count > 0)
                {
                    profiles.Add(currentLoop);
                }
            }
            // De eerste is meestal de buitencontour
            if (profiles != null && profiles.Any())
                profile = profiles.FirstOrDefault();

            // Stap 3: haal de normaalvector van het vlak op
            XYZ faceNormal = null;
            // Als het een plat vlak is, kan de normaalvector-eigenschap direct worden opgehaald
            if (face is PlanarFace planarFace)
                faceNormal = planarFace.FaceNormal;

            // Stap 4: haal de twee geldige hoofdrichtingen van het vlak op (volgens de rechterhandregel)
            var result = face.GetMainDirections();
            var primaryDirection = result.PrimaryDirection;
            var secondaryDirection = result.SecondaryDirection;

            // Standaard is de richting van de lange zijde de HandOrientation en die van de korte zijde de FacingOrientation
            facingOrientation = primaryDirection;
            handOrientation = secondaryDirection;

            // Controleer of dit voldoet aan de rechterhandregel (duim: HandOrientation, wijsvinger: FacingOrientation, middelvinger: FaceNormal)
            if (!facingOrientation.IsRightHandRuleCompliant(handOrientation, faceNormal))
            {
                var newHandOrientation = facingOrientation.GenerateIndexFinger(faceNormal);
                if (newHandOrientation != null)
                {
                    handOrientation = newHandOrientation;
                }
            }

            return (facingOrientation, handOrientation);
        }

        /// <summary>
        /// Haalt de Reference op van het vlak dat het dichtst bij het punt ligt
        /// </summary>
        /// <param name="doc">Huidig document</param>
        /// <param name="location">Doelpuntlocatie</param>
        /// <param name="radius">Zoekradius (interne eenheden)</param>
        /// <returns>De Reference van het dichtstbijzijnde vlak, retourneert null indien niet gevonden</returns>
        public static Reference GetNearestFaceReference(this Document doc, XYZ location, double radius = 1000 / 304.8)
        {
            try
            {
                // Foutmarge verwerken
                location = new XYZ(location.X, location.Y, location.Z + 0.1 / 304.8);

                // Maak of haal een 3D-aanzicht op
                View3D view3D = null;
                FilteredElementCollector collector = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D));

                foreach (View3D v in collector)
                {
                    if (!v.IsTemplate)
                    {
                        view3D = v;
                        break;
                    }
                }

                if (view3D == null)
                {
                    using (Transaction trans = new Transaction(doc, "Create 3D View"))
                    {
                        trans.Start();
                        ViewFamilyType vft = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                        if (vft != null)
                        {
                            view3D = View3D.CreateIsometric(doc, vft.Id);
                        }
                        trans.Commit();
                    }
                }

                if (view3D == null)
                {
                    System.Diagnostics.Trace.TraceWarning("Kan geen 3D-aanzicht maken of ophalen"); // geen dialoog: zou het MCP-verzoek blokkeren
                    return null;
                }

                // Stel stralen in voor 6 richtingen
                XYZ[] directions = new XYZ[]
                {
                  XYZ.BasisX,    // X-richting positief
                  -XYZ.BasisX,   // X-richting negatief
                  XYZ.BasisY,    // Y-richting positief
                  -XYZ.BasisY,   // Y-richting negatief
                  XYZ.BasisZ,    // Z-richting positief
                  -XYZ.BasisZ    // Z-richting negatief
                };

                // Maak filters
                ElementClassFilter wallFilter = new ElementClassFilter(typeof(Wall));
                ElementClassFilter floorFilter = new ElementClassFilter(typeof(Floor));
                ElementClassFilter ceilingFilter = new ElementClassFilter(typeof(Ceiling));
                ElementClassFilter instanceFilter = new ElementClassFilter(typeof(FamilyInstance));

                // Combineer filters
                LogicalOrFilter categoryFilter = new LogicalOrFilter(
                    new ElementFilter[] { wallFilter, floorFilter, ceilingFilter, instanceFilter });


                // 1. Eenvoudigste optie: filter voor alle geïnstantieerde elementen
                //ElementFilter filter = new ElementIsElementTypeFilter(true);

                // Maak de ray-tracer
                ReferenceIntersector refIntersector = new ReferenceIntersector(categoryFilter,
                    FindReferenceTarget.Face, view3D);
                refIntersector.FindReferencesInRevitLinks = true; // Indien nodig ook vlakken in gekoppelde bestanden zoeken

                double minDistance = double.MaxValue;
                Reference nearestFace = null;

                foreach (XYZ direction in directions)
                {
                    // Zend een straal uit vanaf de huidige positie
                    IList<ReferenceWithContext> references = refIntersector.Find(location, direction);

                    foreach (ReferenceWithContext rwc in references)
                    {
                        double distance = rwc.Proximity; // Haal de afstand tot het vlak op

                        // Als het binnen het zoekbereik ligt en dichterbij is
                        if (distance <= radius && distance < minDistance)
                        {
                            minDistance = distance;
                            nearestFace = rwc.GetReference();
                        }
                    }
                }

                return nearestFace;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"Er is een fout opgetreden bij het ophalen van het dichtstbijzijnde vlak: {ex.Message}"); // geen dialoog: zou het MCP-verzoek blokkeren
                return null;
            }
        }

        /// <summary>
        /// Haalt het dichtstbijzijnde element op dat als host kan dienen
        /// </summary>
        /// <param name="doc">Huidig document</param>
        /// <param name="location">Doelpuntlocatie</param>
        /// <param name="familySymbol">Familietype, gebruikt om het hosttype te bepalen</param>
        /// <param name="radius">Zoekradius (interne eenheden)</param>
        /// <returns>Het dichtstbijzijnde hostelement, retourneert null indien niet gevonden</returns>
        public static Element GetNearestHostElement(this Document doc, XYZ location, FamilySymbol familySymbol, double radius = 5.0)
        {
            try
            {
                // Basiscontrole van parameters
                if (doc == null || location == null || familySymbol == null)
                    return null;

                // Haal de hostgedragparameter van de familie op
                Parameter hostParam = familySymbol.Family.get_Parameter(BuiltInParameter.FAMILY_HOSTING_BEHAVIOR);
                int hostingBehavior = hostParam?.AsInteger() ?? 0;

                // Maak of haal een 3D-aanzicht op
                View3D view3D = null;
                FilteredElementCollector viewCollector = new FilteredElementCollector(doc)
                    .OfClass(typeof(View3D));
                foreach (View3D v in viewCollector)
                {
                    if (!v.IsTemplate)
                    {
                        view3D = v;
                        break;
                    }
                }

                if (view3D == null)
                {
                    using (Transaction trans = new Transaction(doc, "Create 3D View"))
                    {
                        trans.Start();
                        ViewFamilyType vft = new FilteredElementCollector(doc)
                            .OfClass(typeof(ViewFamilyType))
                            .Cast<ViewFamilyType>()
                            .FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);

                        if (vft != null)
                        {
                            view3D = View3D.CreateIsometric(doc, vft.Id);
                        }
                        trans.Commit();
                    }
                }

                if (view3D == null)
                {
                    System.Diagnostics.Trace.TraceWarning("Kan geen 3D-aanzicht maken of ophalen"); // geen dialoog: zou het MCP-verzoek blokkeren
                    return null;
                }

                // Maak een typefilter op basis van het hostgedrag
                ElementFilter classFilter;
                switch (hostingBehavior)
                {
                    case 1: // Wall based
                        classFilter = new ElementClassFilter(typeof(Wall));
                        break;
                    case 2: // Floor based
                        classFilter = new ElementClassFilter(typeof(Floor));
                        break;
                    case 3: // Ceiling based
                        classFilter = new ElementClassFilter(typeof(Ceiling));
                        break;
                    case 4: // Roof based
                        classFilter = new ElementClassFilter(typeof(RoofBase));
                        break;
                    default:
                        return null; // Niet-ondersteund hosttype
                }

                // Stel stralen in voor 6 richtingen
                XYZ[] directions = new XYZ[]
                {
                    XYZ.BasisX,    // X-richting positief
                    -XYZ.BasisX,   // X-richting negatief
                    XYZ.BasisY,    // Y-richting positief
                    -XYZ.BasisY,   // Y-richting negatief
                    XYZ.BasisZ,    // Z-richting positief
                    -XYZ.BasisZ    // Z-richting negatief
                };

                // Maak de ray-tracer
                ReferenceIntersector refIntersector = new ReferenceIntersector(classFilter,
                    FindReferenceTarget.Element, view3D);
                refIntersector.FindReferencesInRevitLinks = true; // Indien nodig ook elementen in gekoppelde bestanden zoeken

                double minDistance = double.MaxValue;
                Element nearestHost = null;

                foreach (XYZ direction in directions)
                {
                    // Zend een straal uit vanaf de huidige positie
                    IList<ReferenceWithContext> references = refIntersector.Find(location, direction);

                    foreach (ReferenceWithContext rwc in references)
                    {
                        double distance = rwc.Proximity; // Haal de afstand tot het element op

                        // Als het binnen het zoekbereik ligt en dichterbij is
                        if (distance <= radius && distance < minDistance)
                        {
                            minDistance = distance;
                            nearestHost = doc.GetElement(rwc.GetReference().ElementId);
                        }
                    }
                }

                return nearestHost;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning($"Er is een fout opgetreden bij het ophalen van het dichtstbijzijnde hostelement: {ex.Message}"); // geen dialoog: zou het MCP-verzoek blokkeren
                return null;
            }
        }

        /// <summary>
        /// Finds the nearest wall to a point using wall location-line distance calculation.
        /// More reliable than ray-casting for door/window placement.
        /// </summary>
        /// <param name="doc">Current Revit document</param>
        /// <param name="point">Target point (internal units, feet)</param>
        /// <param name="level">Level to filter walls on</param>
        /// <param name="tolerance">Extra tolerance beyond half wall width (feet). Default ~5mm.</param>
        /// <returns>Tuple of (wall, projectedPoint, wallDirection, distance) or null</returns>
        public static (Wall wall, XYZ projectedPoint, XYZ wallDirection, double distance)?
            GetNearestWallByLocationLine(
                this Document doc,
                XYZ point,
                Level level,
                double tolerance = 5.0 / 304.8)
        {
            if (doc == null || point == null || level == null)
                return null;

            // Collect all walls on the given level
            var walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall))
                .Cast<Wall>()
                .Where(w =>
                {
                    Parameter baseLevelParam = w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    return baseLevelParam != null && baseLevelParam.AsElementId() == level.Id;
                })
                .ToList();

            Wall bestWall = null;
            XYZ bestProjection = null;
            XYZ bestDirection = null;
            double bestDistance = double.MaxValue;

            foreach (Wall wall in walls)
            {
                LocationCurve locCurve = wall.Location as LocationCurve;
                if (locCurve == null) continue;

                Curve curve = locCurve.Curve;
                if (curve == null) continue;

                // Use Curve.Project() which handles both lines and arcs
                IntersectionResult ir = curve.Project(new XYZ(point.X, point.Y, curve.GetEndPoint(0).Z));
                if (ir == null) continue;

                XYZ projectedPt = ir.XYZPoint;
                double distance = new XYZ(point.X - projectedPt.X, point.Y - projectedPt.Y, 0).GetLength();

                // Check if point is within half the wall width + tolerance
                double halfWidth = wall.Width / 2.0;
                if (distance <= halfWidth + tolerance && distance < bestDistance)
                {
                    bestDistance = distance;
                    bestWall = wall;
                    bestProjection = new XYZ(projectedPt.X, projectedPt.Y, point.Z);

                    // Compute wall direction from curve tangent at projected parameter
                    XYZ p0 = curve.GetEndPoint(0);
                    XYZ p1 = curve.GetEndPoint(1);
                    bestDirection = new XYZ(p1.X - p0.X, p1.Y - p0.Y, 0).Normalize();
                }
            }

            if (bestWall == null)
                return null;

            return (bestWall, bestProjection, bestDirection, bestDistance);
        }

        /// <summary>
        /// Markeert het opgegeven vlak visueel
        /// </summary>
        /// <param name="doc">Huidig document</param>
        /// <param name="faceRef">De Reference van het vlak dat gemarkeerd moet worden</param>
        /// <param name="duration">Duur van de markering (milliseconden), standaard 3000 milliseconden</param>
        public static void HighlightFace(this Document doc, Reference faceRef)
        {
            if (faceRef == null) return;

            // Haal het patroon voor volledige vulling op
            FillPatternElement solidFill = new FilteredElementCollector(doc)
                .OfClass(typeof(FillPatternElement))
                .Cast<FillPatternElement>()
                .FirstOrDefault(x => x.GetFillPattern().IsSolidFill);

            if (solidFill == null)
            {
                System.Diagnostics.Trace.TraceWarning("Geen patroon voor volledige vulling gevonden"); // geen dialoog: zou het MCP-verzoek blokkeren
                return;
            }

            // Maak de instellingen voor de markering
            OverrideGraphicSettings ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceForegroundPatternColor(new Color(255, 0, 0)); // Rood
            ogs.SetSurfaceForegroundPatternId(solidFill.Id);
            ogs.SetSurfaceTransparency(0); // Ondoorzichtig

            // Markeer weergeven
            doc.ActiveView.SetElementOverrides(faceRef.ElementId, ogs);
        }

        /// <summary>
        /// Haalt de twee belangrijkste richtingsvectoren van een vlak op
        /// </summary>
        /// <param name="face">Invoervlak</param>
        /// <returns>Tuple met de primaire en secundaire richting</returns>
        /// <exception cref="ArgumentNullException">Wordt gegooid wanneer het vlak leeg is</exception>
        /// <exception cref="ArgumentException">Wordt gegooid wanneer het contour van het vlak onvoldoende is om een geldige vorm te vormen</exception>
        /// <exception cref="InvalidOperationException">Wordt gegooid wanneer er geen geldige richting kan worden bepaald</exception>
        public static (XYZ PrimaryDirection, XYZ SecondaryDirection) GetMainDirections(this Face face)
        {
            // 1. Parametervalidatie
            if (face == null)
                throw new ArgumentNullException(nameof(face), "Het vlak mag niet leeg zijn");

            // 2. Haal de normaalvector van het vlak op, nodig voor eventuele loodrechte-vectorberekeningen
            XYZ faceNormal = face.ComputeNormal(new UV(0.5, 0.5));

            // 3. Haal de buitencontour van het vlak op
            EdgeArrayArray edgeLoops = face.EdgeLoops;
            if (edgeLoops.Size == 0)
                throw new ArgumentException("Het vlak heeft geen geldige randlussen", nameof(face));

            // Meestal is de eerste lus de buitencontour
            EdgeArray outerLoop = edgeLoops.get_Item(0);

            // 4. Bereken de richtingsvector en lengte van elke rand
            List<XYZ> edgeDirections = new List<XYZ>();  // Slaat de eenheidsvectorrichting van elke rand op
            List<double> edgeLengths = new List<double>(); // Slaat de lengte van elke rand op

            foreach (Edge edge in outerLoop)
            {
                Curve curve = edge.AsCurve();
                XYZ startPoint = curve.GetEndPoint(0);
                XYZ endPoint = curve.GetEndPoint(1);

                // Bereken de vector van startpunt naar eindpunt
                XYZ direction = endPoint - startPoint;
                double length = direction.GetLength();

                // Negeer randen die te kort zijn (mogelijk door samenvallende hoekpunten of afrondingsfouten)
                if (length > 1e-10)
                {
                    edgeDirections.Add(direction.Normalize());  // Slaat de genormaliseerde richtingsvector op
                    edgeLengths.Add(length);                    // Slaat de randlengte op
                }
            }

            if (edgeDirections.Count < 4) // Zorg ervoor dat er minstens 4 randen zijn
            {
                throw new ArgumentException("Het opgegeven vlak heeft niet genoeg randen om een geldige vorm te vormen", nameof(face));
            }

            // 5. Groepeer randen met vergelijkbare richtingen
            List<List<int>> directionGroups = new List<List<int>>();  // Slaat richtingsgroepen op, elke groep bevat de indexen van randen

            for (int i = 0; i < edgeDirections.Count; i++)
            {
                bool foundGroup = false;
                XYZ currentDirection = edgeDirections[i];

                // Probeer de huidige rand toe te voegen aan een bestaande richtingsgroep
                for (int j = 0; j < directionGroups.Count; j++)
                {
                    var group = directionGroups[j];
                    // Bereken de gewogen gemiddelde richting van de huidige groep
                    XYZ groupAvgDir = CalculateWeightedAverageDirection(group, edgeDirections, edgeLengths);

                    // Controleer of de huidige richting overeenkomt met de gemiddelde richting van de groep (inclusief tegengestelde richting)
                    double dotProduct = Math.Abs(groupAvgDir.DotProduct(currentDirection));
                    if (dotProduct > 0.8) // Een afwijking binnen ongeveer 30 graden wordt als vergelijkbare richting beschouwd
                    {
                        group.Add(i);  // Voeg de index van de huidige rand toe aan deze richtingsgroep
                        foundGroup = true;
                        break;
                    }
                }

                // Maak een nieuwe groep als de huidige rand niet overeenkomt met een bestaande groep
                if (!foundGroup)
                {
                    List<int> newGroup = new List<int> { i };
                    directionGroups.Add(newGroup);
                }
            }

            // 6. Bereken het totale gewicht (som van randlengtes) en de gemiddelde richting van elke richtingsgroep
            List<double> groupWeights = new List<double>();
            List<XYZ> groupDirections = new List<XYZ>();

            foreach (var group in directionGroups)
            {
                // Bereken de som van de lengtes van alle randen in deze groep
                double totalLength = 0;
                foreach (int edgeIndex in group)
                {
                    totalLength += edgeLengths[edgeIndex];
                }
                groupWeights.Add(totalLength);

                // Bereken de gewogen gemiddelde richting van deze groep
                groupDirections.Add(CalculateWeightedAverageDirection(group, edgeDirections, edgeLengths));
            }

            // 7. Sorteer op gewicht om de belangrijkste richting te bepalen
            int[] sortedIndices = Enumerable.Range(0, groupDirections.Count)
                .OrderByDescending(i => groupWeights[i])
                .ToArray();

            // 8. Stel het resultaat samen
            if (groupDirections.Count >= 2)
            {
                // Er zijn minstens twee richtingsgroepen; neem de twee zwaarste als primaire en secundaire richting
                int primaryIndex = sortedIndices[0];
                int secondaryIndex = sortedIndices[1];

                return (
                    PrimaryDirection: groupDirections[primaryIndex],      // Primaire richting
                    SecondaryDirection: groupDirections[secondaryIndex]   // Secundaire richting
                );
            }
            else if (groupDirections.Count == 1)
            {
                // Er is maar één richtingsgroep; construeer handmatig een secundaire richting loodrecht op de primaire richting
                XYZ primaryDirection = groupDirections[0];
                // Gebruik het kruisproduct van de vlaknormaal en de primaire richting om een loodrechte vector te maken
                XYZ secondaryDirection = faceNormal.CrossProduct(primaryDirection).Normalize();

                return (
                    PrimaryDirection: primaryDirection,         // Primaire richting
                    SecondaryDirection: secondaryDirection      // Kunstmatig geconstrueerde loodrechte secundaire richting
                );
            }
            else
            {
                // Kan geen geldige richting bepalen (komt zelden voor)
                throw new InvalidOperationException("Kan geen geldige richting uit het vlak bepalen");
            }
        }

        /// <summary>
        /// Berekent de gewogen gemiddelde richting van een groep randen op basis van hun lengte
        /// </summary>
        /// <param name="edgeIndices">Lijst met randindexen</param>
        /// <param name="directions">Richtingsvectoren van alle randen</param>
        /// <param name="lengths">Lengtes van alle randen</param>
        /// <returns>Genormaliseerde gewogen gemiddelde richtingsvector</returns>
        public static XYZ CalculateWeightedAverageDirection(List<int> edgeIndices, List<XYZ> directions, List<double> lengths)
        {
            if (edgeIndices.Count == 0)
                return null;

            double sumX = 0, sumY = 0, sumZ = 0;
            XYZ referenceDir = directions[edgeIndices[0]];  // Gebruik de eerste richting in de groep als referentie

            foreach (int i in edgeIndices)
            {
                XYZ currentDir = directions[i];

                // Bereken het puntproduct van de huidige richting met de referentierichting om te bepalen of omkering nodig is
                double dot = referenceDir.DotProduct(currentDir);

                // Als de richting tegengesteld is (negatief puntproduct), keer de vector om voordat de bijdrage wordt berekend
                // Dit zorgt ervoor dat vectoren binnen dezelfde groep consistent wijzen en elkaar niet opheffen
                double factor = (dot >= 0) ? lengths[i] : -lengths[i];

                // Tel de vectorcomponenten op (gewogen)
                sumX += currentDir.X * factor;
                sumY += currentDir.Y * factor;
                sumZ += currentDir.Z * factor;
            }

            // Maak de samengestelde vector en normaliseer deze
            XYZ avgDir = new XYZ(sumX, sumY, sumZ);
            double magnitude = avgDir.GetLength();

            // Voorkom een nulvector
            if (magnitude < 1e-10)
                return referenceDir;  // Val terug op de referentierichting

            return avgDir.Normalize();  // Retourneer de genormaliseerde richtingsvector
        }

        /// <summary>
        /// Bepaalt of drie vectoren zowel voldoen aan de rechterhandregel als strikt loodrecht op elkaar staan
        /// </summary>
        /// <param name="thumb">Richtingsvector van de duim</param>
        /// <param name="indexFinger">Richtingsvector van de wijsvinger</param>
        /// <param name="middleFinger">Richtingsvector van de middelvinger</param>
        /// <param name="tolerance">Tolerantie voor de beoordeling, standaard 1e-6</param>
        /// <returns>True als de drie vectoren voldoen aan de rechterhandregel en loodrecht op elkaar staan, anders false</returns>
        public static bool IsRightHandRuleCompliant(this XYZ thumb, XYZ indexFinger, XYZ middleFinger, double tolerance = 1e-6)
        {
            // Controleer of de drie vectoren loodrecht op elkaar staan (alle puntproducten liggen dicht bij 0)
            double dotThumbIndex = Math.Abs(thumb.DotProduct(indexFinger));
            double dotThumbMiddle = Math.Abs(thumb.DotProduct(middleFinger));
            double dotIndexMiddle = Math.Abs(indexFinger.DotProduct(middleFinger));

            bool areOrthogonal = (dotThumbIndex <= tolerance) &&
                                  (dotThumbMiddle <= tolerance) &&
                                  (dotIndexMiddle <= tolerance);

            // Controleer de rechterhandregel alleen als de drie vectoren loodrecht op elkaar staan
            if (!areOrthogonal)
                return false;

            // Bereken het puntproduct van de kruisproductvector met de duim om te bepalen of dit voldoet aan de rechterhandregel
            XYZ crossProduct = indexFinger.CrossProduct(middleFinger);
            double rightHandTest = crossProduct.DotProduct(thumb);

            // Een positief puntproduct betekent dat aan de rechterhandregel wordt voldaan
            return rightHandTest > tolerance;
        }

        /// <summary>
        /// Genereert de wijsvingerrichting die voldoet aan de rechterhandregel op basis van de duim- en middelvingerrichting
        /// </summary>
        /// <param name="thumb">Richtingsvector van de duim</param>
        /// <param name="middleFinger">Richtingsvector van de middelvinger</param>
        /// <param name="tolerance">Tolerantie voor de loodrechtheidscontrole, standaard 1e-6</param>
        /// <returns>De gegenereerde richtingsvector van de wijsvinger, retourneert null als de invoervectoren niet loodrecht zijn</returns>
        public static XYZ GenerateIndexFinger(this XYZ thumb, XYZ middleFinger, double tolerance = 1e-6)
        {
            // Normaliseer eerst de invoervectoren
            XYZ normalizedThumb = thumb.Normalize();
            XYZ normalizedMiddleFinger = middleFinger.Normalize();

            // Controleer of de twee vectoren loodrecht op elkaar staan (puntproduct dicht bij 0)
            double dotProduct = normalizedThumb.DotProduct(normalizedMiddleFinger);

            // Als de absolute waarde van het puntproduct groter is dan de tolerantie, staan de vectoren niet loodrecht
            if (Math.Abs(dotProduct) > tolerance)
            {
                return null;
            }

            // Bereken de wijsvingerrichting via het kruisproduct en keer deze om
            XYZ indexFinger = normalizedMiddleFinger.CrossProduct(normalizedThumb).Negate();

            // Retourneer de genormaliseerde richtingsvector van de wijsvinger
            return indexFinger.Normalize();
        }

        /// <summary>
        /// Maakt of haalt een peil op met de opgegeven hoogte
        /// </summary>
        /// <param name="doc">Revit-document</param>
        /// <param name="elevation">Hoogte van het peil (ft)</param>
        /// <param name="levelName">Naam van het peil</param>
        /// <returns></returns>
        public static Level CreateOrGetLevel(this Document doc, double elevation, string levelName)
        {
            // Controleer eerst of er al een peil met de opgegeven hoogte bestaat
            Level existingLevel = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => Math.Abs(l.Elevation - elevation) < 0.1 / 304.8);

            if (existingLevel != null)
                return existingLevel;

            // Maak een nieuw peil
            Level newLevel = Level.Create(doc, elevation);
            // Stel de naam van het peil in
            Level namesakeLevel = new FilteredElementCollector(doc)
                 .OfClass(typeof(Level))
                 .Cast<Level>()
                 .FirstOrDefault(l => l.Name == levelName);
            if (namesakeLevel != null)
            {
                levelName = $"{levelName}_{newLevel.Id.GetValue()}";
            }
            newLevel.Name = levelName;

            return newLevel;
        }

        /// <summary>
        /// Zoekt het peil dat het dichtst bij de opgegeven hoogte ligt
        /// </summary>
        /// <param name="doc">Huidig Revit-document</param>
        /// <param name="height">Doelhoogte (Revit-interne eenheden)</param>
        /// <returns>Het peil dat het dichtst bij de doelhoogte ligt, of null als het document geen peilen bevat</returns>
        public static Level FindNearestLevel(this Document doc, double height)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc), "Het document mag niet leeg zijn");

            // Gebruik direct een LINQ-query om het dichtstbijzijnde peil op te halen
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(level => Math.Abs(level.Elevation - height))
                .FirstOrDefault();
        }

        ///// <summary>
        ///// Vernieuwt het aanzicht en voegt een vertraging toe
        ///// </summary>
        //public static void Refresh(this Document doc, int waitingTime = 0, bool allowOperation = true)
        //{
        //    UIApplication uiApp = new UIApplication(doc.Application);
        //    UIDocument uiDoc = uiApp.ActiveUIDocument;

        //    // Controleer of het document kan worden gewijzigd
        //    if (uiDoc.Document.IsModifiable)
        //    {
        //        // Werk het model bij
        //        uiDoc.Document.Regenerate();
        //    }
        //    // Werk de interface bij
        //    uiDoc.RefreshActiveView();

        //    // Wacht vertraging
        //    if (waitingTime != 0)
        //    {
        //        System.Threading.Thread.Sleep(waitingTime);
        //    }

        //    // Sta de gebruiker toe niet-veilige handelingen uit te voeren
        //    if (allowOperation)
        //    {
        //        System.Windows.Forms.Application.DoEvents();
        //    }
        //}

        /// <summary>
        /// Slaat het opgegeven bericht op in het opgegeven bestand op het bureaublad (standaard wordt het bestand overschreven)
        /// </summary>
        /// <param name="message">Inhoud van het op te slaan bericht</param>
        /// <param name="fileName">Doelbestandsnaam</param>
        public static void SaveToDesktop(this string message, string fileName = "temp.json", bool isAppend = false)
        {
            // Zorg ervoor dat logName een extensie bevat
            if (!Path.HasExtension(fileName))
            {
                fileName += ".txt"; // Standaard wordt de extensie .txt toegevoegd
            }

            // Haal het pad naar het bureaublad op
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            // Stel het volledige bestandspad samen
            string filePath = Path.Combine(desktopPath, fileName);

            // Schrijf naar het bestand (overschrijfmodus)
            using (StreamWriter sw = new StreamWriter(filePath, isAppend))
            {
                sw.WriteLine($"{message}");
            }
        }

    }
}
