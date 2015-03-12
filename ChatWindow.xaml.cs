using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

namespace AsuroBot_2
{
    /// <summary>
    /// Interaction logic for ChatWindow.xaml
    /// </summary>
    public partial class ChatWindow : UserControl
    {
        public string SelectedChannelName { get { return ((TabItem)tcChannels.SelectedItem).Header.ToString(); } }

        private Dictionary<string,TabItem> tabs;

        public ChatWindow()
        {
            InitializeComponent();

            tabs = new Dictionary<string,TabItem>();
        }

        public void AddUserMessage(UserMessage message)
        {
            AddChannelMessage(message.channel, message.user.nick + ": " + message.text);
        }

        public void AddInfoMessage(string message)
        {
            SystemText.Text += Environment.NewLine + message;
        }

        private void AddChannelMessage(string channel, string message)
        {
            ChatPanel panel = (ChatPanel)tabs[channel].Content;
            panel.ChatText.Text += Environment.NewLine + message;
        }

        public void AddChanel(string name)
        {
            TabItem ti = new TabItem();
            ti.Header = name;
            ChatPanel panel = new ChatPanel();
            ti.Content = panel;
            panel.ChatText.Text = "---" + name + "---";

            tcChannels.Items.Add(ti);
            tabs.Add(name, ti);
        }

        public void RemoveChannel(string name)
        {
            if(tabs.ContainsKey(name))
            {
                tcChannels.Items.Remove(tabs[name]);
                tabs.Remove(name);
            }
        }

        public void RemoveAllChannels()
        {
            List<string> tempList = new List<string>();

            foreach(string key in tabs.Keys)
            {
                tempList.Add(key);
            }

            tempList.ForEach(RemoveChannel);

            tabs.Clear();
        }
    }
}
