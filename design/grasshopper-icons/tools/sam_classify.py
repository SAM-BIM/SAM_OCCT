"""Classify every inventoried GH object into (object glyph, operation badge, modifiers).

Input : ../manifest_raw.json (from inventory.py)
Output: ../manifest.json + ../manifest.csv  (the source of truth for the icon library)

Rules are ordered and deterministic; per-component exceptions live in OVERRIDES
(keyed by component display name) with a reason.
"""
import csv
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.dirname(HERE)

# ------------------------------------------------------------------ operation verbs (leading word)
VERBS = [
    # (regex on the short name, op)
    (r"^(Create|New)", "create"), (r"^Add", "add"), (r"^(Copy|Duplicate)", "copy"),
    (r"^Get", "get"), (r"^(Filter|Select)", "filter"), (r"^Inspect", "inspect"), (r"^Report", "list"),
    (r"^Set", "set"), (r"^(Update|Map|Replace|Apply)", "update"),
    (r"^(Modify|Fix|Adjust|Rename|Round|Convex|Fill|Resolve|Prepare)", "modify"),
    (r"^(Merge|Join|Combine)", "merge"), (r"^(Split|Cut|Trim)", "split"), (r"^Section", "section"),
    (r"^Offset", "offset"), (r"^(Transform|Move)", "transform"), (r"^Snap", "snap"), (r"^Align", "align"),
    (r"^Extend", "extend"), (r"^Flip", "flip"), (r"^Triangulate", "triangulate"), (r"^Sort", "sort"),
    (r"^Remove", "remove"), (r"^Calculate", "calculate"), (r"^(Check|Is[A-Z])", "validate"),
    (r"^(Label|Display)", "display"), (r"^(Bake|Export|Save|Write)", "export"), (r"^(Load|Import|Read|From)", "import"),
    (r"^To[A-Z]", "convert"),
]

# ------------------------------------------------------------------ object nouns (first match wins; order matters)
OBJECTS = [
    ("ApertureConstructionLibrary", "apertureConstruction", "library"), ("ConstructionLibrary", "construction", "library"),
    ("MaterialLibrary", "material", "library"), ("InternalConditionLibrary", "internalCondition", "library"),
    ("DegreeOfActivityLibrary", "degreeOfActivity", "library"), ("ProfileLibrary", "profile", "library"),
    ("SystemTypeLibrary", "system", "library"), ("SAMLibrary", "object", "library"), ("DefaultLibrary", "object", "library"),
    ("ConstructionManager", "constructionManager", None), ("ApertureConstruction", "apertureConstruction", None),
    ("ConstructionLayer", "constructionLayer", None), ("MaterialLayer", "materialLayer", None),
    ("OpeningProperties", "openingProperties", None), ("Opening", "aperture", None),
    ("AdjacencyCluster", "cluster", None), ("AnalyticalModel", "model", None), ("BuildingModel", "model", None),
    ("ExternalSpace", "externalSpace", None), ("PerimeterSpace", "space", None), ("Space", "space", None), ("Room", "space", None),
    ("Zone", "zone", None), ("IZAM", "izam", None), ("Aperture", "aperture", None), ("Window", "aperture", None),
    ("FeatureShade", "shade", None), ("Shade", "shade", None), ("AirPanel", "panel", None), ("AirPartition", "panel", None),
    ("Panel", "panel", None), ("Roof", "panel", None), ("HostPartition", "panel", None), ("Partition", "panel", None),
    ("Construction", "construction", None), ("TransparentMaterial", "transparentMaterial", None),
    ("GasMaterial", "gasMaterial", None), ("OpaqueMaterial", "material", None), ("Material", "material", None),
    ("InternalCondition", "internalCondition", None), ("DegreeOfActivity", "degreeOfActivity", None),
    ("OccupancyGain", "internalCondition", None), ("LightingGain", "lighting", None), ("EquipmentGain", "equipment", None),
    ("InfiltrationGain", "infiltration", None), ("PollutantGain", "pollutant", None), ("SetPoint", "thermometer", None),
    ("VentilationProfile", "airflow", None), ("Ventilation", "fan", None), ("AirFlow", "airflow", None), ("Airflow", "airflow", None),
    ("AirHandlingUnit", "ahu", None), ("MechanicalSystem", "system", None), ("SystemType", "system", None), ("System", "system", None),
    ("Profile", "profile", None), ("NCM", "internalCondition", None),
    ("HeatTransferCoefficient", "heat", None), ("HeatFlow", "heat", None), ("Daylight", "daylight", None),
    ("DesignDay", "designDay", None), ("WeatherData", "weatherData", None), ("WeatherYear", "weatherYear", None),
    ("WeatherHour", "weatherData", None), ("Weather", "weather", None), ("Comfort", "thermometer", None), ("TM52", "thermometer", None),
    ("Adaptive", "thermometer", None), ("OutdoorAirTemperature", "thermometer", None),
    ("Level", "level", None), ("Elevation", "level", None),
    ("Shell", "shell", None), ("Face3D", "face", None), ("PlanarBoundary", "face", None), ("Mesh", "mesh", None),
    ("Plane", "plane", None), ("Transform3D", "transform3D", None), ("Segment", "segment", None), ("Point", "points", None),
    ("Polycurve", "polyloop", None), ("Loop", "polyloop", None), ("SAMGeometry2D", "geometry2D", None), ("NTS", "geometry2D", None),
    ("Geometry", "geometry", None), ("Vector", "vector", None), ("Normal", "normal", None), ("Grid", "grid", None),
    ("Location", "location", None), ("Address", "address", None), ("CountryCode", "location", None), ("CoutryCode", "location", None),
    ("Azimuth", "compass", None), ("Direction", "compass", None),
    ("Result", "result", None), ("DesignExplorer", "result", None), ("Case", "case", None), ("Scenario", "case", None),
    ("DelimitedFileTable", "table", None), ("Csv", "table", None), ("TableModifier", "table", None),
    ("TextMap", "textMap", None), ("Json", "json", None), ("File", "file", None), ("Log", "log", None),
    ("Color", "colour", None), ("Uint", "colour", None), ("ARGB", "colour", None),
    ("HourOfYear", "clock", None), ("DateTime", "clock", None), ("DayOfYear", "calendar", None), ("Period", "clock", None),
    ("Number", "number", None), ("String", "text", None), ("Text", "text", None), ("Name", "name", None),
    ("Value", "value", None), ("Parameter", "value", None), ("Key", "list", None), ("List", "list", None),
    ("DataTree", "tree", None), ("Path", "folder", None), ("Files", "folder", None),
    ("RelationCluster", "relation", None), ("Related", "relation", None), ("Type", "type", None), ("Settings", "settings", None),
    ("Object", "object", None), ("Guid", "object", None),
]

