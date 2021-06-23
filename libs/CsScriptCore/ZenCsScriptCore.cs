/*************************************************************************
 * Copyright (c) 2020, 2021 Algonia
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <http://www.gnu.org/licenses/>.
 *
 * Contributors:
 *    Tomaž Vinko
 *   
 **************************************************************************/

using System;
using ZenCommon;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Linq;
using Microsoft.CodeAnalysis.Emit;
using System.Text;

public class ZenCsScriptCore
{
    #region Public 
    #region GetCompiledText
    public static string GetCompiledText(string text, ZenCsScriptData scriptData)
    {
        if (text.IndexOf("result") == -1 && text.IndexOf("last_executed_date") == -1 && text.IndexOf("error_code") == -1 && text.IndexOf("error_message") == -1 && text.IndexOf("status") == -1 && text.IndexOf("started") == -1 && text.IndexOf("ms_elapsed") == -1 && text.IndexOf("code") == -1)
            return ZenCsScriptCore.Decode(text);

        string outerHtml = scriptData.ScriptDoc.DocumentNode.OuterHtml;
        outerHtml = ReplaceNodeText(outerHtml, scriptData, "code");
        outerHtml = ReplaceNodeText(outerHtml, scriptData, "result");
        outerHtml = ReplaceNodeText(outerHtml, scriptData, "status");
        outerHtml = ReplaceNodeText(outerHtml, scriptData, "ms_elapsed");
        outerHtml = ReplaceNodeText(outerHtml, scriptData, "last_executed_date");
        outerHtml = ReplaceNodeText(outerHtml, scriptData, "error_message");
        outerHtml = ReplaceNodeText(outerHtml, scriptData, "error_code");
        outerHtml = ReplaceNodeText(outerHtml, scriptData, "started");
        return ZenCsScriptCore.Decode(outerHtml);
    }
    #endregion

    #region Initialize
    public static ZenCsScriptData Initialize(string rawScript, Hashtable elements, IElement element, string fileName, IGadgeteerBoard ParentBoard, bool debug)
    {
        return Initialize(rawScript, elements, element, 0, fileName, null, ParentBoard, debug);
    }

    public static ZenCsScriptData Initialize(string rawScript, Hashtable elements, IElement element, string fileName, IZenCallable callable, IGadgeteerBoard ParentBoard, bool debug)
    {
        return Initialize(rawScript, elements, element, 0, fileName, callable, ParentBoard, debug);
    }

    public static ZenCsScriptData Initialize(string rawScript, Hashtable elements, IElement element, int Order, string fileName, IZenCallable callable, IGadgeteerBoard ParentBoard, bool debug)
    {
        fileName = Path.Combine(Environment.CurrentDirectory, fileName);
        //Make zeno tags (result, code...) recognizable by Html doc parser
        HtmlDocument doc = null;
        if (!File.Exists(fileName))
        {
            doc = new HtmlDocument();
            doc.LoadHtml((RemoveComments(Decode(rawScript))));
            HtmlNode headerNode = doc.DocumentNode.Element("header");
            List<string> deaultReferences = new List<string>();

            // When called from template, references are in Implementation folder.
            // When called from helpers apps (eg zencs), references are in app folder
            string currFolder = string.IsNullOrEmpty(ParentBoard.TemplateRootDirectory) ?
                Environment.CurrentDirectory : Path.Combine(ParentBoard.TemplateRootDirectory, "Implementations");

            deaultReferences.Add("System.Linq.Expressions");
            deaultReferences.Add("Microsoft.CSharp");
            deaultReferences.Add("System.Linq");
            deaultReferences.Add("System.Web.HttpUtility");
            deaultReferences.Add("Newtonsoft.Json");
            deaultReferences.Add(Path.Combine(currFolder, "Algonia.Cs.Common.dll"));
            deaultReferences.Add(Path.Combine(currFolder, "Algonia.Cs.ScriptCore.dll"));

            string[] references = GetReferences(headerNode, deaultReferences, ParentBoard).ToArray();

            string usings = string.Empty;
            if (headerNode != null)
            {
                usings = GetUsings(headerNode.InnerText);
                doc.DocumentNode.RemoveChild(headerNode);
            }

            //Replace result tags with element castings based on their current values and return code as string
            string generatedCode = GenerateCode(elements, element, Order, doc, ParentBoard, debug, usings);

            string coreClrReferences = string.Empty;
            foreach (string s in references)
                coreClrReferences += s + ",";

            string errors = CompileAndSaveAssembly(generatedCode, coreClrReferences, fileName, debug);

            if (!string.IsNullOrEmpty(errors))
            {
                element.LastResult = errors;
                Console.WriteLine(element.LastResult);
                ParentBoard.PublishInfoPrint(element.ID, element.LastResult, "error");
                return null;
            }
        }
        Assembly _compiledScript = Assembly.Load(File.ReadAllBytes(fileName), debug ? File.ReadAllBytes(fileName.Replace(".zen", ".pdb")) : null);
        IZenCsScript script = GetScript(_compiledScript);

        script.Init(elements, element);
        //Load cached raw html
        if (doc == null)
        {
            doc = new HtmlDocument();
            //restore newlines back. They were masked because of property syntax error. For example:
            //string RawHtml
            //{
            //    get {
            //           return 
            //           "test
            //           string";
            //         }
            //}
            doc.LoadHtml(Decode(script.RawHtml).Replace("&n;", "\n").Replace("&rn;", "\r\n"));
        }
        return new ZenCsScriptData(script, doc);
    }
    #endregion

