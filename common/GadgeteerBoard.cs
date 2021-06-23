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
using System.Text;

namespace ZenCommon
{
    public class GadgeteerBoard : IGadgeteerBoard
    {
        public GadgeteerBoard(string projectRoot, string projectId)
        {
            this.TemplateRootDirectory = projectRoot;
            this.TemplateID = projectId;
        }

        public void PublishInfoPrint(string elementId, string text, string type)
        {
           // Console.WriteLine("Not implemented");
        }

        public void PublishInfoPrint(string text, string type)
        {
            //Console.WriteLine("Not implemented");
        }

        public void PublishError(string elementId, string error)
        {
            //Console.WriteLine("Not implemented");
        }

        public void ClearProcesses()
        {
            //Console.WriteLine("Not implemented");
        }

        public void FireElementStopExecutingEvent(IElement element)
        {
            //Console.WriteLine("Not implemented");
        }

        public event OnReboot OnRebootEvent;
        public event OnElementStopExecuting OnElementStopExecutingEvent;
        public bool AuthenticateMqttWithPrivateKey { get; set; }
        public string MqttHost { get; set; }
        public string MqttSslProtocolVersion { get; set; }
        public int MqttPort { get; set; }
        public bool IsDebugEnabled { get; set; }
        public bool AttachDebugger { get; set; }
        public bool IsRemoteDebugEnabled { get; set; }
        public bool IsRemoteUpdateEnabled { get; set; }
        public string WorkstationID { get; set; }
        public string TemplateID { get; set; }
        public byte[] EncryptionKey { get; set; }
        public string TemplateRootDirectory { get; set; }
        public Hashtable Modules { get; }
        public string Relations { get; }
        public string Implementations { get; }
        public string Dependencies { get; }
        public bool IsBreakpointMode { get; set; }
        public void UnregisterMqttEvents()
        {
            //Console.WriteLine("Not implemented");
        }
    }
}
