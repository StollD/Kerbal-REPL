/**
 * Interface.cs - Kerbal-REPL
 * An interactive development shell for Kerbal Space Program
 * Copyright (c) Thomas P. 2016
 * Licensed under the terms of the MIT license
 */

/// System
using System;
using System.Linq;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;
using System.Reflection;
using System.ComponentModel;

/// Mono
using Mono.CSharp;

namespace KerbalREPL
{
    /// <summary>
    /// Here the commands for the REPL are entered and sent to the Interface
    /// inside of KSP through TCP.
    /// </summary>
    public class Shell
    {
        /// <summary>
        /// The port where the interface is listening.
        /// </summary>
        public const Int16 port = 5448;

        /// <summary>
        /// The current socket
        /// </summary>
        public static TcpClient client { get; set; }

        /// <summary>
        /// Whether the console is an atty terminal
        /// </summary>
        public static Boolean isatty = true;

        /// <summary>
        /// Whether the console is a dumb terminal
        /// </summary>
        public static Boolean is_unix = false;

        /// <summary>
        /// A dummy Evaluator, responsible for getting autocompletes
        /// </summary>
        public static Evaluator evaluator;

        /// <summary>
        /// The thread where we listen for new stuff
        /// </summary>
        public static BackgroundWorker receiveThread;

        /// <summary>
        /// Whether the console got locked
        /// </summary>
        public static Boolean locked;

        /// <summary>
        /// The Main method is the method that gets called first in a .NET console application.
        /// Here we create the connection for our REPL and so on.
        /// </summary>
        public static Int32 Main(String[] args)
        {
            /// Create the Connection
            try
            {
                client = new TcpClient();
                client.Connect("localhost", port);
            }
            catch
            {
                /// Abort if the connection failed
                Console.WriteLine("Connection to KSP Instance failed!");
                Console.Write("Please press any key...");
                Console.ReadKey();
                return 1;
            }

            /// Create the Evaluator
            evaluator = new Evaluator(new CompilerContext(new CompilerSettings() { Unsafe = true }, new StreamReportPrinter(new StreamWriter(new MemoryStream()))));
            evaluator.InteractiveBaseClass = typeof(InteractiveBase);
            evaluator.DescribeTypeExpressions = true;
            evaluator.WaitOnTask = true;
            LoadStartupFiles();

            /// Create the second thread
            receiveThread = new BackgroundWorker();
            receiveThread.DoWork += Listen;
            receiveThread.RunWorkerAsync();

            /// Run the Shell
            return ReadEvalPrintLoop();
        }

        /// <summary>
        /// Get new messages and invoke the appropreate functions
        /// </summary>
        public static void Listen(Object sender, DoWorkEventArgs e)
        {
            while (true)
            {
                Thread.Sleep(1000);
                Byte[] buffer = new Byte[8192];
                Int32 count = client.Client.Receive(buffer);
                buffer = buffer.Take(count).ToArray();
                String result = Encoding.UTF8.GetString(buffer);
                if (result == "\x06" + "_NONO_" + "\x06")
                {
                    Thread.Sleep(500);
                    locked = false;
                    continue;
                }
                p(Console.Out, result);
                Console.WriteLine();
                Thread.Sleep(500);
                locked = false;
            }
        }

        /// <summary>
        /// Gets called when Ctrl+C are pressed
        /// </summary>
        protected static void ConsoleInterrupt(object sender, ConsoleCancelEventArgs a)
        {
            /// Do not abort our program
            a.Cancel = true;

            /// Send the interrupt command
            Byte[] buffer = Encoding.UTF8.GetBytes("\x06" + "_INTERRUPT_" + "\x06"); 
            client.Client.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, (IAsyncResult result) => client.Client.EndSend(result), null);
        }

        /// <summary>
        /// Init everything
        /// </summary>
        private static void SetupConsole()
        {
            Console.CancelKeyPress += ConsoleInterrupt;
            Console.InputEncoding = Console.OutputEncoding = Encoding.UTF8;
        }

        /// <summary>
        /// Recieves Input from the CMD
        /// </summary>
        private static String GetLine(bool primary)
        {
            String prompt = primary ? InteractiveBase.Prompt : InteractiveBase.ContinuationPrompt;
            Console.Write(prompt);
            return Console.ReadLine();
        }

        /// <summary>
        /// Add standard usings
        /// </summary>
        private static void InitializeUsing()
        {
            Evaluate("using System; using System.Linq; using System.Collections.Generic; using System.Collections; using UnityEngine;", out locked);
        }

        /// <summary>
        /// Starts the terminal stuff
        /// </summary>
        private static void InitTerminal()
        {
            Int32 p = (Int32)Environment.OSVersion.Platform;
            is_unix = (p == 4) || (p == 128);

            /// Work around, since Console is not accounting for
            /// cursor position when writing to Stderr.  It also
            /// has the undesirable side effect of making
            /// errors plain, with no coloring.
            ///			Report.Stderr = Console.Out;
            SetupConsole();
            Console.WriteLine("KSP C# Shell, type \"help;\" for help\n\nEnter statements below.");

        }

