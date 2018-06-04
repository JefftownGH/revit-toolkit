using System;
using CsvHelper;
using CsvHelper.Configuration;
using CsvHelper.TypeConversion;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeCave.Revit.Toolkit.Parameters.Shared
{
    /// <summary>
    /// This class represents Revit shared parameter file
    /// </summary>
    /// <inheritdoc cref="ICloneable" />
    /// <inheritdoc cref="IEquatable{SharedParameterFile}" />
    /// <seealso cref="System.ICloneable" />
    /// <seealso cref="System.IEquatable{SharedParameterFile}" />
    public sealed partial class SharedParameterFile
    {
        private static readonly Regex SectionRegex;
        private static readonly Configuration CsvConfiguration;

        /// <summary>
        /// Initializes the <see cref="SharedParameterFile"/> class.
        /// </summary>
        static SharedParameterFile()
        {
            SectionRegex = new Regex(@"\*(?<section>[A-Z]+)\t", RegexOptions.IgnoreCase | RegexOptions.Compiled);
            CsvConfiguration = new Configuration
            {
                HasHeaderRecord = true,
                AllowComments = true,
                IgnoreBlankLines = true,
                Delimiter = "\t",
                DetectColumnCountChanges = false,
                QuoteNoFields = true
            };
        }

        /// <summary>
        /// Extracts <see cref="SharedParameterFile"/> object from a .txt file.
        /// </summary>
        /// <param name="sharedParameterFile">The shared parameter file path.</param>
        /// <returns>The shared parameter file</returns>
        /// <exception cref="ArgumentException"></exception>
        public static SharedParameterFile FromFile(string sharedParameterFile)
        {
            if (!File.Exists(sharedParameterFile))
            {
                throw new ArgumentException($"The following parameter file doesn't exist: '{sharedParameterFile}'");
            }

            if (string.IsNullOrWhiteSpace(sharedParameterFile) || !Path.GetExtension(sharedParameterFile).ToLowerInvariant().Contains("txt"))
            {
                throw new ArgumentException($"Shared parameter file must be a .txt file, please check the path you have supplied: '{sharedParameterFile}'");
            }

            var sharedParamsText = File.ReadAllText(sharedParameterFile);
            return FromText(sharedParamsText);
        }

        /// <summary>
        /// Extracts <see cref="SharedParameterFile"/> object from a string.
        /// </summary>
        /// <param name="sharedParameterText">Text content of shared parameter file.</param>
        /// <returns>The shared parameter file</returns>
        /// <exception cref="System.ArgumentException">sharedParameterText</exception>
        public static SharedParameterFile FromText(string sharedParameterText)
        {
            if (string.IsNullOrWhiteSpace(sharedParameterText))
            {
                throw new ArgumentException($"{nameof(sharedParameterText)} must be a non empty string");
            }

            var sharedParamsFileLines = SectionRegex
                .Split(sharedParameterText)
                .Where(line => !line.StartsWith("#")) // Exclude comment lines
                .ToArray();

            var sharedParamsFileSections = sharedParamsFileLines
                .Where((e, i) => i % 2 == 0)
                .Select((e, i) => new { Key = e, Value = sharedParamsFileLines[i * 2 + 1] })
                .ToDictionary(kp => kp.Key, kp => kp.Value.Replace($"{kp.Key}\t", string.Empty));

            if (sharedParamsFileSections == null || sharedParamsFileSections.Count < 3 ||
                !(sharedParamsFileSections.ContainsKey(Sections.META) &&
                  sharedParamsFileSections.ContainsKey(Sections.GROUPS) &&
                  sharedParamsFileSections.ContainsKey(Sections.PARAMS)))
            {
                throw new InvalidDataException("Failed to parse shared parameter file content," +
                                               "because it doesn't contain enough data for being qualified as a valid shared parameter file.");
            }

            var meta = default(Meta);
            var groups = new List<Group>();
            var parameters = new List<Parameter>();

            foreach (var section in sharedParamsFileSections)
            {
                using (var stringReader = new StringReader(section.Value))
                {
                    using (var csvReader = new CsvReader(stringReader, CsvConfiguration))
                    {
                        csvReader.Configuration.TrimOptions = TrimOptions.Trim;
                        csvReader.Configuration.BadDataFound = BadDataFound;

                        // TODO implement
                        // csvReader.Configuration.AllowComments = true;
                        // csvReader.Configuration.Comment = '#';

                        var originalHeaderValidated = csvReader.Configuration.HeaderValidated;
                        csvReader.Configuration.HeaderValidated = (isValid, headerNames, headerIndex, context) =>
                        {
                            // Everything is OK, just go out
                            if (isValid)
                                return;

                            // Allow DESCRIPTION header to be missing (it's actually missing in older shared parameter files)
                            if (nameof(Parameter.Description).Equals(headerNames?.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                                return;

                            // Allow USERMODIFIABLE header to be missing (it's actually missing in older shared parameter files)
                            if (nameof(Parameter.UserModifiable).Equals(headerNames?.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                                return;

                            originalHeaderValidated(false, headerNames, headerIndex, context);
                        };

                        var originalMissingFieldFound = csvReader.Configuration.MissingFieldFound;
                        csvReader.Configuration.MissingFieldFound = (headerNames, index, context) =>
                        {
                            // Allow DESCRIPTION header to be missing (it's actually missing in older shared parameter files)
                            if (nameof(Parameter.Description).Equals(headerNames?.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                                return;

                            // Allow USERMODIFIABLE header to be missing (it's actually missing in older shared parameter files)
                            if (nameof(Parameter.UserModifiable).Equals(headerNames?.FirstOrDefault(), StringComparison.OrdinalIgnoreCase))
                                return;

                            originalMissingFieldFound(headerNames, index, context);
                        };

                        switch (section.Key)
                        {
                            // Parse *META section
                            case Sections.META:
                                csvReader.Configuration.RegisterClassMap<MetaClassMap>();
                                meta = csvReader.GetRecords<Meta>().FirstOrDefault();
                                break;

                            // Parse *GROUP section
                            case Sections.GROUPS:
                                csvReader.Configuration.RegisterClassMap<GroupClassMap>();
                                groups = csvReader.GetRecords<Group>().ToList();
                                break;

                            // Parse *PARAM section
                            case Sections.PARAMS:
                                csvReader.Configuration.RegisterClassMap<ParameterClassMap>();
                                parameters = csvReader.GetRecords<Parameter>().ToList();
                                break;

                            default:
                                Debug.WriteLine($"Unknown section type: {section.Key}");
                                continue;
                        }
                    }
                }
            }

            // Post-process parameters by assigning group names using group IDs
            // and recover UnitType from ParameterType
            parameters = parameters
                .AsParallel()
                .Select(p =>
                {
                    p.GroupName = groups?.FirstOrDefault(g => g.Id == p.GroupId)?.Name;
                    p.UnitType = p.ParameterType.GetUnitType();
                    return p;
                })
                .ToList();

            return new SharedParameterFile(meta, groups, parameters);
        }

        /// <summary>
        /// Handles cases when invalid data raises <see cref="BadDataException"/>.
        /// </summary>
        /// <param name="readingContext">CSV parsing context.</param>
        /// <exception cref="BadDataException"></exception>
        private static void BadDataFound(ReadingContext readingContext)
        {
            if (readingContext.Field.Contains('\"')) // Allow double quotes in parameter names
            {
                return;
            }

            throw new BadDataException(readingContext, $"File contains bad / invalid data: {readingContext.Field}");
        }

        /// <summary>
        /// Defines the names of shared parameter file sections
        /// </summary>
        internal struct Sections
        {
            public const string META = "META";
            public const string GROUPS = "GROUP";
            public const string PARAMS = "PARAM";
        }

        /// <summary>
        /// Represents the entry of the *META section of a shared parameter file
        /// </summary>
        public class Meta
        {
            /// <summary>
            /// Gets or sets the version.
            /// </summary>
            /// <value>
            /// The version.
            /// </value>
            public int Version { get; set; }

            /// <summary>
            /// Gets or sets the minimum version.
            /// </summary>
            /// <value>
            /// The minimum version.
            /// </value>
            public int MinVersion { get; set; }
        }

        /// <summary>
        /// Represents the entries of the *GROUP section of a shared parameter file
        /// </summary>
        public class Group
        {
            /// <summary>
            /// Gets or sets the identifier of the group.
            /// </summary>
            /// <value>
            /// The identifier of the group.
            /// </value>
            public int Id { get; set; }

            /// <summary>
            /// Gets or sets the name of the group.
            /// </summary>
            /// <value>
            /// The name of the group.
            /// </value>
            public string Name { get; set; }
        }

        /// <summary>
        /// Represents the entries of the *PARAM section of a shared parameter file
        /// </summary>
        /// <seealso cref="T:CodeCave.Revit.Toolkit.Parameters.IDefinition" />
        /// <seealso cref="T:CodeCave.Revit.Toolkit.Parameters.IParameter" />
        public class Parameter : IDefinition, IParameter
        {
            /// <inheritdoc />
            /// <summary>
            /// Gets the unique identifier.
            /// </summary>
            /// <value>
            /// The unique identifier.
            /// </value>
            public Guid Guid { get; set; } = Guid.Empty;

            /// <inheritdoc />
            /// <summary>
            /// Gets a value indicating whether parameter is shared.
            /// </summary>
            /// <value>
            /// <c>true</c> if this parameter is shared; otherwise, <c>false</c>.
            /// </value>
            public bool IsShared => true;

            /// <inheritdoc />
            /// <summary>
            /// Gets the name.
            /// </summary>
            /// <value>
            /// The name.
            /// </value>
            public string Name { get; set; }

            /// <inheritdoc />
            /// <summary>
            /// Gets the type of the unit.
            /// </summary>
            /// <value>
            /// The type of the unit.
            /// </value>
            public UnitType UnitType { get; set; } = UnitType.UT_Undefined;

            /// <inheritdoc />
            /// <summary>
            /// Gets the parameter group.
            /// </summary>
            /// <value>
            /// The parameter group.
            /// </value>
            public BuiltInParameterGroup ParameterGroup { get; set; } = BuiltInParameterGroup.INVALID;

            /// <inheritdoc />
            /// <summary>
            /// Gets the type of the parameter.
            /// </summary>
            /// <value>
            /// The type of the parameter.
            /// </value>
            public ParameterType ParameterType { get; set; } = ParameterType.Invalid;

            /// <inheritdoc />
            /// <summary>
            /// Gets the display type of the unit.
            /// </summary>
            /// <value>
            /// The display type of the unit.
            /// </value>
            public DisplayUnitType DisplayUnitType { get; set; } = DisplayUnitType.DUT_UNDEFINED;

            /// <summary>
            /// Gets or sets the group ID.
            /// </summary>
            /// <value>
            /// The group ID.
            /// </value>
            public int GroupId { get; set; } = -1;

            /// <summary>
            /// Gets the name of the group.
            /// </summary>
            /// <value>
            /// The name of the group.
            /// </value>
            public string GroupName { get; internal set; } = "";

            /// <summary>
            /// Gets or sets the data category.
            /// </summary>
            /// <value>
            /// The data category.
            /// </value>
            public string DataCategory { get; set; } = "";

            /// <summary>
            /// Gets or sets a value indicating whether this instance is visible.
            /// </summary>
            /// <value>
            ///   <c>true</c> if this instance is visible; otherwise, <c>false</c>.
            /// </value>
            public bool IsVisible { get; set; } = true;

            /// <summary>
            /// Gets or sets the description.
            /// </summary>
            /// <value>
            /// The description.
            /// </value>
            public string Description { get; set; } = "";

            /// <summary>
            /// Gets or sets a value indicating whether [user modifiable].
            /// </summary>
            /// <value>
            ///   <c>true</c> if [user modifiable]; otherwise, <c>false</c>.
            /// </value>
            public bool UserModifiable { get; set; } = true;

            /// <summary>
            /// Determines whether the specified <see cref="Object" />, is equal to this instance.
            /// </summary>
            /// <param name="obj">The <see cref="Object" /> to compare with this instance.</param>
            /// <returns>
            ///   <c>true</c> if the specified <see cref="Object" /> is equal to this instance; otherwise, <c>false</c>.
            /// </returns>
            public override bool Equals(object obj)
            {
                if (!(obj is Parameter))
                {
                    // ReSharper disable once BaseObjectEqualsIsObjectEquals
                    return base.Equals(obj);
                }

                var other = (Parameter) obj;
                return Guid.Equals(other.Guid) &&
                       Name.Equals(other.Name) &&
                       IsShared.Equals(other.IsShared) &&
                       Description.Equals(other.Description) &&
                       (GroupId.Equals(other.GroupId) || GroupName.Equals(other.GroupName));
            }

            /// <summary>
            /// Returns a hash code for this instance.
            /// </summary>
            /// <returns>
            /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
            /// </returns>
            public override int GetHashCode()
            {
                return -737073652 + EqualityComparer<Guid>.Default.GetHashCode(Guid);
            }
        }

        /// <inheritdoc />
        ///  <summary>
        ///  </summary>
        ///  <seealso cref="T:CsvHelper.Configuration.ClassMap`1" />
        internal sealed class MetaClassMap : ClassMap<Meta>
        {
            /// <inheritdoc />
            /// <summary>
            /// Initializes a new instance of the <see cref="T:CodeCave.Revit.Toolkit.Parameters.Shared.SharedParameterFile.MetaClassMap" /> class.
            /// </summary>
            public MetaClassMap()
            {
                Map(m => m.Version).Name("VERSION");
                Map(m => m.MinVersion).Name("MINVERSION");
            }
        }

        /// <inheritdoc />
        ///  <summary>
        ///  </summary>
        ///  <seealso cref="T:CsvHelper.Configuration.ClassMap`1" />
        internal sealed class GroupClassMap : ClassMap<Group>
        {
            /// <inheritdoc />
            /// <summary>
            /// Initializes a new instance of the <see cref="T:CodeCave.Revit.Toolkit.Parameters.Shared.SharedParameterFile.GroupClassMap" /> class.
            /// </summary>
            public GroupClassMap()
            {
                Map(m => m.Id).Name("ID");
                Map(m => m.Name).Name("NAME");
            }
        }

        internal sealed class ParameterClassMap : ClassMap<Parameter>
        {
            /// <inheritdoc />
            /// <summary>
            /// Initializes a new instance of the <see cref="T:CodeCave.Revit.Toolkit.Parameters.Shared.SharedParameterFile.ParameterClassMap" /> class.
            /// </summary>
            public ParameterClassMap()
            {
                // "Visible" fields
                Map(m => m.Guid).Name("GUID").TypeConverter<GuidConverter>();
                Map(m => m.Name).Name("NAME");
                Map(m => m.ParameterType).Name("DATATYPE").TypeConverter<ParameterTypeConverter>();
                Map(m => m.DataCategory).Name("DATACATEGORY");
                Map(m => m.GroupId).Name("GROUP");
                Map(m => m.IsVisible).Name("VISIBLE").TypeConverter<AdvancedBooleanConverter>();
                Map(m => m.Description).Name("DESCRIPTION");
                Map(m => m.UserModifiable).Name("USERMODIFIABLE").TypeConverter<AdvancedBooleanConverter>();

                // Ignored fields
                Map(m => m.UnitType).Ignore();
                Map(m => m.DisplayUnitType).Ignore();
                Map(m => m.ParameterGroup).Ignore();
                Map(m => m.GroupName).Ignore();
            }

            /// <inheritdoc />
            /// <summary>
            /// Ensures a correct conversion of <see cref="ParameterType"/> values to/from their relative string representations
            /// </summary>
            /// <seealso cref="ITypeConverter" />
            internal class ParameterTypeConverter : ITypeConverter
            {
                /// <inheritdoc />
                /// <summary>
                /// Converts the string to an object.
                /// </summary>
                /// <param name="text">The string to convert to an object.</param>
                /// <param name="row">The <see cref="T:CsvHelper.ICsvReaderRow" /> for the current record.</param>
                /// <param name="propertyMapData">The <see cref="T:CsvHelper.Configuration.CsvPropertyMapData" /> for the property/field being created.</param>
                /// <returns>
                /// The object created from the string.
                /// </returns>
                public object ConvertFromString(string text, IReaderRow row, MemberMapData propertyMapData)
                {
                    return text.FromSharedDataType();
                }

                /// <inheritdoc />
                /// <summary>
                /// Converts the object to a string.
                /// </summary>
                /// <param name="value">The object to convert to a string.</param>
                /// <param name="row">The <see cref="T:CsvHelper.ICsvWriterRow" /> for the current record.</param>
                /// <param name="propertyMapData">The <see cref="T:CsvHelper.Configuration.CsvPropertyMapData" /> for the property/field being written.</param>
                /// <returns>
                /// The string representation of the object.
                /// </returns>
                public string ConvertToString(object value, IWriterRow row, MemberMapData propertyMapData)
                {
                    var parameterType = (ParameterType)value;
                    return parameterType.ToSharedDataType();
                }
            }

            /// <inheritdoc />
            /// <summary>
            /// A specialized CSV field value converter.
            /// Helps to serialize <see cref="bool"/> properties of <see cref="Parameter"/> object correctly.
            /// </summary>
            /// <seealso cref="CsvHelper.TypeConversion.BooleanConverter" />
            internal class AdvancedBooleanConverter : BooleanConverter
            {
                /// <inheritdoc />
                /// <summary>
                /// Converts the object to a string.
                /// </summary>
                /// <param name="value">The object to convert to a string.</param>
                /// <param name="row">The <see cref="T:CsvHelper.ICsvWriterRow" /> for the current record.</param>
                /// <param name="propertyMapData">The <see cref="T:CsvHelper.Configuration.CsvPropertyMapData" /> for the property/field being written.</param>
                /// <returns>
                /// The string representation of the object.
                /// </returns>
                public override string ConvertToString(object value, IWriterRow row, MemberMapData propertyMapData)
                {
                    if (string.IsNullOrWhiteSpace(value?.ToString()))
                        return "0";

                    return (bool.TryParse(value.ToString(), out var boolValue) && boolValue) ? "1" : "0";
                }
            }
        }
    }
}
