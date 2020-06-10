using System.IO;
using System.Text;

using Core.GetText.Extractor;

using GNU.Gettext;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Core.GetText.Test
{
	[TestClass]
	public class ExtrractorTests
	{
		[TestMethod]
		public void CommandLineOptionsDefaultTest()
		{
			CommandLineOptions options = new CommandLineOptions();
			options.UpdateDefaults();

			Assert.AreEqual(2, options.InputFiles.Count);
		}

		[TestMethod]
		public void CommandLineOptionsValidEncodingTest()
		{
			CommandLineOptions options = new CommandLineOptions() { EncodingName = "ASCII"};
			options.UpdateDefaults();

			Assert.AreEqual(Encoding.ASCII, options.Encoding);
		}

		[TestMethod]
		public void CommandLineOptionsInvalidEncodingTest()
		{
			CommandLineOptions options = new CommandLineOptions() { EncodingName = "ASCII2" };
			options.UpdateDefaults();

			Assert.AreEqual(Encoding.Default, options.Encoding);
		}

		[TestMethod()]
		public void RemoveCommentsTest()
		{
			string input = @"
/*
 *
 * This
 * is
 * // Comment
 */
string s = ""/*This is not comment*/"";
string s2 = ""This is //not comment too"";
button1.Text = ""Save""; // Save data.Text = ""10""
//button1.Text = ""Save""; // Save data.Text = ""10""
// button1.Text = ""Save""; // Save data.Text = ""10""
/*button1.Text = ""Save""; // Save data.Text = ""10""*/
";
			string output = ExtractorCSharp.RemoveComments(input);
			Assert.IsTrue(output.IndexOf("/*This is not comment*/") >= 0, "Multiline comment chars in string");
			Assert.IsTrue(output.IndexOf("This is //not comment too") >= 0, "Single line comment chars in string");
			Assert.AreEqual(-1, output.IndexOf("// Save"), "Single line comment");
			Assert.AreEqual(-1, output.IndexOf("//button1"), "Single line comment");
			Assert.AreEqual(-1, output.IndexOf("/*\n"), "Multi line comment");
			Assert.AreEqual(-1, output.IndexOf("/*button1"), "Multi line comment in single line");
		}

		[TestMethod()]
		public void ExtractorCSharpTest()
		{
			string ressourceId = $"{GetType().Assembly.GetName().Name}.{"Data.ExtractorTest.txt"}";
			string text = "";
			using (Stream stream = GetType().Assembly.GetManifestResourceStream(ressourceId))
			{
                using StreamReader reader = new StreamReader(stream);
                text = reader.ReadToEnd();
            }

			CommandLineOptions options = new CommandLineOptions();
			options.InputFiles.Add(@"./Test/File/Name.cs"); // File wont be used, feed the plain text
			options.Output = @"./Test.pot";
			options.MergeExisting = false;
			ExtractorCSharp extractor = new ExtractorCSharp(options);
			extractor.GetMessages(text, options.InputFiles[0]);
			extractor.Save();

			int ctx = 0;
			int multiline = 0;
			foreach (CatalogEntry entry in extractor.Catalog)
			{
				if (entry.HasContext)
					ctx++;
				if (entry.String == "multiline-string-1-string-2" ||
					entry.String == "Multiline Hint for label1")
					multiline++;
			}

			Assert.AreEqual(2, ctx, "Context count");

			Assert.AreEqual(2, extractor.Catalog.PluralFormsCount, "PluralFormsCount");
			Assert.AreEqual(17, extractor.Catalog.Count, "Duplicates may not detected");
			Assert.AreEqual(2, multiline, "Multiline string");
		}
	}
}