    #region GetFunction
    public static string GetFunction(string body)
    {
        return GetFunction(body, string.Empty);
    }
    public static string GetFunction(string body, string functionName)
    {
        string references = string.Empty;
        string code = string.Empty;

        // Check if function already contains return statement.
        //
        // Example of script body without return statement is simple condition statement:
        // <result>ElementId</result> > 1 && <result>ElementId</result> < 100
        // In this case wrap condition inside return statement.
        //          ------------------------------------------------------------------
        // |return| <result>ElementId</result> > 1 && <result>ElementId</result> < 100 |;|
        //          ------------------------------------------------------------------
        //
        // User can also write complex logic that already contains return statement.
        // Leave script body as it is. 
        body = string.Concat(Regex.Match(body, "\\s*return\\s*").Success ?
                string.Empty : "return ", body, ";");

        // Split body in two parts, so that can be correctly  concated afterwards
        SplitReferencesAndCode(body, out references, out code);
        return string.Concat(references, "<code run=\"true\" name=\"" + functionName + "\" type=\"function\">", code, "</code>");
    }
    #endregion

    #region GetProcedure
    public static string GetProcedure(string body)
    {
        string references = string.Empty;
        string code = string.Empty;

        // Split body in two parts, so that can be correctly  concated afterwards
        SplitReferencesAndCode(body, out references, out code);
        return string.Concat(references, "<code run=\"true\" type=\"procedure\">", code, "</code>");
    }
    #endregion

    #region Decode
    public static string Decode(string original)
    {
        return original.Replace("&quot;", "\"").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&#92;", "\\").Replace("&period;", ".").Replace("&apos;", "'").Replace("&comma;", ",").Replace("&amp;", "&");
    }
    #endregion

    #region Encode
    public static string Encode(string original)
    {
        return original.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\\", "&#92;").Replace(".", "&period;").Replace("'", "&apos;").Replace(",", "&comma;");
    }
    #endregion
    #endregion

    #region Private
    static string GetFunctionName(HtmlNode node)
    {
        return node.Attributes.Contains("name") && !string.IsNullOrEmpty(node.Attributes["name"].Value)
                    ? node.Attributes["name"].Value : "Funct_" + Guid.NewGuid().ToString("N");
    }
    #region SplitReferencesAndCode
    // Splits references and code in two parts.
    // For example, this code would be splitted in two parts:
    // <header>
    // reference System.IO;
    // using System.IO;
    // </header>
    // 
    // return System.IO.File.ReadAllText("myFile.txt");
    //
    // references variable would contain header tags and code variable would contain return statement
    static void SplitReferencesAndCode(string body, out string references, out string code)
    {
        references = string.Empty;
        code = string.Empty;
        body = Decode(body);

        MatchCollection match = Regex.Matches(body, @"<(\s*)header(.*?)(\s*)>[^<]*<(\s*)/(\s*)header(\s*)>");
        if (match.Count > 0)
        {
            references = match[0].Value;
            code = body.Replace(references, string.Empty);
        }
        else
            code = body;
    }
    #endregion

    #region ReplaceNodeText
    static string ReplaceNodeText(string outerHtml, ZenCsScriptData scriptData, string nodeName)
    {
        foreach (HtmlNode node in scriptData.ScriptDoc.DocumentNode.Descendants(nodeName))
        {
            if (node.Attributes["id"] != null)
                outerHtml = outerHtml.Replace(node.OuterHtml, scriptData.ZenCsScript.RunCustomCode(node.Attributes["id"].Value).ToString());
        }
        return outerHtml;
    }
    #endregion

    #region AddFunction
    static void AddFunction(HtmlNode node, string script, Dictionary<string, string> functions, IGadgeteerBoard ParentBoard, IElement element, bool debug)
    {
        if (node.ParentNode.Name != "code")
        {
            node.Attributes.Add("id", GetFunctionName(node));
            string tmp = node.OuterHtml.Replace(node.OuterHtml, script);
            functions.Add(node.Attributes["id"].Value, "return " + tmp + ";");
            if (debug)
                ParentBoard.PublishInfoPrint(element.ID, tmp, "info");
        }
    }
    #endregion

