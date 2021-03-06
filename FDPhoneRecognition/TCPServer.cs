﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using utility;

namespace FDPhoneRecognition
{
    //Space: 0x20 (ascii code)
    //LF: 0x0A (ascii code)
    //Format: Command + [arguments] + LF
    //Format:  Response + ID + [arguments] + LF

    /// <summary>
    /// Query Phone Model Parameter 
    /// Success: ACK PMP { pmp name }, for example: “ACK PMP iphone6s RoseGold”
    /// Fail: ERR PMP { reason }, for example: “ERR PMP Recognize timeout”
    /// </summary>
    /// <param name="f_Bitmap"></param>
    /// <returns>Model Name</returns>
    delegate string QueryPMP(System.Drawing.Bitmap f_Bitmap);
    /// <summary>
    /// Query Image Sampling Parameters
    /// Success: ACK ISP { isp name }, for example: “ACK ISP iphone6s”
    /// Fail: ERR ISP { reason }, for example: “ERR ISP Recognize timeout”
    /// </summary>
    /// <returns>Model Name</returns>
    delegate string QueryISP();
    /// <summary>
    /// TCP Server Class is managed the All of Server do, 
    /// like building connection, reconnecting with client, sending/receiving data and Stop Server on
    /// </summary>
    class TCPServer
    {
        private static readonly log4net.ILog m_Log = log4net.LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);
        /// <summary>
        /// The ip of server
        /// </summary>
        private string m_IP;
        /// <summary>
        /// The port of server
        /// </summary>
        private int m_Port;
        /// <summary>
        /// the current statuc of Server
        /// </summary>
        private int m_ServerStatus;
        /// <summary>
        /// the server status list
        /// </summary>

        private enum E_ServerStatus
        {
            e_WaitForClientConnect,
            e_WaitForClientData,
            e_Stop,
            e_Restart,
            e_Running
        }
        /// <summary>
        /// the system ask command
        /// </summary>
        private int m_SystemStatus;
        /// <summary>
        /// the system ask command list
        /// </summary>
        private enum E_SystemStatus
        {
            e_SystemRunning,
            e_SystemStop
        }
        /// <summary>
        /// the one of all main member. it is focus on Connection building
        /// </summary>
        System.Net.Sockets.TcpListener m_TCPListener = null;
        /// <summary>
        /// the one of all main member. it save which one is connect
        /// </summary>
        System.Net.Sockets.TcpClient m_Client = null;
        /// <summary>
        /// A signal control whether a client conectting or not
        /// </summary>
        private System.Threading.AutoResetEvent m_tcpClientConnectedEvent = new System.Threading.AutoResetEvent(false);
        /// <summary>
        /// Focus on communication within client and server 
        /// </summary>
        System.Net.Sockets.NetworkStream m_Stream = null;
        
