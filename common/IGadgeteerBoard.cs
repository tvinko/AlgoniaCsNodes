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
using System.Threading;

namespace ZenCommon
{
    public delegate void OnElementStopExecuting(object sender, IElement args);
    public delegate void OnReboot(object sender, EventArgs arg);

    public interface IGadgeteerBoard
    {
        void PublishInfoPrint(string elementId, string text, string type);
        void PublishInfoPrint(string text, string type);
        void PublishError(string elementId, string error);
        void ClearProcesses();
        void FireElementStopExecutingEvent(IElement element);
        event OnReboot OnRebootEvent;
        event OnElementStopExecuting OnElementStopExecutingEvent;
        bool AuthenticateMqttWithPrivateKey { get; set; }
        string MqttHost { get; set; }
        string MqttSslProtocolVersion { get; set; }
        int MqttPort { get; set; }
        bool IsDebugEnabled { get; set; }
        bool AttachDebugger { get; set; }
        bool IsRemoteDebugEnabled { get; set; }
        bool IsRemoteUpdateEnabled { get; set; }
        string WorkstationID { get; set; }
        string TemplateID { get; set; }
        byte[] EncryptionKey { get; set; }
        string TemplateRootDirectory { get; set; }
        Hashtable Modules { get; }
        string Relations { get; }
        string Implementations { get; }
        string Dependencies { get; }
        bool IsBreakpointMode { get; set; }
        void UnregisterMqttEvents();
    }
}