    #region GenerateCode
    /*
    1)
            <code run="true" type="procedure">
                var i = 0;
                Test(i);
            </code>
            Converted to:
            object Funct_123(object a)
            {
                var i = 0;
                Test(i);
                return null;
            }
          
    2)
            <code run="true" type="function">
                var i = 0;
                Test(i);
                return "abc";
            </code>

            Converted to:
            object Funct_123(object a)
            {
                var i = 0;
                Test(i);
                return "abc";
            }
          
    3)
            <code>
                string CastString(int i)
                {
                    return i.ToString();
                }
                string CastDouble(int i)
                {
                    return Convert.ToDouble (i);
                }
            </code>

            Converter to:
            string CastString(int i)
            {
                return i.ToString();
            }
        
            string CastDouble(int i)
            {
                return Convert.ToDouble (i);
            }

         */
    static string GenerateCode(Hashtable elements, IElement element, int Order, HtmlDocument doc, IGadgeteerBoard ParentBoard, bool debug, string usings)
    {
        string debugFileName = Path.Combine(Environment.CurrentDirectory, "tmp", element.ID + ".cs");

        Dictionary<string, string> functions = new Dictionary<string, string>();
        string userFunctions = string.Empty;

        //*********************  BEGIN INSIDE CODE TAGS LOGIC******************************************/
        foreach (HtmlNode node in doc.DocumentNode.Descendants("code"))
        {
            if (string.IsNullOrEmpty(node.InnerText))
                continue;

            string nodeContent = node.InnerHtml;
            foreach (HtmlNode tagNode in node.Descendants("result"))
                nodeContent = ReplaceResultTagWithCast(tagNode, elements, nodeContent, false);

            foreach (HtmlNode tagNode in node.Descendants("error_code"))
                nodeContent = nodeContent.Replace(tagNode.OuterHtml, "((IElement)_elements[\"" + tagNode.InnerText + "\"]).ErrorCode");

            foreach (HtmlNode tagNode in node.Descendants("error_message"))
                nodeContent = nodeContent.Replace(tagNode.OuterHtml, "((IElement)_elements[\"" + tagNode.InnerText + "\"]).ErrorMessage");

            foreach (HtmlNode tagNode in node.Descendants("status"))
                nodeContent = nodeContent.Replace(tagNode.OuterHtml, "(((IElement)_elements[\"" + tagNode.InnerText + "\"]).Status).ToString()");

            foreach (HtmlNode tagNode in node.Descendants("started"))
                nodeContent = nodeContent.Replace(tagNode.OuterHtml, "((IElement)_elements[\"" + tagNode.InnerText + "\"]).Started");

            foreach (HtmlNode tagNode in node.Descendants("ms_elapsed"))
                nodeContent = nodeContent.Replace(tagNode.OuterHtml, "((IElement)_elements[\"" + tagNode.InnerText + "\"]).MsElapsed");

            foreach (HtmlNode tagNode in node.Descendants("last_executed_date"))
                nodeContent = nodeContent.Replace(tagNode.OuterHtml, "((IElement)_elements[\"" + tagNode.InnerText + "\"]).LastExecutionDate");

            if (node.Attributes.Contains("run") && node.Attributes["run"].Value == "true")
            {
                node.Attributes.Add("id", GetFunctionName(node));

                StringBuilder debugger = new StringBuilder();
                if (debug)
                {
                    debugger.AppendLine("#line 3 \"" + debugFileName + "\"");
                    debugger.AppendLine("if (!System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Launch();System.Diagnostics.Debugger.Break();");
                }
                nodeContent = debugger.ToString() + nodeContent;
                functions.Add(node.Attributes["id"].Value, Decode(!node.Attributes.Contains("type") || node.Attributes["type"].Value == "procedure" ? string.Concat(nodeContent, ";", Environment.NewLine, " return null;") : nodeContent));
            }
            else
                userFunctions += Environment.NewLine + Decode(nodeContent);
        }
        //**********************************************************************************************/

        //*********************  BEGIN OUTSIDE CODE TAGS LOGIC******************************************/
        /*
        Example of script input:
        "SELECT * FROM t_Table WHERE Col = '<result>MyElementID</result>'"
        Example of output:
        return "SELECT * FROM t_Table WHERE Col = Cast.ToString("ElementID")";
        */

        foreach (HtmlNode node in doc.DocumentNode.Descendants("result"))
        {
            if (node.ParentNode.Name != "code")
            {
                string fName = GetFunctionName(node);
                node.Attributes.Add("id", fName);

                string tmp = ReplaceResultTagWithCast(node, elements, node.OuterHtml, false);
                functions.Add(node.Attributes["id"].Value, "return " + Decode(tmp) + ";");
                if (debug)
                    ParentBoard.PublishInfoPrint(element.ID, Decode(tmp), "info");
            }
        }

        foreach (HtmlNode node in doc.DocumentNode.Descendants("status"))
            AddFunction(node, "(((IElement)_elements[\"" + node.InnerHtml + "\"]).Status).ToString()", functions, ParentBoard, element, debug);

        foreach (HtmlNode node in doc.DocumentNode.Descendants("ms_elapsed"))
            AddFunction(node, "((IElement)_elements[\"" + node.InnerHtml + "\"]).MsElapsed", functions, ParentBoard, element, debug);

        foreach (HtmlNode node in doc.DocumentNode.Descendants("last_executed_date"))
            AddFunction(node, "((IElement)_elements[\"" + node.InnerHtml + "\"]).LastExecutionDate", functions, ParentBoard, element, debug);

        foreach (HtmlNode node in doc.DocumentNode.Descendants("error_code"))
            AddFunction(node, "((IElement)_elements[\"" + node.InnerHtml + "\"]).ErrorCode", functions, ParentBoard, element, debug);

        foreach (HtmlNode node in doc.DocumentNode.Descendants("error_message"))
            AddFunction(node, "((IElement)_elements[\"" + node.InnerHtml + "\"]).ErrorMessage", functions, ParentBoard, element, debug);

        foreach (HtmlNode node in doc.DocumentNode.Descendants("started"))
            AddFunction(node, "((IElement)_elements[\"" + node.InnerHtml + "\"]).Started", functions, ParentBoard, element, debug);
        //**********************************************************************************************/

        string sFunctions = string.Empty;
        foreach (KeyValuePair<string, string> function in functions)
        {
            sFunctions += string.Concat("object ", function.Key, "() {", Environment.NewLine + function.Value + Environment.NewLine, "}");
        }

        StringBuilder assemblyResolveCode = new StringBuilder();

        assemblyResolveCode.AppendLine("AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs e) ");
        assemblyResolveCode.AppendLine("{");
        assemblyResolveCode.AppendLine("   string assName = e.Name;");
        assemblyResolveCode.AppendLine("   if (assName.IndexOf(\".resources\") > -1) ");
        assemblyResolveCode.AppendLine("       assName = assName.Replace(\".resources\", string.Empty); ");
        assemblyResolveCode.AppendLine("   foreach(var dir in System.IO.Directory.GetDirectories(System.IO.Path.Combine(Environment.CurrentDirectory,\"libs\"),\"*\",System.IO.SearchOption.AllDirectories))");
        assemblyResolveCode.AppendLine("   {");
        assemblyResolveCode.AppendLine("       System.Collections.Generic.List<string> dirs = dir.Split(System.IO.Path.DirectorySeparatorChar).ToList<string>(); ");
        assemblyResolveCode.AppendLine("       if (!dirs.Contains (\"NETCoreRuntime\")  && !dirs.Contains (\"Implementations\")  )");
        assemblyResolveCode.AppendLine("       {");
        assemblyResolveCode.AppendLine("           foreach(var file in System.IO.Directory.GetFiles(dir, \"*.dll\", System.IO.SearchOption.AllDirectories))");
        assemblyResolveCode.AppendLine("           {");
        assemblyResolveCode.AppendLine("               try");
        assemblyResolveCode.AppendLine("               {");
        assemblyResolveCode.AppendLine("                   if(System.Reflection.AssemblyName.GetAssemblyName(file).ToString() == assName)");
        assemblyResolveCode.AppendLine("                       return System.Reflection.Assembly.LoadFile(file);");
        assemblyResolveCode.AppendLine("               } ");
        assemblyResolveCode.AppendLine("               catch {}");
        assemblyResolveCode.AppendLine("           }");
        assemblyResolveCode.AppendLine("       }");
        assemblyResolveCode.AppendLine("   }");
        assemblyResolveCode.AppendLine("   Console.WriteLine(\"Assembly not found \" + e.Name);");
        assemblyResolveCode.AppendLine("   return null;");
        assemblyResolveCode.AppendLine("};");

        string customCodeWrapper = "public object RunCustomCode(string functionName){ " + Environment.NewLine +
            assemblyResolveCode.ToString() +
        "try{";
        foreach (KeyValuePair<string, string> s in functions)
            customCodeWrapper += "if (functionName == \"" + s.Key + "\") return " + s.Key + "();";

        customCodeWrapper += "}catch(Exception ex){Console.WriteLine(ex.Message);} return null;}";

        if (debug)
        {
            if (!Directory.Exists("tmp"))
                Directory.CreateDirectory("tmp");

            File.WriteAllText(debugFileName, string.Concat(sFunctions, userFunctions));
        }
        //For cache html string replace lt and gt back, so that next time will MakeZenoTagsValid parse correctly
        return GetCode(Order, string.Concat(sFunctions, userFunctions), customCodeWrapper, element, doc.DocumentNode.OuterHtml.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r\n", "&rn;").Replace("\n", "&n;"), usings);
    }
    #endregion

    #region GetCode
    static string GetCode(int Order, string functions, string runCustomCodeWrapper, IElement element, string htmlDoc, string usings)
    {
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("using System;");
        sb.AppendLine("using ZenCommon;");
        sb.AppendLine("using System.Collections;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using Newtonsoft.Json;");
        sb.AppendLine("using System.Web;");

        sb.AppendLine(usings);

        sb.AppendLine("namespace ZenCsScript");
        sb.AppendLine("{");
        sb.AppendLine("public class CsScript : IZenCsScript");
        sb.AppendLine("{");
        sb.AppendLine("IElement _element; Hashtable _elements;");
        sb.AppendLine(functions);

        sb.AppendLine((string.IsNullOrEmpty(runCustomCodeWrapper) ? "public object RunCustomCode(string functionName){ return null; }" : runCustomCodeWrapper));


        sb.AppendLine("public string RawHtml");
        sb.AppendLine("{");
        sb.AppendLine("get { return " + (string.IsNullOrEmpty(htmlDoc) ? "string.Empty" : "\"" + htmlDoc + "\"") + ";}");
        sb.AppendLine("}");

        sb.AppendLine("public int Order { get { return " + Order + "; } }");

        sb.AppendLine("public void Init(Hashtable elements, IElement element)");
        sb.AppendLine("{");
        sb.AppendLine("_element = element; _elements = elements;");
        sb.AppendLine("}");

        sb.AppendLine("public string get_arg(string argName)");
        sb.AppendLine("{ ");
        sb.AppendLine("     var arg = get_args().Find(arg => arg.name == argName);");
        sb.AppendLine("     if (arg == null || arg.value == null) return string.Empty;");
        sb.AppendLine("     return arg.value.Replace(\"&period;\", \".\").Replace(\"&amp;\", \"&\").Trim();");
        sb.AppendLine("}");

        sb.AppendLine("public List<PropertyPair> get_args()");
        sb.AppendLine("{");
        sb.AppendLine("return JsonConvert.DeserializeObject<List<PropertyPair>>(HttpUtility.HtmlDecode(get_property(\"SCRIPT_ARGS\")).Replace(\"&comma;\", \",\"));");
        sb.AppendLine("}");

        sb.AppendLine("void exec(string elementId)");
        sb.AppendLine("{");
        sb.AppendLine("((IElement)_elements[elementId]).IAmStartedYou = _element;");
        sb.AppendLine("((IElement)_elements[elementId]).StartElement(_elements, false);");
        sb.AppendLine("}");

        sb.AppendLine("void set_result(string elementId, object result)");
        sb.AppendLine("{");
        sb.AppendLine("((IElement)_elements[elementId]).LastResultBoxed = result;");
        sb.AppendLine("}");

        sb.AppendLine("void set_result(object result)");
        sb.AppendLine("{");
        sb.AppendLine("_element.LastResultBoxed = result;");
        sb.AppendLine("}");

        sb.AppendLine("void set_result_from_element(string elementId, string elementIdResult)");
        sb.AppendLine("{");
        sb.AppendLine("((IElement)_elements[elementId]).LastResultBoxed = get_result_raw(elementIdResult);");
        sb.AppendLine("}");

        sb.AppendLine("object get_result_raw(string elementId)");
        sb.AppendLine("{");
        sb.AppendLine("return ((IElement)_elements[elementId]).LastResultBoxed;");
        sb.AppendLine("}");

        sb.AppendLine("object get_result_raw()");
        sb.AppendLine("{");
        sb.AppendLine("return _element.LastResultBoxed;");
        sb.AppendLine("}");

        sb.AppendLine("string get_result(string elementId)");
        sb.AppendLine("{");
        sb.AppendLine("return ((IElement)_elements[elementId]).LastResult;");
        sb.AppendLine("}");

        sb.AppendLine("void set_element_arg(string elementId, string value)");
        sb.AppendLine("{");
        sb.AppendLine("((_elements as Hashtable)[elementId] as IElement).SetElementProperty(\"__ARG__\", value);");
        sb.AppendLine("}");

        sb.AppendLine("void set_element_arg(string value)");
        sb.AppendLine("{");
        sb.AppendLine("_element.SetElementProperty(\"__ARG__\", value);");
        sb.AppendLine("}");

        sb.AppendLine("string get_element_arg()");
        sb.AppendLine("{");
        sb.AppendLine("return _element.GetElementProperty(\"__ARG__\");");
        sb.AppendLine("}");

        sb.AppendLine("string get_element_arg(string elementId)");
        sb.AppendLine("{");
        sb.AppendLine("return ((_elements as Hashtable)[elementId] as IElement).GetElementProperty(\"__ARG__\");");
        sb.AppendLine("}");

        sb.AppendLine("void set_element_property(string elementId, string key, string value)");
        sb.AppendLine("{");
        sb.AppendLine("((_elements as Hashtable)[elementId] as IElement).SetElementProperty(key, value);");
        sb.AppendLine("}");

        sb.AppendLine("string get_element_property(string elementId, string key)");
        sb.AppendLine("{");
        sb.AppendLine("return ((_elements as Hashtable)[elementId] as IElement).GetElementProperty(key);");
        sb.AppendLine("}");

        sb.AppendLine("string get_property(string key)");
        sb.AppendLine("{");
        sb.AppendLine("return _element.GetElementProperty(key);");
        sb.AppendLine("}");
        sb.AppendLine("void set_condition(bool condition)");
        sb.AppendLine("{");
        sb.AppendLine("((_elements as Hashtable)[\"" + (element != null ? element.ID : string.Empty) + "\"] as IElement).IsConditionMet = condition;");
        sb.AppendLine("}");

        sb.AppendLine("void set_condition(string elementId, bool condition)");
        sb.AppendLine("{");
        sb.AppendLine("((_elements as Hashtable)[elementId] as IElement).IsConditionMet = condition;");
        sb.AppendLine("}");

        sb.AppendLine("}");
        sb.AppendLine("public class PropertyPair{ public string name { get; set; } public string value { get; set; }}");
        sb.AppendLine("}");

        return sb.ToString();
    }
    #endregion

    #region CompileAndSaveAssembly
    static string CompileAndSaveAssembly(string code, string references, string fileName, bool debug)
    {
        try
        {
            List<MetadataReference> coreReferencesPaths = new List<MetadataReference>();
            coreReferencesPaths.Add(MetadataReference.CreateFromFile(typeof(object).Assembly.Location));

            foreach (string s in Regex.Split(references, ","))
            {
                if (!string.IsNullOrEmpty(s))
                {
                    if (debug)
                        Console.WriteLine("Adding " + s + "...");

                    if (File.Exists(s.Trim()))
                        coreReferencesPaths.Add(MetadataReference.CreateFromFile(s.Trim()));
                    else
                    {
                        AssemblyName assName = null;
                        try
                        {
                            assName = new AssemblyName(s.Trim());
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("******************************************");
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("Problem loading algo " + s.Trim() + "...");
                            Console.WriteLine("Check if algo exists...");
                            Console.WriteLine("Error details : " + ex.Message);
                            Console.WriteLine("******************************************");
                            Console.ReadLine();
                        }
                        coreReferencesPaths.Add(MetadataReference.CreateFromFile(Assembly.Load(assName).Location));
                    }
                }
            }

            var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

            options = options.WithOptimizationLevel(debug ? OptimizationLevel.Debug : OptimizationLevel.Release);
            options = options.WithPlatform(Platform.AnyCpu);

            // Generic types must be enclosed in ``´´ instead of <>
            // The problem is that doc.LoadHtml automatically close unclosed tags.
            // For example: List<String> a = getstring(); 
            // will become List<string> a = getstring();</string>
            var tree = CSharpSyntaxTree.ParseText(code.Replace("``", "<").Replace("´´", ">"));

            var compilation = CSharpCompilation.Create("zenCompile", syntaxTrees: new[] { tree }, references: coreReferencesPaths.ToArray(), options: options);
            string errors = string.Empty;

            if (!Directory.Exists(Path.GetDirectoryName(fileName)))
                Directory.CreateDirectory(Path.GetDirectoryName(fileName));

            var emitResult = emitAndSaveAssemblies(compilation, fileName, debug);

            if (!emitResult.Success)
            {
                foreach (var diagnostic in emitResult.Diagnostics)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                        errors += diagnostic.GetMessage();

                    Console.WriteLine(diagnostic.GetMessage());
                }
                File.WriteAllText(
                    Path.Combine(Path.GetDirectoryName(fileName), "err_" + Path.GetFileNameWithoutExtension(fileName) + ".cs"),
                    code);
            }

            return errors;
        }
        catch (Exception ex)
        {
            if (ex.InnerException != null && !string.IsNullOrEmpty(ex.InnerException.Message))
                Console.WriteLine(ex.InnerException.Message);
            else
                Console.WriteLine(ex.Message);
            Console.ReadLine();
            return null;
        }
    }
    #endregion