PLURAL_RE = r"(Spaces|Panels|Apertures|Zones|Shells|Face3Ds|Materials|Constructions|Profiles|Levels|Objects|Results|Points|Point3Ds|Segment3Ds|Openings|Values|Names|Cases|Scenarios|IZAMs|Systems|Units|Elevations|Layers|Days|Geometries|Keys|Types|Rooms|Hours|Temperatures)"

# ------------------------------------------------------------------ explicit exceptions: name -> (object, op, extra)
OVERRIDES = {
    # --- about / diagnostics
    "SAM.About": ("info", "value", "About enum: SAM emblem tile + info (identity exception)"),
    "SAM.Version": ("info", "get", "Version: emblem tile + get"),
    "SAM.Test": ("object", "validate", "test harness component"),
    "SAMCore.Error": ("log", "error", None),
    "LogToFile": ("log", "export", None),
    "Inspect": ("object", "inspect", None),
    "SAMCore.Convert": ("object", "convert", None),
    "SAMCore.Update": ("object", "update", None),
    "SAMCore.ReplaceByGuid": ("object", "update", None),
    "SAMCore.GetType": ("type", "get", None),
    "SAMCore.FilterByType": ("type", "filter", None),
    "SAMCore.ParameterByType": ("value", "get", None),
    "SAMCore.CombineResults": ("result", "merge", None),
    "SAMCore.StringReplace": ("text", "update", None),
    "SAMCore.TextMapValues": ("textMap", "get", None),
    "SAMCore.NumberFilter": ("number", "filter", None),
    "SAMCore.Round": ("number", "modify", None),
    "SAMCore.TableModifier ": ("table", "create", None),
    "SAMCore.CreateDelimitedFileTableBySAMObjects": ("table", "create", None),
    "SAMCore.LoadMaterialLibrary": ("material", "import", "library"),
    "SAMCore.SaveMaterialLibrary": ("material", "export", "library"),
    "SAMCore.AddMaterials": ("material", "add", "library"),
    "SAMCore.CopyMaterials": ("material", "copy", "library"),
    "GetNames": ("name", "get", None),
    "SelectByName": ("name", "filter", None),
    "GetValue": ("value", "get", None), "SetValue": ("value", "set", None), "SetValues": ("value", "set", "plural"),
    "RemoveValue": ("value", "remove", None), "GetValueFilter": ("value", "filter", None),
    "ToList": ("list", "convert", None), "ToJson": ("json", "export", None), "FromJson": ("json", "import", None),
    "ToFile": ("file", "export", None), "FromFile": ("file", "import", None), "ToCsv": ("table", "export", None),
    "Csv.ToDelimitedFileTable": ("table", "import", None),
    "DelimitedFileTable.GetValue": ("table", "get", None), "DelimitedFileTable.SetValue": ("table", "set", None),
    "DelimitedFileTable.SetColumnNames": ("table", "set", None), "DelimitedFileTable.Map": ("table", "update", None),
    "DelimitedFileTable.Sort": ("table", "sort", None),
    "RelationCluster.AddObjects": ("relation", "add", None), "RelationCluster.Objects": ("relation", "get", None),
    "RelationCluster.RelatedObjects": ("relation", "get", "plural"), "SAMLibrary.AddObjects": ("object", "add", "library"),
    "SAMAnalytical.Objects": ("object", "get", "plural"), "Analytical.RelatedObjects": ("relation", "get", "plural"),
    "SAMArchitectural.RelatedObjects": ("relation", "get", "plural"),
    "AdjacencyCluster.UpdateObjects": ("cluster", "update", None),
    "ARGBToUint": ("colour", "convert", None), "ColorToUint": ("colour", "convert", None),
    "UintToARGB": ("colour", "convert", None), "UintToColor": ("colour", "convert", None),
    "SAMCore.DateTimeToHourOfYear": ("clock", "convert", None), "SAMCore.DayOfYearToHourOfYear": ("calendar", "convert", None),
    "SAMAnalytical.HourOfYearToDateTime": ("clock", "convert", None),
    "SAMHydra.ExportFile": ("file", "export", None),
    # --- enum value lists (GH_SAMEnumComponent): object of the enum + value badge
    "SAMCore.CombineType": ("settings", "value", None), "SAMCore.CoutryCode": ("location", "value", None),
    "SAMCore.Direction": ("compass", "value", None), "SAMCore.NumberComparisonType": ("number", "value", None),
    "SAMCore.ParameterType": ("value", "value", None), "SAMCore.Period": ("clock", "value", None),
    "SAMCore.TextComparisonType": ("text", "value", None), "SAMAnalytical.ApertureType": ("aperture", "value", None),
    "SAMAnalytical.DefaultGasType": ("gasMaterial", "value", None), "SAMAnalytical.HeatFlowDirection": ("heat", "value", None),
    "SAMAnalytical.InternalConditionParameter": ("internalCondition", "value", None),
    "SAMAnalytical.LightingOccupancyControls": ("lighting", "value", None),
    "SAMAnalytical.LightingPhotoelectricControls": ("lighting", "value", None),
    "SAMAnalytical.NCMSystemType": ("system", "value", None), "SAMAnalytical.PanelGroup": ("panel", "value", None),
    "SAMAnalytical.PanelType": ("panel", "value", None), "SAMAnalytical.ProfileGroup": ("profile", "value", None),
    "SAMAnalytical.ProfileType": ("profile", "value", None), "SAMAnalytical.TM52BuildingCategory": ("thermometer", "value", None),
    "SAMAnalytical.ZoneType": ("zone", "value", None), "SAMAnalytical.HostPartitionCategory": ("panel", "value", None),
    "SAMWeather.WeatherDataType": ("weatherData", "value", None),
    # --- analytical specials
    "SAMAnalytical.AdjacentSpaces": ("cluster", "get", None), "SAMAnalytical.GetAdjacentSpaceNames": ("cluster", "list", None),
    "SAMAnalytical.Airflow": ("airflow", "calculate", None), "SAMAnalytical.ApplyTargetedDesignAirFlow": ("airflow", "set", None),
    "SAMAnalytical.ResolveTargetedDesignAirFlow": ("airflow", "calculate", None), "SAMAnalytical.SetAirflow": ("airflow", "set", None),
    "SAMAnalytical.ApertureConstructions": ("apertureConstruction", "get", "plural"),
    "SAMAnalytical.Bake": ("model", "export", None), "SAMAnalytical.Check": ("model", "validate", None),
    "SAMAnalytical.CalculateDaylightFactor": ("daylight", "calculate", None), "SAMAnalytical.DaylightFactor": ("daylight", "analyse", None),
    "SAMAnalytical.CalculateSpacePanelArea": ("space", "calculate", None),
    "SAMAnalytical.CalculateGlazingValueByAperture": ("aperture", "calculate", None),
    "SAMAnalytical.CalculateGlazingValueByConstruction": ("apertureConstruction", "calculate", None),
    "SAMAnalytical.CreateCaseByShade": ("case", "create", None), "SAMAnalytical.CreateCaseByShade1": ("case", "create", None),
    "SAMAnalytical.CreateCaseByVentilation": ("case", "create", None), "SAMAnalytical.CreateCaseByWindowSize": ("case", "create", None),
    "SAMAnalytical.CreateOverheatingScenarios": ("case", "create", None), "SAMAnalytical.PreparePartOIteration": ("case", "modify", None),
    "SAMAnalytical.CreateDegreeOfActivityByTemperature": ("degreeOfActivity", "create", None),
    "SAMAnalytical.CreateIZAM": ("izam", "create", None), "SAMAnalytical.CreateIZAMBySetPoint": ("izam", "create", None),
    "SAMAnalytical.CreateIZAMBySpaces": ("izam", "create", None), "SAMAnalytical.RemoveIZAMs": ("izam", "remove", None),
    "SAMAnalytical.CreateLevels": ("level", "create", "plural"), "SAMArchitectural.CreateLevel": ("level", "create", None),
    "SAMAnalytical.CreateShells": ("shell", "create", "plural"), "SAMAnalytical.CreateShellsByElevations": ("shell", "create", "plural"),
    "SAMAnalytical.CreateShellsByElevationsAndAuxiliaryElevations": ("shell", "create", "plural"),
    "SAMAnalytical.DailyIndoorComfortTemperatures": ("thermometer", "calculate", None),
    "SAMAnalytical.DataTree": ("tree", "convert", None), "SAMAnalytical.DefaultColor": ("colour", "get", None),
    "SAMAnalytical.DefaultConstructionManager": ("constructionManager", "get", None),
    "SAMAnalytical.ExternalPanels": ("panel", "filter", "plural"), "SAMAnalytical.ExternalVector3D": ("vector", "get", None),
    "SAMAnalytical.FillFloorsAndRoofs": ("panel", "add", "plural"), "SAMAnalytical.Filter": ("object", "filter", None),
    "SAMAnalytical.Geometry": ("geometry", "convert", None), "SAMAnalytical.GetDefaultLibrary": ("object", "get", "library"),
    "SAMAnalytical.GetInternalConditionTextMap": ("textMap", "get", None), "SAMAnalytical.GetSortedKeys": ("list", "sort", None),
    "SAMAnalytical.GetValuesByReference": ("value", "get", "plural"), "SAMAnalytical.Grid": ("grid", "create", None),
    "SAMAnalytical.HeatTransferCoefficientByDefaultGasType": ("heat", "calculate", None),
    "SAMAnalytical.HourlyValues": ("weatherData", "get", "plural"), "SAMAnalytical.InsideSpace": ("space", "validate", None),
    "SAMAnalytical.JoinSpacesByPanels": ("space", "merge", "plural"), "SAMAnalytical.LabelPanel": ("panel", "display", None),
    "SAMAnalytical.LabelSpace": ("space", "display", None), "SAMAnalytical.MapAdjacencyCluster": ("cluster", "update", None),
    "SAMAnalytical.Merge": ("model", "merge", None), "SAMAnalytical.MergeSettings": ("settings", "create", None),
    "SAMAnalytical.TypeMergeSettings": ("settings", "create", None), "SAMAnalytical.ModifyObject": ("object", "modify", None),
    "SAMAnalytical.NormalsDisplay": ("normal", "display", None), "SAMAnalytical.UpdateNormals": ("normal", "update", None),
    "SAMAnalytical.FlipPanel": ("normal", "flip", None), "SAMAnalytical.PanelDistance": ("panel", "calculate", None),
    "SAMAnalytical.PanelLocation": ("panel", "get", None), "SAMAnalytical.PanelSpacing": ("panel", "calculate", "plural"),
    "SAMAnalytical.PanelTypeByText": ("panel", "convert", None), "SAMAnalytical.PanelsDifference": ("panel", "difference", None),
    "SAMAnalytical.PanelsIntersection": ("panel", "intersect", None), "SAMAnalytical.PlaneIntersection": ("plane", "intersect", None),
    "SAMAnalytical.Paths": ("folder", "list", None), "SAMAnalytical.Files": ("file", "list", None),
    "SAMAnalytical.PerimeterSpaces": ("space", "filter", "plural"), "SAMAnalytical.PlanarBoundary3D": ("face", "get", None),
    "SAMAnalytical.ProfileBySpace": ("profile", "get", None), "SAMAnalytical.ProfileDivide": ("profile", "divide", None),
    "SAMAnalytical.ProfileMultiply": ("profile", "multiply", None), "SAMAnalytical.ProfileSum": ("profile", "sum", None),
    "SAMAnalytical.SeasonProfileBySetPoint": ("profile", "create", None), "SAMAnalytical.ReplaceObject": ("object", "update", None),
    "SAMAnalytical.ReportRooms": ("space", "list", "plural"), "SAMAnalytical.ReportRoomsList": ("space", "list", "plural"),
    "SAMAnalytical.ReportSpaces": ("space", "list", "plural"), "SAMAnalytical.RoundAzimuth": ("compass", "modify", None),
    "SAMAnalytical.Section": ("sectionBox", "section", None), "SAMAnalytical.SetAdiabatic": ("panel", "set", None),
    "SAMAnalytical.SetLocation": ("location", "set", None), "SAMAnalytical.SnapPoints": ("points", "snap", None),
    "SAMAnalytical.SystemTypes": ("system", "get", "plural"), "SAMAnalytical.TM52SpaceExtendedResultByDaysOfYears": ("thermometer", "analyse", None),
    "SAMAnalytical.Transform": ("model", "transform", None), "SAMAnalytical.TriangulateConcavePanels": ("panel", "triangulate", None),
    "SAMAnalytical.Convex": ("panel", "modify", None), "SAMAnalytical.RemoveInternalEdges": ("panel", "remove", None),
    "SAMAnalytical.UpdateByModel": ("model", "update", None), "SAMAnalytical.UpdateGeometry": ("geometry", "update", None),
    "SAMAnalytical.UpdateLibraries": ("object", "update", "library"), "SAMAnalytical.UpdateName": ("name", "update", None),
    "SAMAnalytical.UpdateTypesByMap": ("type", "update", None), "SAMAnalytical.UpdateTypesByName": ("type", "update", None),
    "SAMAnalytical.FixNames": ("name", "modify", None), "SAMAnalytical.RenameAnalyticalModel": ("model", "modify", None),
    "SAMAnalytical.RenameConstruction": ("construction", "modify", None), "SAMAnalytical.UpdateSpaceNames": ("name", "update", "plural"),
    "SAMAnalytical.UpdateSpaceLoactionPoint": ("space", "update", None), "SAMAnalytical.AlignSpaceLocation": ("space", "align", None),
    "SAMAnalytical.UpdateNCM": ("internalCondition", "update", None), "SAMAnalytical.ModifyZone": ("zone", "modify", None),
    "SAMAnalytical.MapPanels": ("panel", "update", "plural"), "SAMAnalytical.MapInternalConditions": ("internalCondition", "update", "plural"),
    "SAMAnalytical.OverlapPanels": ("panel", "intersect", None), "SAMAnalytical.AddResults": ("result", "add", None),
    "SAMAnalytical.AddMissingMaterials": ("material", "add", "library"), "SAMAnalytical.AddMaterials": ("material", "add", "library"),
    "SAMAnalytical.AddMissingProfiles": ("profile", "add", "library"),
    "SAMAnalytical.RemoveFromConstructionManager": ("constructionManager", "remove", None),
    "SAMAnalytical.CreateHostPartitionType": ("construction", "create", None),
    "SAMAnalytical.UpdateHostPartitionType": ("construction", "update", None),
    "SAMAnalytical.CreateMaterialLayersByNames": ("materialLayer", "create", "plural"),
    "SAMAnalytical.CreateConstructionLayersByMaterials": ("constructionLayer", "create", "plural"),
    "SAMAnalytical.CreateConstructionLayersByNames": ("constructionLayer", "create", "plural"),
    "SAMAnalytical.GetInternalConstructionLayers": ("constructionLayer", "get", "plural"),
    "SAMAnalytical.CopyApertureConstructionLayers": ("apertureConstruction", "copy", None),
    "SAMAnalytical.CopyConstructionLayers": ("constructionLayer", "copy", "plural"),
    "SAMAdjacencyCluster.DuplicateConstructionByPanelType": ("construction", "copy", None),
    "SAMAdjacencyCluster.SetDefaultApertureConstructionLayers": ("apertureConstruction", "set", None),
    "SAMAnalytical.SetConstructionLayersByPanelType": ("constructionLayer", "set", "plural"),
    "SAMAnalytical.SetDefaultConstructionLayerByPanelType": ("constructionLayer", "set", None),
    "SAMAdjacencyCluster.PanelTypeUpdate": ("panel", "update", None),
    "SAMAnalytical.UpdateBuildingModel": ("model", "update", None),
    "SAMAnalytical.SetOccupancyGains": ("internalCondition", "set", None),
    "SAMAnalytical.SetInternalConditionPerArea": ("internalCondition", "set", None),
    "SAMAnalytical.GetExternalMaterials": ("material", "get", "plural"), "SAMAnalytical.GetInternalMaterials": ("material", "get", "plural"),
    "SAMAnalytical.GetDefaultGasMaterials": ("gasMaterial", "get", "plural"),
    "SAMAnalytical.CreatePerimeterZones": ("zone", "create", "plural"), "SAMAnalytical.CreatePerimeterZones_2": ("zone", "create", "plural"),
    "SAMAnalytical.OffsetAperturesOnEdge": ("aperture", "offset", "plural"),
    "SAMAnalytical.AddOpeningsByAzimuth": ("aperture", "add", "plural"), "SAMAnalytical.AddOpenings": ("aperture", "add", "plural"),
    "SAMAnalytical.CreateOpeningsBy3DGeometry": ("aperture", "create", "plural"),
    "SAMAnalytical.AddOpeningProperties": ("openingProperties", "add", None),
    "SAMAnalytical.AddOpeningPropertiesByPartO": ("openingProperties", "add", None),
    "SAMAnalytical.RemoveOpeningProperties": ("openingProperties", "remove", None),
    "SAMAnalytical.AddVentilationPropertiesByPartF": ("openingProperties", "add", None),
    "SAMAnalytical.AddTransferAirDoorsByPartF": ("aperture", "add", "plural"),
    "SAMAnalytical.CreateBuildingModelBy2DGeometries(": ("model", "create", None),
    "SAMAnalytical.CreateBuildingModelByShells": ("model", "create", None),
    "SAMAnalytical.DesignDays": ("designDay", "create", "plural"), "SAMAnalytical.LoadWeatherData": ("weatherData", "import", None),
    "SAMWeather.HourlyValue": ("weatherData", "get", None), "SAMWeather.HourlyValues": ("weatherData", "get", "plural"),
    "SAMWeather.ModifyWeatherData": ("weatherData", "modify", None),
    "SAMWeather.WeatherHoursByHourOfYear": ("weatherData", "filter", None),
    "SAMWeather.WeatherHoursByPercentage": ("weatherData", "filter", None),
    "SAMWeather.AdaptiveSetpointACCI": ("thermometer", "calculate", None),
    "SAMWeather.AdaptiveSetpointACCIByTemperature": ("thermometer", "calculate", None),
    "SAMWeather.PrevailingMeanOutdoorAirTemperatures": ("thermometer", "calculate", "plural"),
    "SAMAnalytical.AddMechanicalSystems": ("system", "add", "plural"),
    "SAMAnalytical.AddVentilationSystem": ("fan", "add", None), "SAMAnalytical.AddVentilationSystemsByZones": ("fan", "add", "plural"),
    "SAMAnalytical.ModifyVentilationSystemsByZone": ("fan", "modify", "plural"),
    "SAMAnalytical.SetSystemTypeNames": ("system", "set", None),
    "SAMAnalytical.AddSpacesByBrep": ("space", "add", "plural"), "SAMAnalytical.RemoveSpacesByBrep": ("space", "remove", "plural"),
    "SAMAnalytical.AddAirPanels": ("panel", "add", "plural"), "SAMAnalytical.AddAirPartitions": ("panel", "add", "plural"),
    "SAMAnalytical.AddPanels": ("panel", "add", "plural"), "SAMAnalytical.AddFeatureShade": ("shade", "add", None),
    "SAMAnalytical.CreateShade": ("shade", "create", None), "SAMAnalytical.CreateShadeByFeatureShade": ("shade", "create", None),
    "SAMAnalytical.CreateFeatureShade": ("shade", "create", None),
    "SAMAnalytical.MergeSpacesByAirPanels": ("space", "merge", "plural"), "SAMAnalytical.MergeSpacesByPanels": ("space", "merge", "plural"),
    "SAMAnalytical.MergeSpacesByZones": ("space", "merge", "plural"),
    "SAMAnalytical.CreateDesignExplorer": ("result", "create", None),
    "SAMAnalytical.CalculateFloorArea": ("floor", "calculate", None),
    "SAMAnalytical.FilterByMaxRectangle3D": ("panel", "filter", "plural"), "SAMAnalytical.Remove": ("object", "remove", None),
    "SAMAnalytical.SnapByLines": ("panel", "snap", "plural"), "SAMAnalytical.SnapByOffset": ("panel", "snap", "plural"),
    "SAMAnalytical.SplitByInternalEdges": ("panel", "split", None),
    "SAMAnalytical.FilterByElevation": ("panel", "filter", "plural"), "SAMAnalytical.SnapByElevations": ("panel", "snap", "plural"),
    "SAMAnalytical.FilterByBoundaryType": ("panel", "filter", "plural"), "SAMAnalytical.FilterByPanelType": ("panel", "filter", "plural"),
    "SAMAnalytical.FilterByPanelAreaAndThinnessRatio": ("panel", "filter", "plural"), "SAMAnalytical.SnapByPoints": ("panel", "snap", "plural"),
    "SAMAnalytical.CheckPartFCompliance": ("openingProperties", "validate", None),
    "SAMAnalytical.SetPartFCommissioningData": ("openingProperties", "set", None),
    "SAMAnalytical.CreateCaseByApertureByAzimuths": ("case", "create", None), "SAMAnalytical.CreateCaseByApertureConstruction": ("case", "create", None),
    "SAMAnalytical.CreateCaseByOpening": ("case", "create", None), "SAMAnalytical.CreateCaseByWeather": ("case", "create", None),
    "SAMAnalytical.FilterByAzimuth": ("panel", "filter", "plural"), "SAMAnalytical.FilterByGeometry": ("object", "filter", None),
    "SAMAnalytical.FilterByPoints": ("object", "filter", None), "SAMCore.Samples": ("folder", "list", None),
    # --- geometry
    "Create.SAMTransform3DOriginToPlane": ("transform3D", "create", None), "Create.SAMTransform3DPlaneToOrigin": ("transform3D", "create", None),
    "Create.SAMTransform3DPlaneToPlane": ("transform3D", "create", None),
    "Geometry.PolycurveLoop2D": ("polyloop", "create", None), "Geometry.SAMGeometry": ("geometry", "convert", None),
    "Geometry.MeshClose": ("mesh", "modify", None), "Geometry.MeshReduce": ("mesh", "modify", None),
    "Geometry.ModifyDifference": ("shell", "difference", None), "Geometry.ModifyIntersection": ("shell", "intersect", None),
    "Geometry.ModifyUnion": ("shell", "union", "plural"), "Geometry.Triangulate": ("face", "triangulate", None),
    "Geometry.SnapByDistance": ("geometry", "snap", None), "Geometry.SnapByPoints": ("geometry", "snap", None),
    "Geometry.ShellsSplitFace3Ds": ("face", "split", None), "Geometry.CutShell": ("shell", "split", None),
    "NTS.SAMGeometry2D": ("geometry2D", "convert", None), "SAMGeometry.SAMGeometry2D": ("geometry2D", "convert", None),
    "SAMGeometry2D.SAMGeometry": ("geometry2D", "convert", None), "SAMGeometry2D.ToNTS": ("geometry2D", "convert", None),
    "SAMGeometry.Geometry": ("geometry", "convert", None), "SAMGeometry.Fill": ("face", "modify", None),
    "SAMGeometry.FlipNormal": ("normal", "flip", None), "SAMGeometry.IsClosed": ("shell", "validate", None),
    "SAMGeometry.ShellContains": ("shell", "validate", None), "SAMGeometry.NakedSegment3Ds": ("segment", "get", "plural"),
    "SAMGeometry.ThinnessRatio": ("face", "calculate", None), "SAMGeometry.Face3DSpacing": ("face", "calculate", None),
    "SAMGeometry.MergeOverlaps": ("face", "merge", None), "SAMGeometry.PlaneIntersection": ("plane", "intersect", None),
    "SAMGeometry.Segment2DIntersection": ("segment", "intersect", None),
    "SAMGeometry.SectionByFace3D": ("sectionBox", "section", None), "SAMGeometry.SectionByShell": ("sectionBox", "section", None),
    "SAMGeometry.ShellDifference": ("shell", "difference", None), "SAMGeometry.ShellIntersection": ("shell", "intersect", None),
    "SAMGeometry.ShellsUnion": ("shell", "union", "plural"), "SAMGeometry.ShellsSplit": ("shell", "split", None),
    "SAMGeometry.ShellsSplitByFace3Ds": ("shell", "split", None), "SAMGeometry.Split": ("geometry", "split", None),
    "SAMGeometry.SplitCoplanarFace3Ds": ("face", "split", None), "SAMGeometry.Transform": ("geometry", "transform", None),
    "SAMGeometry.CreateShellsByOffset": ("shell", "offset", None),
}

