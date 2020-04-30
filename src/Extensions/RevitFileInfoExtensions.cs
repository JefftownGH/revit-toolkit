﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeCave.Revit.Toolkit
{
    public static class RevitFileInfoExtensions
    {
        #region Constants

        /// <summary>
        /// List of known Revit file properties.
        /// </summary>
        internal struct KnownRevitInfoProps
        {
            public const string USERNAME = "Username";
            public const string CENTRAL_MODEL_PATH = "Central Model Path";
            public const string REVIT_BUILD = "Revit Build";
            public const string LAST_SAVE_PATH = "Last Save Path";
            public const string OPEN_WORKSET_DEFAULT = "Open Workset Default";
            public const string PROJECT_SPARK_FILE = "Project Spark File";
            public const string CENTRAL_MODEL_IDENTITY = "Central Model Identity";
            public const string LOCALE = "Locale when saved";
            public const string LOCAL_SAVED_TO_CENTRAL = "All Local Changes Saved To Central";
            public const string CENTRAL_MODEL_VERSION = "Central model's version number corresponding to the last reload latest";
            public const string CENTRAL_MODEL_GUID = "Central model's episode GUID corresponding to the last reload latest";
            public const string UNIQUE_DOCUMENT_GUID = "Unique Document GUID";
            public const string UNIQUE_DOCUMENT_INCREMENT = "Unique Document Increments";
        }

        private static readonly Regex versionExtractor =
            new Regex(@"(?<vendor>\w*) (?<software>(\w|\s)*) (?<version>\d{4}) \(Build\: (?<build>\d*)_(?<revision>\d*)(\((?<arch>\w*)\))?\)",
                RegexOptions.Compiled |
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant);

        #endregion

        #region Methods

        /// <summary>
        /// Parses the username.
        /// </summary>
        /// <param name="revitFileInfo">The revit file information.</param>
        /// <param name="properties">The properties.</param>
        /// <exception cref="T:System.ArgumentNullException">properties</exception>
        public static void ParseUsername(this RevitFileInfo revitFileInfo, Dictionary<string, string> properties)
        {
            if (properties == null)
                throw new ArgumentNullException(nameof(properties));

            // Parse username field
            if (!properties.ContainsKey(KnownRevitInfoProps.USERNAME))
                return;

            revitFileInfo.Username = properties[KnownRevitInfoProps.USERNAME];
        }

        /// <summary>
        /// Parses the revit.
        /// </summary>
        /// <param name="revitFileInfo">The revit file information.</param>
        /// <param name="properties">The properties.</param>
        /// <exception cref="T:System.ArgumentNullException">properties</exception>
        public static void ParseRevit(this RevitFileInfo revitFileInfo, Dictionary<string, string> properties)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));

            // Parse Revit Build string
            if (!properties.ContainsKey(KnownRevitInfoProps.REVIT_BUILD))
                return;

            var versionObj = versionExtractor.Match(properties[KnownRevitInfoProps.REVIT_BUILD]);
            if (!versionObj.Success)
                return;

            revitFileInfo.Vendor = versionObj?.Groups["vendor"]?.Value;
            revitFileInfo.Name = versionObj?.Groups["software"]?.Value;
            revitFileInfo.Is64Bit = versionObj.Groups["arch"]?.Value?.Contains("x64") ?? false;

            var versionString = string.Format("{0}.{1}.{2}",
                versionObj.Groups["version"].Value,
                versionObj.Groups["build"].Value,
                versionObj.Groups["revision"].Value);
            revitFileInfo.Version = new Version(versionString);
        }

        /// <summary>
        /// Parses the locale.
        /// </summary>
        /// <param name="revitFileInfo">The revit file information.</param>
        /// <param name="properties">The properties.</param>
        /// <exception cref="T:System.ArgumentNullException">properties</exception>
        public static void ParseLocale(this RevitFileInfo revitFileInfo, Dictionary<string, string> properties)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));

            // Parse last saved locale
            if (!properties.ContainsKey(KnownRevitInfoProps.LOCALE))
                return;

            var localeRaw = properties[KnownRevitInfoProps.LOCALE];
            revitFileInfo.Locale = CultureInfo
                .GetCultures(CultureTypes.NeutralCultures)
                .FirstOrDefault(c => c.ThreeLetterWindowsLanguageName == localeRaw);
        }

        /// <summary>
        /// Parses the document information.
        /// </summary>
        /// <param name="revitFileInfo">The revit file information.</param>
        /// <param name="properties">The properties.</param>
        /// <exception cref="T:System.ArgumentNullException">properties</exception>
        public static void ParseDocumentInfo(this RevitFileInfo revitFileInfo, Dictionary<string, string> properties)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));

            // Parse document unique id
            if (!properties.ContainsKey(KnownRevitInfoProps.UNIQUE_DOCUMENT_GUID))
                return;

            var guidRaw = properties[KnownRevitInfoProps.UNIQUE_DOCUMENT_GUID];
            revitFileInfo.Guid = new Guid(guidRaw);
        }

        #endregion
    }
}
