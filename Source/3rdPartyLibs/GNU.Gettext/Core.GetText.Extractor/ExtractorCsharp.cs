using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using GNU.Gettext;

[assembly: InternalsVisibleTo("Core.GetText.Test")]

namespace Core.GetText.Extractor
{
    public class ExtractorCSharp
    {
        //     const string CSharpStringPatternExplained = @"
        //(\w+)\s*=\s*    # key =
        //(               # Capturing group for the string
        //    @""               # verbatim string - match literal at-sign and a quote
        //    (?:
        //        [^""]|""""    # match a non-quote character, or two quotes
        //    )*                # zero times or more
        //    ""                #literal quote
        //|               #OR - regular string
        //    ""              # string literal - opening quote
        //    (?:
        //        \\.         # match an escaped character,
        //        |[^\\""]    # or a character that isn't a quote or a backslash
        //    )*              # a few times
        //    ""              # string literal - closing quote
        //)";

        private static readonly char[] unescapeString = { '@', '"' };
        private static readonly string[] attributeTags = "Title Content Text Header ToolTip".Split(' ');
        private static readonly string[] nameTags = "TextBlock Button".Split(' ');

        const string CSharpStringPattern = @"(@""(?:[^""]|"""")*""|""(?:\\.|[^\\""])*"")";
        const string ConcatenatedStringsPattern = @"((@""(?:[^""]|"""")*""|""(?:\\.|[^\\""])*"")\s*(?:\+|;|,|\))\s*){2,}";
        const string TwoStringsArgumentsPattern = CSharpStringPattern + @"\s*,\s*" + CSharpStringPattern;
        const string ThreeStringsArgumentsPattern = TwoStringsArgumentsPattern + @"\s*,\s*" + CSharpStringPattern;

        public const string CSharpStringPatternMacro = "%CSharpString%";

        private readonly Catalog catalog;
        private readonly CommandLineOptions options;

        private readonly Dictionary<string, string> resources = new Dictionary<string, string>();

        const string blockComments = @"/\*(.*?)\*/";
        const string lineComments = @"//(.*?)(\r?\n|$)";

        public Catalog Catalog => catalog;

        #region Constructors
        internal ExtractorCSharp(CommandLineOptions options)
        {
            this.options = options;
            this.catalog = new Catalog();
            if (this.options.MergeExisting && File.Exists(this.options.Output))
            {
                catalog.Load(this.options.Output);
                foreach (CatalogEntry entry in catalog)
                    entry.ClearReferences();
            }
            else
            {
                catalog.Project = "PACKAGE VERSION";
            }

            this.options.Output = Path.GetFullPath(this.options.Output);
        }
        #endregion

