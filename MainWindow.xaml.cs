using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace AsuroBot_2
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const int defaultPort = 6667;
        private ServerConnection server;

        public MainWindow()
        {
            InitializeComponent();
        }

        private bool ParseIpText(string text, out IPAddress ip, out int port)
        {
            ip = IPAddress.Any;
            port = 0;

            if (!text.Contains(":"))
                return false;

            string ipStr = text.Substring(0, text.LastIndexOf(":"));
            string portStr = text.Substring(text.LastIndexOf(":") + 1);

            IPAddress[] addressList = Dns.GetHostAddresses(ipStr);

            if (addressList.Length == 0)
                return false;
            else
                ip = addressList[0];

            if(!int.TryParse(portStr, out port))
                return false;

            return true;
        }

        private void LogMessage(string message)
        {
            Chat.AddInfoMessage(message);
        }

        private void bnServerConnect_Click(object sender, RoutedEventArgs e)
        {
            IPAddress ip;
            int port;

            if(tbChangeUsername.Text.Equals(""))
            {
                LogMessage("Invalid username!");
                return;
            }

            if(!ParseIpText(tbServerConnect.Text, out ip, out port))
            {
                LogMessage("Failed to parse IP and port! Use format myserver.com:6667");
                return;
            }

            server = new ServerConnection(ip, port);

            if(!server.Connect())
            {
                LogMessage("Failed to connect to server!");
                return;
            }

            server.OnSystemMessage += OnNewLine;
            server.OnUserMessage += OnUserMessage;
            server.OnChannelJoin += OnChannelJoin;
            server.OnDisconnect += OnDisconnect;

            server.AttemptLogin(tbChangeUsername.Text, tbChangeUsername.Text);

            LogMessage("Connected to server!");
        }

        private void OnNewLine(object sender, string line)
        {
            Chat.AddInfoMessage(line);
        }

        private void OnUserMessage(object sender, UserMessage message)
        {
            Chat.AddUserMessage(message);
        }

        private void OnChannelJoin(object sender, string channel)
        {
            Chat.AddChanel(channel);
        }

        private void OnDisconnect(object sender, EventArgs e)
        {
            Chat.RemoveAllChannels();
        }

        private void bnChannelJoin_Click(object sender, RoutedEventArgs e)
        {
            if(server != null && !tbChannelJoin.Text.Equals(""))
            {
                server.JoinChannel(tbChannelJoin.Text);
            }
        }

        private void bnChangeUsername_Click(object sender, RoutedEventArgs e)
        {
            if(server != null && !tbChangeUsername.Text.Equals(""))
            {
                server.ChangeNick(tbChangeUsername.Text);
            }
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Enter && !tbChat.Text.Equals(""))
            {
                if(server.Connected)
                {
                    if(Chat.tcChannels.SelectedIndex == 0)
                    {
                        server.SendServerMessage(tbChat.Text);
                    }
                    else
                    {
                        UserMessage message = new UserMessage();
                        message.channel = Chat.SelectedChannelName;
                        message.user = server.User;
                        message.text = tbChat.Text;
                        
                        server.SendChatMessage(message);
                        Chat.AddUserMessage(message);
                    }
                }
                else
                {
                    LogMessage("Server is not connected!");
                }

                tbChat.Text = "";
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (server != null && server.Connected)
                server.Disconnect();
        }
    }

    public class ServerConnection
    {
        public event EventHandler<string> OnSystemMessage;
        public event EventHandler<UserMessage> OnUserMessage;
        public event EventHandler<string> OnChannelJoin;
        public event EventHandler OnDisconnect;

        public bool Connected { get { return socket.Connected; } }
        public User User { get { return myUser; } }

        private IPAddress ip;
        private int port;
        private User myUser;

        private Socket socket;
        private NetworkStream stream;
        private StreamReader reader;
        private StreamWriter writer;
        private IProgress<string> readProgress;

        private Regex serverMessageRegex;
        private Regex clientMessageRegex;

        public ServerConnection(IPAddress serverIp, int serverPort)
        {
            ip = serverIp;
            port = serverPort;
            myUser = new User();

            serverMessageRegex = new Regex("\\S+ (\\d{3}) (\\S+) (.+)", RegexOptions.IgnoreCase);
            clientMessageRegex = new Regex(":(\\S+)!(\\S+)@(\\S+) (\\S+) (.+)", RegexOptions.IgnoreCase);
        }

        public bool Connect()
        {
            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Connect(ip, port);

                stream = new NetworkStream(socket);
                reader = new StreamReader(stream);
                writer = new StreamWriter(stream);

                readProgress = new Progress<string>(InterpretLine);
                Action action = new Action(delegate { ReadMessageLoop(readProgress); });
                Task.Factory.StartNew(action, TaskCreationOptions.LongRunning);

                return true;
            }
            catch(Exception ex)
            {
                if (stream != null)
                    stream.Close();
                if (reader != null)
                    reader.Close();
                if (writer != null)
                    writer.Close();
                throw ex;
            }
        }

        public void AttemptLogin(string nick, string user, string realname = "", string pass = "")
        {
            myUser.nick = nick;
            myUser.user = user;

            if (nick.Equals("") || user.Equals(""))
                return;

            if (realname.Equals(""))
                realname = nick;
                
            myUser.realname = realname;

            // Start sending stuff to the server

            // Send a password message if specified
            if (!pass.Equals(""))
                writer.WriteLine("PASS " + pass);

            // Register with NICK and USER message
            writer.WriteLine("NICK " + nick);
            writer.WriteLine("USER " + user + " 0 * " + realname);
            writer.Flush();
        }

        public void Disconnect()
        {
            if (Connected)
            {
                writer.WriteLine("QUIT");
                writer.Flush();
            }

            socket.Close();
            if (stream != null)
                stream.Close();

            if (OnDisconnect != null)
                OnDisconnect.Invoke(this, null);
        }

        private void ReadMessageLoop(IProgress<string> progress)
        {
            bool cont = true;

            while (Connected && cont)
            {
                try
                {
                    string nextLine = reader.ReadLine();
                    if (!nextLine.Equals(""))
                        progress.Report(nextLine);
                }
                catch (Exception ex)
                {
                    progress.Report("FAIL: Failed to read from socket because of " + ex.ToString() + ": " + ex.Message);
                    cont = false;
                }
            }
        }

        private void InterpretLine(string line)
        {
            string msg = line.ToLower();

            // this is no message from the server, instead the connection failed
            if(msg.StartsWith("fail"))
            {
                Disconnect();
            }

            // always respond to ping message
            if(msg.StartsWith("ping"))
            {
                string[] messageParts = msg.Split(' ');
                SendServerMessage("PONG " + messageParts[1]);
            }

            // Some kind of server message
            Match match = serverMessageRegex.Match(line);
            if(match.Groups.Count == 4) // Regex string has three capture groups + 1 global capture
            {
                
            }

            // message from other client
            match = clientMessageRegex.Match(line);
            if(match.Groups.Count == 6)
            {
                string msgNick = match.Groups[1].Value;
                string msgUser = match.Groups[2].Value;
                string msgHost = match.Groups[3].Value;
                string identifier = match.Groups[4].Value.ToLower();
                string parameters = match.Groups[5].Value;

                // someone joined a channel
                if(identifier.Equals("join"))
                {
                    // did we join a channel?
                    if(msgNick.Equals(myUser.nick))
                    {
                        if (OnChannelJoin != null)
                            OnChannelJoin(this, parameters.Substring(1));
                    }
                    else
                    {
                        // TODO: user x joined channel y
                    }
                }
                // someone sent a message in a channel
                else if(identifier.Equals("privmsg"))
                {
                    User user = new User(); // TODO: Find user by name here
                    UserMessage message = new UserMessage();

                    user.nick = msgNick;
                    message.user = user;
                    message.channel = parameters.Substring(0, parameters.IndexOf(' '));
                    message.text = parameters.Substring(parameters.IndexOf(' ') + 2);

                    if(OnUserMessage != null)
                        OnUserMessage.Invoke(this, message);
                }
            }
            
            // Log
            if (OnSystemMessage != null)
                OnSystemMessage.Invoke(this, line);
        }

        public void SendServerMessage(string message)
        {
            if(Connected)
            {
                writer.WriteLine(message);
                writer.Flush();
            }
        }

        public void SendChatMessage(UserMessage message)
        {
            SendChatMessage(message.channel, message.text);
        }

        public void SendChatMessage(string channel, string message)
        {
            if(Connected)
            {
                writer.WriteLine("PRIVMSG " + channel + " :" + message);
                writer.Flush();
            }
        }

        public void JoinChannel(string channel)
        {
            if(Connected)
            {
                writer.WriteLine("JOIN " + channel);
                writer.Flush();
            }
        }

        public void ChangeNick(string nick)
        {
            if(Connected)
            {
                writer.WriteLine("NICK " + nick);
                writer.Flush();
            }
        }
    }

    public struct User
    {
        public string nick;
        public string user;
        public string realname;
        public Color nameColor;
    }

    public struct UserMessage
    {
        public User user;
        public string channel;
        public string text;
    }
}