        static private string m_ColorSizeModelName = string.Empty;
        static private string m_MemoryStation = string.Empty;
        static private string m_ModelName = string.Empty;

        
        /// <summary>
        /// constuct the TCPServer
        /// </summary>
        /// <param name="f_IP">the IP of server</param>
        /// <param name="f_Port">the port of server</param>
        public TCPServer(string f_IP = "127.0.0.1", int f_Port = 5050)
        {
            LogIt.PushLog($"[TCPServer][Contruct] ++: IP = {f_IP}:{f_Port}");
            m_IP = f_IP;
            m_Port = f_Port;
            m_tcpClientConnectedEvent.Reset();
            LogIt.PushLog($"[TCPServer][Contruct] --");
        }
        /// <summary>
        /// Call the thread to run of the Server
        /// </summary>
        public void SyncRun()
        {
            LogIt.PushLog($"[TCPServer][Sync] ++");
            System.Net.IPAddress localAddr = System.Net.IPAddress.Parse(m_IP);
            m_TCPListener = new System.Net.Sockets.TcpListener(localAddr, m_Port);
            System.Threading.Thread t_Thread = new System.Threading.Thread(new System.Threading.ThreadStart(Run));
            m_ServerStatus = (int)E_ServerStatus.e_WaitForClientConnect;
            m_SystemStatus = (int)E_SystemStatus.e_SystemRunning;
            t_Thread.Start();
            LogIt.PushLog($"[TCPServer][Contruct] --: Server Status = {m_ServerStatus}; System Status = {m_SystemStatus}");
        }
        /// <summary>
        /// 
        /// </summary>
        public void StopRun()
        {
            LogIt.PushLog($"[TCPServer][StopRun] ++: System Status = {m_SystemStatus}");
            m_SystemStatus = (int)E_SystemStatus.e_SystemStop;
            LogIt.PushLog($"[TCPServer][StopRun] --: System Status = {m_SystemStatus}");
        }
        /// <summary>
        /// the thread region. It control all of the TCP server do.
        /// </summary>
        private void Run()
        {
            // Buffer for reading data
            LogIt.PushLog($"[TCPServer][Run] ++");
            Byte[] t_DataBytes = new Byte[256];
            try
            {
                while (m_SystemStatus != (int)E_SystemStatus.e_SystemStop)
                {
                    switch (m_ServerStatus)
                    {
                        case (int)E_ServerStatus.e_Restart:
                            {
                                LogIt.PushLog($"[TCPServer][Run][Restart] ++");
                                m_Client.Close();
                                m_TCPListener.Stop();
                                m_ServerStatus = (int)E_ServerStatus.e_WaitForClientConnect;
                                LogIt.PushLog($"[TCPServer][Run][Restart] --: Next Server Status = {m_ServerStatus}");
                                break;
                            }
                        case (int)E_ServerStatus.e_WaitForClientConnect:
                            {
                                LogIt.PushLog($"[TCPServer][Run][WaitForClientConnect] ++");
                                m_TCPListener.Start();
                                m_TCPListener.BeginAcceptTcpClient(new AsyncCallback(DoAcceptTCPClientCallback), m_TCPListener);
                                
                                m_tcpClientConnectedEvent.WaitOne();
                                m_ServerStatus = (int)E_ServerStatus.e_WaitForClientData;
                                LogIt.PushLog($"[TCPServer][Run][WaitForClientConnect] --: Next  Server Status = {m_ServerStatus}");
                                break;
                            }
                        case (int)E_ServerStatus.e_WaitForClientData:
                            {
                                //LogIt.PushLog($"[TCPServer][Run][WaitForClientData] ++");
                                try
                                {
                                    bool t = m_Client.Client.Connected;
                                    int trt = m_Client.Client.Available;
                                    int t_TotalByteLength = m_Stream.Read(t_DataBytes, 0, t_DataBytes.Length);
                                    //Console.WriteLine($"Connected TotalByteLength = {t_TotalByteLength}");
                                    if (t_TotalByteLength != 0)
                                    {
                                        LogIt.PushLog($"[TCPServer][Run][WaitForClientData]: Receive Data Length = {t_DataBytes.Length}");

                                        string t_FeedBackStirng = DecisionTask(t_DataBytes, t_TotalByteLength);
                                        //m_Stream.Flush();
                                    }
                                    else
                                    {
                                        //Console.WriteLine($"Disconnect TotalByteLength = {t_TotalByteLength}"); //bool t = m_TCPListener.Server.Connected;
                                    }
                                    t_DataBytes = new byte[256];
                                }
                                catch(Exception Ex)
                                {
                                    m_ServerStatus = (int)E_ServerStatus.e_Restart;
                                }
                                //LogIt.PushLog($"[TCPServer][Run][WaitForClientData] --: Next Server Status = {m_ServerStatus}");
                                break;
                            }
                        case (int)E_ServerStatus.e_Stop:
                            {
                                LogIt.PushLog($"[TCPServer][Run][Stop] ++");
                                m_ServerStatus = (int)E_ServerStatus.e_Stop;
                                LogIt.PushLog($"[TCPServer][Run][Stop] --: Server Status = {m_ServerStatus}");
                                break;
                            }
                        default:
                            {
                                break;
                            }
                    }
                }
            }
            catch (System.Net.Sockets.SocketException e)
            {
                LogIt.PushLog($"[TCPServer][Run]: SocketException={e.Message}");
            }
            finally
            {
                m_Stream.Close();
                m_Client.Close();
                m_TCPListener.Stop();
            }
            LogIt.PushLog($"[TCPServer][Run] ++");
        }
        /// <summary>
        /// when Server Receive the Data from client, it will decide which task will do
        /// </summary>
        /// <param name="f_DataBytes">The data content</param>
        /// <param name="f_DataLength">The data length</param>
        /// <returns></returns>
        private string DecisionTask(byte[] f_DataBytes, int f_DataLength)
        {
            
            // Translate data bytes to a ASCII string.
            LogIt.PushLog($"[TCPServer][DecisionTask] ++");
            string t_Data = System.Text.Encoding.ASCII.GetString(f_DataBytes, 0, f_DataLength);
            string t_Feedback =string.Empty;
            LogIt.PushLog($"[TCPServer][DecisionTask]: Received Data = {t_Data}");
            if (t_Data.IndexOf("QueryISP") >= 0)
            {
                //Mapping table when get size and color
                //Start Chris Color and Size Detect
                m_ColorSizeModelName = "Iphone6s Gray";
                if(string.IsNullOrEmpty(m_ColorSizeModelName) == true)
                {
                    t_Feedback = $"ERR ISP UnSuccessful";
                }
                else
                {
                    t_Feedback = $"ACK ISP {m_ColorSizeModelName}{System.Text.Encoding.ASCII.GetString(new byte[] { 0x0A })}";
                }
                byte[] msg = System.Text.Encoding.ASCII.GetBytes(t_Feedback);
                m_Stream.Write(msg, 0, msg.Length);
            }
            if(t_Data.IndexOf("QueryPMP") >= 0)
            {
                //Get modle name
                m_ModelName = "Iphone6 Gray";
                if (string.IsNullOrEmpty(m_ModelName) == true)
                {
                    t_Feedback = $"ERR PMP UnSuccessful";
                }
                else
                {
                    t_Feedback = $"ACK PMP {m_ModelName}{System.Text.Encoding.ASCII.GetString(new byte[] { 0x0A })}";
                }
                byte[] msg = System.Text.Encoding.ASCII.GetBytes(t_Feedback);
                m_Stream.Write(msg, 0, msg.Length);
            }
            if(t_Data.IndexOf("MMI") >=0)
            {
                m_MemoryStation = t_Data.Substring(3).Trim();
                t_Feedback = $"ACK MMI {m_MemoryStation}{System.Text.Encoding.ASCII.GetString(new byte[] { 0x0A })}";

                byte[] msg = System.Text.Encoding.ASCII.GetBytes(t_Feedback);
                m_Stream.Write(msg, 0, msg.Length);

                ShareMemory t_SharedMemory = new ShareMemory(m_MemoryStation, string.Empty);
                t_SharedMemory.SyncGetMemory();

                //Start Chris Model Detect
            }
            // Process the data sent by the client.
            LogIt.PushLog($"[TCPServer][DecisionTask]: Send Data = {t_Feedback}");
            LogIt.PushLog($"[TCPServer][DecisionTask] --");
            return string.Empty ;
        }
        /// <summary>
        /// This is callback when someone connected with server
        /// </summary>
        /// <param name="f_IAsyncResult">the interface from System.Net.Sockets.TcpListener</param>
        private void DoAcceptTCPClientCallback(IAsyncResult f_IAsyncResult)
        {
            LogIt.PushLog($"[TCPServer][DoAcceptTCPClientCallback] ++");
            m_TCPListener = (System.Net.Sockets.TcpListener)f_IAsyncResult.AsyncState;
            m_Client = m_TCPListener.EndAcceptTcpClient(f_IAsyncResult);
            m_Stream = m_Client.GetStream();
            m_tcpClientConnectedEvent.Set();
            LogIt.PushLog($"[TCPServer][DoAcceptTCPClientCallback] --");
        }

