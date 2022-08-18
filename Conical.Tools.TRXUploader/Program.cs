using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.Configuration;

namespace TRXUploader
{
    public static class Program
    {
        private const string CONST_HELPTEXT = @"Conical TRX uploader tool
=========================
This tool is designed to make it easy to upload TRX files from disc to a Conical instance.

Note that this tool assumes that each individual unit test should be represented as its own test run within Conical.

The following options are available:

 -help / -?                     Show this help text

 -server XXX                    The name of the server to connect to (default from appsettings.json)
 -token XXX                     The access token to use (default from appsettings.json)

 -product XXX                   The name of the product to upload to
 -testruntype XXX               The test run type to upload to (default from appsettings.json)
 -testrunsetname XXX            The name of the created TRS
 -testrunsetdescription XXX     The description of the created TRS

 -refdate XXX                   The ref date to use if desired
 -refdateformat XXX             The format of the ref date str to use if desired

 -tag XXX                       Specifies a tag to apply to the created TRS

 -source XXX                    The path to process (required)
";
        
        public static async Task<int> Main(string[] args)
        {
            if( args.Length == 0 )
            {
                Console.WriteLine(CONST_HELPTEXT);
                return 0;
            }

            // Read in the default app settings
            var configBuilder = new Microsoft.Extensions.Configuration.ConfigurationBuilder();
            configBuilder.AddJsonFile("appsettings.json", true);
            var config = configBuilder.Build();
            var uploadSettings = new UploadSettings();
            config.Bind("uploadSettings", uploadSettings);

            string refDateStr = null, refDateFormat = null;

            string server = uploadSettings.Server, token = uploadSettings.Token;
            string productName = uploadSettings.Product, sourceFile = null;
            string testRunSetName = uploadSettings.TestRunSetName, testRunSetDescription = uploadSettings.TestRunSetDescription, testRunType = uploadSettings.TestRunType;
            var tags = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
            for (int argIdx = 0; argIdx < args.Length; argIdx++)
            {
                switch (args[argIdx].ToLower())
                {
                    case "-help":
                    case "-?":
                        Console.WriteLine(CONST_HELPTEXT);
                        return 0;

                    case "-server":
                        server = args[++argIdx];
                        break;

                    case "-token":
                        token = args[++argIdx];
                        break;

                    case "-product":
                        productName = args[++argIdx];
                        break;

                    case "-testrunsetname":
                        testRunSetName = args[++argIdx];
                        break;

                    case "-testrunsetdescription":
                        testRunSetDescription = args[++argIdx];
                        break;

                    case "-testruntype":
                        testRunType = args[++argIdx];
                        break;

                    case "-refdate":
                        refDateStr = args[++argIdx];
                        break;

                    case "-refdateformat":
                        refDateFormat = args[++argIdx];
                        break;

                    case "-tag":
                        tags.Add(args[++argIdx]);
                        break;

                    case "-source":
                        sourceFile = args[++argIdx];
                        break;

                    default:
                        Console.WriteLine($"Unknown command line arg '{args[argIdx]}'");
                        return 1;
                }
            }

            Console.WriteLine($"Conical TRX uploader tool");

            if (string.IsNullOrEmpty(sourceFile))
            {
                Console.WriteLine("No source file");
                return 1;
            }

            var fullSourcePath = System.IO.Path.GetFullPath(sourceFile);
            if (!System.IO.File.Exists(fullSourcePath))
            {
                Console.WriteLine($"Source file '{fullSourcePath}' doesn't exist");
                return 1;
            }

            if (string.IsNullOrEmpty(productName))
            {
                Console.WriteLine("No product name specified");
                return 1;
            }

            if (string.IsNullOrEmpty(testRunSetName))
            {
                Console.WriteLine("No test run set name specified");
                return 1;
            }

            if( string.IsNullOrEmpty(testRunType))
            {
                Console.WriteLine("No test run type specified");
                return 1;
            }

            DateTime? refDate = null;
            if( !string.IsNullOrEmpty( refDateStr ))
            {
                if( string.IsNullOrEmpty( refDateFormat))
                {
                    if (!DateTime.TryParse(refDateStr, out var date))
                    {
                        Console.WriteLine($"Cannot parse '{refDateStr}' as a valid ref date");
                        return 1;
                    }
                    else
                        refDate = date;
                }
                else
                {
                    if (!DateTime.TryParseExact(refDateStr, refDateFormat, null, System.Globalization.DateTimeStyles.None, out var date))
                    {
                        Console.WriteLine($"Cannot parse '{refDateStr}' using format string '{refDateFormat}' as a valid ref date ");
                        return 1;
                    }
                    else
                        refDate = date;
                }
            }


            Console.WriteLine($"Loading - {fullSourcePath}");

            var xmlDoc = new System.Xml.XmlDocument();
            using (var readStream = new System.IO.FileStream(fullSourcePath, FileMode.Open, FileAccess.Read))
            {
                var xmlReaderSettings = new System.Xml.XmlReaderSettings()
                {
                    ConformanceLevel = System.Xml.ConformanceLevel.Auto,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    IgnoreWhitespace = true,
                    CloseInput = false,
                };

                using (var xmlReader = System.Xml.XmlReader.Create(readStream, xmlReaderSettings))
                    xmlDoc.Load(xmlReader);
            }

            Console.WriteLine($"Extracting Data");

            var testRunNode = xmlDoc.DocumentElement;

            // This looks a little bit weird, but when the XML file is loaded from disc, the loading code automatically picks the
            // TRX schema file. if we don't specifiy the name space manager, here, then our xpath queries won't return the
            // set of expected nodes
            var xmlNamespaceManager = new System.Xml.XmlNamespaceManager(xmlDoc.NameTable);
            xmlNamespaceManager.AddNamespace("trx", testRunNode.NamespaceURI);

            var unitTestNodes = testRunNode.SelectNodes($"trx:TestDefinitions/trx:UnitTest", xmlNamespaceManager).
                Cast<System.Xml.XmlNode>().
                Select(node => new { node, name = node.Attributes["name"]?.Value, id = node.Attributes["id"]?.Value }).
                ToDictionary(tuple => tuple.id, tuple => tuple);

            var results = testRunNode.SelectNodes("trx:Results/trx:UnitTestResult", xmlNamespaceManager).
                Cast<System.Xml.XmlNode>().
                Where(node =>
                {
                    return StringComparer.InvariantCultureIgnoreCase.Compare("NotExecuted", node.Attributes["outcome"]?.Value) != 0;
                }).
                Select(node =>
                {
                    var testID = node.Attributes["testId"]?.Value;
                    unitTestNodes.TryGetValue(testID, out var unitTestDetails);

                    var status = BorsukSoftware.Conical.Client.TestRunStatus.Failed;
                    var outcomeAttrStr = node.Attributes["outcome"]?.Value;
                    if (StringComparer.InvariantCultureIgnoreCase.Compare("Passed", outcomeAttrStr) == 0)
                        status = BorsukSoftware.Conical.Client.TestRunStatus.Passed;

                    IReadOnlyCollection<string> logsOutput = Array.Empty<string>();
                    var stdOutNode = node.SelectSingleNode("trx:Output/trx:StdOut", xmlNamespaceManager);
                    if (stdOutNode != null)
                    {
                        logsOutput = stdOutNode.InnerText.Split('\n').Select((s) =>
                        {
                            var sb = new System.Text.StringBuilder(s.Length);
                            foreach (var @char in s)
                                if (@char != '\r')
                                    sb.Append(@char);

                            return sb.ToString();
                        }).ToArray();
                    }

                    return new
                    {
                        node,
                        testName = node.Attributes["testName"]?.Value,
                        testID,
                        unitTestDetails,
                        result = status,
                        logs = logsOutput
                    };
                }).
                OrderBy(tuple => tuple.testName);

            var client = new BorsukSoftware.Conical.Client.REST.AccessLayer(server, accessToken: token);
            BorsukSoftware.Conical.Client.IProduct product;
            try
            {
                product = await client.GetProduct(productName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception caught sourcing product info: {ex}");
                return 1;
            }

            BorsukSoftware.Conical.Client.ITestRunSet trs;
            try
            {
                trs = await product.CreateTestRunSet(
                    testRunSetName,
                    testRunSetDescription,
                    refDate,
                    tags);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception caught creating TRS: {ex}");
                return 1;
            }

            Console.WriteLine($"TRS created (#{trs.ID})");

            Console.WriteLine("Uploading tests");
            int uploadCount = 0;
            foreach (var tuple in results)
            {
                if (++uploadCount % 10 == 0)
                    Console.Write(".");

                var sb = new System.Text.StringBuilder(tuple.testName.Length + 1);
                bool hasSeenBracket = false;
                for (int i = 0; i < tuple.testName.Length; ++i)
                {
                    var charToAdd = tuple.testName[i];
                    switch (charToAdd)
                    {
                        case '(':
                            sb.Append('\\');
                            hasSeenBracket = true;
                            break;

                        case '\\':
                            sb.Append("&#92;");
                            continue;

                        case '.' when !hasSeenBracket:
                            charToAdd = '\\';
                            break;
                    }

                    sb.Append(charToAdd);
                }
                var adjustedName = sb.ToString();

                var tr = await trs.CreateTestRun(adjustedName, "Description", testRunType, tuple.result);

                var xml = $"<test>{tuple.node.OuterXml}{tuple.unitTestDetails?.node?.OuterXml}</test>";
                await tr.PublishTestRunResultsXml(xml);

                if (tuple.logs?.Count > 0)
                {
                    await tr.PublishTestRunLogMessages(tuple.logs);
                }
            }
            Console.WriteLine();

            Console.WriteLine("Uploading additional files");

            using (var trxFileStream = new System.IO.FileStream(fullSourcePath, FileMode.Open, FileAccess.Read))
                await trs.PublishAdditionalFile(System.IO.Path.GetFileName(fullSourcePath), "Source TRX file", trxFileStream);

            await trs.SetStatus(BorsukSoftware.Conical.Client.TestRunSetStatus.Standard);
            Console.WriteLine($"Upload complete");

            return 0;
        }
    }
}