    static EmitResult emitAndSaveAssemblies(CSharpCompilation compilation, string fileName, bool debug)
    {
        EmitResult emitResult;

        var emitOptions = new EmitOptions(false,
        debugInformationFormat: DebugInformationFormat.PortablePdb);

        using (var dllStream = new MemoryStream())
        using (var pdbStream = new MemoryStream())
        {
            emitResult = debug ? compilation.Emit(dllStream, pdbStream, options: emitOptions) : compilation.Emit(dllStream);

            if (!emitResult.Success)
                return emitResult;

            dllStream.Seek(0, SeekOrigin.Begin);
            using (FileStream fs = new FileStream(fileName, FileMode.OpenOrCreate))
            {
                dllStream.CopyTo(fs);
                fs.Flush();
            }

            if (debug)
            {
                pdbStream.Seek(0, SeekOrigin.Begin);
                using (FileStream fs = new FileStream(fileName.Replace(".zen", ".pdb"), FileMode.OpenOrCreate))
                {
                    pdbStream.CopyTo(fs);
                    fs.Flush();
                }
            }
        }
        return emitResult;
    }

    #region IsPrimitiveType
    static bool IsPrimitiveType(string sType)
    {
        switch (sType.ToLower())
        {
            case "bool":
            case "ushort":
            case "uint":
            case "string":
            case "short":
            case "sbyte":
            case "int":
            case "float":
            case "double":
            case "byte":
            case "uint16":
            case "int32":
            case "int64":
            case "byte[]":
            case "uint16[]":
            case "uint32[]":
            case "int16[]":
            case "int32[]":
            case "double[]":
            case "single[]":
                return true;
        }
        return false;
    }
    #endregion

