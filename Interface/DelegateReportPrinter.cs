/**
 * Interface.cs - Kerbal-REPL
 * An interactive development shell for Kerbal Space Program
 * Copyright (c) Thomas P. 2016
 * Licensed under the terms of the MIT license
 */

/// System
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

/// Mono
using Mono.CSharp;

namespace KerbalREPL
{
    /// <summary>
    /// A dynamic ReportPrinter
    /// </summary>
    public class DelegateReportPrinter : ReportPrinter
    {
        /// <summary>
        /// The action that gets executed
        /// </summary>
        protected Action<String, TcpClient> action;

        /// <summary>
        /// The reference to our Interface
        /// </summary>
        protected Interface module;

        /// <summary>
        /// Print sth.
        /// </summary>
        public override void Print(AbstractMessage msg, Boolean showFullPath)
        {
            String output = "";
            base.Print(msg, showFullPath);
            StringBuilder stringBuilder = new StringBuilder();
            if (!msg.Location.IsNull)
            {
                if (!showFullPath)
                    stringBuilder.Append(msg.Location.ToString());
                else
                    stringBuilder.Append(msg.Location.ToStringFullName());
                stringBuilder.Append(" ");
            }
            stringBuilder.AppendFormat("{0} CS{1:0000}: {2}", msg.MessageType, msg.Code, msg.Text);
            if (msg.IsWarning)
                output += stringBuilder.ToString();
            else
                output += FormatText(stringBuilder.ToString());
            if (msg.RelatedSymbols != null)
            {
                String[] relatedSymbols = msg.RelatedSymbols;
                for (Int32 i = 0; i < relatedSymbols.Length; i++)
                    output += String.Concat(relatedSymbols[i], msg.MessageType, ")");
            }
            action(output, module.currentClient);
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public DelegateReportPrinter(Action<String, TcpClient> action, Interface module)
        {
            this.action = action;
            this.module = module;
        }
    }
}
