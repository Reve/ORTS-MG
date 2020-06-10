using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.ComponentModel;
using System.Text;
using System.Threading.Tasks;

namespace Core.GetText.Extractor
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            RootCommand rootCommand = new RootCommand("Extracts strings from C# source code files to creates or updates PO template file");
            rootCommand.AddArgument(new Argument<string>("input file | file mask") 
            { 
                Arity = ArgumentArity.ZeroOrMore, Description = "One or more input files. Wildcard can be used i.e '*.cs'. Defaults to *.cs and *.xaml if not specified", 
                Name = "InputFiles" 
            });
            rootCommand.AddOption(new Option<bool>(new string[] { "--from-file", "-f" }, $"Read the names of the input files from file(s) given as input file instead of getting them from the command line"));
            rootCommand.AddOption(new Option<string>(new string[] { "--output", "-o" }, $"Output PO template file name. Using of '.pot' file type is strongly recommended. If not specified, {CommandLineOptions.DefaultOutput} will be used"));
            rootCommand.AddOption(new Option<string>(new string[] { "--directory", "-D" }, "One or more input directories for C# source code files") 
            {                 
                Argument = new Argument<string>() { Arity = ArgumentArity.OneOrMore }, 
                Name = "Directories"
            });
            rootCommand.AddOption(new Option<bool>(new string[] { "--recursive", "-r" }, "Process all subdirectories"));
            rootCommand.AddOption(new Option<string>(new string[] { "--search-pattern", "-p" }, $"Custom regex pattern to find strings. Macro {ExtractorCSharp.CSharpStringPatternMacro} can be used for C# string. Multiple patterns can be provided") 
            { 
                Argument = new Argument<string>() { Arity = ArgumentArity.OneOrMore } 
            });
            rootCommand.AddOption(new Option<string>(new string[] { "--encoding", "-e" }, $"Specifies the encoding of the input files. If not specified, encoding will be detected from file, or fall back to '{Encoding.Default.EncodingName}'")
            {
                Name = "EncodingName",
            });
            rootCommand.AddOption(new Option<bool>(new string[] { "--merge", "-m" }, "Merge with existing file instead of overwrite")
            { 
                Name = "MergeExisting"

            });
            rootCommand.AddOption(new Option<bool>(new string[] { "--verbose", "-v" }, "Verbose output"));

            rootCommand.Handler = CommandHandler.Create((CommandLineOptions options) =>
                {
                    options.UpdateDefaults();
                    ExtractorCSharp extractor = new ExtractorCSharp(options);
                    extractor.GetMessages();
                    extractor.Save();
                });

            return await rootCommand.InvokeAsync(args).ConfigureAwait(false);
        }
    }
}