    #region GetReplacedComplexTypeScript
    static string GetReplacedComplexTypeScript(HtmlNode node, string scriptText, string sType, string elementId, Hashtable elements)
    {
        switch (sType.ToLower())
        {
            case "jobject":
                string jObjectPostfix = (node.InnerHtml.IndexOf('[') > -1 ? node.InnerHtml.Substring(node.InnerHtml.IndexOf('[')) : string.Empty);
                string currentJObjectTag = string.Concat("((((IElement)_elements[\"" + elementId + "\"]).LastResultBoxed) as JObject)" + jObjectPostfix);

                if (node.Attributes["cast"] == null)
                    scriptText = scriptText.Replace(node.OuterHtml, currentJObjectTag);
                else
                    scriptText = scriptText.Replace(node.OuterHtml, string.Concat(GetPrimitiveTypeCastString(node.Attributes["cast"].Value), currentJObjectTag, ")"));
                break;

            case "dataset":
                string currentDatasetTag = "((DataSet)((IElement)_elements[\"" + elementId + "\"]).LastResultBoxed)" + (node.InnerHtml.Replace("&period;", ".").IndexOf('.') > -1 ? node.InnerHtml.Substring(node.InnerHtml.Replace("&period;", ".").IndexOf('.')) : string.Empty);

                if (node.Attributes["cast"] == null)
                {
                    //If row cast string returns null then just DataSet is placed between result tags: <result>MyDataSet</result>. Mostly within <code>
                    string rowCastString = GetRowCastFromDataSet(elementId, currentDatasetTag, elements);
                    scriptText = scriptText.Replace(node.OuterHtml, string.IsNullOrEmpty(rowCastString) ? currentDatasetTag : string.Concat(rowCastString, currentDatasetTag, ")"));
                }
                else
                    scriptText = scriptText.Replace(node.OuterHtml, string.Concat(GetPrimitiveTypeCastString(node.Attributes["cast"].Value), currentDatasetTag, ")"));
                break;

            case "datatable":
                string currentDatatableTag = "((DataTable)((IElement)_elements[\"" + elementId + "\"]).LastResultBoxed)" + (node.InnerHtml.Replace("&period;", ".").IndexOf('.') > -1 ? node.InnerHtml.Substring(node.InnerHtml.Replace("&period;", ".").IndexOf('.')) : string.Empty);
                if (node.Attributes["cast"] == null)
                {
                    //If row cast string returns null then just DataTable is placed between result tags: <result>MyDataTable</result>. Mostly within <code>
                    string rowCastString = GetRowCastFromDataTable(elementId, currentDatatableTag, elements);
                    scriptText = scriptText.Replace(node.OuterHtml, string.IsNullOrEmpty(rowCastString) ? currentDatatableTag : string.Concat(rowCastString, currentDatatableTag, ")"));
                }
                else
                    scriptText = scriptText.Replace(node.OuterHtml, string.Concat(GetPrimitiveTypeCastString(node.Attributes["cast"].Value), currentDatatableTag, ")"));
                break;

            case "hashtable":
                string castString = string.Empty;
                //Currently integer and string key types are supported : ht[0] & ht["0"]
                bool isHashKeyStringType = false;
                object hashKey;
                if (node.InnerHtml.Split('[')[1].IndexOf('"') > -1)
                {
                    hashKey = node.InnerHtml.Split('[')[1].Replace("]", string.Empty).Replace("\"", string.Empty).Trim();
                    isHashKeyStringType = true;
                }
                else
                    hashKey = Convert.ToInt32(node.InnerHtml.Split('[')[1].Replace("]", string.Empty).Replace("\"", string.Empty).Trim());

                if (!((Hashtable)(((IElement)elements[elementId]).LastResultBoxed)).ContainsKey(hashKey))
                    throw new Exception("ERROR : Element " + elementId + " does not contains key " + hashKey);

                castString = GetPrimitiveTypeCastString(((Hashtable)(((IElement)elements[elementId]).LastResultBoxed))[hashKey].GetType().Name);
                string formattedHashKey = isHashKeyStringType ? "\"" + hashKey + "\"" : hashKey.ToString();
                scriptText = scriptText.Replace(node.OuterHtml, castString + "((Hashtable)((IElement)_elements[\"" + elementId + "\"]).LastResultBoxed)[" + formattedHashKey + "]" + (!string.IsNullOrEmpty(castString) ? ")" : string.Empty));
                break;
        }
        return scriptText;
    }
    #endregion