        /// <summary>
        /// Loads the files provided by KSP
        /// </summary>
        protected static void LoadStartupFiles()
        {
            /// Request the Assemblies
            Byte[] buffer = Encoding.UTF8.GetBytes("\x06" + "_ASM_" + "\x06");
            client.Client.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, (IAsyncResult result) => client.Client.EndSend(result), null);

            /// Receive the answer
            buffer = new Byte[4096];
            Int32 count = client.Client.Receive(buffer);
            String[] asm = Encoding.UTF8.GetString(buffer, 0, count).Split(';');
            
            /// Load the Assemblies
            foreach (String file in asm)
            {
                if (file.Contains("mscorlib") || file.Contains("Steamworks") || file.Contains("System.Core")) /// Steamworks throws BadImageFormatException while loading...
                    continue;
                try
                {
                    Assembly assembly = Assembly.LoadFrom(file);
                    evaluator.ReferenceAssembly(assembly);
                }
                catch
                {
                    /// Something with Strong name is kidding me as it seems
                }
            }
        }

        /// <summary>
        /// Run the programm
        /// </summary>
        public static int ReadEvalPrintLoop()
        {
            InitTerminal();
            InitializeUsing();
            String expr = null;
            while (!InteractiveBase.QuitRequested)
            {
                String input = GetLine(expr == null);
                if (input == null)
                    return 0;
                if (input == "")
                    continue;
                expr = expr == null ? input : expr + "\n" + input;
                expr = Evaluate(expr, out locked);
                while(true)
                {
                    Thread.Sleep(500);
                    if (!locked)
                        break;
                }
            }
            Console.CancelKeyPress -= ConsoleInterrupt;
            return 0;
        }

        /// <summary>
        /// Here we talk to the remote server and send commands
        /// </summary>
        protected static String Evaluate(String input, out Boolean res_set)
        {
            Byte[] buffer = Encoding.UTF8.GetBytes(input);
            client.Client.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, (IAsyncResult result) => client.Client.EndSend(result), null);
            Object o = null;
            try
            {
                return evaluator.Evaluate(input, out o, out res_set);
            }
            catch
            {
                res_set = false;
                return null;
            }
        }

        /// <summary>
        /// Print something
        /// </summary>
        private static void p(TextWriter output, string s)
        {
            output.Write(s);
        }

        // Some types (System.Json.JsonPrimitive) implement
        // IEnumerator and yet, throw an exception when we
        // try to use them, helper function to check for that
        // condition
        internal static bool WorksAsEnumerable(Object obj)
        {
            IEnumerable enumerable = obj as IEnumerable;
            if (enumerable != null)
            {
                try
                {
                    enumerable.GetEnumerator();
                    return true;
                }
                catch
                {
                    // nothing, we return false below
                }
            }
            return false;
        }

        /// <summary>
        /// Formats an object nicely
        /// </summary>
        internal static void PrettyPrint(TextWriter output, Object result)
        {
            if (result == null)
            {
                p(output, "null");
                return;
            }
            if (result is Array)
            {
                Array a = (Array)result;
                p(output, "{ ");
                Int32 top = a.GetUpperBound(0);
                for (Int32 i = a.GetLowerBound(0); i <= top; i++)
                {
                    PrettyPrint(output, a.GetValue(i));
                    if (i != top)
                        p(output, ", ");
                }
                p(output, " }");
            }
            else if (result is Boolean)
            {
                if ((Boolean)result)
                    p(output, "true");
                else
                    p(output, "false");
            }
            else if (result is String)
            {
                p(output, "\"");
                output.Write((String)result);
                p(output, "\"");
            }
            else if (result is IDictionary)
            {
                IDictionary dict = (IDictionary)result;
                Int32 top = dict.Count, count = 0;

                p(output, "{");
                foreach (DictionaryEntry entry in dict)
                {
                    count++;
                    p(output, "{ ");
                    PrettyPrint(output, entry.Key);
                    p(output, ", ");
                    PrettyPrint(output, entry.Value);
                    if (count != top)
                        p(output, " }, ");
                    else
                        p(output, " }");
                }
                p(output, "}");
            }
            else if (WorksAsEnumerable(result))
            {
                Int32 i = 0;
                p(output, "{ ");
                foreach (Object item in (IEnumerable)result)
                {
                    if (i++ != 0)
                        p(output, ", ");
                    PrettyPrint(output, item);
                }
                p(output, " }");
            }
            else if (result is Char)
            {
                output.Write((Char)result);
            }
            else
            {
                p(output, result.ToString());
            }
        }
    }
}

