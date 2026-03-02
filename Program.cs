using System;
using System.Collections.Generic;
using System.Text;
using System.Net.Sockets;
using System.Net;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Configuration;


namespace DNWS
{
    // Main class
    public class Program
    {
        static public IConfigurationRoot Configuration { get; set; }

        // Log to console
        public void Log(String msg)
        {
            Console.WriteLine(msg);
        }

        // Start the server, Singleton here
        public void Start()
        {
            // Start server
            var builder = new ConfigurationBuilder();
            builder.SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("config.json");
            Configuration = builder.Build();
            DotNetWebServer ws = DotNetWebServer.GetInstance(this);
            ws.Start();
        }

        static void Main(string[] args)
        {
            Program p = new Program();
            p.Start();
        }
    }

    public class HTTPProcessor
    {
        protected class PluginInfo
        {
            protected string _path;
            protected string _type;
            protected bool _preprocessing;
            protected bool _postprocessing;
            protected IPlugin _reference;

            public string path { get { return _path;} set {_path = value;} }
            public string type { get { return _type;} set {_type = value;} }
            public bool preprocessing { get { return _preprocessing;} set {_preprocessing = value;} }
            public bool postprocessing { get { return _postprocessing;} set {_postprocessing = value;} }
            public IPlugin reference { get { return _reference;} set {_reference = value;} }
        }

        protected string ROOT = Program.Configuration["DocumentRoot"];
        protected Socket _client;
        protected Program _parent;
        protected Dictionary<string, PluginInfo> plugins;

        public HTTPProcessor(Socket client, Program parent)
        {
            _client = client;
            _parent = parent;
            plugins = new Dictionary<string, PluginInfo>();

            var sections = Program.Configuration.GetSection("Plugins").GetChildren();
            foreach(ConfigurationSection section in sections) {
                PluginInfo pi = new PluginInfo();
                pi.path = section["Path"];
                pi.type = section["Class"];
                pi.preprocessing = section["Preprocessing"].ToLower().Equals("true");
                pi.postprocessing = section["Postprocessing"].ToLower().Equals("true");
                pi.reference = (IPlugin) Activator.CreateInstance(Type.GetType(pi.type));
                plugins[section["Path"]] = pi;
            }
        }

        protected HTTPResponse getFile(String path)
        {
            HTTPResponse response = null;

            string fileType = "text/html";
            if (path.ToLower().EndsWith("jpg") || path.ToLower().EndsWith("jpeg"))
                fileType = "image/jpeg";
            if (path.ToLower().EndsWith("png"))
                fileType = "image/png";

            try
            {
                response = new HTTPResponse(200);
                response.type = fileType;
                response.body = System.IO.File.ReadAllBytes(path);
            }
            catch (FileNotFoundException ex)
            {
                response = new HTTPResponse(404);
                response.body = Encoding.UTF8.GetBytes("<h1>404 Not found</h1>" + ex.Message);
            }
            catch (Exception ex)
            {
                response = new HTTPResponse(500);
                response.body = Encoding.UTF8.GetBytes("<h1>500 Internal Server Error</h1>" + ex.Message);
            }
            return response;
        }

        public void Process()
        {
            NetworkStream ns = new NetworkStream(_client);
            string requestStr = "";
            HTTPRequest request = null;
            HTTPResponse response = null;
            byte[] bytes = new byte[1024];
            int bytesRead;

            do
            {
                bytesRead = ns.Read(bytes, 0, bytes.Length);
                requestStr += Encoding.UTF8.GetString(bytes, 0, bytesRead);
            } while (ns.DataAvailable);

            request = new HTTPRequest(requestStr);
            request.addProperty("RemoteEndPoint", _client.RemoteEndPoint.ToString());

            if(request.Status != 200) {
                response = new HTTPResponse(request.Status);
            }
            else
            {
                bool processed = false;

                foreach(KeyValuePair<string, PluginInfo> plugininfo in plugins) {
                    if(plugininfo.Value.preprocessing)
                        plugininfo.Value.reference.PreProcessing(request);
                }

                foreach(KeyValuePair<string, PluginInfo> plugininfo in plugins) {
                    if(request.Filename.StartsWith(plugininfo.Key)) {
                        response = plugininfo.Value.reference.GetResponse(request);
                        processed = true;
                    }
                }

                if(!processed) {
                    if (request.Filename.Equals(""))
                        response = getFile(ROOT + "/index.html");
                    else
                        response = getFile(ROOT + "/" + request.Filename);
                }

                foreach(KeyValuePair<string, PluginInfo> plugininfo in plugins) {
                    if(plugininfo.Value.postprocessing)
                        response = plugininfo.Value.reference.PostProcessing(response);
                }
            }

            ns.Write(Encoding.UTF8.GetBytes(response.header), 0, response.header.Length);
            if(response.body != null)
                ns.Write(response.body, 0, response.body.Length);

            _client.Shutdown(SocketShutdown.Both);
        }
    }

    public class TaskInfo
    {
        private HTTPProcessor _hp;
        public HTTPProcessor hp { get {return _hp;} set {_hp = value;} }

        public TaskInfo(HTTPProcessor hp)
        {
            this.hp = hp;
        }
    }

    public class DotNetWebServer
    {
        protected int _port;
        protected Program _parent;
        protected Socket serverSocket;
        protected Socket clientSocket;
        private static DotNetWebServer _instance = null;
        protected int id;

        private DotNetWebServer(Program parent)
        {
            _parent = parent;
            id = 0;
        }

        public static DotNetWebServer GetInstance(Program parent)
        {
            if (_instance == null)
                _instance = new DotNetWebServer(parent);
            return _instance;
        }

        public void ThreadProc(Object stateinfo)
        {
            TaskInfo ti = stateinfo as TaskInfo;
            ti.hp.Process();
        }

        public void Start()
        {
            _port = Convert.ToInt32(Program.Configuration["Port"]);
            string threadingMode = Program.Configuration["ThreadingMode"] ?? "Single";
            IPEndPoint localEndPoint = new IPEndPoint(IPAddress.Any, _port);

            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.Bind(localEndPoint);
            serverSocket.Listen(5);

            _parent.Log("Server started at port " + _port + " in " + threadingMode + " threading mode.");

            while (true)
            {
                try
                {
                    clientSocket = serverSocket.Accept();
                    _parent.Log("Client accepted:" + clientSocket.RemoteEndPoint.ToString());

                    HTTPProcessor hp = new HTTPProcessor(clientSocket, _parent);
                    TaskInfo ti = new TaskInfo(hp);

                    Thread t = new Thread(new ParameterizedThreadStart(ThreadProc));
                    t.Start(ti);
                }
                catch (Exception ex)
                {
                    _parent.Log("Server starting error: " + ex.Message + "\n" + ex.StackTrace);
                }
            }
        }
    }
}