    #region GetPrimitiveTypeCastString
    static string GetPrimitiveTypeCastString(string sType)
    {
        switch (sType.ToLower())
        {
            case "bool":
                return "Convert.ToBoolean(";

            case "ushort":
                return "Convert.ToUInt16(";

            case "uint":
            case "uint32":
                return "Convert.ToUInt32(";

            case "string":
                return "Convert.ToString(";

            case "short":
                return "Convert.ToInt16(";

            case "sbyte":
                return "Convert.ToSByte(";

            case "int":
                return "Convert.ToInt32(";

            case "int16":
                return "Convert.ToInt16(";

            case "float":
                return "Convert.ToSingle(";

            case "double":
                return "Convert.ToDouble(";

            case "byte":
                return "Convert.ToByte(";

            case "uint16":
                return "Convert.ToUInt16(";

            case "int32":
                return "Convert.ToInt32(";

            case "int64":
                return "Convert.ToInt64(";

            case "byte[]":
                return "((byte[])";

            case "uint16[]":
                return "((ushort[])";

            case "uint32[]":
                return "((uint[])";

            case "int16[]":
                return "((short[])";

            case "int32[]":
                return "((int[])";

            case "double[]":
                return "((double[])";

            case "single[]":
                return "((float[])";
        }
        return string.Empty;
    }
    #endregion