        #region static functions
        public static void start(System.Threading.EventWaitHandle quitEvent, System.Collections.Specialized.StringDictionary args)
        {
            m_Log.Info($"[start] ++");
            int t_Port = 6280;
            if (args.ContainsKey("port"))
            {
                if(!Int32.TryParse(args["port"], out t_Port))
                {
                    t_Port = 6280;
                }
            }
            m_Log.Info($"[start]: Port = {t_Port}");
            try
            {
                TcpListener server = new TcpListener(IPAddress.Loopback, t_Port);
                server.Start();
                server.BeginAcceptSocket(new AsyncCallback(DoAcceptClientCallback), server);

                Console.WriteLine("Server is started. press any key to terminate.");
                while (!quitEvent.WaitOne(1000))
                {
                    if (Console.KeyAvailable)
                    {
                        Program.logIt("TCPServer::start: is going to be terminated by user key input.");
                        quitEvent.Set();
                    }
                }
            }
            catch (Exception) { }
            Program.logIt("TCPServer::start: --");
        }
        static void DoAcceptClientCallback(IAsyncResult ar)
        {
            Program.logIt("DoAcceptTcpClientCallback: ++");
            try
            {
                TcpListener listener = (TcpListener)ar.AsyncState;
                Socket client = listener.EndAcceptSocket(ar);
                new TaskFactory().StartNew(new Action<object>((c) =>
                {
                    handle_client((Socket)c);
                }), client);
                listener.BeginAcceptSocket(new AsyncCallback(DoAcceptClientCallback), listener);
            }
            catch (Exception) { }
            Program.logIt("DoAcceptTcpClientCallback: --");
        }
        static void handle_client(Socket client)
        {
            Program.logIt("handle_client: ++");
            MemoryStream ms = new MemoryStream();
            Dictionary<string, object> current_task = null;
            try
            {
                bool done = false;
                while (!done)
                {
                    if (client.Poll(1000, SelectMode.SelectRead))
                    {
                        if (client.Available > 0)
                        {
                            byte[] buf = new byte[client.Available];
                            int r = client.Receive(buf);
                            ms.Write(buf, 0, r);
                        }
                        else
                        {
                            client.Shutdown(SocketShutdown.Both);
                            client.Close();
                            done = true;
                        }
                    }
                    // check if incoming data complete, 
                    // Command is terminated by LF (line feed) (0x0a)
                    if (!done && ms.Length > 0)
                    {
                        byte[] data = ms.ToArray();
                        if (data.Last() == 0x0a)
                        {
                            string cmd = System.Text.Encoding.UTF8.GetString(data);
                            ms.SetLength(0);
                            Program.logIt($"handle_client: recv command: {cmd}");
                            Tuple<int, string> res = handle_command(cmd.Split(null), ref current_task);
                            if (res.Item1==0)
                            {
                                // task started success.
                            }
                            else if (res.Item1 == 3)
                            {
                                // task cancelled
                                client.Send(System.Text.Encoding.UTF8.GetBytes(res.Item2));
                            }
                            else if (res.Item1 == 4)
                            {
                                // return "ACK MMI {station name}"
                                client.Send(System.Text.Encoding.UTF8.GetBytes(res.Item2));
                            }
                            else if (res.Item1 == 5)
                            {
                                // return "ERR PMP {reason}"
                                client.Send(System.Text.Encoding.UTF8.GetBytes(res.Item2));
                            }
                            else
                            {
                                // error,
                                string s = $"ERR  ";
                            }
                        }
                    }
                    if (!done && current_task != null)
                    {
                        bool task_done = false;
                        object o;
                        if(current_task.TryGetValue("task", out o))
                        {
                            Task<Dictionary<string, object>> t = (Task<Dictionary<string, object>>)o;
                            if (t.IsCompleted)
                            {
                                task_done = true;
                                // completed, get result
                                Tuple<bool, string> res = handle_command_complete(current_task);
                                if(res.Item1 && !string.IsNullOrEmpty(res.Item2))
                                    client.Send(System.Text.Encoding.UTF8.GetBytes(res.Item2));
                            }
                            else if (t.IsCanceled)
                            {
                                task_done = true;
                                // cancelled
                            }
                            else if (t.IsFaulted)
                            {
                                task_done = true;
                                // unhandled exception
                            }
                        }
                        if (task_done)
                            current_task = null;
                    }
                    //System.Threading.Thread.Sleep(1000);
                }
            }
            catch (Exception) { }
            Program.logIt("handle_client: --");
        }
        static Tuple<bool, string> handle_command_complete(Dictionary<string,object> args)
        {
            object o;
            string tid=string.Empty;
            bool ret = false;
            string response = string.Empty;
            Program.logIt("handle_command_complete: ++");
            if (args.TryGetValue("id", out o))
                tid = o as string;
            Task<Dictionary<string, object>> task = null;
            if (args.TryGetValue("task", out o))
                task = (Task<Dictionary<string, object>>)o;
            if (string.Compare(tid, "ISP", true) == 0 && task!=null)
            {
                // prepare response for command QueryLoad
                Dictionary<string, object> res = task.Result;
                if (res.ContainsKey("errorcode") && res.ContainsKey("script"))
                {
                    if ((int)res["errorcode"] == 0)
                    {
                        response = $"ACK {tid} {res["script"].ToString()}\n";
                    }
                    else
                    {
                        response = $"ERR {tid} {res["errorcode"]}\n";
                    }
                    ret = true;
                }
            }
            else if (string.Compare(tid, "Load", true) == 0 && task != null)
            {
                Dictionary<string, object> res = task.Result;
                if(res.ContainsKey("errorcode") && res.ContainsKey("arrival"))
                {
                    if ((int)res["errorcode"] == 0)
                    {
                        if((bool)res["arrival"])
                        {
                            response = $"ACK {tid}\n";
                        }
                    }
                    else
                    {
                        response = $"ERR {tid} {res["errorcode"]}\n";
                    }
                    ret = true;
                }
            }
            else if (string.Compare(tid, "Unload", true) == 0 && task != null)
            {
                Dictionary<string, object> res = task.Result;
                if (res.ContainsKey("errorcode") && res.ContainsKey("removal"))
                {
                    if ((int)res["errorcode"] == 0)
                    {
                        if ((bool)res["removal"])
                        {
                            response = $"ACK {tid}\n";
                        }
                    }
                    else
                    {
                        response = $"ERR {tid} {res["errorcode"]}\n";
                    }
                    ret = true;
                }
            }
            else if (string.Compare(tid, "PMP", true) == 0 && task != null)
            {
                Dictionary<string, object> res = task.Result;
                if (res.ContainsKey("errorcode") && res.ContainsKey("model"))
                {
                    if ((int)res["errorcode"] == 0)
                    {
                        response = $"ACK {tid} {res["model"]}\n";
                    }
                    else
                    {
                        response = $"ERR {tid} {res["errorcode"]}\n";
                    }
                    ret = true;
                }
            }
            else if (string.Compare(tid, "MMI", true) == 0 && task != null)
            {
                Dictionary<string, object> res = task.Result;
                if (res.ContainsKey("errorcode") && res.ContainsKey("filename"))
                {
                    if ((int)res["errorcode"] == 0)
                    {
                        response = $"ACK {tid} {res["filename"]}\n";
                    }
                    else
                        response = $"ERR {tid} {res["errorcode"]}\n";
                    ret = true;
                }

            }

            Program.logIt($"handle_command_complete: -- {ret} response={response}");
            return new Tuple<bool, string>(ret, response);
        }
        static Tuple<int,string> handle_command(string[] cmds, ref Dictionary<string,object> current_task)
        {
            int error = -1;
            string response = string.Empty;
            Program.logIt($"handle_command: ++ {string.Join(" ", cmds)}");
            if (string.Compare(cmds[0], "Abort", true) == 0)
            {
                if (current_task == null)
                {
                    Program.logIt($"handle_command: no current task can be aborted");
                    error = 2;
                }
                else
                {
                    try
                    {
                        object o;
                        if (current_task.TryGetValue("CancellationTokenSource", out o))
                        {
                            CancellationTokenSource cts = (CancellationTokenSource)o;
                            cts.Cancel();
                            if (current_task.TryGetValue("id", out o))
                                response = $"ERR {o as string} Abort\n";
                            error = 3;
                        }
                    }
                    catch (Exception) { }
                }
            }
            //else if (string.Compare(cmds[0], "QueryPMP", true) == 0)
            //{
            //    if (current_task == null)
            //    {
            //        Program.logIt($"handle_command: not received MMI command");
            //        response = $"ERR PMP no memory map image\n";
            //        error = 5;
            //    }
            //    else
            //    {
            //        // continue wait.
            //    }
            //}
            else
            {
                if (current_task != null)
                {
                    Program.logIt($"handle_command: current task is still running. Cannot start another command.");
                    error = 1;
                }
                else
                {
                    if (string.Compare(cmds[0], "QueryISP", true) == 0)
                    {
                        // wait for QueryISP.
                        // return only when 1) QueryISP return, or 2) abort called
                        
                        var tokenSource = new CancellationTokenSource();
                        Task<Dictionary<string, object>> t = Task.Factory.StartNew((o) =>
                        {
                            Dictionary<string, object> ret = new Dictionary<string, object>();
                            CancellationToken ct = (CancellationToken)o;
                            IniFile ini = new IniFile(Program.getAviaDeviceFilename());
                            ini.WriteValue("query", "command", "ISP");
                            //handle_QueryLoad_command(ct);
                            while (true)
                            {
                                System.Threading.Thread.Sleep(1000);
                                if (ct.IsCancellationRequested)
                                {
                                    Program.logIt($"handle_command: QueryLoad cancelled.");
                                    break;
                                }
                                string s = ini.GetString("device", "device", "");
                                //if (string.Compare(s, "ready", true) == 0)
                                {
                                    ret.Add("errorcode", 0);
                                    string selection = ini.GetString("device", "select", "");
                                    if (string.IsNullOrEmpty(selection))
                                    {
                                        string sid = ini.GetString("device", "sizeid", "");
                                        string cid = ini.GetString("device", "colorid", "");
                                        //ret.Add("sizeid", ini.GetString("device", "sizeid", ""));
                                        //ret.Add("colorid", ini.GetString("device", "colorid", ""));
                                        //ret["script"] = mapSizeColorToScript(ret);
                                        ret["script"] = mapSizeColorToScript(sid, cid);
                                    }
                                    else
                                        ret["script"] = selection;
                                    break;
                                }
                            }
                            return ret;
                        }, tokenSource.Token);
                        error = 0;
                        current_task = new Dictionary<string, object>();
                        current_task.Add("CancellationTokenSource", tokenSource);
                        current_task.Add("task", t);
                        current_task.Add("id", "ISP");
                        current_task.Add("starttime", DateTime.Now);
                    }
                    else if (string.Compare(cmds[0], "QueryPMP", true) == 0)
                    {
                        var tokenSource = new CancellationTokenSource();
                        Task<Dictionary<string, object>> t = Task.Factory.StartNew((o) =>
                        {
                            //CancellationToken ct = (CancellationToken)o;
                            //return handle_QueryPMP_command(ct);  
                            // test get frame
                            if(false)
                            {
                                Process p = new Process();
                                p.StartInfo.FileName = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("FDHOME"), "AVIA", "AviaGetPhoneSize.exe");
                                p.StartInfo.Arguments = $"-QueryFrame";
                                p.StartInfo.UseShellExecute = false;
                                p.StartInfo.CreateNoWindow = true;
                                p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                                p.Start();
                            }
                            Dictionary<string, object> ret = new Dictionary<string, object>();
                            CancellationToken ct = (CancellationToken)o;
                            IniFile ini = new IniFile(Program.getAviaDeviceFilename());
                            ini.WriteValue("query", "command", "PMP");
                            while (true)
                            {
                                System.Threading.Thread.Sleep(1000);
                                if (ct.IsCancellationRequested)
                                {
                                    Program.logIt($"handle_command: QueryPMP cancelled.");
                                    break;
                                }
                                string s = ini.GetString("device", "device", "");
                                string model = ini.GetString("device", "select", "");
                                if (string.IsNullOrEmpty(model))
                                {
                                    model = ini.GetString("device", "model", "");
                                    if (string.Compare(s, "ready", true) == 0 && !string.IsNullOrEmpty(model))
                                    {
                                        ret.Add("errorcode", 0);
                                        ret.Add("model", model);
                                        break;
                                    }
                                }
                                else
                                {
                                    ret.Add("errorcode", 0);
                                    ret.Add("model", model);
                                    break;
                                }
                            }
                            return ret;
                        }, tokenSource.Token);
                        error = 0;
                        current_task = new Dictionary<string, object>();
                        current_task.Add("CancellationTokenSource", tokenSource);
                        current_task.Add("task", t);
                        current_task.Add("id", "PMP");
                        current_task.Add("starttime", DateTime.Now);
                    }
                    else if (string.Compare(cmds[0], "MMI", true) == 0)
                    {
                        // wait for MMI and QueryPMP.
                        // return only when 1) QueryPMP return, or 2) abort called
#if true
                        string fn = string.Empty;
                        if (cmds.Length > 1)
                            fn = cmds[1];
                        else
                            response = "ERR MMI Missing filename\n";
                        try
                        {
                            using (System.IO.MemoryMappedFiles.MemoryMappedFile mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(fn))
                            {
                                IniFile ini = new IniFile(Program.getAviaDeviceFilename());
                                ini.WriteValue("query", "command", "MMI");
                                ini.WriteValue("query", "filename", fn);
                                response = $"ACK MMI {fn}\n";
                                var tokenSource = new CancellationTokenSource();
                                Task<Dictionary<string, object>> t = Task.Factory.StartNew((o) => 
                                {
                                    CancellationToken ct = (CancellationToken)o;
                                    return handle_QueryPMP_command(ct);
                                }, tokenSource.Token);
                            }
                        }
                        catch (Exception ex)
                        {
                            response = $"ERR MMI {ex.Message}\n";
                        }
                        error = 4;
#else
                        string fn = string.Empty;
                        try
                        {
                            fn = cmds[1];
                            var tokenSource = new CancellationTokenSource();
                            Task<Dictionary<string, object>> t = Task.Factory.StartNew((o) =>
                            {
                                Dictionary<string, object> ret = new Dictionary<string, object>();
                                Tuple<CancellationToken, string> args = (Tuple<CancellationToken, string>)o;
                                CancellationToken ct = args.Item1;
                                try
                                {
                                    ret.Add("filename", args.Item2);
                                    using (System.IO.MemoryMappedFiles.MemoryMappedFile mmf = System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(args.Item2))
                                    {
                                        IniFile ini = new IniFile(Program.getAviaDeviceFilename());
                                        ini.WriteValue("query", "command", "MMI");
                                        ini.WriteValue("query", "filename", args.Item2);
                                        ret.Add("errorcode", 0);
                                    }
                                }
                                catch (Exception)
                                {
                                    // fail to open mmf.
                                    ret.Add("errorcode", 6);
                                }
                                return ret;
                            }, new Tuple<CancellationToken, string>(tokenSource.Token, fn));
                            error = 0;
                            current_task = new Dictionary<string, object>();
                            current_task.Add("CancellationTokenSource", tokenSource);
                            current_task.Add("task", t);
                            current_task.Add("id", "MMI");
                            current_task.Add("starttime", DateTime.Now);
                        }
                        catch (Exception) { }
#endif
                    }
                    else if (string.Compare(cmds[0], "QueryLoad", true) == 0)
                    {
                        // wait for user load the device.
                        // return only when 1) device loaded, or 2) abort called
                        var tokenSource = new CancellationTokenSource();
                        Task<Dictionary<string, object>> t = Task.Factory.StartNew((o) =>
                        {
                            CancellationToken ct = (CancellationToken)o;
                            return handle_QueryLoad_command(ct);
                            /*
                            Dictionary<string, object> ret = new Dictionary<string, object>();
                            CancellationToken ct = (CancellationToken)o;
                            IniFile ini = new IniFile(Program.getAviaDeviceFilename());
                            ini.WriteValue("query", "command", "Load");
                            while (true)
                            {
                                System.Threading.Thread.Sleep(1000);
                                if (ct.IsCancellationRequested)
                                {
                                    Program.logIt($"handle_command: QueryLoad cancelled.");
                                    break;
                                }
                                string s = ini.GetString("device", "ready", "");
                                if (string.Compare(s, Boolean.TrueString, true) == 0)
                                {
                                    ret.Add("errorcode", 0);
                                    ret.Add("arrival", true);
                                    break;
                                }
                            }
                            return ret;
                            */
                        }, tokenSource.Token);
                        error = 0;
                        current_task = new Dictionary<string, object>();
                        current_task.Add("CancellationTokenSource", tokenSource);
                        current_task.Add("task", t);
                        current_task.Add("id", "Load");
                        current_task.Add("starttime", DateTime.Now);
                    }
                    else if (string.Compare(cmds[0], "QueryUnload", true) == 0)
                    {
                        // wait for user unload the device.
                        // return only when 1) device loaded, or 2) abort called
                        var tokenSource = new CancellationTokenSource();
                        Task<Dictionary<string, object>> t = Task.Factory.StartNew((o) =>
                        {
                            CancellationToken ct = (CancellationToken)o;
                            return handle_QueryUnload_command(ct);
                            /*
                            Dictionary<string, object> ret = new Dictionary<string, object>();
                            CancellationToken ct = (CancellationToken)o;
                            IniFile ini = new IniFile(Program.getAviaDeviceFilename());
                            ini.WriteValue("query", "command", "Unload");
                            while (true)
                            {
                                System.Threading.Thread.Sleep(1000);
                                if (ct.IsCancellationRequested)
                                {
                                    Program.logIt($"handle_command: QueryLoad cancelled.");
                                    break;
                                }
                                string s = ini.GetString("device", "ready", "");
                                if (string.Compare(s, Boolean.FalseString, true) == 0)
                                {
                                    ret.Add("errorcode", 0);
                                    ret.Add("removal", true);
                                    break;
                                }
                            }
                            return ret;
                            */
                        }, tokenSource.Token);
                        error = 0;
                        current_task = new Dictionary<string, object>();
                        current_task.Add("CancellationTokenSource", tokenSource);
                        current_task.Add("task", t);
                        current_task.Add("id", "Unload");
                        current_task.Add("starttime", DateTime.Now);
                    }
                    else if (string.Compare(cmds[0], "QueryFrame", true) == 0)
                    {
                        Tuple<bool, string> res = VideoCapture.captre_frame();
                        if (res.Item1)
                        {
                            error = 5;
                            response = $"ACK frame {res.Item2}\n";
                        }
                    }
                    else
                    {
                        // un-supported command.
                    }
                }
            }
            Program.logIt($"handle_command: -- ret={error}");
            return new Tuple<int, string>(error,response);
        }
        static Dictionary<string, object> parseKeyValuePair(string[] lines)
        {
            Dictionary<string, object> ret = new Dictionary<string, object>();
            foreach(string line in lines)
            {
                int pos = line.IndexOf('=');
                if(pos>0 && pos < line.Length - 1)
                {
                    string k = line.Substring(0, pos);
                    string v = line.Substring(pos + 1);
                    ret[k] = v;
                }
            }
            return ret;
        }
        static string mapSizeColorToScript(Dictionary<string,object> args)
        {
            string ret = "Flow8Plussilver";
            Program.logIt("mapSizeColorToScript: ++");
            try
            {
                if (args.ContainsKey("colorid") && args.ContainsKey("sizeid"))
                {
                    string fn = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("FDHOME"), "AVIA", "FDPhoneRecognition.json");
                    if (System.IO.File.Exists(fn))
                    {
                        var jss = new System.Web.Script.Serialization.JavaScriptSerializer();
                        Dictionary<string, Object> dict = jss.Deserialize<Dictionary<string, object>>(System.IO.File.ReadAllText(fn));
                        if (dict.ContainsKey("ISP"))
                        {
                            Dictionary<string, Object> isp = (Dictionary<string, Object>)dict["ISP"];
                            if (isp.ContainsKey($"{args["sizeid"].ToString()}-{args["colorid"].ToString()}"))
                            {
                                ret = isp[$"{args["sizeid"].ToString()}-{args["colorid"].ToString()}"].ToString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Program.logIt($"mapSizeColorToScript: exception: {ex.Message}");
                Program.logIt($"mapSizeColorToScript: exception: {ex.StackTrace}");
            }
            Program.logIt($"mapSizeColorToScript: -- ret={ret}");
            return ret;
        }
        static string mapSizeColorToScript(string sizeID, string colorID)
        {
            string ret = "Flow8Plusred";
            try
            {
                IniFile ini = new IniFile(Program.getAviaDeviceFilename());
                string s = ini.GetString("script", "default", "Flow8Plusred");
                ret = ini.GetString("script", $"{sizeID}-{colorID}", s);
            }
            catch (Exception) { }
            return ret;
        }
        static Dictionary<string, object> handle_QueryUnload_command(CancellationToken ct)
        {
            Dictionary<string, object> ret = new Dictionary<string, object>();
            IniFile ini = new IniFile(Program.getAviaDeviceFilename());
            ini.WriteValue("query", "command", "Unload");
            //ini.DeleteSection("device");
            Process p = new Process();
            p.StartInfo.FileName = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("FDHOME"), "AVIA", "AviaGetPhoneSize.exe");
            p.StartInfo.Arguments = $"-QueryISP -start-service";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.OutputDataReceived += (s, o) =>
            {
                if (o.Data != null)
                {
                    Program.logIt($"[AviaGetPhoneSize]: {o.Data}");
                    int pos = o.Data.IndexOf('=');
                    if (pos > 0 && pos < o.Data.Length - 1)
                    {
                        string k = o.Data.Substring(0, pos);
                        string v = o.Data.Substring(pos + 1);
                        if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v))
                            ini.WriteValue("device", k, v);
                    }
                }
            };
            if (true)
            {
                p.Start();
                p.BeginOutputReadLine();
            }
            while (true)
            {
                System.Threading.Thread.Sleep(1000);
                if (ct.IsCancellationRequested)
                {
                    Program.logIt($"handle_command: QueryLoad cancelled.");
                    break;
                }
                string s = ini.GetString("device", "device", "");
                if (string.Compare(s, "removed", true) == 0)
                {
                    ret.Add("errorcode", 0);
                    ret.Add("removal", true);
                    break;
                }
            }
            if (true)
            {
                if (!p.HasExited)
                {
                    Process p1 = new Process();
                    p1.StartInfo.FileName = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("FDHOME"), "AVIA", "AviaGetPhoneSize.exe");
                    p1.StartInfo.Arguments = $"-QueryISP -kill-service";
                    p1.StartInfo.UseShellExecute = false;
                    p1.StartInfo.CreateNoWindow = true;
                    p1.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    p1.Start();
                    p1.WaitForExit();
                }
            }
            return ret;
        }
        static Dictionary<string, object> handle_QueryLoad_command(CancellationToken ct)
        {
            Dictionary<string, object> ret = new Dictionary<string, object>();
            //CancellationToken ct = (CancellationToken)o;
            IniFile ini = new IniFile(Program.getAviaDeviceFilename());
            ini.WriteValue("query", "command", "Load");
            ini.WriteValue("device", "device", "");
            //ini.DeleteSection("device");
            Process p = new Process();
            p.StartInfo.FileName = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("FDHOME"), "AVIA", "AviaGetPhoneSize.exe");
            p.StartInfo.Arguments = $"-QueryISP -start-service";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.OutputDataReceived += (s, o) => 
            {
                if (o.Data != null)
                {
                    Program.logIt($"[AviaGetPhoneSize]: {o.Data}");
                    int pos = o.Data.IndexOf('=');
                    if(pos>0 && pos < o.Data.Length - 1)
                    {
                        string k = o.Data.Substring(0, pos);
                        string v = o.Data.Substring(pos + 1);
                        if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v))
                            ini.WriteValue("device", k, v);
                    }
                }
            };
            if (true)
            {
                p.Start();
                p.BeginOutputReadLine();
            }
            while (true)
            {
                System.Threading.Thread.Sleep(1000);
                if (ct.IsCancellationRequested)
                {
                    Program.logIt($"handle_command: QueryLoad cancelled.");
                    break;
                }
                string s = ini.GetString("device", "device", "");
                if (string.Compare(s, "ready", true) == 0)
                {
                    ret.Add("errorcode", 0);
                    ret.Add("arrival", true);
                    break;
                }
            }
            if (true)
            {
                if (!p.HasExited)
                {
                    Process p1 = new Process();
                    p1.StartInfo.FileName = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("FDHOME"), "AVIA", "AviaGetPhoneSize.exe");
                    p1.StartInfo.Arguments = $"-QueryISP -kill-service";
                    p1.StartInfo.UseShellExecute = false;
                    p1.StartInfo.CreateNoWindow = true;
                    p1.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    p1.Start();
                    p1.WaitForExit();
                }
            }
            return ret;
        }
        static Dictionary<string, object> handle_QueryPMP_command(CancellationToken ct)
        {
            Dictionary<string, object> ret = new Dictionary<string, object>();
            //CancellationToken ct = (CancellationToken)o;
            IniFile ini = new IniFile(Program.getAviaDeviceFilename());
            ini.WriteValue("query", "command", "PMP");
            //ini.DeleteSection("device");
            System.Threading.AutoResetEvent e = new AutoResetEvent(false);
            Process p = new Process();
            p.StartInfo.FileName = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("FDHOME"), "AVIA", "AviaGetPhoneSize.exe");
            p.StartInfo.Arguments = $"-QueryPMP";
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.OutputDataReceived += (s, o) =>
            {
                if (o.Data == null)
                {
                    e.Set();
                }
                else
                {
                    Program.logIt($"[AviaGetPhoneSize]: {o.Data}");
                    int pos = o.Data.IndexOf('=');
                    if (pos > 0 && pos < o.Data.Length - 1)
                    {
                        string k = o.Data.Substring(0, pos);
                        string v = o.Data.Substring(pos + 1);
                        if (!string.IsNullOrEmpty(k) && !string.IsNullOrEmpty(v))
                        {
                            ini.WriteValue("device", k, v);
                            ret[k] = v;
                        }
                    }
                }
            };
            p.Start();
            p.BeginOutputReadLine();
            while (!p.HasExited)
            {
                System.Threading.Thread.Sleep(1000);
                if (ct.IsCancellationRequested)
                {
                    Program.logIt($"handle_command: QueryLoad cancelled.");
                    break;
                }
            }
            e.WaitOne(1000);
            if (p.HasExited)
            {
                ret.Add("errorcode", p.ExitCode);
            }
            else
            {
                p.Kill();
                ret.Add("errorcode", 1460);
            }
            return ret;
        }
        #endregion
    }
}