# Param (GH_PersistentParam / GH_Param) type-name -> object glyph
PARAM_OBJECTS = {
    "Group": "group", "IndexedObjects": "indexed", "Object": "object", "Result": "result", "System": "system",
    "SystemType": "system", "TableModifier": "table", "Address": "address", "AdjacencyCluster": "cluster",
    "AirHandlingUnit": "ahu", "AnalyticalModel": "model", "Aperture": "aperture", "ApertureConstruction": "apertureConstruction",
    "ApertureConstructionLibrary": "apertureConstruction", "BuildingModel": "model", "Construction": "construction",
    "ConstructionLayer": "constructionLayer", "ConstructionLibrary": "construction", "ConstructionManager": "constructionManager",
    "DegreeOfActivity": "degreeOfActivity", "DegreeOfActivityLibrary": "degreeOfActivity", "DelimitedFileTable": "table",
    "DesignDay": "designDay", "ExternalSpace": "externalSpace", "FeatureShade": "shade", "FloorType": "construction",
    "HostPartitionType": "construction", "IAnalyticalEquipment": "equipment", "IAnalyticalObject": "object",
    "IOpening": "aperture", "IPartition": "panel", "IRelationCluster": "relation", "ISAMGeometry": "geometry",
    "ISection": "sectionBox", "IWeatherObject": "weather", "InternalCondition": "internalCondition",
    "InternalConditionLibrary": "internalCondition", "Level": "level", "Location": "location", "Log": "log",
    "Material": "material", "MaterialLayer": "materialLayer", "MaterialLibrary": "material", "Matrix": "matrix",
    "MergeSettings": "settings", "OpeningProperties": "openingProperties", "OpeningType": "apertureConstruction",
    "Panel": "panel", "PlanarBoundary3D": "face", "Profile": "profile", "ProfileLibrary": "profile", "RoofType": "construction",
    "SAMObject": "object", "Space": "space", "SystemTypeLibrary": "system", "T": "object", "TextMap": "textMap",
    "Transform3D": "transform3D", "TypeMergeSettings": "settings", "WallType": "construction", "WeatherData": "weatherData",
    "WeatherYear": "weatherYear", "Text3d": "text",
}
PARAM_LIBRARY = {"ApertureConstructionLibrary", "ConstructionLibrary", "DegreeOfActivityLibrary", "InternalConditionLibrary",
                 "MaterialLibrary", "ProfileLibrary", "SystemTypeLibrary"}

