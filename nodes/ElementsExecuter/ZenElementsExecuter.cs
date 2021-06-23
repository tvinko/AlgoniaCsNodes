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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ZenCommon;

namespace Algonia.Cs.Node.ElementsExecuter
{
    public class ZenElementsExecuter
    {
        #region Fields
        #region DynamicElements
        string DynamicElements;
        #endregion

        #region _scripts
        Dictionary<string, ZenCsScriptData> _scripts = new Dictionary<string, ZenCsScriptData>();
        #endregion

        #region _sync
        object _sync = new object();
        #endregion
        #endregion

        #region Fields
        #region _implementations
        static Dictionary<string, ZenElementsExecuter> _implementations = new Dictionary<string, ZenElementsExecuter>();
        #endregion
        #endregion

        #region Core implementations
        #region GetDynamicElements
        unsafe public static string GetDynamicElements(string currentElementId, void** elements, int elementsCount, int isManaged, string projectRoot, string projectId, ZenNativeHelpers.GetElementProperty getElementPropertyCallback, ZenNativeHelpers.GetElementResultInfo getElementResultInfoCallback, ZenNativeHelpers.GetElementResult getElementResultCallback, ZenNativeHelpers.ExecuteElement execElementCallback, ZenNativeHelpers.SetElementProperty setElementProperty, ZenNativeHelpers.AddEventToBuffer addEventToBuffer)
        {
            ZenNativeHelpers.InitUnmanagedElements(currentElementId, elements, elementsCount, isManaged, projectRoot, projectId, getElementPropertyCallback, getElementResultInfoCallback, getElementResultCallback, execElementCallback, setElementProperty, addEventToBuffer);
            string pluginsToExecute = string.Empty;
            foreach (string outer in (Regex.Split((ZenNativeHelpers.Elements[currentElementId] as IElement).GetElementProperty("ELEMENTS_TO_EXECUTE"), "#101#")))
            {
                if (!string.IsNullOrEmpty(Regex.Split(outer, "#100#")[0].Trim()))
                    pluginsToExecute += Regex.Split(outer, "#100#")[0].Trim() + ",";
            }
            return pluginsToExecute;
        }
        #endregion

        #region InitUnmanagedElements
        unsafe public static void InitUnmanagedElements(string currentElementId, void** elements, int elementsCount, int isManaged, string projectRoot, string projectId, ZenNativeHelpers.GetElementProperty getElementPropertyCallback, ZenNativeHelpers.GetElementResultInfo getElementResultInfoCallback, ZenNativeHelpers.GetElementResult getElementResultCallback, ZenNativeHelpers.ExecuteElement executeElementCallback, ZenNativeHelpers.SetElementProperty setElementProperty, ZenNativeHelpers.AddEventToBuffer addEventToBuffer)
        {
            if (!_implementations.ContainsKey(currentElementId))
                _implementations.Add(currentElementId, new ZenElementsExecuter());
        }
        #endregion

        #region ExecuteAction
        unsafe public static void ExecuteAction(string currentElementId, void** elements, int elementsCount, IntPtr result)
        {
            ZenNativeHelpers.CopyManagedStringToUnmanagedMemory(_implementations[currentElementId].RunConditions(ZenNativeHelpers.Elements, ZenNativeHelpers.Elements[currentElementId] as IElement, ZenNativeHelpers.ParentBoard), result);
        }
        #endregion
        #endregion

