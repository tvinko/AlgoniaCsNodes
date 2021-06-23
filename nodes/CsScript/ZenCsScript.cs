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

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using ZenCommon;
using System;

namespace Algonia.Cs.Node.Scripting
{
    public class ZenCsScript
    {
        #region Fields
        #region _scriptData
        ZenCsScriptData _scriptData;
        #endregion

        #region _syncCsScript
        object _syncCsScript = new object();
        #endregion
        #endregion

        #region _implementations
        static Dictionary<string, ZenCsScript> _implementations = new Dictionary<string, ZenCsScript>();
        #endregion

        unsafe public static string GetDynamicElements(string currentElementId, void** elements, int elementsCount, int isManaged, string projectRoot, string projectId, ZenNativeHelpers.GetElementProperty getElementPropertyCallback, ZenNativeHelpers.GetElementResultInfo getElementResultInfoCallback, ZenNativeHelpers.GetElementResult getElementResultCallback, ZenNativeHelpers.ExecuteElement execElementCallback, ZenNativeHelpers.SetElementProperty setElementProperty, ZenNativeHelpers.AddEventToBuffer addEventToBuffer)
        {
            string pluginsToExecute = string.Empty;

            ZenNativeHelpers.InitUnmanagedElements(currentElementId, elements, elementsCount, isManaged, projectRoot, projectId, getElementPropertyCallback, getElementResultInfoCallback, getElementResultCallback, execElementCallback, setElementProperty, addEventToBuffer);
            foreach (Match match in Regex.Matches((ZenNativeHelpers.Elements[currentElementId] as IElement).GetElementProperty("SCRIPT_TEXT").Replace("&quot;", "\""), @"exec(.*?);"))
            {
                var elementMatch = match.Groups[1].Value;
                int i = 0;
                // Find first double quote: exec("element")
                //                               _
                while (i < elementMatch.Length && elementMatch[i] != '"') i++;

                i++;
                string elementId = string.Empty;
                // Extract element id. Loop till ending double quote: exec("element")
                //                                                                 _
                while (i < elementMatch.Length && elementMatch[i] != '"')
                {
                    elementId += elementMatch[i].ToString();
                    i++;
                }

                if (!string.IsNullOrEmpty(elementId))
                    pluginsToExecute += elementId.Trim() + ",";
            }
            return pluginsToExecute;
        }

        public static int GetResultLen(string currentElementId)
        {
            if (!ZenNativeHelpers.Elements.ContainsKey(currentElementId) || (ZenNativeHelpers.Elements[currentElementId] as IElement).LastResultBoxed == null)
            {
                Console.WriteLine("WARNING: Node with Id " + currentElementId + " does not exists or has NULL value result");
                return -1;
            }

            return (ZenNativeHelpers.Elements[currentElementId] as IElement).LastResultBoxed.ToString().Length;
        }

        unsafe public static void GetResult(string currentElementId, IntPtr result)
        {
            if (!ZenNativeHelpers.Elements.ContainsKey(currentElementId) || (ZenNativeHelpers.Elements[currentElementId] as IElement).LastResultBoxed == null)
            {
                Console.WriteLine("WARNING: Node with Id " + currentElementId + " does not exists or has NULL value result");
                return;
            }

            ZenNativeHelpers.CopyManagedStringToUnmanagedMemory((ZenNativeHelpers.Elements[currentElementId] as IElement).LastResultBoxed.ToString(), result);
        }

        unsafe public static void InitUnmanagedElements(string currentElementId, void** elements, int elementsCount, int isManaged, string projectRoot, string projectId, ZenNativeHelpers.GetElementProperty getElementPropertyCallback, ZenNativeHelpers.GetElementResultInfo getElementResultInfoCallback, ZenNativeHelpers.GetElementResult getElementResultCallback, ZenNativeHelpers.ExecuteElement executeElementCallback, ZenNativeHelpers.AddEventToBuffer addEventToBuffer)
        {
            if (!_implementations.ContainsKey(currentElementId))
                _implementations.Add(currentElementId, new ZenCsScript());
        }
        unsafe public static void OnElementInit(string currentElementId, void** elements, int elementsCount, IntPtr result)
        {
            try
            {
                lock (_implementations[currentElementId]._syncCsScript)
                {
                    _implementations[currentElementId]._scriptData = ZenCsScriptCore.Initialize((ZenNativeHelpers.Elements[currentElementId] as IElement).GetElementProperty("SCRIPT_TEXT"), ZenNativeHelpers.Elements, (ZenNativeHelpers.Elements[currentElementId] as IElement), Path.Combine("tmp", "CsScript", (ZenNativeHelpers.Elements[currentElementId] as IElement).ID + ".zen"), ZenNativeHelpers.ParentBoard, (ZenNativeHelpers.Elements[currentElementId] as IElement).GetElementProperty("DEBUG") == "1");
                }
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && !string.IsNullOrEmpty(ex.InnerException.Message))
                    Console.WriteLine(ex.InnerException.Message);
                else
                    Console.WriteLine(ex.Message);

                Console.ReadLine();
            }
        }

        unsafe public static void ExecuteAction(string currentElementId, void** elements, int elementsCount, IntPtr result)
        {
            //Set result here, because can user set it in script
            (ZenNativeHelpers.Elements[currentElementId] as IElement).IsConditionMet = true;
            _implementations[currentElementId]._scriptData.ZenCsScript.RunCustomCode(_implementations[currentElementId]._scriptData.ScriptDoc.DocumentNode.Descendants("code").FirstOrDefault().Attributes["id"].Value);
            ZenNativeHelpers.CopyManagedStringToUnmanagedMemory(string.Empty, result);
        }
    }
}