# Batches (review sheets) by object family
BATCHES = [
    ("01 Model, cluster & cases", {"model", "cluster", "case", "sectionBox", "level"}),
    ("02 Spaces & zones", {"space", "floor", "externalSpace", "zone", "izam"}),
    ("03 Panels, apertures & shading", {"panel", "aperture", "openingProperties", "shade", "normal"}),
    ("04 Constructions & materials", {"construction", "constructionLayer", "materialLayer", "apertureConstruction", "constructionManager",
                                      "material", "transparentMaterial", "gasMaterial"}),
    ("05 Profiles, internal conditions & gains", {"profile", "internalCondition", "degreeOfActivity", "lighting", "equipment",
                                                   "infiltration", "pollutant", "thermometer"}),
    ("06 Systems & airflow", {"system", "ahu", "fan", "airflow", "heat", "daylight"}),
    ("07 Geometry", {"shell", "face", "geometry", "geometry2D", "plane", "transform3D", "mesh", "polyloop", "segment", "points", "vector", "grid"}),
    ("08 Weather, location & time", {"weather", "weatherData", "weatherYear", "designDay", "location", "address", "compass", "clock", "calendar"}),
    ("09 Data, IO & utilities", {"object", "relation", "group", "indexed", "type", "value", "name", "text", "number", "list", "tree",
                                 "table", "textMap", "json", "file", "folder", "log", "colour", "settings", "matrix", "result", "info"}),
]