        public void GetMessages()
        {
            // Expand input files list
            Dictionary<string, string> inputFiles = new Dictionary<string, string>();
            foreach (string dir in options.Directories)
            {
                foreach (string fileNameOrMask in options.InputFiles)
                {
                    string[] filesInDir = Directory.GetFiles(dir, fileNameOrMask, options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
                    foreach (string fileName in filesInDir)
                    {
                        string fullFileName = Path.GetFullPath(fileName);
                        if (!inputFiles.ContainsKey(fullFileName))
                            inputFiles.Add(fullFileName, fullFileName);
                    }
                }
            }

            foreach (string inputFile in inputFiles.Values)
            {
                GetMessagesFromFile(inputFile);
            }
        }

        private void GetMessagesFromFile(string inputFile)
        {
            inputFile = Path.GetFullPath(inputFile);
            StreamReader input = new StreamReader(inputFile, options.Encoding, options.DetectEncoding);
            string text = input.ReadToEnd();
            input.Close();
            GetMessages(text, inputFile);
        }


        public void GetMessages(string text, string inputFile)
        {
            text = RemoveComments(text);

            // Gettext functions patterns
            ProcessPattern(ExtractMode.Msgid, @"GetString\s*\(\s*" + CSharpStringPattern, text, inputFile);
            ProcessPattern(ExtractMode.Msgid, @"GetStringFmt\s*\(\s*" + CSharpStringPattern, text, inputFile);
            ProcessPattern(ExtractMode.MsgidPlural, @"GetPluralString\s*\(\s*" + TwoStringsArgumentsPattern, text, inputFile);
            ProcessPattern(ExtractMode.MsgidPlural, @"GetPluralStringFmt\s*\(\s*" + TwoStringsArgumentsPattern, text, inputFile);
            ProcessPattern(ExtractMode.ContextMsgid, @"GetParticularString\s*\(\s*" + TwoStringsArgumentsPattern, text, inputFile);
            ProcessPattern(ExtractMode.ContextMsgid, @"GetParticularPluralString\s*\(\s*" + ThreeStringsArgumentsPattern, text, inputFile);


            // Winforms patterns
            ProcessPattern(ExtractMode.Msgid, @"\.\s*Text\s*=\s*" + CSharpStringPattern + @"\s*;", text, inputFile);
            ProcessPattern(ExtractMode.MsgidConcat, @"\.\s*Text\s*=\s*" + ConcatenatedStringsPattern, text, inputFile);

            ProcessPattern(ExtractMode.Msgid, @"\.\s*HeaderText\s*=\s*" + CSharpStringPattern + @"\s*;", text, inputFile);
            ProcessPattern(ExtractMode.MsgidConcat, @"\.\s*HeaderText\s*=\s*" + ConcatenatedStringsPattern, text, inputFile);

            ProcessPattern(ExtractMode.Msgid, @"\.\s*ToolTipText\s*=\s*" + CSharpStringPattern + @"\s*;", text, inputFile);
            ProcessPattern(ExtractMode.MsgidConcat, @"\.\s*ToolTipText\s*=\s*" + ConcatenatedStringsPattern, text, inputFile);

            ProcessPattern(ExtractMode.Msgid, @"\.\s*SetToolTip\s*\([^\\""]*\s*,\s*" + CSharpStringPattern + @"\s*\)\s*;", text, inputFile);
            ProcessPattern(ExtractMode.MsgidConcat, @"\.\s*SetToolTip\s*\([^\\""]*\s*,\s*" + ConcatenatedStringsPattern, text, inputFile);

            if (ReadResources(inputFile))
                ProcessPattern(ExtractMode.MsgidFromResx, @"\.\s*ApplyResources\s*\([^\\""]*\s*,\s*" + CSharpStringPattern + @"\s*\)\s*;", text, inputFile);

            ReadXaml(inputFile);

            // Custom patterns
            foreach (string pattern in options.SearchPattern)
            {
                ProcessPattern(ExtractMode.Msgid, pattern.Replace(CSharpStringPatternMacro, CSharpStringPattern, StringComparison.OrdinalIgnoreCase), text, inputFile);
            }
        }

        public void Save()
        {
            if (File.Exists(options.Output))
            {
                string bakFileName = options.Output + ".bak";
                if (File.Exists(bakFileName))
                    File.Delete(bakFileName);
                File.Move(options.Output, bakFileName);
            }
            catalog.Save(options.Output);
        }

        public static string RemoveComments(string input)
        {
            return Regex.Replace(input, blockComments + "|" + lineComments + "|" + CSharpStringPattern, m =>
            {
                if (m.Value.StartsWith("/*", StringComparison.Ordinal) || m.Value.StartsWith("//", StringComparison.Ordinal))
                {
                    // Replace the comments with empty, i.e. remove them
                    return m.Value.StartsWith("//", StringComparison.Ordinal) ? m.Groups[3].Value : "";
                }
                // Keep the literal strings
                return m.Value;
            }, RegexOptions.Singleline);
        }

        private void ProcessPattern(ExtractMode mode, string pattern, string text, string inputFile)
        {
            Regex r = new Regex(pattern, RegexOptions.IgnorePatternWhitespace | RegexOptions.Multiline);
            MatchCollection matches = r.Matches(text);
            foreach (Match match in matches)
            {
                GroupCollection groups = match.Groups;
                if (groups.Count < 2)
                    throw new Exception($"Invalid pattern '{pattern}'.\nTwo groups are required at least.\nSource: {match.Value}");

                // Initialisation
                string context = string.Empty;
                string msgid = string.Empty;
                string msgidPlural = string.Empty;
                switch (mode)
                {
                    case ExtractMode.Msgid:
                        msgid = Unescape(groups[1].Value);
                        break;
                    case ExtractMode.MsgidConcat:
                        MatchCollection matches2 = Regex.Matches(groups[0].Value, CSharpStringPattern);
                        StringBuilder sb = new StringBuilder();
                        foreach (Match match2 in matches2)
                        {
                            sb.Append(Unescape(match2.Value));
                        }
                        msgid = sb.ToString();
                        break;
                    case ExtractMode.MsgidFromResx:
                        string controlId = Unescape(groups[1].Value);
                        msgid = ExtractResourceString(controlId);
                        if (string.IsNullOrEmpty(msgid))
                        {
                            if (options.Verbose)
                                Trace.WriteLine($"Warning: cannot extract string for control '{controlId}' ({inputFile})");
                            continue;
                        }
                        if (controlId == msgid)
                            continue; // Text property was initialized by controlId and was not changed so this text is not usable in application
                        break;
                    case ExtractMode.MsgidPlural:
                        if (groups.Count < 3)
                            throw new Exception($"Invalid 'GetPluralString' call.\nSource: {match.Value}");
                        msgid = Unescape(groups[1].Value);
                        msgidPlural = Unescape(groups[2].Value);
                        break;
                    case ExtractMode.ContextMsgid:
                        if (groups.Count < 3)
                            throw new Exception($"Invalid get context message call.\nSource: {match.Value}");
                        context = Unescape(groups[1].Value);
                        msgid = Unescape(groups[2].Value);
                        if (groups.Count == 4)
                            msgidPlural = Unescape(groups[3].Value);
                        break;
                }

                if (string.IsNullOrEmpty(msgid))
                {
                    if (options.Verbose)
                        Trace.Write($"WARN: msgid is empty in {inputFile}\r\n");
                }
                else
                {
                    MergeWithEntry(context, msgid, msgidPlural, inputFile, CalcLineNumber(text, match.Index));
                }
            }
        }

        private void MergeWithEntry(string context, string msgid, string msgidPlural, string inputFile, int line)
        {
            // Processing
            CatalogEntry entry = catalog.FindItem(msgid, context);
            bool entryFound = entry != null;
            if (!entryFound)
                entry = new CatalogEntry(catalog, msgid, msgidPlural);

            // Add source reference if it not exists yet
            // Each reference is in the form "path_name:line_number"
            string sourceRef = $"{Path.GetRelativePath(Path.GetFullPath(options.Output), Path.GetFullPath(inputFile))}:{line}";
            entry.AddReference(sourceRef); // Wont be added if exists

            if (FormatValidator.IsFormatString(msgid) || FormatValidator.IsFormatString(msgidPlural))
            {
                if (!entry.IsInFormat("csharp"))
                    entry.Flags += ", csharp-format";
                Trace.WriteLineIf(!FormatValidator.IsValidFormatString(msgid), $"Warning: string format may be invalid: '{msgid}'\nSource: {sourceRef}");
                Trace.WriteLineIf(!FormatValidator.IsValidFormatString(msgidPlural), $"Warning: plural string format may be invalid: '{msgidPlural}'\nSource: {sourceRef}");
            }

            if (!string.IsNullOrEmpty(msgidPlural))
            {
                if (!entryFound)
                {
                    AddPluralsTranslations(entry);
                }
                else
                    UpdatePluralEntry(entry, msgidPlural);
            }
            if (!string.IsNullOrEmpty(context))
            {
                entry.Context = context;
                entry.AddAutoComment($"Context: {context}", true);
            }

            if (!entryFound)
                catalog.AddItem(entry);
        }

        private bool ReadResources(string inputFile)
        {
            resources.Clear();
            string resxFileName = Path.Combine(Path.GetDirectoryName(inputFile), Path.GetFileNameWithoutExtension(inputFile));
            if (Path.GetExtension(resxFileName).Equals(".Designer", StringComparison.OrdinalIgnoreCase))
                resxFileName = Path.Combine(Path.GetDirectoryName(inputFile), Path.GetFileNameWithoutExtension(resxFileName));
            resxFileName += ".resx";

            if (!File.Exists(resxFileName))
                return false;
            if (options.Verbose)
                Debug.WriteLine($"Extracting from resource file: {resxFileName} (Input file: {inputFile})");

            using (ResXResourceReader rsxr = new ResXResourceReader(resxFileName)
            { BasePath = Path.GetDirectoryName(resxFileName) })
            {
                foreach (DictionaryEntry entry in rsxr)
                {
                    if (entry.Value is string)
                    {
                        resources.Add(entry.Key.ToString(), entry.Value.ToString());
                    }
                }
            }
            return true;
        }

        private void ReadXaml(string inputFile)
        {
            if (Path.GetExtension(inputFile) != ".xaml")
                return;

            Dictionary<string, XAttribute> attributes;
            var xaml = XDocument.Load(inputFile, LoadOptions.SetLineInfo);

            foreach (XElement node in xaml.Descendants())
            {
                if (nameTags.Contains(node.Name.LocalName, StringComparer.OrdinalIgnoreCase) && !string.IsNullOrEmpty(node.Value))
                    MergeWithEntry(string.Empty, node.Value, string.Empty, inputFile, ((IXmlLineInfo)node).LineNumber);

                attributes = new Dictionary<string, XAttribute>();
                foreach (var attribute in node.Attributes())
                    attributes.Add(attribute.Name.LocalName, attribute);

                foreach (var tag in attributeTags)
                    if (attributes.Keys.Contains(tag, StringComparer.OrdinalIgnoreCase))
                        MergeWithEntry(string.Empty, (string)attributes[tag], string.Empty, inputFile, ((IXmlLineInfo)attributes[tag]).LineNumber);
            }
        }

        private string ExtractResourceString(string controlId)
        {
            if (!resources.TryGetValue(controlId + ".Text", out string msgid))
                if (!resources.TryGetValue(controlId + ".TooTipText", out msgid))
                    if (!resources.TryGetValue(controlId + ".HeaderText", out msgid))
                        return null;
            return msgid;
        }

        private static int CalcLineNumber(string text, int pos)
        {
            if (pos >= text.Length)
                pos = text.Length - 1;
            int line = 0;
            for (int i = 0; i < pos; i++)
                if (text[i] == '\n')
                    line++;
            return line + 1;
        }

        private void UpdatePluralEntry(CatalogEntry entry, string msgidPlural)
        {
            if (!entry.HasPlural)
            {
                AddPluralsTranslations(entry);
                entry.SetPluralString(msgidPlural);
            }
            else if (entry.HasPlural && entry.PluralString != msgidPlural)
            {
                entry.SetPluralString(msgidPlural);
            }
        }

        private void AddPluralsTranslations(CatalogEntry entry)
        {
            // Creating 2 plurals forms by default
            // Translator should change it using expression for it own country
            // http://translate.sourceforge.net/wiki/l10n/pluralforms
            List<string> translations = new List<string>(catalog.PluralFormsCount);
            for (int i = 0; i < catalog.PluralFormsCount; i++)
                translations.Add("");
            entry.SetTranslations(translations);
        }

        private static string Unescape(string msgid)
        {
            Debug.Assert(!string.IsNullOrEmpty(msgid));
            return StringEscaping.UnEscape(msgid[0] == '@' ? StringEscaping.EscapeMode.CSharpVerbatim : StringEscaping.EscapeMode.CSharp, msgid.Trim(unescapeString));
        }
    }
}