        #region Private functions
        #region RunConditions
        string RunConditions(Hashtable elements, IElement element, IGadgeteerBoard ParentBoard)
        {
            lock (_sync)
            {
                string elements2Execute = !string.IsNullOrEmpty(DynamicElements) ? DynamicElements : element.GetElementProperty("ELEMENTS_TO_EXECUTE");
                DynamicElements = string.Empty;
                foreach (string s in Regex.Split(elements2Execute, "#101#"))
                {
                    if (string.IsNullOrEmpty(s))
                        continue;

                    if (!_scripts.ContainsKey(Regex.Split(s, "#100#")[0]))
                    {
                        string id = Regex.Split(s, "#100#")[0];
                        string condition = Regex.Split(s, "#100#")[1];
                        int order = string.IsNullOrEmpty(Regex.Split(s, "#100#")[2]) ? 0 : Convert.ToInt32(Regex.Split(s, "#100#")[2]);
                        condition = string.IsNullOrEmpty(condition) ? "true" : condition;
                        //TO DO : Don't compile for true statements.....
                        _scripts.Add(id, ZenCsScriptCore.Initialize(ZenCsScriptCore.GetFunction(condition), elements, element, order, Path.Combine("tmp", "ElementsExecuter", element.ID + "_" + id + ".zen"), null, ParentBoard, element.GetElementProperty("PRINT_CODE") == "1"));
                    }
                }

                int iCurrentOrder = 0;
                bool atLeastOneConditionTrue = false;

                string sPluginsToStart = string.Empty;
                if (element.GetElementProperty("DO_DEBUG") == "1")
                {
                    Console.WriteLine("");
                    Console.WriteLine("********" + element.ID + "*******");
                }

                bool error = false;
                foreach (KeyValuePair<string, ZenCsScriptData> item in _scripts.OrderBy(key => key.Value.ZenCsScript.Order))
                {
                    if (iCurrentOrder != item.Value.ZenCsScript.Order)
                    {
                        if (atLeastOneConditionTrue)
                            break;

                        atLeastOneConditionTrue = false;
                        iCurrentOrder = item.Value.ZenCsScript.Order;
                    }
                    error = RunCondition(item, elements, element, ref sPluginsToStart, ref atLeastOneConditionTrue, ParentBoard);
                    if (error)
                        break;
                }

                if (error)
                {
                    sPluginsToStart = string.Empty;
                    int max = _scripts.Max(key => key.Value.ZenCsScript.Order);

                    foreach (KeyValuePair<string, ZenCsScriptData> item in _scripts.Where(c => c.Value.ZenCsScript.Order == max))
                        RunCondition(item, elements, element, ref sPluginsToStart, ref atLeastOneConditionTrue, ParentBoard);
                }

                if (element.GetElementProperty("DO_DEBUG") == "1")
                    Console.WriteLine("");

                element.IsConditionMet = true;

                return sPluginsToStart;
            }
        }
        #endregion

        #region RunCondition
        bool RunCondition(KeyValuePair<string, ZenCsScriptData> item, Hashtable elements, IElement element, ref string sPluginsToStart, ref bool atLeastOneConditionTrue, IGadgeteerBoard ParentBoard)
        {
            try
            {
                bool scriptResult = (bool)item.Value.ZenCsScript.RunCustomCode(item.Value.ScriptDoc.DocumentNode.Descendants("code").FirstOrDefault().Attributes["id"].Value);
                if (element.GetElementProperty("DO_DEBUG") == "1")
                    Console.WriteLine(item.Key + " ==> " + scriptResult);

                if (scriptResult)
                {
                    sPluginsToStart += ((IElement)elements[item.Key]).ID + ",";
                    atLeastOneConditionTrue = true;
                }
            }
            catch (Exception ex)
            {
                string error = (ex.InnerException != null && !string.IsNullOrEmpty(ex.InnerException.Message)) ? ex.InnerException.Message : ex.Message;
                Console.WriteLine("Error in parsing " + item.Key.ToUpper() + " : " + ZenCsScriptCore.Decode(item.Value.ZenCsScript.RawHtml) + Environment.NewLine + error);
                ParentBoard.PublishInfoPrint(element.ID, "Error in parsing " + item.Key.ToUpper() + " : " + ZenCsScriptCore.Decode(item.Value.ZenCsScript.RawHtml) + Environment.NewLine + error, "error");
                return true;
            }
            return false;
        }
        #endregion
        #endregion
    }
}
