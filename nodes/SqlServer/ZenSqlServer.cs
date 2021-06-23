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

using ZenCommon;
using HtmlAgilityPack;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Algonia.Cs.Node.SqlServer
{
    public class ZenSqlServer
    {
        #region Fields
        #region _script
        Dictionary<string, ZenCsScriptData> _scripts = new Dictionary<string, ZenCsScriptData>();
        #endregion

        /*#region _dynamicSpParams
        Hashtable _dynamicSpParams = new Hashtable();
        #endregion
    */
        #region _scriptData
        ZenCsScriptData _scriptData;
        #endregion

        #region _syncCsScript
        object _syncCsScript = new object();
        #endregion
        #endregion

        /*
        #region IZenCallable Implementations
        #region Functions
        public object Call(string actionID, Hashtable param)
        {
            switch (actionID)
            {
                case "ADD_SP_PARAM":
                    foreach (DictionaryEntry pair in param)
                        _dynamicSpParams[pair.Key] = pair.Value;
                    break;

                case "GET_SP_PARAM":
                    foreach (DictionaryEntry pair in param)
                        return _dynamicSpParams[pair.Key];
                    break;

                case "CLEAR_SP_PARAMS":
                    _dynamicSpParams.Clear();
                    break;
            }
            return null;
        }
        #endregion
        #endregion
    */
        #region _implementations
        static Dictionary<string, ZenSqlServer> _implementations = new Dictionary<string, ZenSqlServer>();
        #endregion

        unsafe public static void InitUnmanagedElements(string currentElementId, void** elements, int elementsCount, int isManaged, string projectRoot, string projectId, ZenNativeHelpers.GetElementProperty getElementPropertyCallback, ZenNativeHelpers.GetElementResultInfo getElementResultInfoCallback, ZenNativeHelpers.GetElementResult getElementResultCallback, ZenNativeHelpers.ExecuteElement executeElementCallback, ZenNativeHelpers.SetElementProperty setElementProperty, ZenNativeHelpers.AddEventToBuffer addEventToBuffer)
        {
            if (!_implementations.ContainsKey(currentElementId))
                _implementations.Add(currentElementId, new ZenSqlServer());

            ZenNativeHelpers.InitUnmanagedElements(currentElementId, elements, elementsCount, isManaged, projectRoot, projectId, getElementPropertyCallback, getElementResultInfoCallback, getElementResultCallback, executeElementCallback, setElementProperty, addEventToBuffer);
        }
        unsafe public static void ExecuteAction(string currentElementId, void** elements, int elementsCount, IntPtr result)
        {
            _implementations[currentElementId].ExecuteSql(ZenNativeHelpers.Elements, ZenNativeHelpers.Elements[currentElementId] as IElement, ZenNativeHelpers.ParentBoard);
            ZenNativeHelpers.CopyManagedStringToUnmanagedMemory(string.Empty, result);
        }
        #region Functions
        #region ExecuteSql
        void ExecuteSql(Hashtable elements, IElement element, IGadgeteerBoard ParentBoard)
        {
            SqlConnection conn = null;
            try
            {
                string port = !string.IsNullOrEmpty(element.GetElementProperty("PORT")) ? "PORT=" + element.GetElementProperty("PORT").Trim() + ";" : string.Empty;

                string connectionString = "Server=" + ZenCsScriptCore.Decode(element.GetElementProperty("SERVER")) + ";Database=" + element.GetElementProperty("DATABASE") + ";";
                connectionString += !string.IsNullOrEmpty(element.GetElementProperty("UID")) ?
                    "User Id=" + element.GetElementProperty("UID") + ";" + "Password=" + element.GetElementProperty("PASSWORD") + ";" :
                    "Integrated Security = true;";

                using (conn = new SqlConnection(connectionString))
                {
                    using (SqlCommand command = new SqlCommand())
                    {
                        command.Connection = conn;
                        switch (element.GetElementProperty("COMMAND_TYPE"))
                        {
                            case "1":
                                command.CommandType = CommandType.Text;
                                command.CommandText = GetSqlStatement(element.GetElementProperty("SQL_STATEMENT"), GetCacheFileName(element), elements, element, element.GetElementProperty("PRINT_CODE") == "1", ParentBoard);
                                if (element.GetElementProperty("DO_DEBUG") == "1")
                                    ParentBoard.PublishInfoPrint(element.ID, command.CommandText, "info");
                                break;

                            case "4":
                                command.CommandType = CommandType.StoredProcedure;
                                command.CommandText = element.GetElementProperty("STORED_PROCEDURE");
                                if (!string.IsNullOrEmpty(element.GetElementProperty("SQLSERVER_PARAMETERS")))
                                {
                                    foreach (string s in element.GetElementProperty("SQLSERVER_PARAMETERS").Split("#101#"))
                                    {
                                        if (!string.IsNullOrEmpty(s))
                                        {
                                            string[] paramData = s.Split("#100#");
                                            AddSqlServerParam(paramData[0], paramData[1], elements, element, command, ParentBoard);
                                        }
                                    }
                                }

                                /*if (_dynamicSpParams.Count > 0)
                                {
                                    foreach (DictionaryEntry kvp in _dynamicSpParams)
                                    {
                                        string preFeedbackScript = "Hashtable ht = new Hashtable();";
                                        preFeedbackScript += "ht.Add(\"" + kvp.Key.ToString() + "\",null);";
                                        string feedbackScript = "_callable.Call(\"GET_SP_PARAM\",ht)";
                                        AddSqlServerParam(kvp.Key.ToString(), feedbackScript, preFeedbackScript, elements, element, command);
                                    }
                                }
                                else if (!string.IsNullOrEmpty(element.GetElementProperty("SQLSERVER_PARAMETERS")))
                                {
                                    foreach (string s in element.GetElementProperty("SQLSERVER_PARAMETERS").Split('¨'))
                                    {
                                        if (!string.IsNullOrEmpty(s))
                                            AddSqlServerParam(s.Split('°')[0], s.Split('°')[1], null, elements, element, command);
                                    }
                                }*/
                                break;
                            case "512":
                                throw new Exception("ZenSqlServer : Table direct not supported yet!");
                        }

                        conn.Open();
                        DataSet ds = new DataSet();
                        SqlDataAdapter da = new SqlDataAdapter(command);
                        da.Fill(ds);

                        element.LastResultBoxed = ds;
                        //PrintDebug(element);
                    }
                }
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && !string.IsNullOrEmpty(ex.InnerException.Message))
                    throw new Exception(ex.InnerException.Message);
                else if (!string.IsNullOrEmpty(ex.Message))
                    throw new Exception(ex.Message);
                else
                    throw new Exception("Unknown Error in " + element.ID);
            }
            finally
            {
                if (conn != null && conn.State == ConnectionState.Open)
                    conn.Close();

                element.IsConditionMet = true;
            }
        }
        #endregion

        #region PrintDebug
        void PrintDebug(IElement element)
        {
            if (element.GetElementProperty("DO_DEBUG") == "1")
            {
                Console.WriteLine();
                Console.WriteLine(element.ID + " result details");

                DataSet ds = (DataSet)element.LastResultBoxed;
                for (int i = 0; i < ds.Tables.Count; i++)
                {
                    Console.WriteLine("Table : " + ds.Tables[i].TableName);
                    Console.WriteLine("Columns: ");
                    for (int j = 0; j < ds.Tables[i].Columns.Count; j++)
                    {
                        Console.WriteLine("Column name : " + ds.Tables[i].Columns[j].ColumnName);
                        Console.WriteLine("Column type : " + ds.Tables[i].Columns[j].DataType.Name.ToString());
                        Console.WriteLine("--------------------------------------");
                    }
                }
                Console.WriteLine();
            }
        }
        #endregion

        #region AddSqlServerParam
        void AddSqlServerParam(string key, string scriptText, Hashtable elements, IElement element, SqlCommand command, IGadgeteerBoard ParentBoard)
        {
            if (!_scripts.ContainsKey(key))
            {
                try
                {
                    _scripts.Add(key, ZenCsScriptCore.Initialize(ZenCsScriptCore.GetFunction("return " + scriptText + ";"), elements, element, Path.Combine("tmp", "SqlServer", element.ID + "_" + key + ".zen"), ParentBoard, element.GetElementProperty("PRINT_CODE") == "1"));
                }
                catch (Exception ex)
                {
                    string error = string.Empty;
                    if (ex.InnerException != null && !string.IsNullOrEmpty(ex.InnerException.Message))
                        error = ex.InnerException.Message;
                    else
                        error = ex.Message;

                    throw new Exception("Error in parsing : " + scriptText + Environment.NewLine + error);
                }
            }
            command.Parameters.AddWithValue(key, _scripts[key].ZenCsScript.RunCustomCode(_scripts[key].ScriptDoc.DocumentNode.Descendants("code").FirstOrDefault().Attributes["id"].Value));
        }
        #endregion

        #region GetCacheFileName
        string GetCacheFileName(IElement element)
        {
            return Path.Combine("tmp", "SqlServer", element.ID + ".zen");
        }
        #endregion

        #region GetSqlStatement
        string GetSqlStatement(string text, string file, Hashtable elements, IElement element, bool debug, IGadgeteerBoard ParentBoard)
        {
            lock (_syncCsScript)
            {
                if (_scriptData == null)
                    _scriptData = ZenCsScriptCore.Initialize(text, elements, element, file, ParentBoard, debug);
            }
            return ZenCsScriptCore.GetCompiledText(text, _scriptData);
        }
        #endregion
    }
    #endregion
}
