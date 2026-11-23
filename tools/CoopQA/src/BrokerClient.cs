using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoopQA
{
    /// <summary>JSON-lines TCP client for broker.py. Reads on a background thread; the main thread drains Inbox.</summary>
    internal sealed class BrokerClient
    {
        private readonly TcpClient tcp;
        private readonly StreamWriter writer;
        private readonly StreamReader reader;
        private readonly object sendLock = new object();
        public readonly ConcurrentQueue<JObject> Inbox = new ConcurrentQueue<JObject>();
        public volatile bool Connected;

        public BrokerClient(string host, int port)
        {
            tcp = new TcpClient { NoDelay = true };
            tcp.Connect(host, port);
            var stream = tcp.GetStream();
            writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = false };
            reader = new StreamReader(stream, new UTF8Encoding(false));
            Connected = true;
            var t = new Thread(ReadLoop) { IsBackground = true, Name = "CoopQA.Broker" };
            t.Start();
        }

        /// <summary>Blocks until a message with the given op arrives (used once, for the hello handshake).</summary>
        public JObject WaitFor(string op, int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            var skipped = new System.Collections.Generic.List<JObject>();
            JObject found = null;
            while (DateTime.UtcNow < deadline && found == null)
            {
                if (Inbox.TryDequeue(out var m))
                {
                    if ((string)m["op"] == op) found = m; else skipped.Add(m);
                }
                else Thread.Sleep(5);
            }
            foreach (var m in skipped) Inbox.Enqueue(m);
            return found;
        }

        public void Send(object msg)
        {
            string line = JsonConvert.SerializeObject(msg);
            lock (sendLock)
            {
                try { writer.Write(line); writer.Write('\n'); writer.Flush(); }
                catch (Exception e) { Connected = false; Plugin.Log.LogError("[CoopQA] broker send failed: " + e.Message); }
            }
        }

        private void ReadLoop()
        {
            try
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    try { Inbox.Enqueue(JObject.Parse(line)); }
                    catch (Exception e) { Plugin.Log.LogError("[CoopQA] bad broker line: " + e.Message); }
                }
            }
            catch (Exception e) { Plugin.Log.LogError("[CoopQA] broker read failed: " + e.Message); }
            Connected = false;
        }
    }
}