def short(name):
    return re.sub(r"^(SAM(Analytical|Geometry|Core|Weather|Architectural|Hydra|AdjacencyCluster)?|Geometry)\.", "", name)


def classify(x):
    name = x["name"] or ""
    if x["category"] == "Params" or x["base_type"].startswith(("GH_PersistentParam", "GH_Param")):
        t = re.sub(r"^\{typeof\((?:global::[\w.]+\.)?(\w+)\)\.Name\}$", r"\1", name)
        t = t.split(".")[-1]
        obj = PARAM_OBJECTS[t]
        return dict(kind="param", object=obj, op=None, plural=False,
                    container="library" if t in PARAM_LIBRARY else None, rule="param")
    if name in OVERRIDES:
        obj, op, extra = OVERRIDES[name]
        return dict(kind="component", object=obj, op=op, plural=extra == "plural",
                    container="library" if extra == "library" else None, rule="override",
                    note=extra if extra not in (None, "plural", "library") else None)
    s = short(name)
    op = next((o for rx, o in VERBS if re.match(rx, s)), None)
    body = re.split(r"By[A-Z]", s, 1)[0]
    obj, container = None, None
    for key, o, c in OBJECTS:
        if key in body:
            obj, container = o, c
            break
    if obj is None:
        for key, o, c in OBJECTS:
            if key in s:
                obj, container = o, c
                break
    if x["base_type"].startswith("GH_SAMEnumComponent") and op is None:
        op = "value"
    plural = bool(re.search(PLURAL_RE + r"(By|$|[A-Z])", body)) and container is None
    return dict(kind="component", object=obj, op=op, plural=plural, container=container, rule="rule")


