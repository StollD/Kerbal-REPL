/**
 * Interface.cs - Kerbal-REPL
 * An interactive development shell for Kerbal Space Program
 * Copyright (c) Thomas P. 2016
 * Licensed under the terms of the MIT license
 */

/// System
using System;

/// Mono
using Mono.Terminal;
using Mono.CSharp;

namespace KerbalREPL
{
    public class InteractiveBaseShell : InteractiveBase
    {
        /// <summary>
        /// Whether pressing Tab should trigger auto-complete
        /// </summary>
        private static Boolean tab_at_start_completes;

        /// <summary>
        /// The editor matrix that is used to find matching functions
        /// </summary>
        internal static LineEditor Editor;

        /// <summary>
        /// Whether pressing Tab should trigger auto-complete 
        /// </summary>
        public static Boolean TabAtStartCompletes
        {
            get { return tab_at_start_completes; }
            set
            {
                tab_at_start_completes = value;
                if (Editor != null)
                    Editor.TabAtStartCompletes = value;
            }
        }

        /// <summary>
        /// Extend the help string
        /// </summary>
        public static new String help
        {
            get { return InteractiveBase.help + "  TabAtStartCompletes      - Whether tab will complete even on empty lines\n"; }
        }
    }
}
