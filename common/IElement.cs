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

namespace ZenCommon
{
    public enum ElementStatus { STOPPED, RUNNING, ARRIVED }
    public interface IElement
    {

        #region Properties
        IntPtr Ptr { get; set; }
        IElement IAmStartedYou { get; set; }

        string LastResult { get; set; }
        object LastResultBoxed { get; set; }
        bool IsConditionMet { get; set; }
        string ID { get; set; }
        string ErrorMessage { get; set; }
        int ErrorCode { get; set; }
        string DebugString { get; set; }
        bool DoDebug { get; set; }
        ArrayList DisconnectedElements { get; set; }
        DateTime Started { get; set; }
        bool IsManaged { get; set; }
        string ResultUnit { get; set; }
        string ResultSource { get; set; }
        #endregion

        #region Methods
        void SetElementProperty(string key, string value);
        string GetElementProperty(string key);
        void StartElement(Hashtable elements, bool Async);
        #endregion
    }
}