    #region GetReplacedPrimitiveTypeScript
    static string GetReplacedPrimitiveTypeScript(HtmlNode node, string scriptText, string sType, string elementId)
    {
        return node.Attributes["cast"] == null ? scriptText.Replace(node.OuterHtml, GetPrimitiveTypeCastString(sType) + "((IElement)_elements[\"" + elementId + "\"]).LastResultBoxed)") : scriptText.Replace(node.OuterHtml, "((IElement)_elements[\"" + elementId + "\"]).LastResultBoxed");
    }
    #endregion

    #region ReplaceResultTagWithCast
    /// <summary>
    /// Replaces result tag with actual cast statement for given node and raw string
    /// </summary>
    /// <param name="node">Node that contains result tag</param>
    /// <param name="elements">All system elements</param>
    /// <param name="scriptText">Complete raw script text</param>
    /// <param name="debug">Debug</param>
    /// <returns></returns>
    static string ReplaceResultTagWithCast(HtmlNode node, Hashtable elements, string scriptText, bool debug)
    {
        string elementId = Decode(node.InnerHtml);

        //DataSet etc...
        if (elementId.IndexOf('[') > -1)
            elementId = elementId.Split('[')[0].Trim();

        //Hashtable etc....
        if (elementId.IndexOf('.') > -1)
            elementId = elementId.Split('.')[0].Trim();

        string convertStatement = string.Empty;

        if (!elements.ContainsKey(elementId))
            throw new Exception("ERROR : Element " + elementId + " does not exists!");

        if (((IElement)elements[elementId]).LastResultBoxed == null)
        {
            if (debug && node.Attributes["cast"] == null)
            {
                Console.WriteLine("");
                Console.WriteLine("[" + DateTime.Now.ToShortTimeString() + "] [" + elementId + "] has no result yet. It needs to be casted correctly in template!");
            }
            return node.Attributes["cast"] == null ? scriptText.Replace(node.OuterHtml, "((IElement)_elements[\"" + elementId + "\"]).LastResultBoxed") : scriptText.Replace(node.OuterHtml, GetPrimitiveTypeCastString(node.Attributes["cast"].Value) + "((IElement)_elements[\"" + elementId + "\"]).LastResultBoxed)");
        }

        if (IsPrimitiveType(((IElement)elements[elementId]).LastResultBoxed.GetType().Name))
        {
            if (node.Attributes["cast"] == null)
                scriptText = GetReplacedPrimitiveTypeScript(node, scriptText, ((IElement)elements[elementId]).LastResultBoxed.GetType().Name, elementId);
            else
                scriptText = scriptText.Replace(node.OuterHtml, GetPrimitiveTypeCastString(node.Attributes["cast"].Value) + "((IElement)_elements[\"" + elementId + "\"]).LastResultBoxed)");
        }
        else
            scriptText = GetReplacedComplexTypeScript(node, scriptText, ((IElement)elements[elementId]).LastResultBoxed.GetType().Name, elementId, elements);

        return scriptText;
    }
    #endregion

