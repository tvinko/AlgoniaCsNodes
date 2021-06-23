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
using System.Runtime.InteropServices;
using System.Text;

namespace ZenCommon
{
    public class Element : IElement
    {
        #region Constructor
        public Element(string id, IntPtr Ptr)
        {
            this.ID = id;
            this.Ptr = Ptr;
        }
        #endregion

        #region Fields
        #region _lastResultBoxed
        object _lastResultBoxed;
        #endregion

        #region _resultType
        int _resultType = -1;
        #endregion

        #region PtrResultInstance
        unsafe void** PtrResultInstance
        {
            get
            {
                // Obtain pointer to element result. Warning - element result pointer must already be initialized in this step.
                // Unmanaged code is responsible to allocate pointer before this function call
                return ZenNativeHelpers.GetElementResultCallback(this.Ptr);
            }
        }
        #endregion
        #endregion

        #region Properties
        public IntPtr Ptr { get; set; }
        public IElement IAmStartedYou { get; set; }

        public string LastResult { get; set; }

        public bool IsConditionMet { get; set; }
        public string ID { get; set; }
        public string ErrorMessage { get; set; }
        public int ErrorCode { get; set; }
        public string DebugString { get; set; }
        public bool DoDebug { get; set; }
        public ArrayList DisconnectedElements { get; set; }
        public DateTime Started { get; set; }
        public bool IsManaged { get; set; }
        public string ResultSource { get; set; }
        public string ResultUnit { get; set; }

        unsafe public object LastResultBoxed
        {
            get
            {
                if (!this.IsManaged)
                {
                    if (_resultType < 0)
                        _resultType = ZenNativeHelpers.GetElementResultInfoCallback(this.Ptr);

                    switch (_resultType)
                    {
                        case (int)ZenNativeHelpers.ElementResultType.RESULT_TYPE_INT:
                            return *((int*)(*PtrResultInstance));

                        case (int)ZenNativeHelpers.ElementResultType.RESULT_TYPE_CHAR_ARRAY:
                            return Marshal.PtrToStringAnsi((IntPtr)(*PtrResultInstance));

                        default:
                            break;
                    }
                    return null;
                }
                else
                    return _lastResultBoxed;
            }
            set
            {
                if (!this.IsManaged)
                {
                    switch (value.GetType().Name)
                    {
                        case "Int32":
                            *((int*)(*PtrResultInstance)) = (int)value;
                            break;

                        default:
                            Console.WriteLine("Type " + value.GetType().Name + " is not supported...");
                            break;
                    }
                }
                else
                    _lastResultBoxed = value;
            }
        }
        #endregion

        #region Methods
        public void SetElementProperty(string key, string value)
        {
            ZenNativeHelpers.SetElementPropertyCallback(this.Ptr, key, value);
        }

        public string GetElementProperty(string key)
        {
            return ZenNativeHelpers.GetElementPropertyCallback(this.Ptr, key);
        }

        public void StartElement(Hashtable elements, bool Async)
        {
            ZenNativeHelpers.ExecuteElementCallback(this.Ptr);
        }
        #endregion
    }
}
