/**
 * Interface.cs - Kerbal-REPL
 * An interactive development shell for Kerbal Space Program
 * Copyright (c) Thomas P. 2016
 * Licensed under the terms of the MIT license
 */

/// System
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

/// Mono
using Mono.CSharp;

/// Unity
using UnityEngine;

namespace KerbalREPL
{
    /// <summary>
    /// The interface that applies the command we enter into the shell.
    /// The commands and responses are sent through a TcpSocket.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class Interface  : MonoBehaviour
    {
        /// <summary>
        /// The port where the interface is listening.
        /// </summary>
        public const Int16 port = 5448;

        /// <summary>
        /// The TCP Server object.
        /// </summary>
        public TcpListener server { get; set; }

        /// <summary>
        /// The C# Evaluator
        /// </summary>
        public Evaluator evaluator { get; set; }

        /// <summary>
        /// The current socket
        /// </summary>
        public TcpClient currentClient { get; set; }

        /// <summary>
        /// Awake is the first function that gets called in the lifecycle of a MonoBehaviour
        /// Start everything here.
        /// </summary>
        void Awake()
        {
            /// Don't die
            DontDestroyOnLoad(this);

            /// Say hello
            Debug.Log("[REPL] Interface started");

            /// Create the evaluator
            evaluator = new Evaluator(new CompilerContext(new CompilerSettings() { Unsafe = true }, new DelegateReportPrinter((msg, client) => 
            {
                Byte[] buffer = Encoding.UTF8.GetBytes(msg);
                client.Client.BeginSend(buffer, 0, buffer.Length, SocketFlags.None, (IAsyncResult result_) => Send(result_, client), null);
            }, this)));
            evaluator.InteractiveBaseClass = typeof(InteractiveBaseShell);
            evaluator.DescribeTypeExpressions = true;
            evaluator.WaitOnTask = true;
            AppDomain.CurrentDomain.GetAssemblies().ToList().ForEach(a => { if (!a.Location.Contains("mscorlib") && !a.Location.Contains("System.Core")) evaluator.ReferenceAssembly(a); });

            /// Create the TcpServer
            server = new TcpListener(IPAddress.Any, port);
            server.Start(4);
            Debug.Log("[REPL] TcpServer ready on port " + port);

            /// Accept Clients
            server.BeginAcceptTcpClient(AcceptTcpClient, null);
        }

        /// <summary>
        /// Accepts and saves the connecting clients, and starts to recieve data from them.
        /// </summary>
        void AcceptTcpClient(IAsyncResult result)
        {
            /// Accept the client
            TcpClient client = server.EndAcceptTcpClient(result);

            /// Log
            Debug.Log("[REPL] Accepted client");

            /// Start to accept messages
            Byte[] buffer = new Byte[4096];
            client.Client.BeginReceive(buffer, 0, 4096, SocketFlags.None, (IAsyncResult result_) => Receive(result_, client, buffer), null);

            /// Accept Clients
            server.BeginAcceptTcpClient(AcceptTcpClient, null);
        }

        /// <summary>
        /// Receives messages from the TcpClients
        /// </summary>
        void Receive(IAsyncResult result, TcpClient client, Byte[] buffer)
        {
            /// Update the client
            currentClient = client;

            /// Recieve the message
            Int32 length = client.Client.EndReceive(result);

            /// Get the whole string
            String command = Encoding.UTF8.GetString(buffer, 0, length);

            /// If the command is correct, send all assembly files
            if (command == "\x06" + "_ASM_" + "\x06")
            {
                String asm = String.Join(";", SortByDependencies().Select(a => a.Location).ToArray());
                Byte[] asm_b = Encoding.UTF8.GetBytes(asm);
                client.Client.BeginSend(asm_b, 0, asm_b.Length, SocketFlags.None, (IAsyncResult result_) => { Debug.Log(command); Send(result_, client); }, null);
            }
            else if (command == "\x06" + "_INTERRUPT_" + "\x06")
            {
                evaluator.Interrupt();
            }
            else
            {
                System.Object res;
                Boolean res_set;
                try
                {
                    command = evaluator.Evaluate(command, out res, out res_set);
                    if (res_set)
                    {
                        Byte[] data = Encoding.UTF8.GetBytes(PrettyPrint(res));
                        client.Client.BeginSend(data, 0, data.Length, SocketFlags.None, (IAsyncResult result_) => Send(result_, client), null);
                    }
                    else
                    {
                        Byte[] data = Encoding.UTF8.GetBytes("\x06" + "_NONO_" + "\x06");
                        client.Client.BeginSend(data, 0, data.Length, SocketFlags.None, (IAsyncResult result_) => Send(result_, client), null);
                    }
                }
                catch (Exception e)
                {
                    Byte[] exc = Encoding.UTF8.GetBytes(e.ToString());
                    client.Client.BeginSend(exc, 0, exc.Length, SocketFlags.None, (IAsyncResult result_) => Send(result_, client), null);
                }
            }

            /// Accept messages
            buffer = new Byte[4096];
            client.Client.BeginReceive(buffer, 0, 4096, SocketFlags.None, (IAsyncResult result_) => Receive(result_, client, buffer), null);
        }

        /// <summary>
        /// Sends sth. to the connected clients
        /// </summary>
        void Send(IAsyncResult result, TcpClient client)
        {
            client.Client.EndSend(result);
        }

        // Some types (System.Json.JsonPrimitive) implement
        // IEnumerator and yet, throw an exception when we
        // try to use them, helper function to check for that
        // condition
        internal static bool WorksAsEnumerable(System.Object obj)
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
        internal static String PrettyPrint(System.Object result)
        {
            String output = "";
            if (result == null)
            {
                output += "null";
                return output;
            }
            if (result is Array)
            {
                Array a = (Array)result;
                output += "{ ";
                Int32 top = a.GetUpperBound(0);
                for (Int32 i = a.GetLowerBound(0); i <= top; i++)
                {
                    output += PrettyPrint(a.GetValue(i));
                    if (i != top)
                        output += ", ";
                }
                output += " }";
            }
            else if (result is Boolean)
            {
                if ((Boolean)result)
                    output += "true";
                else
                    output += "false";
            }
            else if (result is String)
            {
                output += "\"";
                output += (String)result;
                output += "\"";
            }
            else if (result is IDictionary)
            {
                IDictionary dict = (IDictionary)result;
                Int32 top = dict.Count, count = 0;

                output += "{";
                foreach (DictionaryEntry entry in dict)
                {
                    count++;
                    output += "{ ";
                    output += PrettyPrint(entry.Key);
                    output += ", ";
                    output += PrettyPrint(entry.Value);
                    if (count != top)
                        output += " }, ";
                    else
                        output += " }";
                }
                output += "}";
            }
            else if (WorksAsEnumerable(result))
            {
                Int32 i = 0;
                output += "{ ";
                foreach (System.Object item in (IEnumerable)result)
                {
                    if (i++ != 0)
                        output += ", ";
                    output += PrettyPrint(item);
                }
                output += " }";
            }
            else if (result is Char)
            {
                output += (Char)result;
            }
            else
            {
                output += result.ToString();
            }
            return output;
        }

        public static Assembly[] SortByDependencies()
        {
            /// Get the assemblies
            List<AssemblyItem> assemblyItems = AppDomain.CurrentDomain.GetAssemblies().OrderBy(a => a.FullName).Select(a => new AssemblyItem(a)).ToList();

            /// Add the dependencies
            foreach (AssemblyItem item in assemblyItems)
            {
                foreach (AssemblyName reference in item.Item.GetReferencedAssemblies())
                {
                    AssemblyItem dependency = assemblyItems.SingleOrDefault(i => i.Item.FullName == reference.FullName);
                    if (dependency != null)
                        item.Dependencies.Add(dependency);
                }
            }

            /// Sort the assemblies
            return TSort(assemblyItems, i => i.Dependencies).Select(i => i.Item).ToArray();
        }

        /// <summary>
        /// Topological Sort
        /// </summary>
        public static IEnumerable<T> TSort<T>(IEnumerable<T> source, Func<T, IEnumerable<T>> dependencies, bool throwOnCycle = false)
        {
            var sorted = new List<T>();
            var visited = new HashSet<T>();

            foreach (var item in source)
                Visit(item, visited, sorted, dependencies, throwOnCycle);

            return sorted;
        }

        /// <summary>
        /// Topological Sort
        /// </summary>
        private static void Visit<T>(T item, HashSet<T> visited, List<T> sorted, Func<T, IEnumerable<T>> dependencies, bool throwOnCycle)
        {
            if (!visited.Contains(item))
            {
                visited.Add(item);
                foreach (T dep in dependencies(item))
                    Visit(dep, visited, sorted, dependencies, throwOnCycle);
                sorted.Add(item);
            }
            else
            {
                if (throwOnCycle && !sorted.Contains(item))
                    throw new Exception("Cyclic dependency found");
            }
        }

        /// <summary>
        /// A small wrapper class for Assemblies
        /// </summary>
        public class AssemblyItem
        {
            public Assembly Item { get; set; }
            public IList<AssemblyItem> Dependencies { get; set; }

            public AssemblyItem(Assembly item)
            {
                Item = item;
                Dependencies = new List<AssemblyItem>();
            }
        }
    }
}