def normalise(c):
    """Grammar rules applied after classification (see ICON_DESIGN_SYSTEM.md)."""
    if c["op"] == "add":
        c["plural"] = False  # ADD always draws existing (neutral) + new (green); plurality is not drawn
    return c


def icon_id(c):
    parts = [c["object"]]
    if c["container"]:
        parts.append(c["container"])
    if c["plural"]:
        parts.append("plural")
    if c["op"]:
        parts.append(c["op"])
    return "_".join(parts)


def resource_name(iid):
    return "SAM_GH_" + "".join(p[:1].upper() + p[1:] for p in iid.split("_"))


def main():
    raw = json.load(open(os.path.join(DESIGN, "manifest_raw.json"), encoding="utf-8"))
    out, problems = [], []
    for x in raw:
        c = normalise(classify(x))
        if not c["object"] or (c["kind"] == "component" and not c["op"]):
            problems.append((x["name"], c))
            continue
        iid = icon_id(c)
        batch = next((b for b, objs in BATCHES if c["object"] in objs), "10 Other")
        if c["kind"] == "param":
            batch = "11 Parameters"
        out.append({
            "class": x["class"], "name": x["name"], "nickname": x["nickname"], "category": x["category"],
            "subcategory": x["subcategory"], "project": x["project"], "source": x["source"], "guid": x["guid"],
            "kind": c["kind"], "exposure": x["exposure"], "obsolete": x["obsolete"],
            "current_icon": x["icon_expr"] or "(inherited from A_SAMAnalyticalBase)",
            "object": c["object"], "op": c["op"], "plural": c["plural"], "container": c["container"],
            "rule": c["rule"], "note": c.get("note"), "icon_id": iid, "resource": resource_name(iid),
            "icon_file": f"svg/{iid}.svg", "batch": batch, "status": "pending",
        })
    if problems:
        for n, c in problems:
            print("UNCLASSIFIED", n, c)
    out.sort(key=lambda r: (r["batch"], r["object"], r["op"] or "", r["name"] or ""))
    json.dump(out, open(os.path.join(DESIGN, "manifest.json"), "w", encoding="utf-8"), indent=1)
    cols = ["batch", "class", "name", "nickname", "category", "subcategory", "project", "source", "guid", "kind", "exposure",
            "obsolete", "current_icon", "object", "op", "plural", "container", "rule", "note", "icon_id", "resource", "icon_file", "status"]
    with open(os.path.join(DESIGN, "manifest.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=cols)
        w.writeheader()
        w.writerows(out)
    print(len(out), "classified,", len(problems), "unclassified,", len({r['icon_id'] for r in out}), "distinct icons")


if __name__ == "__main__":
    main()
