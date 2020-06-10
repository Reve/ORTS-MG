using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Core.GetText.Extractor
{
    internal class CommandLineOptions
	{

		internal Encoding Encoding { get; set; } = Encoding.Default;

		public const string DefaultOutput = "messages.pot";

		public string Output { get; set; } = DefaultOutput;
		public bool MergeExisting { get; set; }
		public bool FromFile { get; set; }
		public bool Recursive { get; set; }
		public bool Verbose { get; set; }
		public string EncodingName { get; set; }
		public bool DetectEncoding { get; set; }
		public List<string> InputFiles { get; private set; } = new List<string>();
		public List<string> Directories { get; private set; } = new List<string>();
		public List<string> SearchPattern { get; private set; } = new List<string>();

		public void UpdateDefaults()
		{

			DetectEncoding = string.IsNullOrEmpty(EncodingName);
			if (!string.IsNullOrEmpty(EncodingName))
			{
				try
				{
					Encoding = Encoding.GetEncoding(EncodingName);
				}
				catch (ArgumentException)
				{
					Encoding = Encoding.Default;
				}
			}

			if (InputFiles == null || InputFiles.Count == 0)
			{
				InputFiles = new List<string>() { "*.cs", "*.xaml" };
			}
			else if (FromFile)
			{
				List<string> files = InputFiles;
				InputFiles = new List<string>();
				foreach (string file in files)
				{
					ReadInputFiles(file);
				}
			}

			if (Directories == null || Directories.Count == 0)
            {
				Directories.Add(Environment.CurrentDirectory);
			}
		}

		private void ReadInputFiles(string fileName)
		{
			using (StreamReader r = new StreamReader(fileName))
			{
				string line;
				while ((line = r.ReadLine()) != null && line.Trim().Length > 0)
				{
					if (!InputFiles.Contains(line))
						InputFiles.Add(line);
				}
			}
		}
	}
}