    #region GetScript
    static IZenCsScript GetScript(Assembly script)
    {
        // Now that we have a compiled script, lets run them
        foreach (Type type in script.GetExportedTypes())
        {
            foreach (Type iface in type.GetInterfaces())
            {
                if (iface == typeof(IZenCsScript))
                {
                    ConstructorInfo constructor = type.GetConstructor(System.Type.EmptyTypes);
                    if (constructor != null && constructor.IsPublic)
                    {
                        IZenCsScript scriptObject = constructor.Invoke(null) as IZenCsScript;
                        if (scriptObject != null)
                            return scriptObject;

                        else
                        { }
                    }
                    else
                    { }
                }
            }
        }
        return null;
    }

    #region GetRowCastFromDataSet
    static string GetRowCastFromDataSet(string elementId, string dataset, Hashtable elements)
    {
        DataSet ds = ((DataSet)((IElement)elements[elementId]).LastResultBoxed);
        dataset = Decode(dataset);
        if (dataset.IndexOf("Tables") > -1)
        {
            string[] splittesDatasetClause = dataset.Substring(dataset.IndexOf("Tables")).Split('.');
            if (splittesDatasetClause.Length > 1 && splittesDatasetClause[0].IndexOf("Tables") > -1 && splittesDatasetClause[1].IndexOf("Rows") > -1)
            {
                string tableName = splittesDatasetClause[0].Substring(splittesDatasetClause[0].IndexOf('[') + 1).Substring(0, splittesDatasetClause[0].Substring(splittesDatasetClause[0].IndexOf('[') + 1).IndexOf(']')).Trim();
                string colName = splittesDatasetClause[1].Substring(splittesDatasetClause[1].LastIndexOf('[') + 1).Substring(0, splittesDatasetClause[1].Substring(splittesDatasetClause[1].LastIndexOf('[') + 1).LastIndexOf(']')).Trim();

                DataTable dt = null;

                if (tableName.Contains("\""))
                    dt = ds.Tables[tableName.Replace("\"", string.Empty)];
                else
                    dt = ds.Tables[Convert.ToInt16(tableName)];

                DataColumn dc = null;

                if (colName.Contains("\""))
                    dc = dt.Columns[colName.Replace("\"", string.Empty)];
                else
                    dc = dt.Columns[Convert.ToInt16(colName)];

                return GetPrimitiveTypeCastString(dc.DataType.Name.ToLower());
            }
        }
        return string.Empty;
    }
    #endregion

    #region GetRowCastFromDataTable
    static string GetRowCastFromDataTable(string elementId, string datatable, Hashtable elements)
    {
        DataTable dt = ((DataTable)((IElement)elements[elementId]).LastResultBoxed);
        datatable = Decode(datatable);
        if (datatable.IndexOf("Rows") > -1 && datatable.IndexOf("[") > -1 && datatable.IndexOf("]") > -1)
        {
            string colName = datatable.Substring(datatable.LastIndexOf('[') + 1).Substring(0, datatable.Substring(datatable.LastIndexOf('[') + 1).LastIndexOf(']')).Trim();
            DataColumn dc = null;

            if (colName.Contains("\""))
                dc = dt.Columns[colName.Replace("\"", string.Empty)];
            else
                dc = dt.Columns[Convert.ToInt16(colName)];

            return GetPrimitiveTypeCastString(dc.DataType.Name.ToLower());
        }
        return string.Empty;
    }
    #endregion
    #endregion

    #region GetReferences
    static List<string> GetReferences(HtmlNode headerNode, List<string> references, IGadgeteerBoard ParentBoard)
    {
        references.Add("System.Console");
        references.Add("System.IO.FileSystem");
        references.Add("System.Collections");
        references.Add("System.Runtime.Extensions");
        references.Add("System.Runtime");
        references.Add("System.Linq");
        references.Add("netstandard");

        if (headerNode != null)
        {
            foreach (string line in headerNode.InnerText.Split(';'))
            {
                if (line.Trim().StartsWith("reference"))
                {
                    references.Add(line.Trim().Replace("reference", string.Empty));
                }
                else if (line.Trim().StartsWith("//"))
                {
                    //Ignore comments in references section
                }
                else
                    break;
            }
        }
        return references;
    }
    #endregion

    #region RemoveComments
    static string RemoveComments(string code)
    {
        var blockComments = @"/\*(.*?)\*/";
        var lineComments = @"//(.*?)\r?\n";
        var strings = @"""((\\[^\n]|[^""\n])*)""";
        var verbatimStrings = @"@(""[^""]*"")+";

        return Regex.Replace(code, blockComments + "|" + lineComments + "|" + strings + "|" + verbatimStrings,
                    me =>
                    {
                        if (me.Value.StartsWith("/*") || me.Value.StartsWith("//"))
                            return me.Value.StartsWith("//") ? Environment.NewLine : "";

                        return me.Value;
                    }, RegexOptions.Singleline).Trim();
    }
    #endregion

    #region GetUsings
    static string GetUsings(string code)
    {
        StringBuilder userUsings = new StringBuilder();
        foreach (string line in code.Split(';'))
        {
            if (line.Trim().StartsWith("using"))
            {
                string[] values = Regex.Split(line, "using");
                if (values.Length == 2)
                {
                    if (values[0].Trim() == string.Empty)
                        userUsings.AppendLine(line.Trim() + ";");
                }
                else
                    break;
            }
            else if (line.Trim().StartsWith("reference") || line.Trim().StartsWith("//"))
            {
                //Ignore comments and references in using section
            }
            else
                break;
        }
        return userUsings.ToString();
    }
    #endregion
    #endregion
}